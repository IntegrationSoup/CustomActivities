using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using HL7Soup.Integrations;
using HL7Soup.Integrations.ExtensionBridge;

namespace Popokey.ExtensionRunners
{
    internal sealed class BridgeFault : Exception
    {
        internal BridgeFault(string code, string state, string message) : base(message) { Code = code; State = state; }
        internal string Code { get; }
        internal string State { get; }
        internal static BridgeFault Invalid(string message) { return new BridgeFault("InvalidRequest", "NotStarted", message); }
    }

    internal sealed class BridgeProvider
    {
        private readonly string providerId;
        private readonly ExtensionDescribeResult catalog;
        private readonly Dictionary<string, Type> types = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly Dictionary<string, Session> sessions = new Dictionary<string, Session>(StringComparer.Ordinal);
        private readonly HashSet<string> dispatched = new HashSet<string>(StringComparer.Ordinal);
        private readonly object businessGate = new object();
        private readonly object controlGate = new object();
        private ActiveCall active;
        private readonly PersistentRunnerRequestHandler legacy;

        internal BridgeProvider(string id, string assemblyName, PersistentRunnerRequestHandler handler, params Type[] extensionTypes)
        {
            providerId = id;
            legacy = handler;
            catalog = new ExtensionDescribeResult { ProviderId = id, ProviderVersion = "5.0.0.1" };
            foreach (Type type in extensionTypes)
            {
                if (type.IsAbstract) throw new ArgumentException("An extension type cannot be abstract.");
                string identifier = type.FullName + ", " + assemblyName + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
                types.Add(identifier, type);
                catalog.Extensions.Add(DescribeType(type, identifier, assemblyName));
            }
        }

        internal ExtensionResponseEnvelope Dispatch(ExtensionRequestEnvelope envelope, bool controlOnly = false)
        {
            try
            {
                if (envelope == null) throw BridgeFault.Invalid("Request envelope is required.");
                if (envelope.ProtocolVersion != 1) throw new BridgeFault("VersionUnsupported", "NotStarted", "Envelope version 1 is required.");
                if (string.IsNullOrWhiteSpace(envelope.RequestId) || envelope.RequestId.Length > 256) throw BridgeFault.Invalid("A bounded request identifier is required.");
                if (controlOnly && envelope.Operation != ExtensionBridgeProtocol.Cancel) throw BridgeFault.Invalid("Only extension.cancel is accepted on the control listener.");
                string payload;
                switch (envelope.Operation)
                {
                    case ExtensionBridgeProtocol.Describe:
                        var describe = Parse<ExtensionDescribeRequest>(envelope.PayloadJson);
                        ValidateIdentity(describe.SchemaVersion, describe.ProviderId, true);
                        payload = PersistentRunnerJson.Serialize(catalog);
                        break;
                    case ExtensionBridgeProtocol.Cancel:
                        var cancel = Parse<ExtensionCancelRequest>(envelope.PayloadJson);
                        ValidateIdentity(cancel.SchemaVersion, cancel.ProviderId, false);
                        if (string.IsNullOrWhiteSpace(cancel.TargetRequestId) || string.IsNullOrWhiteSpace(cancel.SessionId)) throw BridgeFault.Invalid("Cancellation requires target request and session identifiers.");
                        lock (controlGate)
                        {
                            if (active != null && active.RequestId == cancel.TargetRequestId && active.SessionId == cancel.SessionId)
                                active.Cancellation.Cancel();
                        }
                        payload = PersistentRunnerJson.Serialize(new ExtensionCancelResult { Acknowledged = true });
                        break;
                    case ExtensionBridgeProtocol.Invoke:
                        payload = PersistentRunnerJson.Serialize(Invoke(envelope.RequestId, Parse<ExtensionInvokeRequest>(envelope.PayloadJson)));
                        break;
                    default: throw BridgeFault.Invalid("Unknown bridge operation.");
                }
                return new ExtensionResponseEnvelope { RequestId = envelope.RequestId, Success = true, PayloadJson = payload };
            }
            catch (Exception ex)
            {
                var fault = ex as BridgeFault;
                // Business exception text may contain credentials supplied to an SDK.
                string message = fault == null ? "Extension execution failed (" + ex.GetType().Name + ")." : fault.Message;
                return new ExtensionResponseEnvelope { RequestId = envelope?.RequestId, Success = false, ErrorMessage = message,
                    PayloadJson = PersistentRunnerJson.Serialize(new ExtensionFailure { Code = fault?.Code ?? "ExecutionFailed", ExecutionState = fault?.State ?? "Unknown", Message = message }) };
            }
        }

        private static T Parse<T>(string json) where T : class
        {
            try
            {
                using (var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 32, DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
                {
                    var token = Newtonsoft.Json.Linq.JToken.Load(reader, new Newtonsoft.Json.Linq.JsonLoadSettings { DuplicatePropertyNameHandling = Newtonsoft.Json.Linq.DuplicatePropertyNameHandling.Error });
                    if (!(token is Newtonsoft.Json.Linq.JObject) || reader.Read()) throw BridgeFault.Invalid("Expected one payload object.");
                }
                return PersistentRunnerJson.Deserialize<T>(json) ?? throw BridgeFault.Invalid("Request payload is required.");
            }
            catch (BridgeFault) { throw; }
            catch { throw BridgeFault.Invalid("Malformed request payload."); }
        }

        private void ValidateIdentity(int schema, string id, bool bootstrap)
        {
            if (schema != 1) throw new BridgeFault("VersionUnsupported", "NotStarted", "Schema version 1 is required.");
            if (bootstrap && string.IsNullOrEmpty(id)) return;
            if (!string.Equals(id, providerId, StringComparison.Ordinal)) throw new BridgeFault("UnknownExtension", "NotStarted", "Provider identity does not match.");
        }

        private ExtensionInvokeResult Invoke(string requestId, ExtensionInvokeRequest request)
        {
            ValidateIdentity(request.SchemaVersion, request.ProviderId, false);
            ExtensionDescriptor descriptor = Resolve(request.TypeName);
            if (descriptor.Kind != request.Kind) throw BridgeFault.Invalid("Extension kind does not match.");
            if (string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Length > 256) throw BridgeFault.Invalid("A bounded session identifier is required.");
            bool transformer = descriptor.Kind == "Transformer";
            if (transformer ? request.Phase != "Transform" : !new[] { "Prepare", "Process", "AllFunctionsSucceeded", "RollBack", "Close" }.Contains(request.Phase))
                throw BridgeFault.Invalid("Unknown lifecycle phase.");
            DateTimeOffset deadline;
            if (string.IsNullOrWhiteSpace(request.DeadlineUtc) || !DateTimeOffset.TryParse(request.DeadlineUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out deadline) || deadline.Offset != TimeSpan.Zero)
                throw BridgeFault.Invalid("DeadlineUtc must be an ISO UTC timestamp.");
            var parameters = NamedValues(request.Parameters);
            using (var context = new SourceAdapterContext(request))
            lock (businessGate)
            {
                if (deadline <= DateTimeOffset.UtcNow) throw new BridgeFault("DeadlineExceeded", "NotStarted", "Deadline elapsed before execution.");
                if (dispatched.Contains(requestId)) throw BridgeFault.Invalid("A dispatched request cannot be replayed.");
                if (dispatched.Count >= 100000) throw new BridgeFault("Unavailable", "NotStarted", "Runner request capacity reached; start a new generation.");
                Session session = null;
                if (!transformer)
                {
                    if (request.Phase == "Prepare")
                    {
                        if (sessions.ContainsKey(request.SessionId)) throw BridgeFault.Invalid("Session already prepared.");
                        if (sessions.Count >= 1024) throw new BridgeFault("Unavailable", "NotStarted", "Too many prepared sessions.");
                        session = new Session { TypeName = descriptor.TypeName, Activity = (CustomActivity)Activator.CreateInstance(types[descriptor.TypeName]) };
                    }
                    else if (!sessions.TryGetValue(request.SessionId, out session) || session.TypeName != descriptor.TypeName)
                        throw new BridgeFault("UnknownSession", "NotStarted", "Prepared session is unavailable; it cannot be recreated or replayed.");
                }
                dispatched.Add(requestId);
                using (var cancellation = new CancellationTokenSource())
                {
                    var call = new ActiveCall { RequestId = requestId, SessionId = request.SessionId, Cancellation = cancellation };
                    lock (controlGate) active = call;
                    using (var timer = new Timer(_ => { lock (controlGate) { if (active == call) cancellation.Cancel(); } }, null,
                        (int)Math.Min(int.MaxValue, Math.Max(1, (deadline - DateTimeOffset.UtcNow).TotalMilliseconds)), Timeout.Infinite))
                    {
                        try
                        {
                            SourceAdapterDispatch.Handler = legacy;
                            if (transformer) ((CustomTransformer)Activator.CreateInstance(types[descriptor.TypeName])).Transform(context, context.CurrentActivityInstance.Message, parameters);
                            else
                            {
                                switch (request.Phase)
                                {
                                    case "Prepare": session.Activity.Prepare(context, context.CurrentActivityInstance); sessions.Add(request.SessionId, session); break;
                                    case "Process": session.Activity.Process(context, context.CurrentActivityInstance, parameters); break;
                                    case "AllFunctionsSucceeded": session.Activity.AllFunctionsSucceded(context, context.CurrentActivityInstance); break;
                                    case "RollBack": session.Activity.RollBack(context, context.CurrentActivityInstance); break;
                                    case "Close": session.Activity.Close(); sessions.Remove(request.SessionId); break;
                                }
                            }
                            if (cancellation.IsCancellationRequested || deadline <= DateTimeOffset.UtcNow)
                            {
                                sessions.Remove(request.SessionId);
                                throw new BridgeFault(deadline <= DateTimeOffset.UtcNow ? "DeadlineExceeded" : "Cancelled", "Unknown", "Execution may have occurred; the result was discarded and the session invalidated.");
                            }
                            return context.Complete();
                        }
                        catch
                        {
                            sessions.Remove(request.SessionId);
                            // Close is best-effort resource cleanup, never a replay or rollback
                            // of Process. Preserve the original uncertain execution failure.
                            if (session != null && request.Phase != "Close")
                                try { session.Activity.Close(); } catch { }
                            throw;
                        }
                        finally { SourceAdapterDispatch.Handler = null; lock (controlGate) active = null; }
                    }
                }
            }
        }

        private ExtensionDescriptor Resolve(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) throw new BridgeFault("UnknownExtension", "NotStarted", "Extension type is required.");
            var matches = catalog.Extensions.Where(d => d.TypeName == identifier || d.LegacyTypeNames.Contains(identifier)).ToList();
            if (matches.Count == 0) matches = catalog.Extensions.Where(d => SameIdentity(identifier, d.TypeName)).ToList();
            if (matches.Count != 1) throw new BridgeFault("UnknownExtension", "NotStarted", "Unknown or ambiguous extension identity.");
            return matches[0];
        }

        private static bool SameIdentity(string left, string right)
        {
            try
            {
                int a = left.IndexOf(','), b = right.IndexOf(',');
                if (a <= 0 || b <= 0 || left.Substring(0, a).Trim() != right.Substring(0, b).Trim()) return false;
                var la = new AssemblyName(left.Substring(a + 1).Trim()); var ra = new AssemblyName(right.Substring(b + 1).Trim());
                return string.Equals(la.Name, ra.Name, StringComparison.OrdinalIgnoreCase) && (la.GetPublicKeyToken() ?? new byte[0]).SequenceEqual(ra.GetPublicKeyToken() ?? new byte[0]);
            }
            catch { return false; }
        }

        internal static Dictionary<string, string> NamedValues(List<ExtensionNamedValue> values, bool ignoreCase = false)
        {
            var result = new Dictionary<string, string>(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            if (values != null && values.Count > 10000) throw BridgeFault.Invalid("Too many named values.");
            foreach (var pair in values ?? new List<ExtensionNamedValue>())
            {
                if (pair == null || string.IsNullOrWhiteSpace(pair.Name) || pair.Name.Length > 1024 || result.ContainsKey(pair.Name)) throw BridgeFault.Invalid("Named values require unique bounded nonempty names.");
                result.Add(pair.Name, pair.Value);
            }
            return result;
        }

        private static ExtensionDescriptor DescribeType(Type type, string identifier, string assemblyName)
        {
            var descriptor = new ExtensionDescriptor { Kind = typeof(CustomTransformer).IsAssignableFrom(type) ? "Transformer" : "Activity", TypeName = identifier,
                DisplayName = type.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? type.Name,
                LegacyTypeNames = new List<string> { type.FullName + ", " + assemblyName }, RequiredContextCapabilities = new List<string> { ExtensionBridgeProtocol.MessageContextCapability } };
            if (type.FullName == "ValidateTransformer.ValidateHL7ToAckTransformer")
                descriptor.RequiredContextCapabilities.Add(ExtensionBridgeProtocol.WorkflowResponseCapability);
            foreach (ParameterAttribute parameter in type.GetCustomAttributes<ParameterAttribute>(true))
            {
                var value = new ExtensionParameter { Name = parameter.Name, Description = parameter.Description, IsRequired = parameter.IsRequired };
                value.IsOutputVariableName = type.Namespace == "HL7ValueTransformers" && parameter.Name == "Output Variable";
                object ui = type.GetCustomAttributes(true).FirstOrDefault(a => a.GetType().Name == "ParameterUiAttribute" && (string)a.GetType().GetProperty("ParameterName")?.GetValue(a) == parameter.Name);
                if (ui != null)
                {
                    value.EditorType = (string)ui.GetType().GetProperty("EditorType")?.GetValue(ui) ?? "Text";
                    value.Purpose = (string)ui.GetType().GetProperty("Purpose")?.GetValue(ui) ?? "Text";
                    value.Options = ((string[])ui.GetType().GetProperty("Options")?.GetValue(ui) ?? new string[0]).ToList();
                    value.ValidationRegex = (string)ui.GetType().GetProperty("ValidationRegex")?.GetValue(ui);
                    value.ValidationMessage = (string)ui.GetType().GetProperty("ValidationMessage")?.GetValue(ui);
                }
                descriptor.Parameters.Add(value);
            }
            foreach (VariableAttribute variable in type.GetCustomAttributes<VariableAttribute>(true)) descriptor.Variables.Add(new ExtensionNamedValue { Name = variable.Name, Value = variable.SampleValue });
            var input = type.GetCustomAttribute<InMessageAttribute>();
            if (input != null) descriptor.InMessage = new ExtensionMessageMetadata { MessageType = (int)input.MessageType, DefaultMessageType = (int)input.MessageType, SampleMessage = input.SampleTemplateMessage, UserCanEditTemplate = input.UserCanEditTemplate };
            var output = type.GetCustomAttribute<OutMessageAttribute>();
            if (output != null) descriptor.OutMessage = new ExtensionMessageMetadata { MessageType = (int)output.MessageType, DefaultMessageType = (int)output.DefaultMessageType, SampleMessage = output.SampleResponseMessage, UserCanEditTemplate = true };
            // No source restriction is declared on these transformers. User-defined
            // activities likewise accept every actual message kind in the host.
            descriptor.SupportedMessageTypes = input == null || input.MessageType == TypeOfMessages.UserDefined
                ? new List<int> { 1, 2, 3, 4, 5, 6, 11, 13, 14, 16 } : new List<int> { (int)input.MessageType };
            return descriptor;
        }

        private sealed class Session { internal string TypeName; internal CustomActivity Activity; }
        private sealed class ActiveCall { internal string RequestId; internal string SessionId; internal CancellationTokenSource Cancellation; }
    }
}
