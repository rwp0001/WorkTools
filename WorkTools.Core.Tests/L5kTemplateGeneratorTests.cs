namespace WorkTools.Core.Tests;

[TestClass]
public class L5kTemplateGeneratorTests
{
    private const string SampleL5k =
        """
        ADD_ON_INSTRUCTION_DEFINITION MotorFaceplate
        PARAMETERS
            StartCmd : BOOL (Usage := Input, Description := "Start command");
            SpeedRef : REAL (Usage := InOut);
            StatusCode : DINT (Usage := Output);
            InternalTimer : TIMER (Usage := Input);
            OutputArray : DINT[5] (Usage := Output);
        END_PARAMETERS
        END_ADD_ON_INSTRUCTION_DEFINITION
        """;

    private const string SampleL5x =
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <RSLogix5000Content SchemaRevision="1.0" SoftwareRevision="35.00" TargetName="Controller" TargetType="Controller" ContainsContext="true" ExportDate="Mon Jan 01 00:00:00 2024" ExportOptions="References NoRawData L5KData DecoratedData Context Dependencies ForceProtectedEncoding AllProjDocTrans">
          <Controller Use="Context" Name="Controller">
            <AddOnInstructionDefinitions Use="Context">
              <AddOnInstructionDefinition Use="Target" Name="MotorFaceplate">
                <Parameters>
                  <Parameter Name="StartCmd" TagType="Base" DataType="BOOL" Usage="Input" Required="false" Visible="true" ExternalAccess="Read/Write">
                    <Description><![CDATA[Start command]]></Description>
                  </Parameter>
                  <Parameter Name="SpeedRef" TagType="Base" DataType="REAL" Usage="InOut" Required="false" Visible="true" ExternalAccess="Read/Write"/>
                  <Parameter Name="StatusCode" TagType="Base" DataType="DINT" Usage="Output" Required="false" Visible="true" ExternalAccess="Read/Write"/>
                  <Parameter Name="ComplexOnly" TagType="Base" DataType="TIMER" Usage="Input" Required="false" Visible="true" ExternalAccess="Read/Write"/>
                  <Parameter Name="ArrayValue" TagType="Base" DataType="DINT" Usage="Output" Dimension="5" Required="false" Visible="true" ExternalAccess="Read/Write"/>
                </Parameters>
              </AddOnInstructionDefinition>
            </AddOnInstructionDefinitions>
          </Controller>
        </RSLogix5000Content>
        """;

    [TestMethod]
    public void ExtractAtomicParameters_ReturnsOnlyAtomicWithDirection()
    {
        var parameters = L5kTemplateGenerator.ExtractAtomicParameters(SampleL5k);

        Assert.AreEqual(3, parameters.Count);
        Assert.IsTrue(parameters.Any(p => p.Name == "StartCmd" && p.DataType == "BOOL" && p.Direction == "Input" && p.Description == "Start command"));
        Assert.IsTrue(parameters.Any(p => p.Name == "SpeedRef" && p.DataType == "REAL" && p.Direction == "InOut"));
        Assert.IsTrue(parameters.Any(p => p.Name == "StatusCode" && p.DataType == "DINT" && p.Direction == "Output"));
    }

    [TestMethod]
    public void GenerateCrimsonTemplate_CreatesExpectedSections()
    {
        string template = L5kTemplateGenerator.GenerateCrimsonTemplate(SampleL5k);

        Assert.IsTrue(template.Contains("[Flag.5.0]", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("[Numeric.1.0]", StringComparison.Ordinal));
        Assert.IsFalse(template.Contains("InternalTimer", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("{{1}}.StartCmd", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("[{{1}}.StatusCode]", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("\t\t\tStart command\t\tTwo-State", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GenerateCrimsonTemplate_NoSupportedParameters_Throws()
    {
        const string emptyL5k =
            """
            PARAMETERS
                ComplexOnly : TIMER (Usage := Input);
            END_PARAMETERS
            """;

        Assert.ThrowsExactly<InvalidOperationException>(() => L5kTemplateGenerator.GenerateCrimsonTemplate(emptyL5k));
    }

    [TestMethod]
    public void ExtractAtomicParameters_FromL5x_ReturnsOnlyAtomicWithDirection()
    {
        var parameters = L5kTemplateGenerator.ExtractAtomicParameters(SampleL5x);

        Assert.AreEqual(3, parameters.Count);
        Assert.IsTrue(parameters.Any(p => p.Name == "StartCmd" && p.DataType == "BOOL" && p.Direction == "Input" && p.Description == "Start command"));
        Assert.IsTrue(parameters.Any(p => p.Name == "SpeedRef" && p.DataType == "REAL" && p.Direction == "InOut"));
        Assert.IsTrue(parameters.Any(p => p.Name == "StatusCode" && p.DataType == "DINT" && p.Direction == "Output"));
    }

    [TestMethod]
    public void GenerateCrimsonTemplate_FromL5x_CreatesExpectedSections()
    {
        string template = L5kTemplateGenerator.GenerateCrimsonTemplate(SampleL5x);

        Assert.IsTrue(template.Contains("[Flag.5.0]", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("[Numeric.1.0]", StringComparison.Ordinal));
        Assert.IsFalse(template.Contains("ComplexOnly", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("{{1}}.StartCmd", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("[{{1}}.StatusCode]", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("\t\t\tStart command\t\tTwo-State", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ExtractAtomicParameters_L5kDescription_StripsCrLfAndTrims()
    {
        const string l5kWithMultilineDescription = "PARAMETERS\n"
            + "    StartCmd : BOOL (Usage := Input, Description := \"Line1\r\nLine2\");\n"
            + "    StopCmd : BOOL (Usage := Output, Description := \"  LineA\nLineB\tX  \");\n"
            + "END_PARAMETERS";

        var parameters = L5kTemplateGenerator.ExtractAtomicParameters(l5kWithMultilineDescription);

        Assert.AreEqual(2, parameters.Count);
        Assert.AreEqual("Line1 Line2", parameters.First(p => p.Name == "StartCmd").Description);
        Assert.AreEqual("LineA LineB X", parameters.First(p => p.Name == "StopCmd").Description);
    }

    [TestMethod]
    public void ExtractAtomicParameters_L5xDescription_StripsCrLfAndTrims()
    {
        const string l5xWithMultilineDescription = "<RSLogix5000Content>"
            + "<Controller><AddOnInstructionDefinitions><AddOnInstructionDefinition><Parameters>"
            + "<Parameter Name=\"StartCmd\" DataType=\"BOOL\" Usage=\"Input\">"
            + "<Description><![CDATA[  Line1\r\nLine2\tX  ]]></Description>"
            + "</Parameter></Parameters></AddOnInstructionDefinition></AddOnInstructionDefinitions></Controller>"
            + "</RSLogix5000Content>";

        var parameters = L5kTemplateGenerator.ExtractAtomicParameters(l5xWithMultilineDescription);

        Assert.AreEqual(1, parameters.Count);
        Assert.AreEqual("Line1 Line2 X", parameters[0].Description);
    }

    [TestMethod]
    public void GenerateCrimsonTemplate_L5kLocalString_AddsStringWithQuotedLiteralValue()
    {
        const string l5kWithLocalString =
            """
            PARAMETERS
                StartCmd : BOOL (Usage := Input);
            END_PARAMETERS
            LOCAL_TAGS
                LocalMessage : STRING := "Hello	World";
            END_LOCAL_TAGS
            """;

        string template = L5kTemplateGenerator.GenerateCrimsonTemplate(l5kWithLocalString);

        Assert.IsTrue(template.Contains("{{1}}.LocalMessage", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("\"Hello World\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void GenerateCrimsonTemplate_L5xLocalString_AddsStringWithQuotedLiteralValue()
    {
        const string l5xWithLocalString =
            """
            <RSLogix5000Content>
              <Controller>
                <AddOnInstructionDefinitions>
                  <AddOnInstructionDefinition>
                    <Parameters>
                      <Parameter Name="StartCmd" DataType="BOOL" Usage="Input" />
                    </Parameters>
                    <LocalTags>
                      <Tag Name="LocalMessage" DataType="STRING">
                        <Data><![CDATA[Hello	World]]></Data>
                      </Tag>
                    </LocalTags>
                  </AddOnInstructionDefinition>
                </AddOnInstructionDefinitions>
              </Controller>
            </RSLogix5000Content>
            """;

        string template = L5kTemplateGenerator.GenerateCrimsonTemplate(l5xWithLocalString);

        Assert.IsTrue(template.Contains("{{1}}.LocalMessage", StringComparison.Ordinal));
        Assert.IsTrue(template.Contains("\"Hello World\"", StringComparison.Ordinal));
    }
}
