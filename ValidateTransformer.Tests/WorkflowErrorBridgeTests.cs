using HL7Soup.BaseTypes;
using HL7Soup.Integrations;
using HL7Soup.Integrations.MessageTypeOptions;
using ValidateTransformer;

namespace ValidateTransformer.Tests;

public sealed class WorkflowErrorBridgeTests
{
    [Fact]
    public void Invalid_outcome_promotes_the_ack_and_marks_the_workflow_without_throwing()
    {
        HostWorkflowInstance workflow = new();
        TestMessage response = new("MSH|...\rMSA|AE|123\rERR|...");
        ValidationOutcome outcome = BuildInvalidOutcome();

        Exception? exception = Record.Exception(() =>
            ValidationActivity.HandleInvalidOutcome(
                workflow,
                response,
                outcome,
                errorIfInvalid: true,
                promoteResponse: true));

        Assert.Null(exception);
        Assert.Same(response, workflow.PromotedResponse);
        Assert.Contains("ADT A01 Validation", workflow.ErrorMessage);
        Assert.Contains("MSH-15: is not in message", workflow.ErrorMessage);
        Assert.Equal("WORKFLOWERROR", workflow.LastVariableName);
        Assert.Equal("true", workflow.LastVariableValue);
    }

    [Fact]
    public void Json_result_marks_the_workflow_without_promoting_the_json_as_the_tcp_response()
    {
        HostWorkflowInstance workflow = new();
        TestMessage response = new("{\"hasErrors\":true}");

        ValidationActivity.HandleInvalidOutcome(
            workflow,
            response,
            BuildInvalidOutcome(),
            errorIfInvalid: true,
            promoteResponse: false);

        Assert.Null(workflow.PromotedResponse);
        Assert.NotNull(workflow.ErrorMessage);
        Assert.Equal("WORKFLOWERROR", workflow.LastVariableName);
        Assert.Equal("true", workflow.LastVariableValue);
    }

    [Fact]
    public void Bridge_failures_are_swallowed_so_response_handling_can_continue()
    {
        ThrowingWorkflowInstance workflow = new();
        TestMessage response = new("ACK");

        Exception? exception = Record.Exception(() =>
        {
            Assert.False(WorkflowErrorBridge.TryPromoteResponse(workflow, response));
            Assert.False(WorkflowErrorBridge.TryMarkErrored(workflow, "validation failed"));
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Missing_host_bridge_methods_do_not_throw()
    {
        InterfaceOnlyWorkflowInstance workflow = new();
        TestMessage response = new("ACK");

        Assert.False(WorkflowErrorBridge.TryPromoteResponse(workflow, response));
        Assert.True(WorkflowErrorBridge.TryMarkErrored(workflow, "validation failed"));
        Assert.Equal("WORKFLOWERROR", workflow.LastVariableName);
        Assert.Equal("true", workflow.LastVariableValue);
    }

    private static ValidationOutcome BuildInvalidOutcome()
    {
        return ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"Errors\":[{\"Path\":\"MSH-15\",\"Reason\":\"is not in message\"}]}",
            "ADT A01 Validation");
    }

    public class InterfaceOnlyWorkflowInstance : IWorkflowInstance
    {
        public List<IActivityInstance> Activities { get; set; } = new();

        public int InstanceId { get; set; }

        public Guid Id { get; } = Guid.NewGuid();

        public IActivityInstance? CurrentActivity { get; set; }

        public IActivityInstance CurrentActivityInstance => CurrentActivity!;

        public IActivityInstance? ReceivingActivity { get; set; }

        public IActivityInstance ReceivingActivityInstance => ReceivingActivity!;

        public IMessage? MessageToCreate { get; set; }

        public MessageTypes? LastCreatedMessageType { get; private set; }

        public string? LastCreatedMessageText { get; private set; }

        public int CreateMessageCallCount { get; private set; }

        public string? LastVariableName { get; private set; }

        public string? LastVariableValue { get; private set; }

        public IActivityInstance GetActivityInstance(Guid settingId) => throw new NotSupportedException();

        public IMessage CreateMessage(MessageTypes messageType, string text)
        {
            CreateMessageCallCount++;
            LastCreatedMessageType = messageType;
            LastCreatedMessageText = text;
            return MessageToCreate ?? new TestMessage(text);
        }

        public IMessage CreateMessage(MessageTypes messageType, string text, IMessageTypeOptions options) => new TestMessage(text);

        public string GetVariable(string variableName) => string.Empty;

        public virtual void SetVariable(string variableName, string value)
        {
            LastVariableName = variableName;
            LastVariableValue = value;
        }

        public void CreateNotification(string text) => throw new NotSupportedException();

        public void CreateNotification(string text, string uniquenessCode) => throw new NotSupportedException();

        public void CreateNotification(string text, bool critical, string uniquenessCode) => throw new NotSupportedException();

        public string LookupValue(string lookupTableName, string from) => throw new NotSupportedException();

        public string DataTableGetValue(string dataTableName, string fieldName) => throw new NotSupportedException();

        public string DataTableGetValue(string dataTableName) => throw new NotSupportedException();

        public void DataTableMoveToRow(string dataTableName, string key) => throw new NotSupportedException();

        public string DataTableGetRandomValue(string dataTableName) => throw new NotSupportedException();

        public void DataTableMoveToNextRow(string dataTableName) => throw new NotSupportedException();

        public void DataTableMoveToRandomRow(string dataTableName) => throw new NotSupportedException();

        public List<string> DataTableGetFieldNames(string dataTableName) => throw new NotSupportedException();

        public int DataTableGetRowCount(string dataTableName) => throw new NotSupportedException();

        public bool DataTableContainsRow(string dataTableName, string key) => throw new NotSupportedException();
    }

    public sealed class HostWorkflowInstance : InterfaceOnlyWorkflowInstance
    {
        public IMessage? PromotedResponse { get; private set; }

        public string? ErrorMessage { get; private set; }

        public void SetReponseMessage(IMessage responseMessage)
        {
            PromotedResponse = responseMessage;
        }

        public void Errored(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }
    }

    public sealed class ThrowingWorkflowInstance : InterfaceOnlyWorkflowInstance
    {
        public void SetReponseMessage(IMessage responseMessage)
        {
            throw new InvalidOperationException("host response promotion failed");
        }

        public void Errored(string errorMessage)
        {
            throw new InvalidOperationException("host error marking failed");
        }

        public override void SetVariable(string variableName, string value)
        {
            throw new InvalidOperationException("host variable marking failed");
        }
    }

    public sealed class TestMessage : IMessage
    {
        public TestMessage(string text)
        {
            Text = text;
        }

        public string Text { get; private set; }

        public string GetValueAtPath(string path) => throw new NotSupportedException();

        public void SetValueAtPath(string toPath, string fromValue) => throw new NotSupportedException();

        public void SetStructureAtPath(string toPath, string fromValue) => throw new NotSupportedException();

        public void SetText(string text)
        {
            Text = text;
        }

        public void Dispose()
        {
        }
    }
}
