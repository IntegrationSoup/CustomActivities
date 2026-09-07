using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class Program
{
    private static int checks;
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--server") return Popokey.ExtensionRunners.PersistentRunnerServer.RunIfRequested(args, (operation,json) => {
                string capture = Environment.GetEnvironmentVariable("BRIDGE_FIXTURE_CAPTURE");
                if (string.IsNullOrWhiteSpace(capture)) throw new InvalidOperationException("Missing fixture capture path");
                string typeName = operation == "upload-blob" ? "AzureActivities.BlobSender" : operation == "upload-s3" ? "AmazonActivities.S3Sender" : operation == "upload-sftp" ? "SftpActivities.SftpUpload" : operation == "download-sftp" ? "SftpActivities.SftpDownload" : operation == "convert-rtf-to-pdf" ? "RtfToPdfActivities.RtfToPdfConverter" : throw new InvalidOperationException("Unknown fixture operation");
                capture = Path.Combine(Path.GetDirectoryName(capture), typeName + ".json");
                File.WriteAllText(capture, new JObject { ["Operation"] = operation, ["Payload"] = JObject.Parse(json) }.ToString());
                return FixtureResponse;
            }) ?? 2;
            string root = Path.GetFullPath(args[0]), api = Path.GetFullPath(args[1]);
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
                string file = Path.Combine(Path.GetDirectoryName(api), new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            Assembly.LoadFrom(api);
            TestValues(root);
            TestActivities(root);
            TestExternalBoundaries(root);
            Console.WriteLine("PASS " + checks + " compatibility checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void TestValues(string root)
    {
        Assembly legacy = Assembly.LoadFrom(Path.Combine(root, "HL7ValueTransformers/bin/Release/net472/HL7ValueTransformers.dll"));
        using (var runner = new Runner(Path.Combine(root, "HL7ValueTransformers.Runner/bin/Release/net48/HL7ValueTransformersRunner.exe")))
        {
            JObject catalog = runner.Success("extension.describe", new { SchemaVersion = 1, ProviderId = "" });
            Equal("popokey.hl7valuetransformers", (string)catalog["ProviderId"], "provider");
            var descriptors = (JArray)catalog["Extensions"];
            Equal(7, descriptors.Count, "all transformer types");
            var inputs = new[] { "(021) 555-0100", "z aa-0067", "abc, 123   def!", "Example Patient", "Dr Example Doctor", "1 Example Street, Wollongong NSW 2500", "+64 (21) 555 0100" };
            int index = 0;
            foreach (JObject descriptor in descriptors)
            {
                Type type = legacy.GetType(((string)descriptor["TypeName"]).Split(',')[0], true);
                Equal(type.AssemblyQualifiedName, (string)descriptor["TypeName"], "saved identity");
                CompareMetadata(type, descriptor);
                foreach (var parameters in new[] {
                    new Dictionary<string,string> { ["Value"] = inputs[index] },
                    new Dictionary<string,string> { ["Value"] = inputs[index], ["Output Variable"] = " CustomResult ", ["Name Order"] = "FML", ["Person Identifier"] = "EXAMPLE-123" },
                    new Dictionary<string,string> { ["Value"] = "" },
                    new Dictionary<string,string>() })
                {
                    var expected = LegacyTransform(type, "fallback message 123", parameters);
                    var request = Invocation((string)catalog["ProviderId"], (string)descriptor["TypeName"], "Transformer", "Transform", Guid.NewGuid().ToString("N"), parameters, 13, "fallback message 123", 13, "unchanged response");
                    JObject result = runner.Success("extension.invoke", request);
                    Equal(null, (string)result["Message"], "transform does not replace input");
                    Equal(null, (string)result["ResponseMessage"], "transform does not replace response");
                    var actual = ((JArray)result["VariableUpdates"]).ToDictionary(p => (string)p["Name"], p => (string)p["Value"]);
                    Equal(JsonConvert.SerializeObject(expected), JsonConvert.SerializeObject(actual), type.Name + " legacy/bridge output");
                }
                index++;
            }
            var valid = Invocation((string)catalog["ProviderId"], (string)descriptors[0]["TypeName"], "Transformer", "Transform", Guid.NewGuid().ToString("N"), new Dictionary<string,string> { ["Value"]="123" }, 13,"",13,"");
            var bad = JObject.FromObject(valid); bad["ProviderId"] = "wrong"; runner.Failure("extension.invoke",bad,"UnknownExtension","NotStarted");
            bad = JObject.FromObject(valid); bad["TypeName"] = "Malicious.DigitsOnlyTransformer, Other"; runner.Failure("extension.invoke",bad,"UnknownExtension","NotStarted");
            bad = JObject.FromObject(valid); bad["Phase"] = "Process"; runner.Failure("extension.invoke",bad,"InvalidRequest","NotStarted");
            bad = JObject.FromObject(valid); bad["SchemaVersion"] = 2; runner.Failure("extension.invoke",bad,"VersionUnsupported","NotStarted");
            bad = JObject.FromObject(valid); bad["DeadlineUtc"] = DateTime.UtcNow.AddSeconds(-1).ToString("O"); runner.Failure("extension.invoke",bad,"DeadlineExceeded","NotStarted");
            bad = JObject.FromObject(valid); ((JArray)bad["Parameters"]).Add(new JObject { ["Name"]="Value", ["Value"]="456" }); runner.Failure("extension.invoke",bad,"InvalidRequest","NotStarted");
            string id = Guid.NewGuid().ToString("N"); runner.Success("extension.invoke", valid,id); runner.Failure("extension.invoke",valid,"InvalidRequest","NotStarted",id);
            bad = JObject.FromObject(valid); bad["TypeName"] = ((string)bad["TypeName"]).Replace("Version=1.0.0.0", "Version=4.9.0.0"); runner.Success("extension.invoke",bad);
            runner.Failure("extension.describe",new { SchemaVersion=1,ProviderId="wrong" },"UnknownExtension","NotStarted");
            runner.Failure("extension.unknown",new { SchemaVersion=1 },"InvalidRequest","NotStarted");
            Console.WriteLine("HL7 Value Transformers: all 7 types, 28 v4/v5 result comparisons, metadata, identity/defaults/empty values and negative protocol checks passed.");
        }
    }

    private static void CompareMetadata(Type type, JObject descriptor)
    {
        var attributes = type.GetCustomAttributes(true);
        Equal(attributes.OfType<System.ComponentModel.DisplayNameAttribute>().Single().DisplayName, (string)descriptor["DisplayName"], "display name");
        var parameters = attributes.Where(a=>a.GetType().Name=="ParameterAttribute").ToArray();
        Equal(parameters.Length, ((JArray)descriptor["Parameters"]).Count, "parameter count");
        foreach(var a in parameters)
        {
            var match = descriptor["Parameters"].Single(p=>(string)p["Name"]==(string)a.GetType().GetProperty("Name").GetValue(a));
            foreach(var property in new[]{"Name","Description","IsRequired"}) Equal(JToken.FromObject(a.GetType().GetProperty(property).GetValue(a) ?? "").ToString(), match[property]?.ToString() ?? "", property);
            Equal(null, (string)match["DefaultValue"], "no invented authored default");
            Equal(type.Namespace == "HL7ValueTransformers" && (string)match["Name"] == "Output Variable", (bool)match["IsOutputVariableName"], "resolved output variable annotation");
            object ui = attributes.FirstOrDefault(x=>x.GetType().Name=="ParameterUiAttribute" && (string)x.GetType().GetProperty("ParameterName").GetValue(x)==(string)match["Name"]);
            foreach (string property in new[]{"EditorType","Purpose","ValidationRegex","ValidationMessage"})
            {
                string expectedUi = ui == null ? null : (string)ui.GetType().GetProperty(property).GetValue(ui);
                if ((property == "EditorType" || property == "Purpose") && expectedUi == null) expectedUi = "Text";
                Equal(expectedUi, (string)match[property], "parameter UI " + property);
            }
            string[] options = ui == null ? new string[0] : (string[])ui.GetType().GetProperty("Options").GetValue(ui) ?? new string[0];
            Equal(true, JToken.DeepEquals(JArray.FromObject(options), match["Options"]), "parameter options");
            Equal(true, (bool)match["AllowVariableBinding"], "parameter binding support");
        }
        var variables = attributes.Where(a=>a.GetType().Name=="VariableAttribute").ToArray();
        Equal(variables.Length, ((JArray)descriptor["Variables"]).Count, "variable count");
        foreach(var a in variables)
        {
            string name=(string)a.GetType().GetProperty("Name").GetValue(a);
            Equal((string)a.GetType().GetProperty("SampleValue").GetValue(a),(string)descriptor["Variables"].Single(v=>(string)v["Name"]==name)["Value"],"variable sample");
        }
        foreach (string kind in new[] { "In", "Out" })
        {
            object a = attributes.SingleOrDefault(x => x.GetType().Name == kind + "MessageAttribute");
            if (a == null) { Equal(null, (string)descriptor[kind + "Message"], kind + " absent metadata"); continue; }
            JObject value = (JObject)descriptor[kind + "Message"];
            Equal(Convert.ToInt32(a.GetType().GetProperty("MessageType").GetValue(a)), (int)value["MessageType"], kind + " message type");
            Equal((string)a.GetType().GetProperty(kind == "In" ? "SampleTemplateMessage" : "SampleResponseMessage").GetValue(a), (string)value["SampleMessage"], kind + " sample");
            Equal(kind == "In" ? (bool)a.GetType().GetProperty("UserCanEditTemplate").GetValue(a) : true, (bool)value["UserCanEditTemplate"], kind + " editability");
            if (kind == "Out") Equal(Convert.ToInt32(a.GetType().GetProperty("DefaultMessageType").GetValue(a)), (int)value["DefaultMessageType"], "default response type");
        }
    }

    internal const string FixtureResponse = "{\"Message\":\"fixture response\",\"OutputBase64\":\"Rml4dHVyZSBkb3dubG9hZA==\",\"PdfBase64\":\"Rml4dHVyZSBkb3dubG9hZA==\",\"BytesTransferred\":16,\"RemotePath\":\"/fixture\"}";
    private static void TestExternalBoundaries(string root)
    {
        string folder = Path.Combine(root, "artifacts", "boundary-proof"); Directory.CreateDirectory(folder);
        var parameters = new Dictionary<string,string>{{"Connection String","fixture-connection"},{"Container Name","fixture-container"},{"File Name","fixture.bin"},{"Bucket Name","fixture-bucket"},{"Region","us-west-1"},{"Access Key ID","fixture-access"},{"Secret Access Key","fixture-secret"},{"Host Name","fixture.invalid"},{"User Name","fixture-user"},{"Password","fixture-password"},{"SSH Host Key Fingerprint","fixture-fingerprint"},{"Remote Path","/fixture"},{"Delete Remote File After Download","yes"}};
        foreach (var spec in new[] {
            new[] {"AzureActivities/AzureActivities","AzureActivities.BlobSender","AZUREACTIVITIES_RUNNER_PATH"},
            new[] {"AmazonActivities/AmazonActivities","AmazonActivities.S3Sender","AWSACTIVITIES_RUNNER_PATH"},
            new[] {"SftpActivities","SftpActivities.SftpUpload","SFTPACTIVITIES_RUNNER_PATH"},
            new[] {"SftpActivities","SftpActivities.SftpDownload","SFTPACTIVITIES_RUNNER_PATH"},
            new[] {"RtfToPdfActivities","RtfToPdfActivities.RtfToPdfConverter","RTFTOPDF_RENDERER_PATH"} })
        {
            string capture = Path.Combine(folder, spec[1]+".json");
            string childFolder = Path.Combine(folder, spec[1]); Directory.CreateDirectory(childFolder);
            string childExecutable = Path.Combine(childFolder,"BridgeCompatibility.Tests.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location,childExecutable,true);
            File.Copy(typeof(JObject).Assembly.Location,Path.Combine(childFolder,"Newtonsoft.Json.dll"),true);
            Environment.SetEnvironmentVariable("BRIDGE_FIXTURE_CAPTURE",capture);
            Environment.SetEnvironmentVariable(spec[2],childExecutable);
            string assemblyName=spec[0].Split('/').Last();
            Type type=Assembly.LoadFrom(Path.Combine(root,spec[0],"bin/Release/net48",assemblyName+".dll")).GetType(spec[1],true);
            string result=LegacyProcess(type,"{\\rtf1 Fixture upload}",parameters);
            // Each adapter's static persistent client can retain its child. Capture
            // the returned boundary record under its type even when a sibling shares it.
            Equal(true,!string.IsNullOrWhiteSpace(result),"v4 mocked external result");
            Equal(true,File.Exists(capture),"v4 captured original operation payload");
        }
        Console.WriteLine("Unchanged v4 Azure/AWS/SFTP/RTF adapters completed against a synthetic legacy business boundary; captured payloads are compared by BridgeRuntime.Tests. No external service/converter was invoked by this fixture.");
    }

    private static void TestActivities(string root)
    {
        string fixture = Path.Combine(root, "artifacts", "bridge-fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        string source = Path.Combine(fixture, "source"); Directory.CreateDirectory(source); File.WriteAllText(Path.Combine(source,"sample.txt"),"Synthetic ZIP content",new UTF8Encoding(false));
        string pdf = Convert.ToBase64String(SyntheticPdf());
        File.WriteAllBytes(Path.Combine(fixture,"input.pdf"),Convert.FromBase64String(pdf));
        foreach (var spec in new[] {
            new[] { "DataFromPdfActivities", "DataFromPdfActivities.Runner", "DataFromPdfRunner", "DATAFROMPDF_RUNNER_PATH" },
            new[] { "ZipActivities", "ZipActivities.Runner", "ZipActivitiesRunner", "ZIPACTIVITIES_RUNNER_PATH" },
            new[] { "HtmlToPdfActivities", "HtmlToPdfActivities.Renderer", "HtmlToPdfRenderer", "HTMLTOPDF_RENDERER_PATH" },
            new[] { "RtfToPdfActivities", "RtfToPdfActivities.Renderer", "RtfToPdfRenderer", "RTFTOPDF_RENDERER_PATH" },
            new[] { "AzureActivities/AzureActivities", "AzureActivities.Runner", "AzureActivitiesRunner", "AZUREACTIVITIES_RUNNER_PATH" },
            new[] { "AmazonActivities/AmazonActivities", "AmazonActivities.Runner", "AwsActivitiesRunner", "AWSACTIVITIES_RUNNER_PATH" },
            new[] { "HL7SoupEncryptionActivities/HL7SoupEncryptionActivities", "HL7SoupEncryptionActivities.Runner", "EncryptionActivitiesRunner", "ENCRYPTIONACTIVITIES_RUNNER_PATH" },
            new[] { "ValidateTransformer", "ValidateTransformer.Runner", "ValidateTransformerRunner", "BRIDGE_VALIDATE_FIXTURE_PATH" },
            new[] { "SftpActivities", "SftpActivities.Runner", "SftpActivitiesRunner", "SFTPACTIVITIES_RUNNER_PATH" } })
        {
            string assemblyName = spec[0].Split('/').Last();
            Assembly legacy = Assembly.LoadFrom(Path.Combine(root,spec[0],assemblyName == "ValidateTransformer" ? "bin/Release/netstandard2.0" : "bin/Release/net48",assemblyName+".dll"));
            string exe=Path.Combine(root,spec[1],assemblyName == "ValidateTransformer" ? "bin/Release/net10.0-windows" : "bin/Release/net48",spec[2]+".exe");
            Environment.SetEnvironmentVariable(spec[3],exe);
            using(var runner=new Runner(exe))
            {
                JObject catalog=runner.Success("extension.describe",new {SchemaVersion=1,ProviderId=""});
                foreach(JObject descriptor in (JArray)catalog["Extensions"])
                {
                    Type type=legacy.GetType(((string)descriptor["TypeName"]).Split(',')[0],true);
                    Equal(type.AssemblyQualifiedName,(string)descriptor["TypeName"],"activity saved identity"); CompareMetadata(type,descriptor);
                    if(type.Name=="EncryptMessage") { TestEncryption(runner,catalog,legacy); continue; }
                    if(type.Name=="HtmlToPdfConverter") { TestHtml(root,runner,catalog,type,descriptor); continue; }
                    var parameters=new Dictionary<string,string>(); int inputType=13,responseType=13; string input="";
                    if(assemblyName=="DataFromPdfActivities") {input=pdf;inputType=14;responseType=11;}
                    else if(type.Name=="CreateZipMessage") {parameters["Source Directory"]=source;parameters["Include Base Directory"]="yes";responseType=14;}
                    else if(type.Name=="CreateZipFile") {parameters["Source Directory"]=source;parameters["ZIP File Path"]=Path.Combine(fixture,"sample.zip");parameters["Overwrite Existing File"]="on";}
                    else if(type.Name=="ExtractZipFile") {parameters["ZIP File Path"]=Path.Combine(fixture,"sample.zip");parameters["Destination Directory"]=Path.Combine(fixture,"extracted");parameters["Overwrite Existing Files"]="1";}
                    else continue; // These providers have metadata proof here; separate safe operation fixtures follow.
                    string expected=LegacyProcess(type,input,parameters);
                    string session=Guid.NewGuid().ToString("N");
                    Func<string,object> request=phase=>Invocation((string)catalog["ProviderId"],(string)descriptor["TypeName"],"Activity",phase,session,parameters,inputType,input,responseType,"");
                    runner.Failure("extension.invoke",request("Process"),"UnknownSession","NotStarted");
                    runner.Success("extension.invoke",request("Prepare"));
                    runner.Failure("extension.invoke",request("Prepare"),"InvalidRequest","NotStarted");
                    JObject result=runner.Success("extension.invoke",request("Process"));
                    Equal(expected,(string)result["ResponseMessage"]["Text"],type.Name+" actual v4/v5 response");
                    Equal(responseType,(int)result["ResponseMessage"]["MessageType"],"preserve response kind");
                    runner.Success("extension.invoke",request("AllFunctionsSucceeded")); runner.Success("extension.invoke",request("RollBack")); runner.Success("extension.invoke",request("Close"));
                    runner.Failure("extension.invoke",request("Process"),"UnknownSession","NotStarted");
                    if(assemblyName=="DataFromPdfActivities") Equal(true,expected.Contains("Bridge fixture patient"),"real PDF extraction");
                }
                Console.WriteLine(assemblyName+": exact v4 descriptor comparison passed"+(assemblyName=="DataFromPdfActivities"||assemblyName=="ZipActivities"?"; real operation and lifecycle comparisons passed.":"; business I/O not exercised by this metadata test."));
            }
        }
        Equal("Synthetic ZIP content",File.ReadAllText(Path.Combine(fixture,"extracted","sample.txt")),"ZIP extracted fixture");
    }

    private static string LegacyProcess(Type type,string text,Dictionary<string,string> parameters)
    {
        var method=type.GetMethod("Process"); Type activityType=method.GetParameters()[1].ParameterType;
        string response="";
        Type messageType=activityType.GetProperty("Message").PropertyType;
        var message=new InterfaceProxy(messageType,(name,args)=>name=="get_Text"?text:null).GetTransparentProxy();
        var output=new InterfaceProxy(messageType,(name,args)=>{if(name=="get_Text")return response;if(name=="SetText")response=(string)args[0];return null;}).GetTransparentProxy();
        var activity=new InterfaceProxy(activityType,(name,args)=>name=="get_Message"?message:name=="get_ResponseMessage"?output:null).GetTransparentProxy();
        method.Invoke(Activator.CreateInstance(type),new object[]{null,activity,parameters});
        return response;
    }

    private static JObject Process(Runner runner,JObject catalog,JObject descriptor,string text,int inputType,int outputType,Dictionary<string,string> parameters)
    {
        string session=Guid.NewGuid().ToString("N");
        runner.Success("extension.invoke",Invocation((string)catalog["ProviderId"],(string)descriptor["TypeName"],"Activity","Prepare",session,parameters,inputType,text,outputType,""));
        JObject result=runner.Success("extension.invoke",Invocation((string)catalog["ProviderId"],(string)descriptor["TypeName"],"Activity","Process",session,parameters,inputType,text,outputType,""));
        runner.Success("extension.invoke",Invocation((string)catalog["ProviderId"],(string)descriptor["TypeName"],"Activity","Close",session,parameters,inputType,text,outputType,""));
        return result;
    }
    private static void TestEncryption(Runner runner,JObject catalog,Assembly legacy)
    {
        var parameters=new Dictionary<string,string>{{"Encryption Key","fixture-key-at-least-12"}};
        string clear="Synthetic Unicode message: café 日本語";
        JObject encrypt=(JObject)catalog["Extensions"].Single(d=>((string)d["TypeName"]).Contains(".EncryptMessage,"));
        JObject decrypt=(JObject)catalog["Extensions"].Single(d=>((string)d["TypeName"]).Contains(".DecryptMessage,"));
        string v4Cipher=LegacyProcess(legacy.GetType("HL7SoupEncryptionActivities.EncryptMessage"),clear,parameters);
        Equal(clear,(string)Process(runner,catalog,decrypt,v4Cipher,13,13,parameters)["ResponseMessage"]["Text"],"v4 encryption/v5 decryption");
        string v5Cipher=(string)Process(runner,catalog,encrypt,clear,13,13,parameters)["ResponseMessage"]["Text"];
        Equal(clear,LegacyProcess(legacy.GetType("HL7SoupEncryptionActivities.DecryptMessage"),v5Cipher,parameters),"v5 encryption/v4 decryption");
        Console.WriteLine("Encryption: real v4/v5 cross-decryption with Unicode fixture passed.");
    }
    private static void TestHtml(string root,Runner runner,JObject catalog,Type type,JObject descriptor)
    {
        string html="<html><body><p>Bridge fixture patient</p></body></html>";
        var parameters=new Dictionary<string,string>();
        string v4=LegacyProcess(type,html,parameters);
        string v5=(string)Process(runner,catalog,descriptor,html,13,14,parameters)["ResponseMessage"]["Text"];
        using(var pdf=new Runner(Path.Combine(root,"DataFromPdfActivities.Runner/bin/Release/net48/DataFromPdfRunner.exe")))
        {
            JObject pdfCatalog=pdf.Success("extension.describe",new{SchemaVersion=1,ProviderId=""});JObject pdfDescriptor=(JObject)pdfCatalog["Extensions"][0];
            foreach(string bytes in new[]{v4,v5})
            {
                string extracted=(string)Process(pdf,pdfCatalog,pdfDescriptor,bytes,14,11,parameters)["ResponseMessage"]["Text"];
                Equal(true,extracted.Contains("Bridge fixture patient"),"real HTML PDF text");
            }
        }
        Console.WriteLine("HTML: unchanged v4 adapter and v5 runner rendered real PDFs with matching extracted fixture text.");
    }

    private static byte[] SyntheticPdf()
    {
        string text="BT /F1 12 Tf 72 720 Td (Bridge fixture patient) Tj ET";
        var objects=new[]{"<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>","<< /Length "+text.Length+" >>\nstream\n"+text+"\nendstream"};
        var builder=new StringBuilder("%PDF-1.4\n");var offsets=new List<int>();for(int i=0;i<objects.Length;i++){offsets.Add(builder.Length);builder.Append((i+1)+" 0 obj\n"+objects[i]+"\nendobj\n");}
        int xref=builder.Length;builder.Append("xref\n0 6\n0000000000 65535 f \n");foreach(int offset in offsets)builder.Append(offset.ToString("D10")+" 00000 n \n");builder.Append("trailer\n<< /Root 1 0 R /Size 6 >>\nstartxref\n"+xref+"\n%%EOF\n");return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static Dictionary<string,string> LegacyTransform(Type type,string text,Dictionary<string,string> parameters)
    {
        var result = new Dictionary<string,string>();
        MethodInfo method=type.GetMethod("Transform");
        var workflow = new InterfaceProxy(method.GetParameters()[0].ParameterType, (name,args)=> { if(name=="SetVariable") { result[(string)args[0]]=(string)args[1]; return null; } throw new NotSupportedException(name); }).GetTransparentProxy();
        var message = new InterfaceProxy(method.GetParameters()[1].ParameterType, (name,args)=> { if(name=="get_Text") return text; if(name=="Dispose")return null; throw new NotSupportedException(name); }).GetTransparentProxy();
        method.Invoke(Activator.CreateInstance(type),new[]{workflow,message,parameters});
        return result;
    }

    private static object Invocation(string provider,string type,string kind,string phase,string session,Dictionary<string,string> parameters,int messageType,string text,int responseType,string response)
    {
        return new { SchemaVersion=1, ProviderId=provider, TypeName=type, Kind=kind, Phase=phase, SessionId=session, DeadlineUtc=DateTime.UtcNow.AddSeconds(30).ToString("O"),
            Parameters=parameters.Select(p=>new { Name=p.Key,Value=p.Value }).ToArray(), Message=new {MessageType=messageType,Text=text},ResponseMessage=new{MessageType=responseType,Text=response},
            Context=new{WorkflowId=Guid.NewGuid().ToString(),InstanceId=1,ActivityId=Guid.NewGuid().ToString(),ActivityName="Synthetic fixture",Variables=new object[0],Activities=new object[0]} };
    }
    private static void Equal(object expected,object actual,string name) { if(!object.Equals(expected,actual))throw new Exception(name+": expected "+expected+", actual "+actual); checks++; }

    private sealed class InterfaceProxy : RealProxy
    {
        private readonly Func<string,object[],object> invoke;
        private readonly Type interfaceType;
        internal InterfaceProxy(Type type,Func<string,object[],object> invoke):base(type){this.invoke=invoke;interfaceType=type;}
        public override IMessage Invoke(IMessage message)
        {
            var call=(IMethodCallMessage)message;
            try{return new ReturnMessage(call.MethodName == "GetType" ? interfaceType : invoke(call.MethodName,call.Args),null,0,call.LogicalCallContext,call);}
            catch(Exception ex){return new ReturnMessage(ex,call);}
        }
    }

    private sealed class Runner : IDisposable
    {
        internal readonly string PipeName="bridge-proof-"+Guid.NewGuid().ToString("N");
        private readonly Process process;
        internal Runner(string exe)
        {
            process=System.Diagnostics.Process.Start(new ProcessStartInfo(exe,"--server --pipe-name "+PipeName+" --parent-pid "+System.Diagnostics.Process.GetCurrentProcess().Id+" --bridge-version 1") {UseShellExecute=false,CreateNoWindow=true});
        }
        internal JObject Success(string operation,object payload,string id=null)
        {
            JObject result=Call(operation,payload,id);
            if(!(bool)result["Success"])throw new Exception(result.ToString());
            return JObject.Parse((string)result["PayloadJson"]);
        }
        internal void Failure(string operation,object payload,string code,string state,string id=null)
        {
            JObject response=Call(operation,payload,id); Equal(false,(bool)response["Success"],"failure");
            JObject error=JObject.Parse((string)response["PayloadJson"]); Equal(code,(string)error["Code"],"failure code"); Equal(state,(string)error["ExecutionState"],"execution state");
        }
        private JObject Call(string operation,object payload,string id)
        {
            using(var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut))
            {
                pipe.Connect(10000);
                var bytes=Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new {ProtocolVersion=1,RequestId=id??Guid.NewGuid().ToString("N"),Operation=operation,PayloadJson=JsonConvert.SerializeObject(payload)}));
                pipe.Write(BitConverter.GetBytes(bytes.Length),0,4);pipe.Write(bytes,0,bytes.Length);pipe.Flush();
                int length=BitConverter.ToInt32(Read(pipe,4),0);if(length<=0||length>64*1024*1024)throw new Exception("Invalid response length");
                return JObject.Parse(Encoding.UTF8.GetString(Read(pipe,length)));
            }
        }
        private static byte[] Read(Stream stream,int length){byte[] bytes=new byte[length];for(int offset=0;offset<length;){int read=stream.Read(bytes,offset,length-offset);if(read==0)throw new EndOfStreamException();offset+=read;}return bytes;}
        public void Dispose(){if(!process.HasExited){process.Kill();process.WaitForExit(10000);}process.Dispose();}
    }
}
