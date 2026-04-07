using System.Text.RegularExpressions;

namespace WorkTools.Core.Tests;

[TestClass]
public class TemplateExpanderTests
{
    private const string NumericSection =
        """

        [Numeric.1.0]

        Col1	Col2	Col3
        A1	A2	A3
        B1	B2	B3
        """;

    private const string FlagSection =
        """

        [Flag.2.1]

        Enabled	Visible
        Yes	No
        """;

    private const string StringSection =
        """

        [String.3.2]

        Name	Value	Description
        Alpha	100	First item
        Beta	200	Second item
        """;

    private const string MultiSectionTemplate =
        """

        [Numeric.1.0]

        Col1	Col2	Col3
        {{1}}_A1	{{1}}_A2	{{1}}_A3

        [Flag.2.1]

        Enabled	Visible
        {{1}}_Yes	{{1}}_No

        [String.3.2]

        Name	Value	Description
        {{1}}_Alpha	{{1}}_100	{{1}}_First
        """;

    private const string MultiPlaceholderTemplate =
        """

        [Numeric.1.0]

        Name	Value	Alias
        {{1}}	[PLC.{{2}}]	{{3}}
        """;

    private const string MultiPlaceholderRows = "Motor1\tPump101\tP-101\nMotor2\tPump102\tP-102";
    private const string Tags = "Tag1\nTag2";
    private const string RequiredTagColumns =
        "Name\tValue\tLabel\tAlias\tDesc\tClass\tFormType\tColType\tSec / Access\tSec / Logging";
    private const string RequiredTagDataRow =
        "A\tB\tL\tAl\tD\tC\tNumeric\tGeneral\tAuthenticated Users\tDefault for Object";

    private static string[] SplitOutputLines(string output) =>
        output.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

    [TestMethod]
    public void ParseTemplate_NumericSection_ParsesCorrectly()
    {
        string[] lines = SplitOutputLines(NumericSection);
        var sections = TemplateExpander.ParseTemplate(lines);

        Assert.AreEqual(1, sections.Count);
        Assert.AreEqual("[Numeric.1.0]", sections[0].SectionHeader);
        Assert.AreEqual("Col1\tCol2\tCol3", sections[0].ColumnHeader);
        Assert.AreEqual(2, sections[0].DataRows.Count);
    }

    [TestMethod]
    public void ParseTemplate_FlagSection_ParsesCorrectly()
    {
        string[] lines = SplitOutputLines(FlagSection);
        var sections = TemplateExpander.ParseTemplate(lines);

        Assert.AreEqual(1, sections.Count);
        Assert.AreEqual("[Flag.2.1]", sections[0].SectionHeader);
        Assert.AreEqual("Enabled\tVisible", sections[0].ColumnHeader);
        Assert.AreEqual(1, sections[0].DataRows.Count);
    }

    [TestMethod]
    public void ParseTemplate_StringSection_ParsesCorrectly()
    {
        string[] lines = SplitOutputLines(StringSection);
        var sections = TemplateExpander.ParseTemplate(lines);

        Assert.AreEqual(1, sections.Count);
        Assert.AreEqual("[String.3.2]", sections[0].SectionHeader);
        Assert.AreEqual("Name\tValue\tDescription", sections[0].ColumnHeader);
        Assert.AreEqual(2, sections[0].DataRows.Count);
    }

    [TestMethod]
    public void ParseTemplate_MultiSection_ParsesAllSections()
    {
        string[] lines = SplitOutputLines(MultiSectionTemplate);
        var sections = TemplateExpander.ParseTemplate(lines);

        Assert.AreEqual(3, sections.Count);
        Assert.AreEqual("[Numeric.1.0]", sections[0].SectionHeader);
        Assert.AreEqual("[Flag.2.1]", sections[1].SectionHeader);
        Assert.AreEqual("[String.3.2]", sections[2].SectionHeader);
    }

    [TestMethod]
    public void ParseTemplate_SectionHeaders_MatchDatatypeFormat()
    {
        string[] lines = SplitOutputLines(MultiSectionTemplate);
        var sections = TemplateExpander.ParseTemplate(lines);
        var headerPattern = new Regex(@"^\[(Numeric|Flag|String)\.\d\.\d\]$");

        foreach (var section in sections)
        {
            Assert.IsTrue(
                headerPattern.IsMatch(section.SectionHeader),
                $"Section header '{section.SectionHeader}' does not match [Datatype.N.N] format.");
        }
    }

    [TestMethod]
    public void ValidateTemplate_FormatTypeValidation_FlagWithFormatType5_IsValid()
    {
        string template =
            """

            [Flag.5.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Two-State	General	Authenticated Users	Default for Object
            """;
        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_FormatTypeValidation_FlagWithFormatType8_ReturnsError()
    {
        string template =
            """

            [Flag.8.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Two-State	General	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("Format Type '8 (String)'", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ValidateTemplate_FormatTypeValidation_StringWithFormatType8_IsValid()
    {
        string template =
            """

            [String.8.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	String	General	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_ColorTypeValidation_FlagWithColorType2_IsValid()
    {
        string template =
            """

            [Flag.5.2]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Color / On	Color / Off	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Two-State	Two-State	Blue on Black	Black on White	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_ColorTypeValidation_StringWithColorType2_ReturnsError()
    {
        string template =
            """

            [String.8.2]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Color / On	Color / Off	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	String	Two-State	Blue on Black	Black on White	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("Color Type '2 (Two-State)'", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ValidateTemplate_ColorTypeValidation_ErrorIncludesColorTypeName()
    {
        string template =
            """

            [String.8.2]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Color / On	Color / Off	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	String	Two-State	Blue on Black	Black on White	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("2 (Two-State)", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ValidateTemplate_ColorTypeValidation_NumericWithColorType3_IsValid()
    {
        string template =
            """

            [Numeric.6.3]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Multi-State	Multi-State	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_FormType_MismatchWithSectionFormatType_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Linked	General	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("does not match section Format Type 'Numeric'", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_FormType_LinkedMissingFormatLinkColumn_ReturnsError()
    {
        string template =
            """

            [Numeric.7.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Linked	General	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("requires column 'Format / Link'", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_FormType_LinkedBlankFormatLink_ReturnsError()
    {
        string template =
            """

            [Numeric.7.0]

            Name	Value	Label	Alias	Desc	Class	FormType	Format / Link	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Linked		General	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("required field 'Format / Link' cannot be blank", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_ColType_MismatchWithSectionColorType_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	Fixed	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("does not match section Color Type 'General'", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_ColType_TwoStateMissingColorColumns_ReturnsError()
    {
        string template =
            """

            [Flag.5.2]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Two-State	Two-State	Authenticated Users	Default for Object
            """;
        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("requires column 'Color / On'", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(errors.Any(e => e.Contains("requires column 'Color / Off'", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_ColType_TwoStateBlankColorValue_ReturnsError()
    {
        string template =
            """

            [Flag.5.2]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Color / On	Color / Off	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Two-State	Two-State		Black on White	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("required field 'Color / On' cannot be blank", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_RequiredTagFields_MissingFields_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value
            A	B
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("missing required tag field(s)", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_SecAccess_LiteralValue_IsValid()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	No Access	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_SecAccess_RoleListWithCbo_IsValid()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	M¬R1¬P with CBO	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_SecAccess_InvalidToken_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	M¬R9	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("invalid 'Sec / Access' value", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_SecAccess_DuplicateToken_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	M¬R1¬R1	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("Duplicate access token(s): R1", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_Persist_Retentive_IsValid()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Persist	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	Retentive	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_Persist_Blank_IsValid()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Persist	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General		Authenticated Users	Default for Object
            """;
        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_OptionalFields_AllBlank_IsValid()
    {
        var rows = new[]
        {
            "Name\tValue\tLabel\tAlias\tDesc\tClass\tFormType\tColType\tSec / Access\tSec / Logging",
            "TagA\t100\t\t\t\t\tNumeric\tGeneral\tAuthenticated Users\tDefault for Object"
        };

        string template = $"""

            [Numeric.1.0]

            {rows[0]}
            {rows[1]}
            """;
        var errors = TemplateExpander.ValidateTemplate(template);

        if (errors.Count > 0)
        {
            string errorDetails = string.Join("; ", errors);
            Assert.Fail($"Expected no errors but got: {errorDetails}");
        }

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_RequiredField_Name_Blank_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            	100	L	Al	D	C	Numeric	General	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("required field 'Name' cannot be blank", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_RequiredField_Value_Blank_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            TagA		L	Al	D	C	Numeric	General	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("required field 'Value' cannot be blank", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_Persist_InvalidValue_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Persist	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	Sticky	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("invalid 'Persist' value", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_Access_ReadOnly_IsValid()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Access	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	Read Only	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_Access_InvalidValue_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Access	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	Execute	Authenticated Users	Default for Object
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("invalid 'Access' value", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void ValidateTemplate_SecLogging_LogChangesByUsers_IsValid()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	Authenticated Users	Log Changes by Users
            """;
        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.AreEqual(0, errors.Count);
    }

    [TestMethod]
    public void ValidateTemplate_SecLogging_InvalidValue_ReturnsError()
    {
        string template =
            """

            [Numeric.1.0]

            Name	Value	Label	Alias	Desc	Class	FormType	ColType	Sec / Access	Sec / Logging
            A	B	L	Al	D	C	Numeric	General	Authenticated Users	Always Log
            """;

        var errors = TemplateExpander.ValidateTemplate(template);

        Assert.IsTrue(errors.Any(e => e.Contains("invalid 'Sec / Logging' value", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void GeneratePreview_OutputSections_StartWithBlankLine()
    {
        string output = TemplateExpander.GeneratePreview(MultiSectionTemplate, Tags);
        string[] lines = SplitOutputLines(output);

        Assert.AreEqual(string.Empty, lines[0], "Output should start with a blank line.");
    }

    [TestMethod]
    public void GeneratePreview_EachSection_HasBlankLineThenHeaderThenBlankLineThenColumns()
    {
        string output = TemplateExpander.GeneratePreview(MultiSectionTemplate, Tags);
        string[] lines = SplitOutputLines(output);
        var headerPattern = new Regex(@"^\[(Numeric|Flag|String)\.\d\.\d\]$");

        // Find each section header and verify the surrounding structure
        for (int i = 0; i < lines.Length; i++)
        {
            if (headerPattern.IsMatch(lines[i]))
            {
                // Line before section header should be blank
                Assert.IsTrue(i > 0, "Section header should not be the first line.");
                Assert.AreEqual(string.Empty, lines[i - 1],
                    $"Line before section header '{lines[i]}' at index {i} should be blank.");

                // Line after section header should be blank
                Assert.IsTrue(i + 1 < lines.Length, "Section header should not be the last line.");
                Assert.AreEqual(string.Empty, lines[i + 1],
                    $"Line after section header '{lines[i]}' at index {i} should be blank.");

                // Line after the blank should be the column header (tab-separated)
                Assert.IsTrue(i + 2 < lines.Length, "Column header line expected after blank line.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(lines[i + 2]),
                    $"Column header line after section '{lines[i]}' should not be blank.");
            }
        }
    }

    [TestMethod]
    public void GeneratePreview_DataRows_HaveFieldForEachHeader()
    {
        string output = TemplateExpander.GeneratePreview(MultiSectionTemplate, Tags);
        string[] lines = SplitOutputLines(output);
        var headerPattern = new Regex(@"^\[(Numeric|Flag|String)\.\d\.\d\]$");

        for (int i = 0; i < lines.Length; i++)
        {
            if (!headerPattern.IsMatch(lines[i]))
                continue;

            // Column header is at i+2
            string columnHeaderLine = lines[i + 2];
            int expectedFieldCount = columnHeaderLine.Split('\t').Length;

            // Data rows follow starting at i+3 until next blank or end
            for (int d = i + 3; d < lines.Length; d++)
            {
                if (string.IsNullOrWhiteSpace(lines[d]))
                    break;

                int actualFieldCount = lines[d].Split('\t').Length;
                Assert.AreEqual(expectedFieldCount, actualFieldCount,
                    $"Data row at line {d} ('{lines[d]}') has {actualFieldCount} field(s) " +
                    $"but header '{columnHeaderLine}' has {expectedFieldCount} field(s).");
            }
        }
    }

    [TestMethod]
    public void GeneratePreview_DataRows_AreTabSeparated()
    {
        string output = TemplateExpander.GeneratePreview(MultiSectionTemplate, Tags);
        string[] lines = SplitOutputLines(output);
        var headerPattern = new Regex(@"^\[(Numeric|Flag|String)\.\d\.\d\]$");

        for (int i = 0; i < lines.Length; i++)
        {
            if (!headerPattern.IsMatch(lines[i]))
                continue;

            // Data rows start at i+3
            for (int d = i + 3; d < lines.Length; d++)
            {
                if (string.IsNullOrWhiteSpace(lines[d]))
                    break;

                Assert.IsTrue(lines[d].Contains('\t'),
                    $"Data row at line {d} ('{lines[d]}') should be tab-separated.");
            }
        }
    }

    [TestMethod]
    public void GeneratePreview_TagReplacements_AppliedToAllSections()
    {
        string output = TemplateExpander.GeneratePreview(MultiSectionTemplate, Tags);

        Assert.IsFalse(output.Contains("{{1}}"),
            "Output should not contain unreplaced {{1}} placeholders.");
        Assert.IsTrue(output.Contains("Tag1"), "Output should contain expanded Tag1.");
        Assert.IsTrue(output.Contains("Tag2"), "Output should contain expanded Tag2.");
    }

    [TestMethod]
    public void GeneratePreviewBySections_ReturnsOneEntryPerSection()
    {
        var results = TemplateExpander.GeneratePreviewBySections(MultiSectionTemplate, Tags);

        Assert.AreEqual(3, results.Count);
        Assert.AreEqual("Numeric.1.0", results[0].SectionHeader);
        Assert.AreEqual("Flag.2.1", results[1].SectionHeader);
        Assert.AreEqual("String.3.2", results[2].SectionHeader);
    }

    [TestMethod]
    public void GeneratePreviewBySections_EachSectionContent_StartsWithColumnHeader()
    {
        var results = TemplateExpander.GeneratePreviewBySections(MultiSectionTemplate, Tags);

        string[] expectedHeaders = ["Col1\tCol2\tCol3", "Enabled\tVisible", "Name\tValue\tDescription"];

        for (int i = 0; i < results.Count; i++)
        {
            string firstLine = SplitOutputLines(results[i].Content)[0];
            Assert.AreEqual(expectedHeaders[i], firstLine,
                $"Section '{results[i].SectionHeader}' content should start with its column header.");
        }
    }

    [TestMethod]
    public void GeneratePreviewBySections_DataRows_HaveFieldForEachHeader()
    {
        var results = TemplateExpander.GeneratePreviewBySections(MultiSectionTemplate, Tags);

        foreach (var (sectionHeader, content) in results)
        {
            string[] contentLines = SplitOutputLines(content);
            string columnHeaderLine = contentLines[0];
            int expectedFieldCount = columnHeaderLine.Split('\t').Length;

            for (int d = 1; d < contentLines.Length; d++)
            {
                if (string.IsNullOrWhiteSpace(contentLines[d]))
                    continue;

                int actualFieldCount = contentLines[d].Split('\t').Length;
                Assert.AreEqual(expectedFieldCount, actualFieldCount,
                    $"Section '{sectionHeader}' data row at line {d} has {actualFieldCount} field(s) " +
                    $"but header has {expectedFieldCount} field(s).");
            }
        }
    }

    [TestMethod]
    public void GetStats_OutputLineCount_MatchesGeneratedPreviewLineCount()
    {
        string output = TemplateExpander.GeneratePreview(MultiSectionTemplate, Tags);
        var (_, _, outputLineCount) = TemplateExpander.GetStats(MultiSectionTemplate, Tags);

        Assert.AreEqual(SplitOutputLines(output).Length, outputLineCount);
    }

    [TestMethod]
    public void GetStats_MultiSection_ReturnsCorrectCounts()
    {
        var (tagCount, replacementCount, outputLineCount) =
            TemplateExpander.GetStats(MultiSectionTemplate, Tags);

        Assert.AreEqual(2, tagCount);
        // Per tag row: Numeric has 3 placeholders, Flag has 2, String has 3, so 8 replacements per row × 2 rows = 16.
        Assert.AreEqual(16, replacementCount);
    }

    [TestMethod]
    public void ParseReplacementRows_TrimsValuesAndIgnoresBlankLines()
    {
        string tagsText = " Motor1 \t Pump101 \t P-101 \r\n\r\nMotor2\tPump102\tP-102 ";

        string[][] rows = TemplateExpander.ParseReplacementRows(tagsText);

        Assert.AreEqual(2, rows.Length);
        CollectionAssert.AreEqual(new[] { "Motor1", "Pump101", "P-101" }, rows[0]);
        CollectionAssert.AreEqual(new[] { "Motor2", "Pump102", "P-102" }, rows[1]);
    }

    [TestMethod]
    public void ValidateReplacementData_MissingPlaceholderValue_ReturnsError()
    {
        string tagsText = "Motor1\tPump101\tP-101\nMotor2\tPump102";

        var errors = TemplateExpander.ValidateReplacementData(MultiPlaceholderTemplate, tagsText);

        Assert.AreEqual(1, errors.Count);
        Assert.IsTrue(errors[0].Contains("Replacement row 2", StringComparison.Ordinal));
        Assert.IsTrue(errors[0].Contains("{{3}}", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ParseTagNames_MultiColumnInput_ReturnsFirstColumnValues()
    {
        string[] tags = TemplateExpander.ParseTagNames(MultiPlaceholderRows);

        CollectionAssert.AreEqual(new[] { "Motor1", "Motor2" }, tags);
    }

    [TestMethod]
    public void ParseTagNames_TrimsWhitespaceAndIgnoresBlankLines()
    {
        string tagsText = "  Tag1  \r\n\r\n\tTag2\t\n   ";

        string[] tags = TemplateExpander.ParseTagNames(tagsText);

        CollectionAssert.AreEqual(new[] { "Tag1", "Tag2" }, tags);
    }

    #region Test.txt file-based tests

    private static string TestFilePath => Path.Combine(AppContext.BaseDirectory, "Test.txt");

    private static string ReadTestFile() => File.ReadAllText(TestFilePath);

    private static string[] ReadTestFileLines() => File.ReadAllLines(TestFilePath);

    [TestMethod]
    public void TestFile_ParseTemplate_FindsAllFiveSections()
    {
        string[] lines = ReadTestFileLines();
        var sections = TemplateExpander.ParseTemplate(lines);

        Assert.AreEqual(5, sections.Count);
    }

    [TestMethod]
    public void TestFile_ParseTemplate_SectionHeadersMatchExpected()
    {
        string[] lines = ReadTestFileLines();
        var sections = TemplateExpander.ParseTemplate(lines);

        Assert.AreEqual("[Flag.5.2]", sections[0].SectionHeader);
        Assert.AreEqual("[Numeric.1.0]", sections[1].SectionHeader);
        Assert.AreEqual("[Numeric.6.3]", sections[2].SectionHeader);
        Assert.AreEqual("[Numeric.7.0]", sections[3].SectionHeader);
        Assert.AreEqual("[String.8.1]", sections[4].SectionHeader);
    }

    [TestMethod]
    public void TestFile_ParseTemplate_AllSectionHeaders_MatchDatatypeFormat()
    {
        string[] lines = ReadTestFileLines();
        var sections = TemplateExpander.ParseTemplate(lines);
        var headerPattern = new Regex(@"^\[(Numeric|Flag|String)\.\d\.\d\]$");

        foreach (var section in sections)
        {
            Assert.IsTrue(
                headerPattern.IsMatch(section.SectionHeader),
                $"Section header '{section.SectionHeader}' does not match [Datatype.N.N] format.");
        }
    }

    [TestMethod]
    public void TestFile_ParseTemplate_EachSection_HasAtLeastOneDataRow()
    {
        string[] lines = ReadTestFileLines();
        var sections = TemplateExpander.ParseTemplate(lines);

        foreach (var section in sections)
        {
            Assert.IsTrue(section.DataRows.Count > 0,
                $"Section '{section.SectionHeader}' should have at least one data row.");
        }
    }

    [TestMethod]
    public void TestFile_ParseTemplate_DataRows_HaveFieldForEachHeader()
    {
        string[] lines = ReadTestFileLines();
        var sections = TemplateExpander.ParseTemplate(lines);

        foreach (var section in sections)
        {
            int expectedFieldCount = section.ColumnHeader.Split('\t').Length;

            for (int d = 0; d < section.DataRows.Count; d++)
            {
                int actualFieldCount = section.DataRows[d].Split('\t').Length;
                Assert.AreEqual(expectedFieldCount, actualFieldCount,
                    $"Section '{section.SectionHeader}' data row {d} has {actualFieldCount} field(s) " +
                    $"but header has {expectedFieldCount} field(s).");
            }
        }
    }

    [TestMethod]
    public void TestFile_ParseTemplate_ColumnHeaders_AreTabSeparated()
    {
        string[] lines = ReadTestFileLines();
        var sections = TemplateExpander.ParseTemplate(lines);

        foreach (var section in sections)
        {
            Assert.IsTrue(section.ColumnHeader.Contains('\t'),
                $"Section '{section.SectionHeader}' column header should be tab-separated.");
        }
    }

    [TestMethod]
    public void TestFile_GeneratePreviewBySections_ReturnsOneEntryPerSection()
    {
        string templateText = ReadTestFile();
        string tags = "TestTag";

        var results = TemplateExpander.GeneratePreviewBySections(templateText, tags);

        Assert.AreEqual(5, results.Count);
        Assert.AreEqual("Flag.5.2", results[0].SectionHeader);
        Assert.AreEqual("Numeric.1.0", results[1].SectionHeader);
        Assert.AreEqual("Numeric.6.3", results[2].SectionHeader);
        Assert.AreEqual("Numeric.7.0", results[3].SectionHeader);
        Assert.AreEqual("String.8.1", results[4].SectionHeader);
    }

    [TestMethod]
    public void TestFile_GeneratePreviewBySections_DataRows_HaveFieldForEachHeader()
    {
        string templateText = ReadTestFile();
        string tags = "TestTag";

        var results = TemplateExpander.GeneratePreviewBySections(templateText, tags);

        foreach (var (sectionHeader, content) in results)
        {
            string[] contentLines = SplitOutputLines(content);
            string columnHeaderLine = contentLines[0];
            int expectedFieldCount = columnHeaderLine.Split('\t').Length;

            for (int d = 1; d < contentLines.Length; d++)
            {
                if (string.IsNullOrWhiteSpace(contentLines[d]))
                    continue;

                int actualFieldCount = contentLines[d].Split('\t').Length;
                Assert.AreEqual(expectedFieldCount, actualFieldCount,
                    $"Section '{sectionHeader}' data row at line {d} has {actualFieldCount} field(s) " +
                    $"but header has {expectedFieldCount} field(s).");
            }
        }
    }

    [TestMethod]
    public void TestFile_GeneratePreview_OutputStartsWithBlankLine()
    {
        string templateText = ReadTestFile();
        string tags = "TestTag";

        string output = TemplateExpander.GeneratePreview(templateText, tags);
        string[] lines = SplitOutputLines(output);

        Assert.AreEqual(string.Empty, lines[0], "Output should start with a blank line.");
    }

    [TestMethod]
    public void TestFile_GeneratePreview_EachSection_HasCorrectStructure()
    {
        string templateText = ReadTestFile();
        string tags = "TestTag";

        string output = TemplateExpander.GeneratePreview(templateText, tags);
        string[] lines = SplitOutputLines(output);
        var headerPattern = new Regex(@"^\[(Numeric|Flag|String)\.\d\.\d\]$");

        for (int i = 0; i < lines.Length; i++)
        {
            if (!headerPattern.IsMatch(lines[i]))
                continue;

            // Blank line before section header
            Assert.AreEqual(string.Empty, lines[i - 1],
                $"Line before '{lines[i]}' at index {i} should be blank.");

            // Blank line after section header
            Assert.AreEqual(string.Empty, lines[i + 1],
                $"Line after '{lines[i]}' at index {i} should be blank.");

            // Column header line after the blank
            Assert.IsFalse(string.IsNullOrWhiteSpace(lines[i + 2]),
                $"Column header after '{lines[i]}' should not be blank.");
            Assert.IsTrue(lines[i + 2].Contains('\t'),
                $"Column header after '{lines[i]}' should be tab-separated.");
        }
    }

    [TestMethod]
    public void TestFile_GeneratePreview_DataRows_AreTabSeparated()
    {
        string templateText = ReadTestFile();
        string tags = "TestTag";

        string output = TemplateExpander.GeneratePreview(templateText, tags);
        string[] lines = SplitOutputLines(output);
        var headerPattern = new Regex(@"^\[(Numeric|Flag|String)\.\d\.\d\]$");

        for (int i = 0; i < lines.Length; i++)
        {
            if (!headerPattern.IsMatch(lines[i]))
                continue;

            // Data rows start at i+3
            for (int d = i + 3; d < lines.Length; d++)
            {
                if (string.IsNullOrWhiteSpace(lines[d]))
                    break;

                Assert.IsTrue(lines[d].Contains('\t'),
                    $"Data row at line {d} under '{lines[i]}' should be tab-separated.");
            }
        }
    }

    [TestMethod]
    public void TestFile_GeneratePreview_DataRows_HaveFieldForEachHeader()
    {
        string templateText = ReadTestFile();
        string tags = "TestTag";

        string output = TemplateExpander.GeneratePreview(templateText, tags);
        string[] lines = SplitOutputLines(output);
        var headerPattern = new Regex(@"^\[(Numeric|Flag|String)\.\d\.\d\]$");

        for (int i = 0; i < lines.Length; i++)
        {
            if (!headerPattern.IsMatch(lines[i]))
                continue;

            // Column header is at i+2
            string columnHeaderLine = lines[i + 2];
            int expectedFieldCount = columnHeaderLine.Split('\t').Length;

            for (int d = i + 3; d < lines.Length; d++)
            {
                if (string.IsNullOrWhiteSpace(lines[d]))
                    break;

                int actualFieldCount = lines[d].Split('\t').Length;
                Assert.AreEqual(expectedFieldCount, actualFieldCount,
                    $"Data row at line {d} under '{lines[i]}' has {actualFieldCount} field(s) " +
                    $"but header has {expectedFieldCount} field(s).");
            }
        }
    }

    [TestMethod]
    public void TestFile_ParseCrimsonExportTagNames_ExtractsKnownTagNames()
    {
        string templateText = ReadTestFile();

        string[] tags = TemplateExpander.ParseCrimsonExportTagNames(templateText);

        Assert.IsTrue(tags.Length > 0);
        CollectionAssert.Contains(tags, "VFD.AC_2210A_C.OCmd_AcqLock");
    }

    [TestMethod]
    public void TestFile_BuildTagListFromCrimsonExport_ReturnsOneTagPerLine()
    {
        string templateText = ReadTestFile();

        string tagList = TemplateExpander.BuildTagListFromCrimsonExport(templateText);
        string[] tagLines = tagList.Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);

        Assert.IsTrue(tagLines.Length > 0);
        Assert.AreEqual(tagLines.Distinct(StringComparer.OrdinalIgnoreCase).Count(), tagLines.Length,
            "Tag list should be de-duplicated.");
    }

    [TestMethod]
    public void TestFile_BuildTagListFromCrimsonExport_SortOption_ReturnsSortedTags()
    {
        string templateText = ReadTestFile();

        string[] tagLines = TemplateExpander.BuildTagListFromCrimsonExport(templateText, sort: true)
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries);
        string[] sorted = tagLines.OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToArray();

        CollectionAssert.AreEqual(sorted, tagLines);
    }

    [TestMethod]
    public void TestFile_BuildTagListFromCrimsonExportFile_ReadsUtf16Export()
    {
        string tagList = TemplateExpander.BuildTagListFromCrimsonExportFile(TestFilePath);

        Assert.IsTrue(tagList.Contains("VFD.AC_2210A_C.OCmd_AcqLock", StringComparison.Ordinal));
    }

    #endregion

    #region TestTemplate.txt + TestList.txt performance tests

    private static string TestTemplatePath => Path.Combine(AppContext.BaseDirectory, "TextTemplate.txt");
    private static string TestListPath => Path.Combine(AppContext.BaseDirectory, "TestList.txt");

    private static string ReadTestTemplate() => File.ReadAllText(TestTemplatePath);
    private static string ReadTestList() => File.ReadAllText(TestListPath);

    [TestMethod]
    [Timeout(5000)]
    public void Performance_GeneratePreview_CompletesWithinTimeout()
    {
        string templateText = ReadTestTemplate();
        string tagsText = ReadTestList();

        string output = TemplateExpander.GeneratePreview(templateText, tagsText);

        Assert.IsFalse(string.IsNullOrEmpty(output));
        Assert.IsFalse(output.Contains("{{1}}"),
            "Output should not contain unreplaced {{1}} placeholders.");
    }

    [TestMethod]
    [Timeout(5000)]
    public void Performance_GeneratePreviewBySections_CompletesWithinTimeout()
    {
        string templateText = ReadTestTemplate();
        string tagsText = ReadTestList();

        var results = TemplateExpander.GeneratePreviewBySections(templateText, tagsText);

        Assert.IsTrue(results.Count > 0);
        foreach (var (_, content) in results)
        {
            Assert.IsFalse(content.Contains("{{1}}"),
                "Section content should not contain unreplaced {{1}} placeholders.");
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public void Performance_ExpandSection_CompletesWithinTimeout()
    {
        string templateText = ReadTestTemplate();
        string tagsText = ReadTestList();

        var sections = TemplateExpander.ParseTemplateSections(templateText);
        var tagNames = TemplateExpander.ParseTagNames(tagsText);

        foreach (var section in sections)
        {
            var (header, content) = TemplateExpander.ExpandSection(section, tagNames);

            Assert.IsFalse(string.IsNullOrEmpty(header));
            Assert.IsFalse(string.IsNullOrEmpty(content));
            Assert.IsFalse(content.Contains("{{1}}"),
                $"Section '{header}' should not contain unreplaced {{{{1}}}} placeholders.");
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public void Performance_GetStats_CompletesWithinTimeout()
    {
        string templateText = ReadTestTemplate();
        string tagsText = ReadTestList();

        var (tagCount, replacementCount, outputLineCount) =
            TemplateExpander.GetStats(templateText, tagsText);

        Assert.IsTrue(tagCount > 0);
        Assert.IsTrue(replacementCount > 0);
        Assert.IsTrue(outputLineCount > 0);
    }

    #endregion
}
