using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HL7Soup.Integrations.ExtensionBridge
{
    // Source-linkable wire contract: no runtime, designer or extension assembly dependency.
    // Existing version 4 envelopes and operation payloads remain unchanged.
    public static class ExtensionBridgeProtocol
    {
        public const int EnvelopeVersion = 1;
        public const int SchemaVersion = 1;
        public const string Describe = "extension.describe";
        public const string Invoke = "extension.invoke";
        public const string Designer = "extension.designer";
        public const string Cancel = "extension.cancel";
        public const string RegistryPath = @"SOFTWARE\Popokey\IntegrationSoup\ExtensionProviders";
        public const int DefaultMaxMessageBytes = 64 * 1024 * 1024;
        public const int MaximumMessageBytes = 256 * 1024 * 1024;
        public const int MaximumDescribeBytes = 4 * 1024 * 1024;
        public const string MessageContextCapability = "message-context-v1";
        public const string WorkflowResponseCapability = "workflow-response-v1";
    }

    [DataContract]
    public sealed class ExtensionRequestEnvelope
    {
        [DataMember(Order = 1)] public int ProtocolVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string RequestId { get; set; }
        [DataMember(Order = 3)] public string Operation { get; set; }
        [DataMember(Order = 4)] public string PayloadJson { get; set; }
    }

    [DataContract]
    public sealed class ExtensionResponseEnvelope
    {
        [DataMember(Order = 1)] public int ProtocolVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string RequestId { get; set; }
        [DataMember(Order = 3)] public bool Success { get; set; }
        [DataMember(Order = 4)] public string PayloadJson { get; set; }
        [DataMember(Order = 5)] public string ErrorMessage { get; set; }
    }

    [DataContract]
    public sealed class ExtensionProviderManifest
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string ProviderId { get; set; }
        [DataMember(Order = 3)] public string Revision { get; set; }
        [DataMember(Order = 4)] public string Transport { get; set; }
        [DataMember(Order = 5)] public string ExecutablePath { get; set; }
        [DataMember(Order = 6)] public List<string> Arguments { get; set; } = new List<string>();
        [DataMember(Order = 7)] public string Endpoint { get; set; }
        [DataMember(Order = 8)] public string AuthHeaderName { get; set; }
        [DataMember(Order = 9)] public string CredentialEnvironmentVariable { get; set; }
        [DataMember(Order = 10)] public int TimeoutSeconds { get; set; } = 120;
        [DataMember(Order = 11)] public int MaxMessageBytes { get; set; } = ExtensionBridgeProtocol.DefaultMaxMessageBytes;
        [DataMember(Order = 12)] public bool Enabled { get; set; } = true;
        [DataMember(Order = 13)] public string PipeName { get; set; }
    }

    [DataContract]
    public sealed class ExtensionDescribeRequest
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        // Empty only for initial connection discovery; subsequent calls pin the returned identity.
        [DataMember(Order = 2)] public string ProviderId { get; set; }
    }

    [DataContract]
    public sealed class ExtensionDescribeResult
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string ProviderId { get; set; }
        [DataMember(Order = 3)] public string ProviderVersion { get; set; }
        [DataMember(Order = 4)] public List<ExtensionDescriptor> Extensions { get; set; } = new List<ExtensionDescriptor>();
    }

    [DataContract]
    public sealed class ExtensionDescriptor
    {
        [DataMember(Order = 1)] public string Kind { get; set; }
        [DataMember(Order = 2)] public string TypeName { get; set; }
        [DataMember(Order = 3)] public string DisplayName { get; set; }
        [DataMember(Order = 4)] public List<string> LegacyTypeNames { get; set; } = new List<string>();
        [DataMember(Order = 5)] public List<ExtensionParameter> Parameters { get; set; } = new List<ExtensionParameter>();
        [DataMember(Order = 6)] public List<ExtensionNamedValue> Variables { get; set; } = new List<ExtensionNamedValue>();
        [DataMember(Order = 7)] public ExtensionMessageMetadata InMessage { get; set; }
        [DataMember(Order = 8)] public ExtensionMessageMetadata OutMessage { get; set; }
        [DataMember(Order = 9)] public List<int> SupportedMessageTypes { get; set; } = new List<int>();
        [DataMember(Order = 10)] public List<string> RequiredContextCapabilities { get; set; } = new List<string>();
        [DataMember(Order = 11)] public ExtensionDesignerCapabilities Designer { get; set; }
        [DataMember(Order = 12)] public string Description { get; set; }
    }

    [DataContract]
    public sealed class ExtensionParameter
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public string Description { get; set; }
        [DataMember(Order = 3)] public bool IsRequired { get; set; }
        [DataMember(Order = 4)] public string DefaultValue { get; set; }
        [DataMember(Order = 5)] public string EditorType { get; set; } = "Text";
        [DataMember(Order = 6)] public string Purpose { get; set; } = "Text";
        [DataMember(Order = 7)] public bool AllowVariableBinding { get; set; } = true;
        [DataMember(Order = 8)] public List<string> Options { get; set; } = new List<string>();
        [DataMember(Order = 9)] public string ValidationRegex { get; set; }
        [DataMember(Order = 10)] public string ValidationMessage { get; set; }
        // A nonblank resolved value names an allowed variable output (trimmed).
        [DataMember(Order = 11)] public bool IsOutputVariableName { get; set; }
    }

    [DataContract]
    public sealed class ExtensionMessageMetadata
    {
        [DataMember(Order = 1)] public int MessageType { get; set; }
        [DataMember(Order = 2)] public int DefaultMessageType { get; set; }
        [DataMember(Order = 3)] public string SampleMessage { get; set; }
        [DataMember(Order = 4)] public bool UserCanEditTemplate { get; set; }
    }

    [DataContract]
    public sealed class ExtensionNamedValue
    {
        [DataMember(Order = 1)] public string Name { get; set; }
        [DataMember(Order = 2)] public string Value { get; set; }
    }

public sealed class ExtensionDesignerCapabilities
    {
        public bool FieldChanges { get; set; }
        public List<ExtensionDesignerAction> Actions { get; set; } = new List<ExtensionDesignerAction>();
    }
    public sealed class ExtensionDesignerAction
    {
        public string Id { get; set; }
        public string Label { get; set; }
    }
    public sealed class ExtensionDesignerRequest
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProviderId { get; set; }
        public string ProviderVersion { get; set; }
        public string TypeName { get; set; }
        public string Kind { get; set; } = "Activity";
        public string EventType { get; set; }
        public string ControlName { get; set; }
        public long Revision { get; set; }
        public string DeadlineUtc { get; set; }
        public List<ExtensionNamedValue> Parameters { get; set; } = new List<ExtensionNamedValue>();
    }
    public sealed class ExtensionDesignerResult
    {
        public int SchemaVersion { get; set; } = 1;
        public long Revision { get; set; }
        public List<ExtensionDesignerFieldUpdate> Fields { get; set; } = new List<ExtensionDesignerFieldUpdate>();
    }
    public sealed class ExtensionDesignerFieldUpdate
    {
        public string Name { get; set; }
        // Null leaves a value/UI property unchanged; an empty value clears it.
        public string Value { get; set; }
        public bool? IsVisible { get; set; }
        public bool? IsEnabled { get; set; }
        public List<string> Options { get; set; }
        public string ValidationMessage { get; set; }
    }

    [DataContract]
    public sealed class ExtensionMessage
    {
        [DataMember(Order = 1)] public int MessageType { get; set; }
        [DataMember(Order = 2)] public string Text { get; set; }
    }

    [DataContract]
    public sealed class ExtensionActivitySnapshot
    {
        [DataMember(Order = 1)] public string ActivityId { get; set; }
        [DataMember(Order = 2)] public string ActivityName { get; set; }
        [DataMember(Order = 3)] public bool Filtered { get; set; }
        [DataMember(Order = 4)] public ExtensionMessage Message { get; set; }
        [DataMember(Order = 5)] public ExtensionMessage ResponseMessage { get; set; }
    }

    [DataContract]
    public sealed class ExtensionInvocationContext
    {
        [DataMember(Order = 1)] public string WorkflowId { get; set; }
        [DataMember(Order = 2)] public int InstanceId { get; set; }
        [DataMember(Order = 3)] public string ActivityId { get; set; }
        [DataMember(Order = 4)] public string ActivityName { get; set; }
        [DataMember(Order = 5)] public string ReceivingActivityId { get; set; }
        [DataMember(Order = 6)] public List<ExtensionNamedValue> Variables { get; set; } = new List<ExtensionNamedValue>();
        [DataMember(Order = 7)] public List<ExtensionActivitySnapshot> Activities { get; set; } = new List<ExtensionActivitySnapshot>();
    }

    [DataContract]
    public sealed class ExtensionInvokeRequest
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string ProviderId { get; set; }
        [DataMember(Order = 3)] public string TypeName { get; set; }
        [DataMember(Order = 4)] public string Kind { get; set; }
        [DataMember(Order = 5)] public string Phase { get; set; }
        [DataMember(Order = 6)] public string SessionId { get; set; }
        [DataMember(Order = 7)] public string DeadlineUtc { get; set; }
        [DataMember(Order = 8)] public List<ExtensionNamedValue> Parameters { get; set; } = new List<ExtensionNamedValue>();
        [DataMember(Order = 9)] public ExtensionMessage Message { get; set; }
        [DataMember(Order = 10)] public ExtensionMessage ResponseMessage { get; set; }
        [DataMember(Order = 11)] public ExtensionInvocationContext Context { get; set; }
    }

    [DataContract]
    public sealed class ExtensionInvokeResult
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        // Null means unchanged; a non-null message with empty Text clears its contents.
        [DataMember(Order = 2)] public ExtensionMessage Message { get; set; }
        [DataMember(Order = 3)] public ExtensionMessage ResponseMessage { get; set; }
        [DataMember(Order = 4)] public List<ExtensionNamedValue> VariableUpdates { get; set; } = new List<ExtensionNamedValue>();
        [DataMember(Order = 5)] public List<ExtensionNotification> Notifications { get; set; } = new List<ExtensionNotification>();
        [DataMember(Order = 6)] public string WorkflowError { get; set; }
        // Promote the current activity response through the native SetReponseMessage API.
        [DataMember(Order = 7)] public bool PromoteResponseToWorkflow { get; set; }
    }

    [DataContract]
    public sealed class ExtensionNotification
    {
        [DataMember(Order = 1)] public string Text { get; set; }
        [DataMember(Order = 2)] public bool Critical { get; set; }
        [DataMember(Order = 3)] public string UniquenessCode { get; set; }
    }

    [DataContract]
    public sealed class ExtensionCancelRequest
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string ProviderId { get; set; }
        [DataMember(Order = 3)] public string TargetRequestId { get; set; }
        [DataMember(Order = 4)] public string SessionId { get; set; }
    }

    [DataContract]
    public sealed class ExtensionCancelResult
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        [DataMember(Order = 2)] public bool Acknowledged { get; set; }
    }

    [DataContract]
    public sealed class ExtensionFailure
    {
        [DataMember(Order = 1)] public int SchemaVersion { get; set; } = 1;
        [DataMember(Order = 2)] public string Code { get; set; }
        [DataMember(Order = 3)] public string ExecutionState { get; set; }
        [DataMember(Order = 4)] public string Message { get; set; }
    }
}
