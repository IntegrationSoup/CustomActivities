using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HL7Soup.Integrations;
using HL7Soup.Integrations.ExtensionBridge;
using Newtonsoft.Json.Linq;
using Popokey.ExtensionRunners;

internal static class RuntimeTests
{
    private static int checks;
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--fixture-parent") { Thread.Sleep(Timeout.Infinite); return 0; }
            if (args.Length > 0 && args[0] == "--server") return BridgeRunner.RunIfRequested(args, (op,json)=>throw new NotSupportedException(), ()=>Provider(typeof(Probe))) ?? 2;
            TestTransport();
            TransportFixture.Run();
            TestCancellation();
            TestNetworkBoundary();
            if(args.Length>0) TestValidation(Path.GetFullPath(args[0]));
            Console.WriteLine("PASS " + checks + " shared runtime/validation checks");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static void TestTransport()
    {
        foreach(int length in new[]{0,-1,1025}) Throws(()=>BridgeWire.ReadFrame(new MemoryStream(BitConverter.GetBytes(length)),1024),"frame length");
        Throws(()=>BridgeWire.ReadFrame(new MemoryStream(new byte[]{4,0,0,0,1}),1024),"truncated frame");
        Throws(()=>BridgeWire.WriteFrame(new MemoryStream(),new byte[1025],1024),"write bound");
        var provider=Provider(typeof(Probe));
        var request=Envelope("extension.describe",new ExtensionDescribeRequest { ProviderId="fixture" });
        var json=Encoding.UTF8.GetBytes(PersistentRunnerJson.Serialize(request));
        using(var stream=new MemoryStream()){BridgeWire.WriteFrame(stream,json,1024);stream.Position=0;Equal(true,json.SequenceEqual(BridgeWire.ReadFrame(stream,1024)),"frame roundtrip");}
        var response=PersistentRunnerJson.Deserialize<ExtensionResponseEnvelope>(Encoding.UTF8.GetString(BridgeWire.DispatchJson(json,provider,65536)));
        Equal(true,response.Success,"HTTP-hostable unframed envelope");
        response=PersistentRunnerJson.Deserialize<ExtensionResponseEnvelope>(Encoding.UTF8.GetString(BridgeWire.DispatchJson(Encoding.UTF8.GetBytes("{\"ProtocolVersion\":1,\"ProtocolVersion\":2}"),provider,65536)));
        Equal(false,response.Success,"duplicate envelope members");
        var duplicatePayload = provider.Dispatch(new ExtensionRequestEnvelope { RequestId = "duplicate-inner", Operation = "extension.describe", PayloadJson = "{\"SchemaVersion\":1,\"SchemaVersion\":1,\"ProviderId\":\"fixture\"}" });
        Equal(false,duplicatePayload.Success,"duplicate inner members");
        response=PersistentRunnerJson.Deserialize<ExtensionResponseEnvelope>(Encoding.UTF8.GetString(BridgeWire.DispatchJson(new byte[]{0xff},provider,65536)));
        Equal(false,response.Success,"invalid UTF-8");
        var rules=BridgeRunner.CreateSecurity().GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<PipeAccessRule>().ToList();
        Equal(true,BridgeRunner.CreateSecurity().AreAccessRulesProtected,"protected pipe DACL");
        var allowed=rules.Where(r=>r.AccessControlType==AccessControlType.Allow).Select(r=>r.IdentityReference.Value).Distinct().ToList();
        Equal(true,allowed.All(s=>s==WindowsIdentity.GetCurrent().User.Value || s=="S-1-5-18"),"only launch SID and SYSTEM allowed");
        Equal(true,rules.Any(r=>r.IdentityReference.Value=="S-1-5-2"&&r.AccessControlType==AccessControlType.Deny),"network SID denied");
        Console.WriteLine("Transport: exact frame bounds/truncation, JSON envelope seam and launch-identity/SYSTEM ACL verified.");
    }
    private static void TestCancellation()
    {
        var provider=Provider(typeof(Probe));
        string type=Describe(provider).Extensions[0].TypeName;
        Success(provider,Request(type,"Prepare"));
        var process=Envelope("extension.invoke",Request(type,"Process"));
        Probe.Started.Reset();
        Task<ExtensionResponseEnvelope> task=Task.Run(()=>provider.Dispatch(process));
        if(!Probe.Started.Wait(5000))throw new Exception("Probe did not start");
        var watch=Stopwatch.StartNew();
        var cancel=provider.Dispatch(Envelope("extension.cancel",new ExtensionCancelRequest{ProviderId="fixture",SessionId="session",TargetRequestId=process.RequestId}),true);
        Equal(true,cancel.Success,"cancel ack");Equal(true,watch.ElapsedMilliseconds<300,"cancellation bypasses business lock");
        var result=task.GetAwaiter().GetResult();var failure=PersistentRunnerJson.Deserialize<ExtensionFailure>(result.PayloadJson);
        Equal("Cancelled",failure.Code,"cancel result discarded");Equal("Unknown",failure.ExecutionState,"no guarantee of external effects");
        Equal(1,Probe.Effects,"business may finish after ack");
        result=provider.Dispatch(Envelope("extension.invoke",Request(type,"Process")));
        Equal("UnknownSession",PersistentRunnerJson.Deserialize<ExtensionFailure>(result.PayloadJson).Code,"cancel invalidates session");
        Console.WriteLine("Cancellation: independent dispatch acknowledged before slow work completed, discarded result, preserved uncertain effects and invalidated session.");
    }
    private static void TestNetworkBoundary()
    {
        foreach(Type type in new[]{typeof(AzureActivities.BlobSender),typeof(AmazonActivities.S3Sender),typeof(SftpActivities.SftpUpload),typeof(SftpActivities.SftpDownload),typeof(RtfToPdfActivities.RtfToPdfConverter)})
        {
            JObject captured=null;string operation=null;
            PersistentRunnerRequestHandler boundary=(op,json)=>{operation=op;captured=JObject.Parse(json);return "{\"Message\":\"fixture response\",\"OutputBase64\":\"Rml4dHVyZSBkb3dubG9hZA==\",\"PdfBase64\":\"Rml4dHVyZSBkb3dubG9hZA==\",\"BytesTransferred\":16,\"RemotePath\":\"/fixture\"}";};
            var provider=new BridgeProvider("fixture","Fixture",boundary,type);string name=Describe(provider).Extensions[0].TypeName;
            var request=Request(name,"Prepare"); Success(provider,request);
            request.Phase="Process";request.Message=new ExtensionMessage{MessageType=14,Text=Convert.ToBase64String(new byte[]{0,1,128,255})};request.ResponseMessage=new ExtensionMessage{MessageType=14,Text=""};
            request.Parameters=new Dictionary<string,string>{{"Connection String","fixture-connection"},{"Container Name","fixture-container"},{"File Name","fixture.bin"},{"Bucket Name","fixture-bucket"},{"Region","us-west-1"},{"Access Key ID","fixture-access"},{"Secret Access Key","fixture-secret"},{"Host Name","fixture.invalid"},{"User Name","fixture-user"},{"Password","fixture-password"},{"SSH Host Key Fingerprint","fixture-fingerprint"},{"Remote Path","/fixture"},{"Delete Remote File After Download","yes"}}.Select(p=>new ExtensionNamedValue{Name=p.Key,Value=p.Value}).ToList();
            var result=Success(provider,request);
            Equal(true,captured!=null,"source adapter called network boundary");
            if(type==typeof(SftpActivities.SftpDownload)){Equal("Rml4dHVyZSBkb3dubG9hZA==",result.ResponseMessage.Text,"binary download");Equal(true,(bool)captured["DeleteRemoteFileAfterDownload"],"delete flag mapping");}
            else if (type == typeof(RtfToPdfActivities.RtfToPdfConverter)) Equal("Rml4dHVyZSBkb3dubG9hZA==", result.ResponseMessage.Text, "RTF boundary PDF bytes preserved");
            else Equal("AAGA/w==",(string)captured["InputBase64"],"binary upload mapping");
            Equal(true,!string.IsNullOrWhiteSpace(operation),"legacy operation selected");
            request.Message = new ExtensionMessage { MessageType = 13, Text = "{\\rtf1 Fixture upload}" };
            Success(provider,request);
            string baselinePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../artifacts/boundary-proof",type.FullName+".json"));
            JObject baseline = JObject.Parse(File.ReadAllText(baselinePath));
            Equal((string)baseline["Operation"], operation, "v4/v5 external operation identifier");
            Equal(true,JToken.DeepEquals(baseline["Payload"],captured),"unchanged v4/v5 business boundary payload");
        }
        Console.WriteLine("Azure/AWS/SFTP/RTF: unchanged v4/v5 operation payloads agree at a synthetic business boundary; binary/result mapping also verified. No remote service or LibreOffice was contacted.");
    }

    private static void TestValidation(string directory)
    {
        ValidateTransformer.Runner.Program.RuntimeDirectory=directory;
        AssemblyLoadContext.Default.Resolving+=(_,name)=>{string path=Path.Combine(directory,name.Name+".dll");return File.Exists(path)?AssemblyLoadContext.Default.LoadFromAssemblyPath(path):null;};
        Assembly runtime=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory,"HL7SoupWorkflow.dll"));
        object environment=Activator.CreateInstance(runtime.GetType("HL7Soup.Functions.EnvironmentFunctionsWithoutUI",true));
        string fixtureDirectory=Path.Combine(Path.GetTempPath(),"bridge-validation-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtureDirectory);Set(environment,"ProgramDataPath",fixtureDirectory);Set(environment,"LocalApplicationDataPath",fixtureDirectory);Set(environment,"ApplicationDataPath",fixtureDirectory);
        runtime.GetType("HL7Soup.Functions.EnvironmentFunctions",true).GetMethod("SetInstance").Invoke(null,new[]{environment});
        Type managerType=runtime.GetType("HL7Soup.MessageHighlighters.ValidationManager",true);
        object manager=Activator.CreateInstance(managerType,true);
        managerType.GetField("instance",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,manager);
        var profiles=(IDictionary)managerType.GetField("LoadedHighlightersCollection",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(manager);
        Type collectionType=profiles.GetType().GetGenericArguments()[1];
        object valid=Activator.CreateInstance(collectionType);profiles.Add("bridge-fixture-valid",valid);
        object invalid=Activator.CreateInstance(collectionType);
        object highlighter=Activator.CreateInstance(runtime.GetType("HL7Soup.MessageHighlighters.MessageHighlighter",true));
        object pathSplitter=Activator.CreateInstance(runtime.GetType("HL7Soup.PathSplitter",true),new object[]{"PID-7"});
        Set(highlighter,"Path",pathSplitter);Set(highlighter,"ColorName","Red (Invalid)");Set(highlighter,"InvalidatesMessage",true);
        object filter=Activator.CreateInstance(runtime.GetType("HL7Soup.MessageFilters.DateMessageFilter",true));
        Set(filter,"Path","PID-7");var comparer=filter.GetType().GetProperty("Comparer");comparer.SetValue(filter,Enum.Parse(comparer.PropertyType,"InvalidDate"));
        var from=filter.GetType().GetProperty("FromType");from.SetValue(filter,Enum.Parse(from.PropertyType,"HL7V2"));Set(highlighter,"MessageFilter",filter);
        ((IList)collectionType.GetProperty("Highlighters").GetValue(invalid)).Add(highlighter);profiles.Add("bridge-fixture-invalid",invalid);
        SnapshotMessage.TypedFactory=value=>value.MessageType==1?new ValidateTransformer.Runner.ValidationMessage(value):value.MessageType==11?new SnapshotJsonMessage(value):new SnapshotMessage(value);
        foreach(Type type in new[]{typeof(ValidateTransformer.ValidateHL7Transformer),typeof(ValidateTransformer.ValidateHL7ToAckTransformer)})
        {
            var provider=Provider(type);string name=Describe(provider).Extensions[0].TypeName;
            foreach(string profile in new[]{"bridge-fixture-valid","bridge-fixture-invalid"})
            {
                var request=Request(name,"Prepare");request.SessionId=Guid.NewGuid().ToString("N");Success(provider,request);
                request.Phase="Process";request.Message=new ExtensionMessage{MessageType=1,Text="MSH|^~\\&|Fixture|Sender|Fixture|Receiver|20260907120000||ADT^A01|BRIDGE1|P|2.5.1\rPID|1||123||Example^Patient||notdate|U\r"};request.ResponseMessage=new ExtensionMessage{MessageType=type==typeof(ValidateTransformer.ValidateHL7Transformer)?11:1,Text=""};
                request.Parameters=new List<ExtensionNamedValue>{new ExtensionNamedValue{Name="Profile",Value=profile},new ExtensionNamedValue{Name="Error if invalid",Value="true"}};
                var result=Success(provider,request);
                LegacyValidationFixture.Compare(runtime, type, request, result);
                Equal(true,!string.IsNullOrWhiteSpace(result.ResponseMessage?.Text),"validation response");
                Equal(type == typeof(ValidateTransformer.ValidateHL7ToAckTransformer) && profile.EndsWith("invalid"), result.PromoteResponseToWorkflow, "only invalid ACK error promotes workflow response");
                Equal(type == typeof(ValidateTransformer.ValidateHL7ToAckTransformer), Describe(provider).Extensions[0].RequiredContextCapabilities.Contains(ExtensionBridgeProtocol.WorkflowResponseCapability), "ACK promotion capability");
                if(profile.EndsWith("invalid")){Equal(true,!string.IsNullOrWhiteSpace(result.WorkflowError),"explicit workflow error");Equal("true",result.VariableUpdates.Single(v=>v.Name=="WORKFLOWERROR").Value,"WORKFLOWERROR flag");}
                else Equal(null,result.WorkflowError,"valid message no error");
            }
        }
        Console.WriteLine("Validation: unchanged v4 DLL and bridge agree on JSON/ACK, variables, error and promotion using the real parser/highlighter with synthetic in-memory valid/invalid profiles; no profile files or user settings changed.");
    }
    private static void Set(object target,string name,object value){target.GetType().GetProperty(name).SetValue(target,value);}
    private static BridgeProvider Provider(params Type[] types){return new BridgeProvider("fixture","Fixture",(op,json)=>throw new NotSupportedException(),types);}
    private static ExtensionDescribeResult Describe(BridgeProvider provider){var r=provider.Dispatch(Envelope("extension.describe",new ExtensionDescribeRequest{ProviderId="fixture"}));return PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(r.PayloadJson);}
    private static ExtensionRequestEnvelope Envelope<T>(string operation,T payload){return new ExtensionRequestEnvelope{RequestId=Guid.NewGuid().ToString("N"),Operation=operation,PayloadJson=PersistentRunnerJson.Serialize(payload)};}
    private static ExtensionInvokeRequest Request(string type,string phase){return new ExtensionInvokeRequest{ProviderId="fixture",TypeName=type,Kind="Activity",Phase=phase,SessionId="session",DeadlineUtc=DateTime.UtcNow.AddSeconds(20).ToString("O"),Message=new ExtensionMessage{MessageType=13,Text="fixture"},ResponseMessage=new ExtensionMessage{MessageType=13,Text=""},Context=new ExtensionInvocationContext()};}
    private static ExtensionInvokeResult Success(BridgeProvider provider,ExtensionInvokeRequest request){var r=provider.Dispatch(Envelope("extension.invoke",request));if(!r.Success)throw new Exception(r.PayloadJson);return PersistentRunnerJson.Deserialize<ExtensionInvokeResult>(r.PayloadJson);}
    private static void Equal(object expected,object actual,string name){if(!object.Equals(expected,actual))throw new Exception(name+": expected "+expected+", got "+actual);checks++;}
    private static void Throws(Action action,string name){try{action();}catch{checks++;return;}throw new Exception("Expected rejection: "+name);}
    public sealed class Probe : CustomActivity
    {
        internal static readonly ManualResetEventSlim Started=new ManualResetEventSlim();internal static int Effects;
        public override void Process(IWorkflowInstance workflow,IActivityInstance activity,Dictionary<string,string> parameters){Console.WriteLine("PROBE_STARTED");Started.Set();Thread.Sleep(1000);Effects++;activity.ResponseMessage.SetText("done");}
    }
}
