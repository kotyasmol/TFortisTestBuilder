using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Tests.Services;

[Collection("Graph editor UI")]
public class DataTestRuntimeCheckTests
{
    [Theory]
    [InlineData("C:/Windows/System32/Npcap/wpcap.dll", null, null, true)]
    [InlineData("C:/Windows/System32/wpcap.dll", "Npcap", "Packet capture", true)]
    [InlineData("C:/Windows/System32/wpcap.dll", "WinPcap", "WinPcap capture", false)]
    public void RecognizesNpcapInCompatibilityMode(string path, string? product, string? description, bool expected) =>
        Assert.Equal(expected, DataTestRuntimeCheck.IsNpcap(path, product, description));

    [Fact]
    public void LibraryWithoutAdaptersDoesNotClaimReadiness()
    {
        var report = DataTestRuntimeCheck.Format(0, true, new[] { "build-42" });
        Assert.Contains("не найдены", report.Summary);
        Assert.Contains("build-42", report.Details);
        Assert.Contains("права доступа", report.Details);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AvailableLibraryDoesNotCertifyTrafficPerformance(bool npcap)
    {
        var report = DataTestRuntimeCheck.Format(10, npcap, Array.Empty<string>());
        Assert.Contains("10", report.Summary);
        Assert.Contains("основным замером", report.Summary);
        if (!npcap) Assert.Contains("устанавливается отдельно", report.Details);
    }

    [Fact]
    public async Task RuntimeCheckDoesNotChangeBindingsOrRequireConnectedDut()
    {
        var calls = 0;
        var cards = DataTestNetworkConfigurator.BenchIps.Select((ip, i) =>
            new BenchAdapter($"card-{i}", "Ethernet", "001122334455", "Down", ip)).ToArray();
        var vm = new NetworkSetupViewModel(() => cards, () => true,
            () => { calls++; return new("Найдено карт: 10", "Одна общая сборка"); });
        var before = vm.Rows.Select(r => r.SelectedAdapter?.Id).ToArray();
        await vm.CheckRuntimeAsync();
        Assert.Equal(1, calls);
        Assert.Equal(before, vm.Rows.Select(r => r.SelectedAdapter?.Id));
        Assert.Equal("Найдено карт: 10", vm.RuntimeSummary);
        Assert.False(vm.IsCheckingRuntime);
    }

    [Fact]
    public async Task RunningTestCannotStartAnAdditionalNativeProbe()
    {
        var vm = new NetworkSetupViewModel(() => Array.Empty<BenchAdapter>(), () => false,
            () => throw new InvalidOperationException("must not run"));
        await vm.CheckRuntimeAsync();
        Assert.Contains("ещё не проверены", vm.RuntimeSummary);
    }

    [Fact]
    public async Task OperatorCanReadDiagnosticsWithoutPermissionToEditNetwork()
    {
        var vm = new NetworkSetupViewModel(() => Array.Empty<BenchAdapter>(), () => false,
            () => new("Проверка выполнена", "build"), canInspectRuntime: () => true);
        Assert.False(vm.CanEdit);
        await vm.CheckRuntimeAsync();
        Assert.Equal("Проверка выполнена", vm.RuntimeSummary);
    }
}
