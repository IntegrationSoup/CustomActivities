using HL7Soup.BaseTypes;
using HL7Soup.Integrations;
using ValidateTransformer;

namespace ValidateTransformer.Tests;

public sealed class ValidationActivityTests
{
    [Fact]
    public void Error_if_invalid_defaults_to_false_when_missing()
    {
        Assert.False(ValidationActivity.GetErrorIfInvalid(null!));
        Assert.False(ValidationActivity.GetErrorIfInvalid(new Dictionary<string, string>()));
        Assert.False(ValidationActivity.GetErrorIfInvalid(new Dictionary<string, string>
        {
            [ValidationActivity.ErrorIfInvalidParameterName] = " "
        }));
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("yes", true)]
    [InlineData("1", true)]
    [InlineData("on", true)]
    [InlineData("False", false)]
    [InlineData("no", false)]
    [InlineData("0", false)]
    [InlineData("off", false)]
    public void Error_if_invalid_accepts_checkbox_and_text_fallback_values(string value, bool expected)
    {
        bool actual = ValidationActivity.GetErrorIfInvalid(new Dictionary<string, string>
        {
            [ValidationActivity.ErrorIfInvalidParameterName] = value
        });

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Error_if_invalid_rejects_an_unknown_value()
    {
        Exception exception = Assert.Throws<Exception>(() =>
            ValidationActivity.GetErrorIfInvalid(new Dictionary<string, string>
            {
                [ValidationActivity.ErrorIfInvalidParameterName] = "sometimes"
            }));

        Assert.Contains("must be true or false", exception.Message);
    }

    [Fact]
    public void Error_message_contains_the_profile_and_first_validation_error()
    {
        string message = ValidationActivity.BuildErrorMessage(BuildInvalidOutcome());

        Assert.Contains("ADT A01 Validation", message);
        Assert.Contains("MSH-15: is not in message", message);
    }

    [Fact]
    public void Invalid_outcome_is_ignored_when_option_is_false()
    {
        WorkflowErrorBridgeTests.HostWorkflowInstance workflow = new();
        WorkflowErrorBridgeTests.TestMessage response = new("ACK");

        ValidationActivity.HandleInvalidOutcome(
            workflow,
            response,
            BuildInvalidOutcome(),
            errorIfInvalid: false,
            promoteResponse: true);

        Assert.Null(workflow.PromotedResponse);
        Assert.Null(workflow.ErrorMessage);
        Assert.Null(workflow.LastVariableName);
    }

    [Fact]
    public void Required_input_returns_the_activity_hl7_message_without_taking_ownership()
    {
        WorkflowErrorBridgeTests.InterfaceOnlyWorkflowInstance workflow = new();
        TestActivityInstance activity = new(new TestHl7Message("MSH|^~\\&|direct"));

        IHL7Message actual = ValidationActivity.GetRequiredInputMessage(
            workflow,
            activity,
            "Validate HL7",
            out IDisposable? ownedMessage);

        Assert.Same(activity.Message, actual);
        Assert.Null(ownedMessage);
        Assert.Equal(0, workflow.CreateMessageCallCount);
    }

    [Fact]
    public void Required_input_falls_back_to_the_receiving_hl7_message_for_a_null_or_empty_activity()
    {
        TestHl7Message received = new("MSH|^~\\&|received");
        WorkflowErrorBridgeTests.InterfaceOnlyWorkflowInstance workflow = new()
        {
            ReceivingActivity = new TestActivityInstance(received)
        };

        IHL7Message fromNullActivity = ValidationActivity.GetRequiredInputMessage(
            workflow,
            null!,
            "Validate HL7",
            out IDisposable? nullActivityOwnedMessage);
        IHL7Message fromEmptyActivity = ValidationActivity.GetRequiredInputMessage(
            workflow,
            new TestActivityInstance(null),
            "Validate HL7",
            out IDisposable? emptyActivityOwnedMessage);

        Assert.Same(received, fromNullActivity);
        Assert.Same(received, fromEmptyActivity);
        Assert.Null(nullActivityOwnedMessage);
        Assert.Null(emptyActivityOwnedMessage);
        Assert.Equal(0, workflow.CreateMessageCallCount);
    }

    [Fact]
    public void Required_input_reparses_a_generic_activity_message_as_owned_hl7()
    {
        const string text = "MSH|^~\\&|generic";
        TestHl7Message parsed = new(text);
        WorkflowErrorBridgeTests.InterfaceOnlyWorkflowInstance workflow = new()
        {
            MessageToCreate = parsed
        };

        IHL7Message actual = ValidationActivity.GetRequiredInputMessage(
            workflow,
            new TestActivityInstance(new WorkflowErrorBridgeTests.TestMessage(text)),
            "Validate HL7",
            out IDisposable? ownedMessage);

        Assert.Same(parsed, actual);
        Assert.Same(parsed, ownedMessage);
        Assert.Equal(1, workflow.CreateMessageCallCount);
        Assert.Equal(MessageTypes.HL7V2, workflow.LastCreatedMessageType);
        Assert.Equal(text, workflow.LastCreatedMessageText);
    }

    [Fact]
    public void Required_input_throws_a_helpful_error_when_no_message_is_available()
    {
        WorkflowErrorBridgeTests.InterfaceOnlyWorkflowInstance workflow = new();

        Exception exception = Assert.Throws<Exception>(() =>
            ValidationActivity.GetRequiredInputMessage(
                workflow,
                new TestActivityInstance(null),
                "Validate HL7 to ACK",
                out _));

        Assert.Contains("Validate HL7 to ACK requires an HL7 input message", exception.Message);
        Assert.Contains("Bind an HL7 message", exception.Message);
        Assert.Contains("HL7 receiver", exception.Message);
    }

    [Fact]
    public void Valid_outcome_is_ignored_when_option_is_true()
    {
        WorkflowErrorBridgeTests.HostWorkflowInstance workflow = new();
        WorkflowErrorBridgeTests.TestMessage response = new("ACK");

        ValidationActivity.HandleInvalidOutcome(
            workflow,
            response,
            new ValidationOutcome
            {
                Profile = "ADT A01 Validation",
                HasErrors = false
            },
            errorIfInvalid: true,
            promoteResponse: true);

        Assert.Null(workflow.PromotedResponse);
        Assert.Null(workflow.ErrorMessage);
        Assert.Null(workflow.LastVariableName);
    }

    private static ValidationOutcome BuildInvalidOutcome()
    {
        return ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"Errors\":[{\"Path\":\"MSH-15\",\"Reason\":\"is not in message\"}]}",
            "ADT A01 Validation");
    }

    private sealed class TestActivityInstance : IActivityInstance
    {
        internal TestActivityInstance(IMessage? message)
        {
            Message = message!;
        }

        public bool Filtered => false;
        public Guid Id { get; } = Guid.NewGuid();
        public IMessage Message { get; }
        public IMessage ResponseMessage => throw new NotSupportedException();
        public string Name => "Test activity";
    }

    private sealed class TestHl7Message : IHL7Message
    {
        internal TestHl7Message(string text)
        {
            Text = text;
        }

        public string Text { get; private set; }

        public string ValidateWithHighlighters(string profileName) => throw new NotSupportedException();
        public bool ValidatesWithHighlighters(string profileName) => throw new NotSupportedException();
        public IHL7Segments GetSegments() => throw new NotSupportedException();
        public IHL7Segments GetSegments(string header) => throw new NotSupportedException();
        public IHL7Segment GetSegment(string locationCode) => throw new NotSupportedException();
        public void AddSegment(IHL7Segment segment) => throw new NotSupportedException();
        public void AddSegment(string segment) => throw new NotSupportedException();
        public IHL7Part GetPart(string path) => throw new NotSupportedException();
        public void RemoveSegment(IHL7Segment segment) => throw new NotSupportedException();
        public void BeginUpdate() => throw new NotSupportedException();
        public void EndUpdate() => throw new NotSupportedException();
        public string GetValueAtPath(string path) => throw new NotSupportedException();
        public void SetValueAtPath(string toPath, string fromValue) => throw new NotSupportedException();
        public void SetStructureAtPath(string toPath, string fromValue) => throw new NotSupportedException();
        public void SetText(string text) => Text = text;
        public void Dispose()
        {
        }
    }
}
