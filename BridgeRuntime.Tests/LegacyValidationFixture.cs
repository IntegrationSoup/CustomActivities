using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HL7Soup.Integrations.ExtensionBridge;
using Newtonsoft.Json.Linq;

// Loads the unchanged netstandard v4 DLL against the actual host API/parser in
// the test process. No adapter DLL is loaded by the production v5 host bridge.
internal static class LegacyValidationFixture
{
    internal static void Compare(Assembly runtime, Type sourceType, ExtensionInvokeRequest request, ExtensionInvokeResult actual)
    {
        string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../ValidateTransformer/bin/Release/netstandard2.0/ValidateTransformer.dll"));
        Type legacy = Assembly.LoadFrom(path).GetType(sourceType.FullName, true);
        MethodInfo method = legacy.GetMethod("Process");
        Type workflowApi = method.GetParameters()[0].ParameterType, activityApi = method.GetParameters()[1].ParameterType;
        object input = Activator.CreateInstance(runtime.GetType("HL7Soup.Functions.HL7V2MessageType", true), new object[] { request.Message.Text, null });
        object output = Activator.CreateInstance(runtime.GetType(request.ResponseMessage.MessageType == 1 ? "HL7Soup.Functions.HL7V2MessageType" : "HL7Soup.Functions.JSONMessage", true), new object[] { "", null });
        try
        {
            var activity = (ValidationProxy)DispatchProxy.Create(activityApi, typeof(ValidationProxy));
            activity.Handle = (name,args) => name == "get_Message" ? input : name == "get_ResponseMessage" ? output : null;
            var workflow = (ValidationProxy)DispatchProxy.Create(workflowApi, typeof(ValidationProxy));
            var updates = new Dictionary<string,string>();
            workflow.Handle = (name,args) =>
            {
                if (name == "get_CurrentActivityInstance") return activity;
                if (name == "SetVariable") { updates[(string)args[0]] = (string)args[1]; return null; }
                return null;
            };
            var parameters = new Dictionary<string,string>(); foreach (var pair in request.Parameters) parameters.Add(pair.Name, pair.Value);
            method.Invoke(Activator.CreateInstance(legacy), new object[] { workflow, activity, parameters });
            string expected = (string)output.GetType().GetProperty("Text").GetValue(output);
            bool equal = request.ResponseMessage.MessageType == 11 ? JToken.DeepEquals(JToken.Parse(expected), JToken.Parse(actual.ResponseMessage.Text)) : expected == actual.ResponseMessage.Text;
            if (!equal || workflow.Error != actual.WorkflowError || workflow.Promoted != actual.PromoteResponseToWorkflow) throw new Exception("Unchanged Validate DLL disagrees with bridge result/error/promotion");
            if (updates.Count != actual.VariableUpdates.Count) throw new Exception("Validation variable count differs");
            foreach (var pair in actual.VariableUpdates) if (!updates.TryGetValue(pair.Name, out string value) || value != pair.Value) throw new Exception("Validation variable differs");
        }
        finally { (input as IDisposable)?.Dispose(); (output as IDisposable)?.Dispose(); }
    }
}

public class ValidationProxy : DispatchProxy
{
    internal Func<string,object[],object> Handle;
    internal bool Promoted;
    internal string Error;
    public void SetReponseMessage(object message) { Promoted = true; }
    public void Errored(string message) { Error = message; }
    protected override object Invoke(MethodInfo method, object[] args) => Handle(method.Name, args);
}
