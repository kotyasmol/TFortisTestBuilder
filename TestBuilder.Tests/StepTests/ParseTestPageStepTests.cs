using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class ParseTestPageStepTests
{
    [Fact]
    public async Task ParseTestPageStep_ExtractsSettingsXml_AndSavesConfiguredFields()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable(
            "TestPageRaw",
            "noise <!DOCTYPE settings><settings>" +
            "<dev_type>SW-8</dev_type>" +
            "<firmvare_vers>1.2.3</firmvare_vers>" +
            "<default_mac>AA:BB:CC:DD:EE:FF</default_mac>" +
            "<link_0>1</link_0>" +
            "<poe_a_st0>ok</poe_a_st0>" +
            "</settings> trailing");

        var step = CreateStep(
            fieldNames: "dev_type\nfirmvare_vers\ndefault_mac\nlink[0]\npoe_a_st[0]",
            requiredFieldNames: "dev_type");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.True(context.GetVariable<bool>("TestPageParsed"));
        Assert.Equal("SW-8", context.GetVariable<string>("Dut.dev_type"));
        Assert.Equal("1.2.3", context.GetVariable<string>("Dut.firmvare_vers"));
        Assert.Equal("AA:BB:CC:DD:EE:FF", context.GetVariable<string>("Dut.default_mac"));
        Assert.Equal("1", context.GetVariable<string>("Dut.link[0]"));
        Assert.Equal("ok", context.GetVariable<string>("Dut.poe_a_st[0]"));
        Assert.Contains("</settings>", context.GetVariable<string>("TestPageXml"));
        Assert.Equal(string.Empty, context.GetVariable<string>("TestPageParseError"));
    }

    [Fact]
    public async Task ParseTestPageStep_AppliesPsw2gAdc25Fix_WhenSecondOpeningTagIsPresent()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable(
            "TestPageRaw",
            "<settings><dev_type>PSW-2G+</dev_type><adc_2_5>2500<adc_2_5></settings>");

        var step = CreateStep(
            fieldNames: "dev_type\nadc_2_5",
            requiredFieldNames: "dev_type",
            applyFix: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal("2500", context.GetVariable<string>("Dut.adc_2_5"));
        Assert.Contains("</adc_2_5>", context.GetVariable<string>("TestPageXml"));
    }

    [Fact]
    public async Task ParseTestPageStep_ReturnsFalse_WhenRequiredFieldIsMissing()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("TestPageRaw", "<settings><firmvare_vers>1.2.3</firmvare_vers></settings>");

        var step = CreateStep(
            fieldNames: "firmvare_vers",
            requiredFieldNames: "dev_type");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("TestPageParsed"));
        Assert.Contains("dev_type", context.GetVariable<string>("TestPageParseError"));
        Assert.True(context.HasCriticalError);
    }

    [Fact]
    public async Task ParseTestPageStep_UsesCustomPrefix_AndCustomFields()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("TestPageRaw", "<settings><custom_field>42</custom_field></settings>");

        var step = CreateStep(
            outputPrefix: "Board",
            fieldNames: "custom_field",
            requiredFieldNames: "custom_field");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal("42", context.GetVariable<string>("Board.custom_field"));
    }

    [Fact]
    public async Task ParseTestPageStep_SupportsOpenEndedArrayRange()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable(
            "TestPageRaw",
            "<settings><dev_type>SW-8</dev_type><tlp_input_0>1</tlp_input_0><tlp_input_2>3</tlp_input_2></settings>");

        var step = CreateStep(
            fieldNames: "dev_type\ntlp_input[0..]",
            requiredFieldNames: "dev_type");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal("1", context.GetVariable<string>("Dut.tlp_input[0]"));
        Assert.Equal("3", context.GetVariable<string>("Dut.tlp_input[2]"));
    }

    [Fact]
    public async Task ParseTestPageStep_ReturnsFalse_WhenXmlIsInvalid()
    {
        var context = new TestContext(new RegisterState());
        context.SetVariable("TestPageRaw", "<settings><dev_type>SW-8</settings>");

        var step = CreateStep(fieldNames: "dev_type", requiredFieldNames: "dev_type");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.False(context.GetVariable<bool>("TestPageParsed"));
        Assert.Contains("Некорректный XML", context.GetVariable<string>("TestPageParseError"));
    }

    private static ParseTestPageStep CreateStep(
        string inputVariableName = "TestPageRaw",
        string outputPrefix = "Dut",
        bool failOnInvalidXml = true,
        bool applyFix = true,
        string fieldNames = "dev_type",
        string requiredFieldNames = "dev_type")
    {
        return new ParseTestPageStep(
            NullLogger.Instance,
            inputVariableName,
            outputPrefix,
            failOnInvalidXml,
            applyFix,
            fieldNames,
            requiredFieldNames);
    }
}
