using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Services;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class CustomDataTestSpeedTests
{
    private static T Find<T>(Control window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Control window, string title) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, title))
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Layout(Control window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); }
    private static RunDataTestNodeViewModel Node() => new() { PortsText = "A,192.168.0.2,192.168.0.3,100", DisabledPortsText = "B,192.168.0.4,192.168.0.5,100" };

    [Theory]
    [InlineData(37)] [InlineData(250)] [InlineData(375)] [InlineData(750)]
    public void ManualSpeedSurvivesProfileRoundTripAndReachesTheGenerator(int mbps)
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var node = Node(); var editor = new DataTestPairEditorViewModel(node);
        editor.Rows[0].MbpsText = mbps.ToString(CultureInfo.InvariantCulture); editor.Apply(node);
        vm.Nodes.Clear(); vm.Nodes.Add(node);
        GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, "Скорость"), vm);
        var loaded = Assert.Single(vm.Nodes.OfType<RunDataTestNodeViewModel>());
        var pair = Assert.Single(loaded.ParsePorts());
        var step = Assert.IsType<RunDataTestStep>(loaded.CreateStep(NullLogger.Instance));
        Assert.Equal(mbps, pair.TargetBandwidthMbps); Assert.Equal(mbps, step.ResolveTargetBandwidthMbps(pair));
        Assert.Equal("100", new DataTestPairEditorViewModel(loaded).Rows.Single(r => !r.Enabled).MbpsText);
    }

    [Theory]
    [InlineData("")] [InlineData("0")] [InlineData("1001")] [InlineData("12.5")] [InlineData("12,5")]
    public void InvalidTextCannotSilentlyKeepThePreviousSpeed(string input)
    {
        var node = Node(); var editor = new DataTestPairEditorViewModel(node); var original = node.PortsText;
        editor.Rows[0].MbpsText = input;
        Assert.Contains("целое число", Assert.Throws<InvalidOperationException>(() => editor.Apply(node)).Message);
        Assert.Equal(original, node.PortsText); Assert.False(node.AllowGigabit);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ManualAndAllPairsFieldsApplyTheTypedValuesWithoutEnablingExcludedPairs(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var theme = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var node = Node(); var original = node.PortsText; var window = new DataTestPairsView(node);
            try
            {
                window.Show(); Layout(window);
                Find<TextBox>(window, "AllPairsSpeed").Text = "675"; Click(window, "Применить всем"); Layout(window);
                var inputs = window.GetVisualDescendants().OfType<TextBox>().Where(t => t.Name == "PairSpeed").ToArray();
                Assert.All(inputs, input => Assert.Equal("675", input.Text)); Assert.Equal(original, node.PortsText);
                using (var frame = WorkspacePageTesting.CaptureRenderedFrame(window)) frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-custom-speed-{dark}.png"));
                var active = inputs.Single(t => ((DataTestPairRow)t.DataContext!).Enabled);
                active.Text = "12.5"; Click(window, "Применить к ноде");
                Assert.True(window.IsVisible); Assert.Equal(original, node.PortsText);
                Assert.Contains("целое число", Find<TextBlock>(window, "DataTestPairsError").Text);
                active.Text = "375"; Click(window, "Применить к ноде");
                Assert.Equal(375, Assert.Single(node.ParsePorts()).TargetBandwidthMbps);
                var disabled = new DataTestPairEditorViewModel(node).Rows.Single(r => !r.Enabled);
                Assert.Equal(675, disabled.Mbps); Assert.True(node.AllowGigabit);
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = theme; }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CustomSpeedForAllPortsIsSavedAndImportedAsTheExactNodeSpeed()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var source = NetworkPortBatchTests.Plan(2, 100); BenchNetworkPlan? saved = null;
            var window = new NetworkPlanView(source, plan => saved = plan); var old = AppSettings.Instance.NetworkPlan;
            try
            {
                window.Show(); Layout(window);
                Find<TextBox>(window, "AllPortsSpeed").Text = "275"; Click(window, "Применить всем");
                Assert.All(source.Addresses, p => Assert.Equal(100, p.TestMbps));
                Click(window, "Сохранить план"); Assert.NotNull(saved);
                AppSettings.Instance.NetworkPlan = saved;
                var node = new RunDataTestNodeViewModel(); node.ImportNetworkPlanCommand.Execute(null);
                Assert.Equal(275, Assert.Single(node.ParsePorts()).TargetBandwidthMbps);
                Assert.All(saved.Addresses, p => Assert.Equal(275, p.TestMbps));
            }
            finally { window.Close(); AppSettings.Instance.NetworkPlan = old; }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BulkPortsUseTheManualSpeedAndRejectAnEmptyValue()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var source = new BenchNetworkPlan { Addresses = new() }; var owner = new Window(); owner.Show();
            var window = new NetworkPortBatchView(source); var result = window.ShowAsync<BenchNetworkPlan?>(owner);
            try
            {
                Layout(window); Find<TextBox>(window, "BatchSpeed").Text = ""; Click(window, "Добавить в план");
                Assert.True(window.IsVisible); Assert.Contains("целое число", Find<TextBlock>(window, "BatchError").Text);
                Assert.Empty(source.Addresses);
                Find<TextBox>(window, "BatchSpeed").Text = "625"; Click(window, "Добавить в план");
                var plan = await result; Assert.NotNull(plan); Assert.Equal(24, plan.Addresses.Count);
                Assert.All(plan.Addresses, p => Assert.Equal(625, p.TestMbps)); Assert.Empty(source.Addresses);
            }
            finally { window.Close(); owner.Close(); }
            return true;
        }, CancellationToken.None);
    }
}
