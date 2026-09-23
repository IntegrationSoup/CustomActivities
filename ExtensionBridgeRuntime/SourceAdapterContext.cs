using System;
using System.Collections.Generic;
using System.Linq;
using HL7Soup.Integrations;
using HL7Soup.Integrations.ExtensionBridge;

namespace Popokey.ExtensionRunners
{
    internal class SnapshotMessage : IMessage
    {
        internal static Func<ExtensionMessage, SnapshotMessage> TypedFactory { get; set; }
        internal SnapshotMessage(ExtensionMessage message) { Text = message.Text; MessageType = (HL7Soup.BaseTypes.MessageTypes)message.MessageType; }
        public string Text { get; private set; }
        public HL7Soup.BaseTypes.MessageTypes MessageType { get; private set; }
        internal bool Changed { get; private set; }
        public void SetText(string text) { Text = text; Changed = true; }
        public virtual void Dispose() { }
        internal ExtensionMessage Replacement() { return Changed ? new ExtensionMessage { Text = Text, MessageType = (int)MessageType } : null; }
        internal static SnapshotMessage From(ExtensionMessage value)
        {
            if (value == null) return null;
            if (!Enum.IsDefined(typeof(HL7Soup.BaseTypes.MessageTypes), value.MessageType)) throw BridgeFault.Invalid("Unknown message type.");
            if (TypedFactory != null) return TypedFactory(value);
            return value.MessageType == 16 ? new SnapshotDicomMessage(value) : value.MessageType == 11 ? new SnapshotJsonMessage(value) : new SnapshotMessage(value);
        }
    }

    internal sealed class SnapshotJsonMessage : SnapshotMessage, IJsonMessage
    {
        internal SnapshotJsonMessage(ExtensionMessage value) : base(value) { }
    }

    internal sealed class SnapshotDicomMessage : SnapshotMessage, IDicomMessage
    {
        internal SnapshotDicomMessage(ExtensionMessage value) : base(value) { }
        public string GetBase64EncodedDicom() { return Text; }
    }

    internal sealed class SnapshotActivity : IActivityInstance
    {
        public bool Filtered { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; }
        public IMessage Message { get; set; }
        public IMessage ResponseMessage { get; set; }
    }

    internal sealed class SourceAdapterContext : IWorkflowInstance, IDisposable
    {
        private readonly Dictionary<string, string> variables;
        internal readonly ExtensionInvokeResult Result = new ExtensionInvokeResult();
        internal SourceAdapterContext(ExtensionInvokeRequest request)
        {
            variables = BridgeProvider.NamedValues(request.Context?.Variables, true);
            if (request.Context?.Activities?.Count > 10000) throw BridgeFault.Invalid("Too many activity snapshots.");
            CurrentActivityInstance = new SnapshotActivity { Id = ParseId(request.Context?.ActivityId), Name = request.Context?.ActivityName,
                Message = SnapshotMessage.From(request.Message), ResponseMessage = SnapshotMessage.From(request.ResponseMessage) };
            ExtensionActivitySnapshot receiver = request.Context?.Activities?.FirstOrDefault(a => a != null && a.ActivityId == request.Context.ReceivingActivityId);
            if (receiver != null) ReceivingActivityInstance = new SnapshotActivity { Id = ParseId(receiver.ActivityId), Name = receiver.ActivityName,
                Filtered = receiver.Filtered, Message = SnapshotMessage.From(receiver.Message), ResponseMessage = SnapshotMessage.From(receiver.ResponseMessage) };
        }
        private static Guid ParseId(string value) { Guid id; return Guid.TryParse(value, out id) ? id : Guid.Empty; }
        public IActivityInstance CurrentActivityInstance { get; private set; }
        public IActivityInstance ReceivingActivityInstance { get; private set; }
        public string GetVariable(string name) { string value; return variables.TryGetValue(name, out value) ? value : null; }
        public IMessage CreateMessage(HL7Soup.BaseTypes.MessageTypes type, string text) { return SnapshotMessage.From(new ExtensionMessage { MessageType = (int)type, Text = text }); }
        public void Errored(string message) { Result.WorkflowError = message; }
        // Existing validation code discovers this concrete public method by reflection.
        // The wire capability promotes only the current response, never an arbitrary activity.
        public void SetReponseMessage(IMessage message)
        {
            if (message == null || !ReferenceEquals(message, CurrentActivityInstance.ResponseMessage))
                throw BridgeFault.Invalid("Only the current activity response can be promoted.");
            Result.PromoteResponseToWorkflow = true;
        }
        public void SetVariable(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name)) throw BridgeFault.Invalid("Variable name is required.");
            variables[name] = value;
            Result.VariableUpdates.RemoveAll(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
            Result.VariableUpdates.Add(new ExtensionNamedValue { Name = name, Value = value });
        }
        internal ExtensionInvokeResult Complete()
        {
            Result.Message = (CurrentActivityInstance.Message as SnapshotMessage)?.Replacement();
            Result.ResponseMessage = (CurrentActivityInstance.ResponseMessage as SnapshotMessage)?.Replacement();
            return Result;
        }
        public void Dispose()
        {
            foreach (var message in new[] { CurrentActivityInstance?.Message, CurrentActivityInstance?.ResponseMessage, ReceivingActivityInstance?.Message, ReceivingActivityInstance?.ResponseMessage }.Where(m => m != null).Distinct())
                message.Dispose();
        }
    }
}
