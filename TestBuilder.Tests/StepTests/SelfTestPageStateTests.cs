using TestBuilder.Domain.Execution;

namespace TestBuilder.Tests.StepTests;

public class SelfTestPageStateTests
{
    [Fact]
    public void NewState_ReportsThatPageHasNotBeenLoaded()
    {
        var state = new SelfTestPageState();

        Assert.Equal(SelfTestPageLoadState.NotLoaded, state.Current.LoadState);
        Assert.Empty(state.Current.Fields);
        Assert.Null(state.Current.LoadedAt);
    }

    [Fact]
    public void SetLoaded_ReplacesTheCompletePreviousSnapshot()
    {
        var state = new SelfTestPageState();
        state.SetLoaded(
            "http://dut/first",
            "Dut",
            new Dictionary<string, string>
            {
                ["init_ok"] = "1",
                ["removed_by_firmware"] = "old"
            });

        state.SetLoaded(
            "http://dut/second",
            "DutFinal",
            new Dictionary<string, string>
            {
                ["init_ok"] = "0",
                ["temperature"] = "42"
            });

        Assert.Equal(SelfTestPageLoadState.Loaded, state.Current.LoadState);
        Assert.Equal("http://dut/second", state.Current.Url);
        Assert.Equal("DutFinal", state.Current.OutputPrefix);
        Assert.Equal(2, state.Current.Fields.Count);
        Assert.Equal("0", state.Current.Fields["init_ok"]);
        Assert.Equal("42", state.Current.Fields["temperature"]);
        Assert.DoesNotContain("removed_by_firmware", state.Current.Fields.Keys);
    }

    [Fact]
    public void LoadingAndError_KeepLastSuccessfulValuesVisible()
    {
        var state = new SelfTestPageState();
        state.SetLoaded(
            "http://dut/page",
            "Dut",
            new Dictionary<string, string> { ["init_ok"] = "1" });

        state.BeginLoading("http://dut/page", "Dut");
        Assert.Equal(SelfTestPageLoadState.Loading, state.Current.LoadState);
        Assert.Equal("1", state.Current.Fields["init_ok"]);

        state.SetError("http://dut/page", "Dut", "DUT unavailable");
        Assert.Equal(SelfTestPageLoadState.Error, state.Current.LoadState);
        Assert.Equal("DUT unavailable", state.Current.ErrorMessage);
        Assert.Equal("1", state.Current.Fields["init_ok"]);
    }
}
