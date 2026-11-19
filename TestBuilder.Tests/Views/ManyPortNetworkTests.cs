using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Services;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ManyPortNetworkTests
{
    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Click(Window window, string label) => window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label))
        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static BenchAdapter[] Cards(BenchNetworkPlan plan) => plan.Addresses.Select((a, i) =>
        new BenchAdapter($"card-{i}", $"Ethernet {i + 1}", $"00112233{i:0000}", i % 3 == 0 ? "Down" : "Up", a.Ip, 1000)).ToArray();

    [Fact]
    public async Task FilteringDoesNotRemoveAssignmentsOrReduceTheNumberOfProbedPairs()
    {
        var settings = AppSettings.Instance; var old = settings.NetworkPlan; var bindings = settings.DataTestAdapterIds;
        var plan = NetworkPortBatchTests.Plan(); settings.NetworkPlan = plan; settings.DataTestAdapterIds = new();
        var cards = Cards(plan); var probed = new List<string>();
        try
        {
            var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>(),
                probePair: (pair, _) =>
                {
                    probed.Add(pair.Name);
                    return Task.FromResult(new NetworkProbeResult(pair, "OK", "", new(true, 10, 10, ""), new(true, 10, 10, "")));
                });
            Assert.Equal(24, vm.VisibleRows.Count);
            vm.RowSearch = "Порт 24"; Assert.Single(vm.VisibleRows); Assert.Equal(plan.Addresses[^1].Ip, vm.VisibleRows[0].Ip);
            await vm.ProbeConnectionsCommand.ExecuteAsync(null);
            Assert.Equal(12, probed.Count); Assert.Equal(plan.Addresses[^1].Pair, probed[^1]); Assert.Equal(24, vm.Rows.Count);
            vm.RowSearch = ""; vm.RowFilterIndex = 2;
            Assert.Equal(8, vm.VisibleRows.Count);
            vm.RowFilterIndex = 1; Assert.Empty(vm.VisibleRows);
            vm.Rows[^1].SelectedAdapter = null; Assert.Single(vm.VisibleRows);
            vm.Rows[^1].SelectedAdapter = cards[^1]; Assert.Empty(vm.VisibleRows);
            vm.RowFilterIndex = 0; vm.RowSearch = cards[12].Mac;
            Assert.Single(vm.VisibleRows); Assert.Equal("card-12", vm.VisibleRows[0].SelectedAdapter!.Id);
            Assert.All(vm.Rows, row => Assert.NotNull(row.SelectedAdapter));
        }
        finally { settings.NetworkPlan = old; settings.DataTestAdapterIds = bindings; }
    }

    [Theory]
    [InlineData(12, false)] [InlineData(24, false)] [InlineData(24, true)] [InlineData(48, false)]
    public async Task EveryPairRoundTripsAndTheLastPairReachesTheReport(int ports, bool failLast)
    {
        var plan = NetworkPortBatchTests.Plan(ports);
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var node = new RunDataTestNodeViewModel { AllowGigabit = true, PortsText = plan.BuildDataTestPairs(), TargetBandwidthMbps = 1000 };
        vm.Nodes.Add(node);
        GraphSerializer.Deserialize(GraphSerializer.Serialize(vm, "Many ports"), vm);
        var restored = Assert.Single(vm.Nodes.OfType<RunDataTestNodeViewModel>());
        var pairs = restored.ParsePorts().ToArray(); Assert.Equal(ports / 2, pairs.Length);
        Assert.True(restored.PairsSummary.Split(Environment.NewLine).Length <= 6);
        var graph = new GraphWorkspaceViewModel(); graph.Nodes.Add(new SelfTestCheckNodeViewModel()); graph.Nodes.Add(restored);
        var sources = ReportSourceCatalog.Collect(graph, ReportSourceKind.Entry);
        Assert.Contains("Передача данных " + pairs[^1].Name, sources); Assert.Contains($"link_{ports - 1}", sources);
        Assert.Contains($"DataTest.Port{pairs.Length - 1}.ActualTxMbps", ReportSourceCatalog.Collect(graph, ReportSourceKind.Variable));
        var step = Assert.IsType<RunDataTestStep>(restored.CreateStep(NullLogger.Instance));
        var context = new TestContext(new RegisterState()); context.SetVariable("SerialNumber", 3300001);
        for (var i = 0; i < pairs.Length; i++)
        {
            var measurement = DataTestPortResult.Fail("") with
            {
                Passed = !(failLast && i == pairs.Length - 1), TransmittedPackets = 100,
                ReceivedPackets = failLast && i == pairs.Length - 1 ? 98 : 100, ExpectedPackets = 100,
                ActualTxMbps = 1000, ActualRxMbps = 1000, TargetBandwidthMbps = 1000, Error = "",
                LossPercent = failLast && i == pairs.Length - 1 ? 2 : 0, TxDeficitPercent = 0
            };
            step.WritePortResult(context, i, pairs[i], DataTestPairResult.FromDirections(measurement, measurement), "");
        }
        Assert.Equal(pairs.Length, context.ReportEntries.Count);
        var build = new BuildTestReportStep(NullLogger.Instance, "Report", "Test stand", "SerialNumber", "", "production", false);
        Assert.Equal(StepResult.True, await build.ExecuteAsync(context, default));
        Assert.Contains("Передача данных " + pairs[^1].Name + (failLast ? "=false=" : "=true="), context.GetVariable<string>("Report"));
        Assert.Contains("test_result=true=" + (failLast ? "0" : "1"), context.GetVariable<string>("Report"));
        Assert.Equal(100, context.GetVariable<int>($"DataTest.Port{pairs.Length - 1}.Forward.TransmittedPackets"));
    }

    [Theory]
    [InlineData(false, 900)] [InlineData(true, 1200)]
    public async Task TwentyFourPortTableIsCompactSearchableAndVirtualized(bool dark, int width)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var settings = AppSettings.Instance; var old = settings.NetworkPlan; var bindings = settings.DataTestAdapterIds;
            var theme = Application.Current!.RequestedThemeVariant;
            var plan = NetworkPortBatchTests.Plan(); settings.NetworkPlan = plan; settings.DataTestAdapterIds = new();
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var vm = new NetworkSetupViewModel(() => Cards(plan), () => true);
            var window = new Window { Width = width, Height = 850, Content = new NetworkSetupView { DataContext = vm } };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                var grid = Find<DataGrid>(window, "NetworkAdaptersGrid");
                Assert.Equal(24, grid.ItemsSource.Cast<object>().Count());
                Assert.InRange(grid.GetVisualDescendants().OfType<DataGridRow>().Count(), 1, 15);
                using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-network-24-{dark}.png"));
                Find<TextBox>(window, "NetworkPortSearch").Text = "Порт 24"; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Single(vm.VisibleRows); Assert.Equal("card-23", vm.VisibleRows[0].SelectedAdapter!.Id);
                using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-network-search-{dark}.png"));
                Assert.Equal(24, vm.Rows.Count);
                Find<TextBox>(window, "NetworkPortSearch").Text = ""; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                grid.ScrollIntoView(vm.Rows[^1], grid.Columns[0]); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                grid.ScrollIntoView(vm.Rows[0], grid.Columns[0]); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                Assert.All(vm.Rows.Select((row, index) => (row, index)), item => Assert.Equal($"card-{item.index}", item.row.SelectedAdapter?.Id));
            }
            finally { window.Close(); settings.NetworkPlan = old; settings.DataTestAdapterIds = bindings; Application.Current.RequestedThemeVariant = theme; }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BulkDialogAddsToTheDraftBeforeThePlanIsSaved()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var source = new BenchNetworkPlan { Addresses = new() }; BenchNetworkPlan? saved = null;
            var owner = new NetworkPlanWindow(source, plan => saved = plan);
            try
            {
                owner.Show(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Click(owner, "Добавить порты…"); Dispatcher.UIThread.RunJobs();
                var dialog = Assert.IsType<NetworkPortBatchWindow>(Assert.Single(owner.OwnedWindows));
                dialog.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                Assert.Contains("24", Find<TextBlock>(dialog, "BatchSummary").Text);
                using (var frame = dialog.CaptureRenderedFrame()) frame!.Save(Path.Combine(Path.GetTempPath(), "tfortis-network-batch.png"));
                Click(dialog, "Добавить в план");
                for (var i = 0; i < 20 && owner.GetVisualDescendants().OfType<DataGrid>().Single().ItemsSource.Cast<object>().Count() != 24; i++)
                { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
                Assert.Empty(source.Addresses); Assert.Null(saved);
                Assert.Equal(24, owner.GetVisualDescendants().OfType<DataGrid>().Single().ItemsSource.Cast<object>().Count());
                Click(owner, "Сохранить план"); Assert.NotNull(saved); Assert.Equal(24, saved.Addresses.Count);
                Assert.Equal(12, saved.BuildDataTestPairs().Split(Environment.NewLine).Length);
            }
            finally { foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close(); owner.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PlanSpeedCanBeChangedForEveryPortWithoutChangingTheSavedSource()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var source = NetworkPortBatchTests.Plan(); BenchNetworkPlan? saved = null;
            var window = new NetworkPlanWindow(source, plan => saved = plan);
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Click(window, "Всем 100 Мбит/с"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var grid = Find<DataGrid>(window, "NetworkPlanAddresses");
                var rows = grid.ItemsSource.Cast<BenchNetworkAddress>().ToArray();
                Assert.All(rows, row => Assert.Equal(100, row.TestMbps));
                Assert.Equal("100", Assert.IsType<TextBlock>(grid.Columns[3].GetCellContent(rows[0])).Text);
                Assert.All(source.Addresses, row => Assert.Equal(1000, row.TestMbps));
                Click(window, "Сохранить план"); Assert.NotNull(saved);
                Assert.Equal(12, saved.BuildDataTestPairs().Split(Environment.NewLine).Length);
                Assert.All(saved.Addresses, row => Assert.Equal(100, row.TestMbps));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public void NewlyAddedNodeUsesTheWholePlanButExistingProfilesStayIndependent()
    {
        var settings = AppSettings.Instance; var old = settings.NetworkPlan; settings.NetworkPlan = NetworkPortBatchTests.Plan();
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        try
        {
            vm.IsOperatorView = false;
            vm.AddNodeAtLocation("Run Data Test", new Point(200, 200));
            var created = Assert.Single(vm.Nodes.OfType<RunDataTestNodeViewModel>());
            Assert.Equal(12, created.ParsePorts().Count());
            var pairs = created.PortsText;
            settings.NetworkPlan = NetworkPortBatchTests.Plan(48);
            Assert.Equal(pairs, created.PortsText); Assert.Equal(pairs, Assert.IsType<RunDataTestNodeViewModel>(created.Clone()).PortsText);
            GraphSerializer.Deserialize("""{"name":"Legacy","nodes":[{"id":"0","type":"Run Data Test"}],"connections":[]}""", vm);
            Assert.Equal(RunDataTestNodeViewModel.DefaultPortsText.Replace("\r\n", Environment.NewLine), Assert.Single(vm.Nodes.OfType<RunDataTestNodeViewModel>()).PortsText);
        }
        finally { settings.NetworkPlan = old; }
    }

    [Fact]
    public void NewNodeCannotSilentlyUseTheOldTenCardExampleWhenThePlanHasNoPairs()
    {
        var settings = AppSettings.Instance; var old = settings.NetworkPlan; settings.NetworkPlan = new BenchNetworkPlan();
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        try
        {
            vm.IsOperatorView = false; vm.AddNodeAtLocation("Run Data Test", new Point(200, 200));
            var node = Assert.Single(vm.Nodes.OfType<RunDataTestNodeViewModel>());
            Assert.Equal("", node.PortsText); Assert.NotEmpty(node.NetworkPlanStatus);
            Assert.Throws<InvalidOperationException>(() => node.CreateStep(NullLogger.Instance));
            Assert.Contains("DataTest.Passed", ReportSourceCatalog.Collect(vm.RootGraph, ReportSourceKind.Variable));
        }
        finally { settings.NetworkPlan = old; }
    }
}
