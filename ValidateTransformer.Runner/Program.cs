using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using HL7Soup.Integrations;
using HL7Soup.Integrations.ExtensionBridge;
using Popokey.ExtensionRunners;

namespace ValidateTransformer.Runner
{
    internal static class Program
    {
        internal static string RuntimeDirectory;
        private static int Main(string[] args)
        {
            try
            {
                var arguments = new List<string>(args);
                int index = arguments.IndexOf("--runtime-directory");
                if (index >= 0)
                {
                    if (index + 1 == arguments.Count || !Path.IsPathFullyQualified(arguments[index + 1])) throw new ArgumentException("Runtime directory must be absolute.");
                    RuntimeDirectory = Path.GetFullPath(arguments[index + 1]);
                    arguments.RemoveRange(index, 2);
                }
                else RuntimeDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
                // This runner uses the installed v5 parser inside its own process.
                // Neither Describe nor assembly startup loads that business runtime.
                SnapshotMessage.TypedFactory = value => value.MessageType == 1 ? new ValidationMessage(value) :
                    value.MessageType == 11 ? new SnapshotJsonMessage(value) : new SnapshotMessage(value);
                return BridgeRunner.RunIfRequested(arguments.ToArray(), UnsupportedLegacy, () => new BridgeProvider(
                    "popokey.validatehl7transformer", "ValidateTransformer", UnsupportedLegacy,
                    typeof(ValidateHL7Transformer), typeof(ValidateHL7ToAckTransformer))) ?? 2;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message); return 1; }
        }
        private static string UnsupportedLegacy(string operation, string json) { throw new InvalidOperationException("Use the v4 DLL or schema 1 bridge."); }
    }

    internal sealed class ValidationMessage : SnapshotMessage, IHL7Message
    {
        private static Assembly runtime;
        private object message;
        internal ValidationMessage(ExtensionMessage value) : base(value) { }
        public override void Dispose() { (message as IDisposable)?.Dispose(); message = null; }
        private object Message
        {
            get
            {
                if (message != null) return message;
                if (runtime == null)
                {
                    string path = Path.Combine(Program.RuntimeDirectory, "HL7SoupWorkflow.dll");
                    if (!File.Exists(path)) throw new BridgeFault("Unavailable", "NotStarted", "The installed version 5 HL7 parser runtime was not found.");
                    AssemblyLoadContext.Default.Resolving += (_, name) =>
                    {
                        string dependency = Path.Combine(Program.RuntimeDirectory, name.Name + ".dll");
                        return File.Exists(dependency) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency) : null;
                    };
                    runtime = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
                    Type environment = runtime.GetType("HL7Soup.Functions.EnvironmentFunctions", true);
                    try { environment.GetProperty("Instance").GetValue(null); }
                    catch (TargetInvocationException)
                    {
                        object withoutUi = Activator.CreateInstance(runtime.GetType("HL7Soup.Functions.EnvironmentFunctionsWithoutUI", true));
                        environment.GetMethod("SetInstance").Invoke(null, new[] { withoutUi });
                    }
                }
                message = Activator.CreateInstance(runtime.GetType("HL7Soup.Functions.HL7V2MessageType", true), new object[] { Text, null });
                return message;
            }
        }
        public string ValidateWithHighlighters(string profileName) { return (string)Call(Message, "ValidateWithHighlighters", new[] { typeof(string) }, new object[] { profileName }); }
        public IHL7Segment GetSegment(string name)
        {
            object segment = Call(Message, "GetSegment", new[] { typeof(string) }, new object[] { name });
            return segment == null ? null : new ValidationSegment(segment);
        }
        private static object Call(object target, string method, Type[] types, object[] args)
        {
            try { return target.GetType().GetMethod(method, types).Invoke(target, args); }
            catch (TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw(); throw; }
        }
        private sealed class ValidationSegment : IHL7Segment
        {
            private readonly object segment;
            internal ValidationSegment(object segment) { this.segment = segment; }
            public string Text => (string)segment.GetType().GetProperty("Text").GetValue(segment);
            public string GetFieldValue(int field) { return (string)Call(segment, "GetFieldValue", new[] { typeof(int) }, new object[] { field }); }
            public string GetComponentValue(int field, int component) { return (string)Call(segment, "GetComponentValue", new[] { typeof(int), typeof(int) }, new object[] { field, component }); }
        }
    }
}
