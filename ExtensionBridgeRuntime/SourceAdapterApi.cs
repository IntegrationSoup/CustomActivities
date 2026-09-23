// Runner-private source API, not an ABI replacement for HL7SoupIntegrations.
// Existing adapter sources are compiled unchanged into the isolated executable.
// Only snapshot/message operations audited in these packages are exposed here;
// adding an unsupported host callback fails compilation instead of emulating it.
using System;
using System.Collections.Generic;

namespace HL7Soup.Integrations
{
    public interface IMessage : IDisposable
    {
        string Text { get; }
        void SetText(string text);
    }

    public interface IDicomMessage : IMessage { string GetBase64EncodedDicom(); }
    public interface IJsonMessage : IMessage { }
    public interface IHL7Message : IMessage
    {
        string ValidateWithHighlighters(string profileName);
        IHL7Segment GetSegment(string name);
    }
    public interface IHL7Segment
    {
        string Text { get; }
        string GetFieldValue(int field);
        string GetComponentValue(int field, int component);
    }

    public interface IActivityInstance
    {
        bool Filtered { get; }
        Guid Id { get; }
        string Name { get; }
        IMessage Message { get; }
        IMessage ResponseMessage { get; }
    }

    public interface IWorkflowInstance
    {
        void SetVariable(string name, string value);
        string GetVariable(string name);
        IActivityInstance CurrentActivityInstance { get; }
        IActivityInstance ReceivingActivityInstance { get; }
        IMessage CreateMessage(HL7Soup.BaseTypes.MessageTypes type, string text);
    }

    public abstract class CustomTransformer
    {
        public abstract void Transform(IWorkflowInstance workflow, IMessage message, Dictionary<string, string> parameters);
    }

    public abstract class CustomActivity
    {
        public virtual void Prepare(IWorkflowInstance workflow, IActivityInstance activity) { }
        public abstract void Process(IWorkflowInstance workflow, IActivityInstance activity, Dictionary<string, string> parameters);
        public virtual void AllFunctionsSucceded(IWorkflowInstance workflow, IActivityInstance activity) { }
        public virtual void RollBack(IWorkflowInstance workflow, IActivityInstance activity) { }
        public virtual void Cancel() { }
        public virtual void Close() { }
    }
}
