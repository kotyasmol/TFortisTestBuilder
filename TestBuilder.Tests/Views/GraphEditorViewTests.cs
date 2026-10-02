using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[CollectionDefinition("Graph editor UI", DisableParallelization = true)]
public class GraphEditorUiCollection;

[Collection("Graph editor UI")]
public class GraphEditorViewTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

    [Fact]
    public async Task ProfileClicks_OpenOnFirstClick_AndRepeatedClickKeepsGraphAndEdits()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"profile-click-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, """
                {"name":"Click profile","nodes":[
                    {"id":"0","type":"Start","x":5000,"y":3000},
                    {"id":"1","type":"End","x":5500,"y":3000}
                ],"connections":[]}
                """);
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            using var view = new GraphEditorView { DataContext = vm };
            var profiles = new ProfileListView { DataContext = vm, Width = 300 };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*"), Children = { profiles, view } };
            Grid.SetColumn(view, 1);
            var window = new Window { Width = 1300, Height = 750, Content = grid };
            try
            {
                var profile = GraphSerializer.ReadProfile(path);
                vm.Profiles.Clear();
                vm.Profiles.Add(profile);
                window.Show();
                Layout(window);
                var item = profiles.GetVisualDescendants().OfType<ListBoxItem>()
                    .Single(item => ReferenceEquals(item.DataContext, profile));
                var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;

                void Click()
                {
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    Layout(window);
                }

                Click();
                Assert.Same(profile, vm.SelectedProfile);
                AssertGraphVisible(view, 2);
                var editedNode = vm.Nodes[0];
                editedNode.Location = new Point(6000, 3500);
                var editor = view.FindControl<NodifyEditor>("Editor")!;
                editor.ViewportLocation = new Point(-20000, -20000);
                Click();
                Assert.Same(profile, vm.SelectedProfile);
                Assert.Same(editedNode, vm.Nodes[0]);
                Assert.Equal(new Point(6000, 3500), editedNode.Location);
                AssertGraphVisible(view, 2);
            }
            finally { window.Close(); File.Delete(path); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProProfile_FirstShowAndQuickSwitchesFitLoadedGraph(bool loadBeforeShow)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            using var view = new TestView();
            var graphView = view.FindControl<GraphEditorView>("GraphEditor")!;
            var profiles = view.GetLogicalDescendants().OfType<ProfileListView>().Single();
            var profileFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "profiles"));
            var pro = GraphSerializer.ReadProfile(Path.Combine(profileFolder, "PSW_UPS_Box_8x2Pro_parallel_el60.json"));
            var small = GraphSerializer.ReadProfile(Path.Combine(profileFolder, "manual_label_printing.json"));
            vm.Profiles.Clear();
            vm.Profiles.Add(pro);
            vm.Profiles.Add(small);
            var window = new Window { Width = 1600, Height = 900, Content = view };
            try
            {
                if (loadBeforeShow)
                {
                    vm.SelectedProfile = pro;
                    view.DataContext = vm;
                }
                window.Show();
                Layout(window);
                if (!loadBeforeShow)
                {
                    // MainWindow also assigns the view model after constructing the views.
                    view.DataContext = vm;
                    Layout(window);
                    Click(pro);
                    Layout(window);
                }
                Assert.Same(pro, vm.SelectedProfile);
                Assert.NotEmpty(vm.Nodes);
                AssertGraphVisible(graphView, vm.Nodes.Count);

                // Open an inner graph, then re-select the profile to return to its root.
                var subtest = vm.Nodes.OfType<SubtestNodeViewModel>().First();
                vm.OpenCompositeNodeBodyCommand.Execute(subtest);
                Layout(window);
                Click(pro);
                Layout(window);
                Assert.Same(vm.RootGraph, vm.CurrentGraph);
                Assert.Same(subtest, vm.Nodes.OfType<SubtestNodeViewModel>().First());
                AssertGraphVisible(graphView, vm.Nodes.Count);

                // Multiple selections may arrive before the next layout pass.
                Click(small);
                Click(pro);
                Layout(window);
                Assert.Same(pro, vm.SelectedProfile);
                AssertGraphVisible(graphView, vm.Nodes.Count);
                Click(small);
                Layout(window);
                Assert.Same(small, vm.SelectedProfile);
                AssertGraphVisible(graphView, vm.Nodes.Count);

                void Click(GraphProfile profile)
                {
                    var item = profiles.GetVisualDescendants().OfType<ListBoxItem>()
                        .Single(item => ReferenceEquals(item.DataContext, profile));
                    var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CompletedParallelNode_KeepsGreenBorderWhileSiblingRunsOrFails()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            using var view = new GraphEditorView { DataContext = vm };
            var first = new DelayNodeViewModel { Location = new Point(80, 120) };
            var second = new DelayNodeViewModel { Location = new Point(500, 120) };
            vm.RootGraph.Nodes.Add(first);
            vm.RootGraph.Nodes.Add(second);
            var window = new Window { Width = 1000, Height = 700, Content = view };
            try
            {
                window.Show();
                Layout(window);
                var items = view.FindControl<NodifyEditor>("Editor")!.GetVisualDescendants().OfType<ItemContainer>().ToArray();
                var firstItem = items.Single(i => ReferenceEquals(i.DataContext, first));
                var secondItem = items.Single(i => ReferenceEquals(i.DataContext, second));
                var root = new TestContext(new RegisterState());
                var firstContext = root.CreateParallelBranch(CancellationToken.None);
                var secondContext = root.CreateParallelBranch(CancellationToken.None);
                var firstNode = new TestNode(null, first);
                var secondNode = new TestNode(null, second);
                await vm.NodeStartedAsync(firstNode, firstContext, CancellationToken.None);
                await vm.NodeStartedAsync(secondNode, secondContext, CancellationToken.None);
                await vm.NodeCompletedAsync(firstNode, StepResult.Next, firstContext, CancellationToken.None);
                await vm.NodeFinishedAsync(firstNode, firstContext, CancellationToken.None);
                await vm.ParallelBranchFinishedAsync(firstContext, CancellationToken.None);
                Layout(window);
                Assert.Equal(Color.Parse("#22C55E"), ((ISolidColorBrush)firstItem.BorderBrush!).Color);
                Assert.Equal(new Thickness(5), firstItem.BorderThickness);
                Assert.Equal(Color.Parse("#FFD60A"), ((ISolidColorBrush)secondItem.BorderBrush!).Color);

                await vm.NodeCompletedAsync(secondNode, StepResult.False, secondContext, CancellationToken.None);
                await vm.NodeFailedAsync(secondNode, secondContext, CancellationToken.None);
                await vm.NodeFinishedAsync(secondNode, secondContext, CancellationToken.None);
                Layout(window);
                Assert.Equal(Color.Parse("#22C55E"), ((ISolidColorBrush)firstItem.BorderBrush!).Color);
                Assert.Equal(Color.Parse("#EF4444"), ((ISolidColorBrush)secondItem.BorderBrush!).Color);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LoadProfile_FitsNodesAfterEditorBecomesVisible(bool loadBeforeShow, bool hidden)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"graph-editor-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, """
                {"name":"Offset graph","nodes":[
                    {"id":"0","type":"Start","x":5000,"y":3000},
                    {"id":"1","type":"End","x":5500,"y":3000}
                ],"connections":[]}
                """);
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            using var view = new GraphEditorView { DataContext = vm };
            var parent = new Grid { IsVisible = !hidden, Children = { view } };
            var window = new Window { Width = 1000, Height = 700, Content = parent };
            try
            {
                if (!loadBeforeShow) { window.Show(); Layout(window); }
                vm.SelectedProfile = GraphSerializer.ReadProfile(path);
                if (loadBeforeShow) window.Show();
                Layout(window);
                parent.IsVisible = true;
                Layout(window);
                Assert.Equal("Загружен профиль: Offset graph", vm.StatusMessage);
                AssertGraphVisible(view, 2);

                // Loading another profile must also recover a viewport panned away by the operator.
                var editor = view.FindControl<NodifyEditor>("Editor")!;
                editor.ViewportLocation = new Point(-20000, -20000);
                vm.SelectedProfile = GraphSerializer.ReadProfile(path);
                Layout(window);
                AssertGraphVisible(view, 2);

                // Ordinary layout must preserve the operator's pan/zoom.
                var manualLocation = new Point(4500, 2800);
                editor.ViewportLocation = manualLocation;
                window.Width += 100;
                Layout(window);
                Assert.Equal(manualLocation, editor.ViewportLocation);
            }
            finally { window.Close(); File.Delete(path); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OpenBodyBeforeFirstLayout_FitsAfterWindowIsShown()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1000, Height = 700, Content = view };
            try
            {
                var subtest = new SubtestNodeViewModel();
                subtest.BodyGraph.Clear();
                subtest.BodyGraph.Nodes.Add(new StartNodeViewModel { Location = new Point(5000, 3000) });
                subtest.BodyGraph.Nodes.Add(new EndNodeViewModel { Location = new Point(5500, 3000) });
                vm.RootGraph.Nodes.Add(subtest);
                vm.OpenCompositeNodeBodyCommand.Execute(subtest);
                Dispatcher.UIThread.RunJobs();
                window.Show();
                Layout(window);
                AssertGraphVisible(view, 2);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ConnectWithRegisterNodesVisible_KeepsNodesAndSelections()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var registry = SlaveRegistry.Instance;
            var originalSlaves = registry.Slaves.ToArray();
            var wasConnected = registry.IsConnected;
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1500, Height = 800, Content = view };
            try
            {
                registry.NotifyConnected(false);
                registry.Slaves.Clear();
                var slave = new IO2Model(21, modbus);
                var address = (ushort)slave.RegisterItems.First(r => !r.IsReadOnly).Address;
                var write = new ModbusWriteNodeViewModel { SlaveId = 21, Address = address };
                var check = new CheckRegisterRangeNodeViewModel { SlaveId = 21, Address = address, Location = new Point(450, 0) };
                var wait = new WaitUntilNodeViewModel { SlaveId = 21, Address = address, Location = new Point(900, 0) };
                vm.Nodes.Add(write);
                vm.Nodes.Add(check);
                vm.Nodes.Add(wait);
                window.Show();
                Layout(window);
                registry.Slaves.Add(slave);
                for (var i = 0; i < 2; i++)
                {
                    registry.NotifyConnected(true);
                    Layout(window);
                    AssertGraphVisible(view, 3);
                    Assert.Same(slave, write.SelectedSlave);
                    Assert.Same(slave, check.SelectedSlave);
                    Assert.Same(slave, wait.SelectedSlave);
                    Assert.Equal((int)address, write.SelectedRegister?.Address);
                    Assert.Equal((int)address, check.SelectedRegister?.Address);
                    Assert.Equal((int)address, wait.SelectedRegister?.Address);
                    registry.NotifyConnected(false);
                    Layout(window);
                }
            }
            finally
            {
                window.Close();
                vm.Dispose();
                registry.NotifyConnected(false);
                registry.Slaves.Clear();
                foreach (var slave in originalSlaves) registry.Slaves.Add(slave);
                registry.NotifyConnected(wasConnected);
            }
        }, CancellationToken.None);
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        // Pointer input hit-tests the rendered scene, not just the latest layout bounds.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void AssertGraphVisible(GraphEditorView view, int count)
    {
        var editor = view.FindControl<NodifyEditor>("Editor")!;
        Assert.Equal(count, editor.GetVisualDescendants().OfType<ItemContainer>().Count());
        Assert.True(double.IsFinite(editor.ViewportZoom) && editor.ViewportZoom > 0);
        Assert.True(double.IsFinite(editor.ViewportLocation.X) && double.IsFinite(editor.ViewportLocation.Y));
        var viewport = new Rect(editor.ViewportLocation, editor.ViewportSize);
        Assert.True(viewport.Contains(editor.ItemsExtent), $"Viewport {viewport} does not contain graph {editor.ItemsExtent}");
    }
}
