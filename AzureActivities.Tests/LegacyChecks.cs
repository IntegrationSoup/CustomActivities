using Newtonsoft.Json.Linq;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;

namespace AzureActivities.Tests
{
    // Genuine host API/DLL boundary plus the real persistent named-pipe client.
    // The SDK request path is independently exercised by Checks/FakeTransport.
    internal static class LegacyChecks
    {
        internal static int Server(string[] args) => PersistentRunnerServer.RunIfRequested(args, (operation, json) =>
        {
            File.WriteAllText(Environment.GetEnvironmentVariable("AZURE_LEGACY_CAPTURE"), json);
            if (operation == "upload-blob" && (string)JObject.Parse(json)["FileName"] == "force-sdk-failure")
            {
                var transport = new FakeTransport();
                transport.Enqueue(r => {}, 403, "<Error><Code>AuthorizationFailure</Code><Message>DO-NOT-ECHO-SIGNED-SECRET-PAYLOAD</Message></Error>");
                Runner.Program.ContainerClientFactory = (connection, container, options) => { options.Transport = transport; return new Azure.Storage.Blobs.BlobContainerClient(connection, container, options); };
                var handler = (PersistentRunnerRequestHandler)Delegate.CreateDelegate(typeof(PersistentRunnerRequestHandler), typeof(Runner.Program).GetMethod("HandleServerRequest", BindingFlags.Static | BindingFlags.NonPublic));
                return handler(operation, json);
            }
            if (operation == "download-blob" && (string)JObject.Parse(json)["FileName"] == "force-stream-failure")
            {
                var transport = new FakeTransport();
                transport.Enqueue(r => {}, 200, null, 1);
                for (int index = 0; index < 6; index++) transport.Enqueue(r => {}, 200, new InterruptedStream("DO-NOT-ECHO-CREDENTIAL-PAYLOAD"), 1);
                Runner.BlobDownloadOperation.ContainerClientFactory = (connection, container, options) => { options.Transport = transport; return new Azure.Storage.Blobs.BlobContainerClient(connection, container, options); };
                var handler = (PersistentRunnerRequestHandler)Delegate.CreateDelegate(typeof(PersistentRunnerRequestHandler), typeof(Runner.Program).GetMethod("HandleServerRequest", BindingFlags.Static | BindingFlags.NonPublic));
                return handler(operation, json);
            }
            if (operation == "download-blob") return new JObject { ["Base64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("MSH|caf\u00e9\r")), ["MetadataJson"] = "{\"size\":10}" }.ToString();
            if (operation == "upload-blob") return "{\"Message\":\"Code Executed Successfully\",\"BlobPath\":\"inbox/a\",\"ETag\":\"etag\",\"VersionId\":\"version\"}";
            throw new InvalidOperationException("Unexpected legacy operation.");
        }) ?? 2;

        internal static int Run(string dll, string api)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                string path = Path.Combine(Path.GetDirectoryName(api), new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Assembly.LoadFrom(api);
            var assembly = Assembly.LoadFrom(dll);
            string capture = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "legacy-request.json");
            Environment.SetEnvironmentVariable("AZURE_LEGACY_CAPTURE", capture);
            Environment.SetEnvironmentVariable("AZUREACTIVITIES_RUNNER_PATH", Assembly.GetExecutingAssembly().Location);
            var parameters = new Dictionary<string, string> { ["Connection String"] = "synthetic", ["Container Name"] = "inbox", ["File Name"] = " \u00e9/a " };
            var variables = new Dictionary<string, string>();
            string text = Invoke(assembly.GetType("AzureActivities.BlobDownload", true), parameters, variables, null, false, false);
            Require(text == "MSH|caf\u00e9\r", "text response through actual DLL and pipe");
            Require((string)JObject.Parse(File.ReadAllText(capture))["FileName"] == " \u00e9/a ", "exact filename serialized by actual DLL");
            Require(variables.ContainsKey("AzureBlobMetadataJson"), "metadata variable through actual workflow API");
            string binary = Invoke(assembly.GetType("AzureActivities.BlobDownload", true), parameters, variables, null, false, true);
            Require(Encoding.UTF8.GetString(Convert.FromBase64String(binary)) == text, "binary response through actual DLL and pipe");
            parameters["Create Container If Missing"] = "True"; parameters["New Container Access"] = "Blob";
            parameters["Overwrite Existing Blob"] = "True"; parameters["Expected ETag"] = "etag";
            Require(Invoke(assembly.GetType("AzureActivities.BlobSender", true), parameters, variables, "body", false, false) == "Code Executed Successfully", "upload through actual DLL and pipe");
            var sent = JObject.Parse(File.ReadAllText(capture));
            Require((string)sent["FileName"] == " \u00e9/a " && (string)sent["NewContainerAccess"] == "Blob" && (string)sent["OverwriteExistingBlob"] == "True" && (string)sent["ExpectedETag"] == "etag" && (string)sent["CreateContainerIfMissing"] == "True", "all upload options cross legacy boundary");
            Require(variables["AzureBlobETag"] == "etag" && variables["AzureBlobVersionId"] == "version", "upload result variables through actual API");
            string before = File.ReadAllText(capture);
            bool failed = false;
            try { Invoke(assembly.GetType("AzureActivities.BlobSender", true), parameters, variables, "not base64!", true, false); }
            catch (TargetInvocationException exception) when (exception.InnerException is FormatException) { failed = true; }
            Require(failed && File.ReadAllText(capture) == before, "invalid binary input rejected before dispatch");
            parameters["Connection String"] = "BlobEndpoint=https://fixture.blob.core.windows.net/;SharedAccessSignature=sv=2021-12-02&sr=c&sp=w&sig=DO-NOT-ECHO-CREDENTIAL";
            parameters["File Name"] = "force-sdk-failure"; parameters["Create Container If Missing"] = "False"; parameters["New Container Access"] = "Private";
            string error = null;
            try { Invoke(assembly.GetType("AzureActivities.BlobSender", true), parameters, variables, "body", false, false); }
            catch (TargetInvocationException exception) { error = exception.InnerException.Message; }
            Require(error != null && error.Contains("403") && !error.Contains("DO-NOT-ECHO") && !error.Contains("sig="), "actual v4 SDK handler sanitizes response body and credentials");
            parameters["File Name"] = "force-stream-failure"; error = null;
            try { Invoke(assembly.GetType("AzureActivities.BlobDownload", true), parameters, variables, null, false, false); }
            catch (TargetInvocationException exception) { error = exception.InnerException.Message; }
            Require(error != null && error.Contains("interrupted body reads") && !error.Contains("DO-NOT-ECHO") && !error.Contains("sig="), "actual v4 body-retry failure is sanitized");
            Console.WriteLine("PASS 10 genuine-v4 DLL/API/named-pipe checks; synthetic runner, no cloud network.");
            return 0;
        }
        private static string Invoke(Type type, Dictionary<string, string> parameters, Dictionary<string, string> variables, string input, bool binaryInput, bool binaryOutput)
        {
            MethodInfo method = type.GetMethod("Process");
            Type activityType = method.GetParameters()[1].ParameterType;
            Type messageType = activityType.GetProperty("ResponseMessage").PropertyType;
            string output = "";
            object message = input == null ? null : new Proxy(messageType, (name, args) => name == "get_Text" ? input : null, binaryInput).GetTransparentProxy();
            object response = new Proxy(messageType, (name, args) => { if (name == "SetText") output = (string)args[0]; return name == "get_Text" ? output : null; }, binaryOutput).GetTransparentProxy();
            object activity = new Proxy(activityType, (name, args) => name == "get_Message" ? message : name == "get_ResponseMessage" ? response : null).GetTransparentProxy();
            object workflow = new Proxy(method.GetParameters()[0].ParameterType, (name, args) => { if (name == "SetVariable") variables[(string)args[0]] = (string)args[1]; return null; }).GetTransparentProxy();
            method.Invoke(Activator.CreateInstance(type), new[] { workflow, activity, parameters });
            return output;
        }
        private static void Require(bool value, string name) { if (!value) throw new Exception("Legacy check failed: " + name); }
        private sealed class BinaryMessage { }
        private sealed class Proxy : RealProxy
        {
            private readonly Func<string, object[], object> invoke;
            private readonly Type reflectedType;
            internal Proxy(Type type, Func<string, object[], object> invoke, bool binary = false) : base(type) { this.invoke = invoke; reflectedType = binary ? typeof(BinaryMessage) : type; }
            public override IMessage Invoke(IMessage message)
            {
                var call = (IMethodCallMessage)message;
                try { return new ReturnMessage(call.MethodName == "GetType" ? reflectedType : invoke(call.MethodName, call.Args), null, 0, call.LogicalCallContext, call); }
                catch (Exception exception) { return new ReturnMessage(exception, call); }
            }
        }
    }
}
