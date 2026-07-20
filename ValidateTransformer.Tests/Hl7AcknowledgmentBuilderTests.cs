using ValidateTransformer;

namespace ValidateTransformer.Tests;

public sealed class Hl7AcknowledgmentBuilderTests
{
    [Fact]
    public void V24_ack_preserves_header_values_swaps_parties_and_uses_legacy_err()
    {
        const string msh = "MSH|^~\\&|Sender|SendFac|Receiver|RecvFac|20260720120000+1000||ORM^O01^ORM_O01|MSG123|P|2.4^AUS&Australia&ISO3166_1|||AL|AL|AUS";
        ValidationOutcome outcome = BuildMissingFieldOutcome();

        string ack = Hl7AcknowledgmentBuilder.Build(msh, outcome);
        string[] segments = ack.Split('\r');

        Assert.Equal("MSH|^~\\&|Receiver|RecvFac|Sender|SendFac|20260720120000+1000||ACK^O01^ACK|MSG123|P|2.4^AUS&Australia&ISO3166_1|||AL|AL|AUS", segments[0]);
        Assert.Equal("MSA|AE|MSG123|MSH-15: is not in message", segments[1]);
        Assert.StartsWith("ERR|MSH^1^15^101&Required field missing&HL70357&PROFILE_REQUIRED&", segments[2]);
        Assert.DoesNotContain("ERR||", ack);
    }

    [Fact]
    public void V24_ack_repeats_errors_inside_err_1()
    {
        const string msh = "MSH|^~\\&|Sender|SendFac|Receiver|RecvFac|20260720120000||ADT^A01|MSG123|P|2.4";
        ValidationOutcome outcome = BuildMissingFieldOutcome();
        outcome.Errors.Add(ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"Errors\":[{\"Path\":\"PID-5.1\",\"Reason\":\"is not equal to expected\"}]}",
            "Profile").Errors[0]);

        string ack = Hl7AcknowledgmentBuilder.Build(msh, outcome);
        string err = ack.Split('\r')[2];

        Assert.Contains("~PID^1^5^102&Data type error&HL70357&PROFILE_RULE&", err);
        Assert.Single(ack.Split('\r'), segment => segment.StartsWith("ERR|", StringComparison.Ordinal));
    }

    [Fact]
    public void V21_ack_uses_the_basic_legacy_error_location_and_code()
    {
        const string msh = "MSH|^~\\&|Sender|SendFac|Receiver|RecvFac|20260720120000||ADT^A01|MSG123|P|2.1";

        string ack = Hl7AcknowledgmentBuilder.Build(msh, BuildMissingFieldOutcome());

        Assert.Equal("ERR|MSH^1^15^101", ack.Split('\r')[2]);
    }

    [Fact]
    public void V251_ack_uses_modern_err_fields_and_keeps_literal_path()
    {
        const string msh = "MSH|^~\\&|Sender|SendFac|Receiver|RecvFac|20260720120000||ADT^A01^ADT_A01|MSG123|P|2.5.1";
        ValidationOutcome outcome = BuildMissingFieldOutcome();

        string ack = Hl7AcknowledgmentBuilder.Build(msh, outcome);
        string err = ack.Split('\r')[2];

        Assert.StartsWith("ERR||MSH^1^15|101^Required field missing^HL70357|E|PROFILE_REQUIRED^Required by validation profile^L||", err);
        Assert.Contains("Path: MSH-15", err);
        Assert.EndsWith("|MSH-15: is not in message", err);
    }

    [Fact]
    public void Msa_2_echoes_an_already_encoded_control_id_without_double_escaping()
    {
        const string msh = "MSH|^~\\&|Sender|SendFac|Receiver|RecvFac|20260720120000||ADT^A01|ABC\\F\\123|P|2.5.1";

        string ack = Hl7AcknowledgmentBuilder.Build(msh, BuildMissingFieldOutcome());

        Assert.StartsWith("MSA|AE|ABC\\F\\123|", ack.Split('\r')[1]);
        Assert.DoesNotContain("ABC\\E\\F\\E\\123", ack);
    }

    [Fact]
    public void Custom_delimiters_are_preserved_and_free_text_is_escaped()
    {
        const string msh = "MSH*$%!\\*Sender*SendFac*Receiver*RecvFac*20260720120000**ADT$A01*MSG123*P*2.5.1";
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile * $ % ! slash\",\"Errors\":[{\"Path\":\"PID-5.1\",\"Reason\":\"bad * $ % ! value\"}]}",
            "Profile * $ % ! slash");

        string ack = Hl7AcknowledgmentBuilder.Build(msh, outcome);
        string[] segments = ack.Split('\r');

        Assert.StartsWith("MSH*$%!\\*Receiver*RecvFac*Sender*SendFac*20260720120000**ACK$A01*MSG123*P*2.5.1", segments[0]);
        Assert.StartsWith("MSA*AE*MSG123*PID-5.1: bad !F! !S! !R! !E! value", segments[1]);
        Assert.StartsWith("ERR**PID$1$5$$1*102$Data type error$HL70357*E*PROFILE_RULE$Validation profile rule failed$L**", segments[2]);
        Assert.Contains("Profile !F! !S! !R! !E! slash", segments[2]);
    }

    [Fact]
    public void V27_truncation_character_is_escaped_in_free_text()
    {
        const string msh = "MSH|^~\\&#|Sender|SendFac|Receiver|RecvFac|20260720120000||ADT^A01|MSG123|P|2.7";
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile#1\",\"Errors\":[{\"Path\":\"PID-5.1\",\"Reason\":\"value # was truncated\"}]}",
            "Profile#1");

        string ack = Hl7AcknowledgmentBuilder.Build(msh, outcome);

        Assert.Contains("value \\P\\ was truncated", ack);
        Assert.Contains("Profile\\P\\1", ack);
    }

    [Fact]
    public void Msa_3_is_limited_to_eighty_encoded_characters()
    {
        const string msh = "MSH|^~\\&|Sender|SendFac|Receiver|RecvFac|20260720120000||ADT^A01|MSG123|P|2.4";
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"Errors\":[{\"Path\":\"PID-5.1\",\"Reason\":\"" + new string('x', 200) + "\"}]}",
            "Profile");

        string msa3 = ackField(Hl7AcknowledgmentBuilder.Build(msh, outcome).Split('\r')[1], 3);

        Assert.Equal(80, msa3.Length);
    }

    [Fact]
    public void Modern_diagnostic_and_user_message_respect_field_lengths()
    {
        const string msh = "MSH|^~\\&|Sender|SendFac|Receiver|RecvFac|20260720120000||ADT^A01|MSG123|P|2.5.1";
        ValidationOutcome outcome = ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"" + new string('p', 2200) + "\",\"Errors\":[{\"Path\":\"PID-5.1\",\"Reason\":\"" + new string('x', 500) + "\"}]}",
            new string('p', 2200));

        string[] errFields = Hl7AcknowledgmentBuilder.Build(msh, outcome).Split('\r')[2].Split('|');

        Assert.Equal(2048, errFields[7].Length);
        Assert.Equal(250, errFields[8].Length);
    }

    [Fact]
    public void Successful_validation_returns_aa_without_err()
    {
        const string msh = "MSH|^~\\&|Sender|SendFac|Receiver|RecvFac|20260720120000||ADT^A01|MSG123|P|2.3.1";
        ValidationOutcome outcome = new ValidationOutcome
        {
            Profile = "Profile",
            HasErrors = false,
            AcknowledgmentCode = "AA"
        };

        string ack = Hl7AcknowledgmentBuilder.Build(msh, outcome);

        Assert.Equal("MSH|^~\\&|Receiver|RecvFac|Sender|SendFac|20260720120000||ACK^A01|MSG123|P|2.3.1\rMSA|AA|MSG123", ack);
        Assert.DoesNotContain("ERR", ack);
    }

    private static ValidationOutcome BuildMissingFieldOutcome()
    {
        return ValidationOutcomeBuilder.Build(
            "{\"Profile\":\"Profile\",\"Errors\":[{\"Path\":\"MSH-15\",\"Reason\":\"is not in message\"}]}",
            "Profile");
    }

    private static string ackField(string segment, int index)
    {
        return segment.Split('|')[index];
    }
}
