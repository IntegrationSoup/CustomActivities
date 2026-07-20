using HL7Soup.BaseTypes;
using HL7Soup.Integrations;
using HL7Soup.Integrations.MessageTypeOptions;
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
    [InlineData("Example Middle Patient", "Patient^Example^Middle")]
    [InlineData("Patient, Example Middle", "Patient^Example^Middle")]
    [InlineData("Donald X Duck", "Duck^Donald^X")]
    [InlineData("Professor Example Doctor", "Doctor^Example^^^Prof")]
    public void Hl7Xpn_formats_display_names(string input, string expected)
    {
        Assert.Equal(expected, Hl7ValueFormatter.FormatHl7Xpn(input));
    }

    [Theory]
    [InlineData("CORLEY BRIAN THOMAS", "LFM", "CORLEY^BRIAN^THOMAS")]
    [InlineData("CORLEY, BRIAN THOMAS", "L,F M", "CORLEY^BRIAN^THOMAS")]
    [InlineData("BRIAN THOMAS CORLEY", "FML", "CORLEY^BRIAN^THOMAS")]
    [InlineData("Dr BRIAN THOMAS CORLEY", "TFML", "CORLEY^BRIAN^THOMAS^^Dr")]
    [InlineData("Christina (Chrissy) Smith", "", "Smith^Christina (Chrissy)")]
    public void Hl7Xpn_supports_name_order_and_bracketed_nicknames(string input, string nameOrder, string expected)
    {
        Assert.Equal(expected, Hl7ValueFormatter.FormatHl7Xpn(input, nameOrder));
    }

    [Fact]
    public void Hl7Xpn_escapes_existing_components()
    {
        Assert.Equal(@"Family^Given\F\Name^Middle\E\Name", Hl7ValueFormatter.FormatHl7Xpn(@"Family^Given|Name^Middle\Name"));
    }

    [Theory]
    [InlineData("CORLEY BRIAN THOMAS", "", "", "^THOMAS^CORLEY^BRIAN")]
    [InlineData("CORLEY BRIAN THOMAS", "LFM", "", "^CORLEY^BRIAN^THOMAS")]
    [InlineData("Dr CORLEY BRIAN THOMAS", "LFM", "", "^CORLEY^BRIAN^THOMAS^^Dr")]
    [InlineData("CORLEY BRIAN THOMAS", "LFM", "12345", "12345^CORLEY^BRIAN^THOMAS")]
    [InlineData("12345 CORLEY BRIAN THOMAS", "", "", "12345^THOMAS^CORLEY^BRIAN")]
    [InlineData("A1234 CORLEY BRIAN THOMAS", "I L F M", "", "A1234^CORLEY^BRIAN^THOMAS")]
    [InlineData("Christina Smith (A1234)", "", "", "A1234^Smith^Christina")]
    [InlineData("12345 Christina (Chrissy) Smith", "", "", "12345^Smith^Christina (Chrissy)")]
    public void Hl7Xcn_formats_clinician_names_and_identifiers(string input, string nameOrder, string personIdentifier, string expected)
    {
        Assert.Equal(expected, Hl7ValueFormatter.FormatHl7Xcn(input, nameOrder, personIdentifier));
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
        Assert.Equal("DUCK^DONALD^X", Hl7ValueFormatter.FormatHl7Xpn("DONALD X DUCK"));
        Assert.Equal("1 WALSH STREET^^HAMILTON^^3200", Hl7ValueFormatter.FormatHl7Xad("1 WALSH STREET\nHAMILTON 3200"));
    }

    [Fact]
    public void Transformer_sets_default_output_variable_from_formatted_value_parameter()
    {
        TestWorkflowInstance workflowInstance = new TestWorkflowInstance();
        TestMessage message = new TestMessage("ignored");

        new DigitsOnlyTransformer().Transform(
            workflowInstance,
            message,
            new Dictionary<string, string>
            {
                ["Value"] = "(021) 555-0100"
            });

        Assert.Equal("0215550100", workflowInstance.GetVariable("Digits"));
        Assert.Equal("ignored", message.Text);
    }

    [Fact]
    public void Transformer_uses_output_variable_parameter()
    {
        TestWorkflowInstance workflowInstance = new TestWorkflowInstance();
        TestMessage message = new TestMessage("ignored");

        new DigitsOnlyTransformer().Transform(
            workflowInstance,
            message,
            new Dictionary<string, string>
            {
                ["Value"] = "(021) 555-0100",
                ["Output Variable"] = "PhoneDigits"
            });

        Assert.Equal("0215550100", workflowInstance.GetVariable("PhoneDigits"));
        Assert.Equal(string.Empty, workflowInstance.GetVariable("Digits"));
        Assert.Equal("ignored", message.Text);
    }

    [Fact]
    public void Xpn_transformer_uses_name_order_parameter()
    {
        TestWorkflowInstance workflowInstance = new TestWorkflowInstance();
        TestMessage message = new TestMessage("ignored");

        new TextToHl7NameTransformer().Transform(
            workflowInstance,
            message,
            new Dictionary<string, string>
            {
                ["Value"] = "CORLEY BRIAN THOMAS",
                ["Name Order"] = "LFM"
            });

        Assert.Equal("CORLEY^BRIAN^THOMAS", workflowInstance.GetVariable("XPN"));
        Assert.Equal("ignored", message.Text);
    }

    [Fact]
    public void Xcn_transformer_uses_identifier_and_name_order_parameters()
    {
        TestWorkflowInstance workflowInstance = new TestWorkflowInstance();
        TestMessage message = new TestMessage("ignored");

        new TextToHl7ClinicianNameTransformer().Transform(
            workflowInstance,
            message,
            new Dictionary<string, string>
            {
                ["Value"] = "CORLEY BRIAN THOMAS",
                ["Name Order"] = "LFM",
                ["Person Identifier"] = "12345"
            });

        Assert.Equal("12345^CORLEY^BRIAN^THOMAS", workflowInstance.GetVariable("XCN"));
        Assert.Equal("ignored", message.Text);
    }

    [Fact]
    public void Transformer_falls_back_to_message_text_when_value_parameter_is_missing()
    {
        TestWorkflowInstance workflowInstance = new TestWorkflowInstance();
        TestMessage message = new TestMessage("(021) 555-0100");

        new DigitsOnlyTransformer().Transform(workflowInstance, message, new Dictionary<string, string>());

        Assert.Equal("0215550100", workflowInstance.GetVariable("Digits"));
        Assert.Equal("(021) 555-0100", message.Text);
    }

    private sealed class TestWorkflowInstance : IWorkflowInstance
    {
        private readonly Dictionary<string, string> variables = new Dictionary<string, string>();

        public List<IActivityInstance> Activities { get; set; } = new List<IActivityInstance>();

        public int InstanceId { get; set; }

        public Guid Id { get; } = Guid.NewGuid();

        public IActivityInstance CurrentActivityInstance => throw new NotSupportedException();

        public IActivityInstance ReceivingActivityInstance => throw new NotSupportedException();

        public IActivityInstance GetActivityInstance(Guid settingId)
        {
            throw new NotSupportedException();
        }

        public IMessage CreateMessage(MessageTypes messageType, string text)
        {
            return new TestMessage(text);
        }

        public IMessage CreateMessage(MessageTypes messageType, string text, IMessageTypeOptions options)
        {
            return new TestMessage(text);
        }

        public string GetVariable(string variableName)
        {
            return variables.TryGetValue(variableName, out string? value) ? value : string.Empty;
        }

        public void SetVariable(string variableName, string value)
        {
            variables[variableName] = value;
        }

        public void CreateNotification(string text)
        {
            throw new NotSupportedException();
        }

        public void CreateNotification(string text, string uniquenessCode)
        {
            throw new NotSupportedException();
        }

        public void CreateNotification(string text, bool critical, string uniquenessCode)
        {
            throw new NotSupportedException();
        }

        public string LookupValue(string lookupTableName, string from)
        {
            throw new NotSupportedException();
        }

        public string DataTableGetValue(string dataTableName, string fieldName)
        {
            throw new NotSupportedException();
        }

        public string DataTableGetValue(string dataTableName)
        {
            throw new NotSupportedException();
        }

        public void DataTableMoveToRow(string dataTableName, string key)
        {
            throw new NotSupportedException();
        }

        public string DataTableGetRandomValue(string dataTableName)
        {
            throw new NotSupportedException();
        }

        public void DataTableMoveToNextRow(string dataTableName)
        {
            throw new NotSupportedException();
        }

        public void DataTableMoveToRandomRow(string dataTableName)
        {
            throw new NotSupportedException();
        }

        public List<string> DataTableGetFieldNames(string dataTableName)
        {
            throw new NotSupportedException();
        }

        public int DataTableGetRowCount(string dataTableName)
        {
            throw new NotSupportedException();
        }

        public bool DataTableContainsRow(string dataTableName, string key)
        {
            throw new NotSupportedException();
        }
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
