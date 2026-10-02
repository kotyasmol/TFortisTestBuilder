using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[CollectionDefinition("Graph editor UI", DisableParallelization = true)]
public class GraphEditorUiCollection;

[Collection("Graph editor UI")]
public class GraphEditorViewTests
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());

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
