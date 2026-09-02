using TestBuilder.ViewModels;

namespace TestBuilder.Tests.StepTests;

public class SelfTestPageViewModelTests
{
    [Theory]
    [InlineData("init_ok", SelfTestPageFieldCategorizer.Device)]
    [InlineData("link_12", SelfTestPageFieldCategorizer.Ethernet)]
    [InlineData("poe_a_3_v", SelfTestPageFieldCategorizer.PoeA)]
    [InlineData("poe_b_4_state", SelfTestPageFieldCategorizer.PoeB)]
    [InlineData("sfp_2_pres", SelfTestPageFieldCategorizer.Sfp)]
    [InlineData("akb_voltage", SelfTestPageFieldCategorizer.Power)]
    [InlineData("input_9", SelfTestPageFieldCategorizer.Inputs)]
    [InlineData("temperature", SelfTestPageFieldCategorizer.Climate)]
    [InlineData("future_firmware_field", SelfTestPageFieldCategorizer.Other)]
    public void Categorizer_AssignsKnownFieldFamilies(string fieldName, string expectedCategory)
    {
        Assert.Equal(expectedCategory, SelfTestPageFieldCategorizer.GetCategoryKey(fieldName));
    }

    [Fact]
    public void FieldComparer_SortsNumericSuffixesNaturally()
    {
        Assert.True(SelfTestPageFieldCategorizer.CompareFieldNames("link_2", "link_10") < 0);
        Assert.True(SelfTestPageFieldCategorizer.CompareFieldNames("poe_a_9_v", "poe_a_10_v") < 0);
    }

    [Fact]
    public void Parameter_HighlightsOnlyAnActualValueChange()
    {
        var parameter = new SelfTestPageParameterViewModel("ups_rez", "0");
        var highlightUntil = DateTimeOffset.UtcNow.AddSeconds(5);

        Assert.False(parameter.UpdateValue("0", highlight: true, highlightUntil));
        Assert.False(parameter.IsChanged);

        Assert.True(parameter.UpdateValue("1", highlight: true, highlightUntil));
        Assert.True(parameter.IsChanged);
        Assert.Equal("1", parameter.Value);
        Assert.Equal("Было: 0", parameter.ChangeDescription);

        parameter.ClearChangeHighlight();
        Assert.False(parameter.IsChanged);
    }
}
