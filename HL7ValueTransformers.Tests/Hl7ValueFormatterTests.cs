using HL7Soup.Integrations;
using HL7ValueTransformers;

namespace HL7ValueTransformers.Tests;

public sealed class Hl7ValueFormatterTests
{
    [Theory]
    [InlineData("(021) 555-0100", "0215550100")]
    [InlineData("abc 123", "123")]
    public void DigitsOnly_keeps_only_numeric_digits(string input, string expected)
    {
        Assert.Equal(expected, Hl7ValueFormatter.FormatDigitsOnly(input));
    }

    [Theory]
    [InlineData("z aa-0067", "ZAA0067")]
    [InlineData("abc-123!", "ABC123")]
    public void LettersAndDigitsOnly_keeps_letters_and_digits_and_uppercases(string input, string expected)
    {
        Assert.Equal(expected, Hl7ValueFormatter.FormatLettersAndDigitsOnly(input));
    }

    [Theory]
    [InlineData("abc, 123   def!", "ABC 123 DEF")]
    [InlineData("  a\tb-- c  ", "A B C")]
    public void LettersDigitsAndSpacesOnly_removes_punctuation_and_collapses_spaces(string input, string expected)
    {
        Assert.Equal(expected, Hl7ValueFormatter.FormatLettersDigitsAndSpacesOnly(input));
    }

    [Theory]
    [InlineData("Example Patient", "Patient^Example")]
    [InlineData("Patient, Example", "Patient^Example")]
    [InlineData("Dr Example Doctor", "Doctor^Example^^^Dr")]
    [InlineData("Example Middle Patient", "Patient^Example Middle")]
    [InlineData("Professor Example Doctor", "Doctor^Example^^^Prof")]
    public void Hl7Xpn_formats_display_names(string input, string expected)
    {
        Assert.Equal(expected, Hl7ValueFormatter.FormatHl7Xpn(input));
    }

    [Fact]
    public void Hl7Xpn_escapes_existing_components()
    {
        Assert.Equal(@"Family^Given\F\Name^Middle\E\Name", Hl7ValueFormatter.FormatHl7Xpn(@"Family^Given|Name^Middle\Name"));
    }

    [Theory]
    [InlineData("1 Example Street\nWollongong NSW 2500", "1 Example Street^^Wollongong^NSW^2500")]
    [InlineData("Flat 2\n1 Example Street\nAuckland 1010", "1 Example Street^Flat 2^Auckland^^1010")]
    [InlineData("Flat 2, 1 Example Street, Auckland, 1010", "1 Example Street^Flat 2^Auckland^^1010")]
    public void Hl7Xad_formats_common_address_shapes(string input, string expected)
    {
        Assert.Equal(expected, Hl7ValueFormatter.FormatHl7Xad(input));
    }

    [Fact]
    public void Hl7Xad_escapes_components()
    {
        Assert.Equal(@"1 Example \T\ Test Street^^Wollongong^NSW^2500", Hl7ValueFormatter.FormatHl7Xad("1 Example & Test Street\nWollongong NSW 2500"));
    }

    [Theory]
    [InlineData("+64 (0)21 555 0100", "+64215550100")]
    [InlineData("0061 (0)2 5555 0100", "+61255550100")]
    [InlineData("+64+21 555 0100", "+64215550100")]
    [InlineData("09 555 0100 ext 123", "095550100^^^^^^^123")]
    [InlineData("09 555 0100 #123", "095550100^^^^^^^123")]
    public void Hl7Xtn_formats_phone_numbers(string input, string expected)
    {
        Assert.Equal(expected, Hl7ValueFormatter.FormatHl7Xtn(input));
    }

    [Fact]
    public void Sample_acc_block_name_and_address_lines_format_for_hl7_insertion()
    {
        Assert.Equal("DUCK^DONALD X", Hl7ValueFormatter.FormatHl7Xpn("DONALD X DUCK"));
        Assert.Equal("1 WALSH STREET^^HAMILTON^^3200", Hl7ValueFormatter.FormatHl7Xad("1 WALSH STREET\nHAMILTON 3200"));
    }

    [Fact]
    public void Transformer_sets_message_text_to_formatted_value()
    {
        TestMessage message = new TestMessage("(021) 555-0100");

        new DigitsOnlyTransformer().Transform(null!, message, new Dictionary<string, string>());

        Assert.Equal("0215550100", message.Text);
    }

    private sealed class TestMessage : IMessage
    {
        public TestMessage(string text)
        {
            Text = text;
        }

        public string Text { get; private set; }

        public string GetValueAtPath(string path)
        {
            throw new NotSupportedException();
        }

        public void SetValueAtPath(string toPath, string fromValue)
        {
            throw new NotSupportedException();
        }

        public void SetStructureAtPath(string toPath, string fromValue)
        {
            throw new NotSupportedException();
        }

        public void SetText(string text)
        {
            Text = text;
        }

        public void Dispose()
        {
        }
    }
}
