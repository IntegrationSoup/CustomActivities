using System.ComponentModel;
using HL7Soup.Integrations;
using ValidateTransformer;

namespace ValidateTransformer.Tests;

public sealed class ActivityContractTests
{
    [Theory]
    [InlineData(typeof(ValidateHL7Transformer), "Validate HL7 Message", TypeOfMessages.JSON)]
    [InlineData(typeof(ValidateHL7ToAckTransformer), "Validate HL7 Message to HL7 ACK", TypeOfMessages.HL7)]
    public void Activities_expose_the_expected_message_and_parameter_contracts(
        Type activityType,
        string displayName,
        TypeOfMessages responseType)
    {
        InMessageAttribute input = Assert.IsType<InMessageAttribute>(
            Assert.Single(activityType.GetCustomAttributes(typeof(InMessageAttribute), false)));
        OutMessageAttribute output = Assert.IsType<OutMessageAttribute>(
            Assert.Single(activityType.GetCustomAttributes(typeof(OutMessageAttribute), false)));
        ParameterAttribute[] parameters = activityType
            .GetCustomAttributes(typeof(ParameterAttribute), false)
            .Cast<ParameterAttribute>()
            .ToArray();
        ParameterUiAttribute parameterUi = Assert.IsType<ParameterUiAttribute>(
            Assert.Single(activityType.GetCustomAttributes(typeof(ParameterUiAttribute), false)));
        DisplayNameAttribute name = Assert.IsType<DisplayNameAttribute>(
            Assert.Single(activityType.GetCustomAttributes(typeof(DisplayNameAttribute), false)));

        ParameterAttribute profileParameter = Assert.Single(parameters, parameter => parameter.Name == "Profile");
        ParameterAttribute errorParameter = Assert.Single(
            parameters,
            parameter => parameter.Name == ValidationActivity.ErrorIfInvalidParameterName);

        Assert.Equal(TypeOfMessages.HL7, input.MessageType);
        Assert.Equal(responseType, output.MessageType);
        Assert.Equal(displayName, name.DisplayName);
        Assert.True(profileParameter.IsRequired);
        Assert.Equal("Error if invalid", errorParameter.Name);
        Assert.Equal("Mark Workflow Error on Invalid", errorParameter.Description);
        Assert.False(errorParameter.IsRequired);
        Assert.Equal(ValidationActivity.ErrorIfInvalidParameterName, parameterUi.ParameterName);
        Assert.Equal("Checkbox", parameterUi.EditorType);
        Assert.StartsWith("MSH", input.SampleTemplateMessage);
        Assert.False(string.IsNullOrWhiteSpace(output.SampleResponseMessage));
    }
}
