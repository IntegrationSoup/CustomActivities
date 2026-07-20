using System.Text.Json;
using ValidateTransformer;

namespace ValidateTransformer.Tests;

public sealed class ValidationOutcomeBuilderTests
{
    [Fact]
    public void Enriches_missing_field_error_and_uses_has_errors()
    {
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"cached alias\",\"IsValid\":false,\"Errors\":[{\"Path\":\"MSH-15\",\"Reason\":\"is not in messsage\"}]}",
            "ADT A01 Validation");

        Assert.True(outcome.HasErrors);
        Assert.Equal("AE", outcome.AcknowledgmentCode);
        Assert.Equal("ADT A01 Validation", outcome.Profile);

        ValidationError error = Assert.Single(outcome.Errors);
        Assert.Equal("MSH-15", error.Path);
        Assert.Equal("is not in message", error.Reason);
        Assert.Equal("MSH", error.ErrorSegment);
        Assert.Equal(1, error.ErrorSegmentOccurrence);
        Assert.Equal(15, error.ErrorField);
        Assert.Null(error.ErrorFieldRepetition);
        Assert.Null(error.ErrorComponent);
        Assert.Null(error.ErrorSubcomponent);
        Assert.Equal("E", error.Severity);
        Assert.Equal("101", error.Hl7ErrorCode);
        Assert.Equal("HL70357", error.Hl7ErrorCodingSystem);
        Assert.Equal("PROFILE_REQUIRED", error.ApplicationErrorCode);
        Assert.Equal("L", error.ApplicationErrorCodingSystem);
    }

    [Fact]
    public void Splits_repeated_segment_field_component_and_subcomponent_path()
    {
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"Errors\":[{\"Path\":\"OBX[2]-5[3].2.1\",\"Reason\":\"is not equal to expected\"}]}",
            "Profile");

        ValidationError error = Assert.Single(outcome.Errors);
        Assert.Equal("OBX", error.ErrorSegment);
        Assert.Equal(2, error.ErrorSegmentOccurrence);
        Assert.Equal(5, error.ErrorField);
        Assert.Equal(3, error.ErrorFieldRepetition);
        Assert.Equal(2, error.ErrorComponent);
        Assert.Equal(1, error.ErrorSubcomponent);
        Assert.Equal("102", error.Hl7ErrorCode);
        Assert.Equal("PROFILE_RULE", error.ApplicationErrorCode);
    }

    [Theory]
    [InlineData("MSH-9.1", "200", "PROFILE_MESSAGE_TYPE")]
    [InlineData("MSH-9.2", "201", "PROFILE_EVENT_CODE")]
    [InlineData("MSH-11", "202", "PROFILE_PROCESSING_ID")]
    [InlineData("MSH-12.1", "203", "PROFILE_VERSION_ID")]
    public void Uses_specific_header_error_codes(string path, string hl7Code, string applicationCode)
    {
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"Errors\":[{\"Path\":\"" + path + "\",\"Reason\":\"is not equal to expected\"}]}",
            "Profile");

        ValidationError error = Assert.Single(outcome.Errors);
        Assert.Equal(hl7Code, error.Hl7ErrorCode);
        Assert.Equal(applicationCode, error.ApplicationErrorCode);
    }

    [Theory]
    [InlineData("MSH-9.1", "200", "PROFILE_MESSAGE_TYPE")]
    [InlineData("MSH-9.2", "201", "PROFILE_EVENT_CODE")]
    [InlineData("MSH-11", "202", "PROFILE_PROCESSING_ID")]
    [InlineData("MSH-12.1", "203", "PROFILE_VERSION_ID")]
    public void Uses_specific_header_codes_before_generic_list_classification(string path, string hl7Code, string applicationCode)
    {
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"Errors\":[{\"Path\":\"" + path + "\",\"Reason\":\"is not in list\"}]}",
            "Profile");

        ValidationError error = Assert.Single(outcome.Errors);
        Assert.Equal(hl7Code, error.Hl7ErrorCode);
        Assert.Equal(applicationCode, error.ApplicationErrorCode);
    }

    [Fact]
    public void Empty_errors_returns_application_accept_even_when_source_is_valid_is_false()
    {
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"IsValid\":false,\"Errors\":[]}",
            "Profile");

        Assert.False(outcome.HasErrors);
        Assert.Equal("AA", outcome.AcknowledgmentCode);
    }

    [Fact]
    public void Json_contract_does_not_return_version_location_or_is_valid()
    {
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"Errors\":[{\"Path\":\"PID-5.1\",\"Reason\":\"is invalid\"}]}",
            "Profile");

        string json = ValidationJson.Serialize(outcome);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.False(root.TryGetProperty("Hl7Version", out _));
        Assert.False(root.TryGetProperty("ErrorLocation", out _));
        Assert.False(root.TryGetProperty("IsValid", out _));
        Assert.Equal("PID", root.GetProperty("Errors")[0].GetProperty("ErrorSegment").GetString());
        Assert.Equal(1, root.GetProperty("Errors")[0].GetProperty("ErrorComponent").GetInt32());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Profile\":\"Profile\",\"Errors\":null}")]
    public void Missing_error_list_does_not_produce_a_false_application_accept(string rawJson)
    {
        Exception exception = Assert.Throws<Exception>(() => ValidationOutcomeBuilder.Build(rawJson, "Profile"));

        Assert.Contains("without an Errors list", exception.Message);
    }
}
