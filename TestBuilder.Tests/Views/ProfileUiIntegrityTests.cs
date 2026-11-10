using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileUiIntegrityTests
{
    [Fact]
    public async Task EveryPaletteNode_CanBeViewedAndSavedWithoutChangingParameters()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1400, Height = 1100, Content = view };
            try
            {
                window.Show();
                foreach (var sample in vm.AvailableNodes)
                {
                    vm.ResetToRootGraph();
                    vm.RootGraph.Clear();
                    vm.Nodes.Add(sample.Clone());
                    var before = Save(vm);
                    var rawBefore = RawKeys(vm.RootGraph);
                    for (var repeat = 0; repeat < 2; repeat++)
                    {
                        Layout(window);
                        Assert.Equal(rawBefore, RawKeys(vm.RootGraph));
                        Assert.True(before == Save(vm), $"Rendering {sample.GetType().Name} changed its persisted parameters.\nBefore: {before}\nAfter: {Save(vm)}");
                        GraphSerializer.Deserialize(Save(vm), vm);
                    }
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    public static IEnumerable<object[]> ProductionProfiles() => Directory.GetFiles(
        Path.GetDirectoryName(TestProfilePaths.Find("PSW+UPS-Box 8x2Pro · Параллельная проверка с SFP · 10.11.json"))!, "*.json")
        .OrderBy(path => path).Select(path => new object[] { Path.GetFileName(path) });

    [Theory]
    [MemberData(nameof(ProductionProfiles))]
    public async Task ProductionProfile_AllNestedEditorsPreserveData(string file)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            GraphSerializer.Deserialize(File.ReadAllText(TestProfilePaths.Find(file)), vm);
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1440, Height = 1100, Content = view };
            var before = Save(vm);
            var rawBefore = RawKeys(vm.RootGraph);
            try
            {
                window.Show();
                Visit(vm, vm.RootGraph, window);
                Assert.Equal(rawBefore, RawKeys(vm.RootGraph));
                Assert.Equal(before, Save(vm));
                foreach (var action in vm.RootGraph.QuickActions)
                {
                    var actionWindow = new ProfileActionEditorWindow(action, vm.RootGraph.Hardware);
                    var editors = new[] { "OnEditor", "OffEditor" }.Select(name =>
                        (TestViewModel)actionWindow.FindControl<GraphEditorView>(name)!.DataContext!).ToArray();
                    var snapshots = editors.Select(Save).ToArray();
                    var rawSnapshots = editors.Select(editor => RawKeys(editor.RootGraph)).ToArray();
                    try
                    {
                        actionWindow.Show();
                        for (var index = 0; index < editors.Length; index++)
                        {
                            actionWindow.FindControl<TabControl>("GraphTabs")!.SelectedIndex = index;
                            Visit(editors[index], editors[index].RootGraph, actionWindow);
                            Assert.Equal(rawSnapshots[index], RawKeys(editors[index].RootGraph));
                            Assert.Equal(snapshots[index], Save(editors[index]));
                        }
                    }
                    finally { actionWindow.Close(); }
                }
                GraphSerializer.Deserialize(Save(vm), vm);
                Visit(vm, vm.RootGraph, window);
                Assert.Equal(before, Save(vm));
            }
            finally { window.Close(); }

            void Visit(TestViewModel editor, GraphWorkspaceViewModel graph, Window owner)
            {
                Layout(owner);
                foreach (var node in graph.Nodes.OfType<CompositeNodeViewModel>().ToArray())
                {
                    editor.OpenCompositeNodeBodyCommand.Execute(node);
                    Visit(editor, node.BodyGraph, owner);
                    editor.GoBackGraphCommand.Execute(null);
                }
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("production", "Производственная")]
    [InlineData("repair", "Ремонтная")]
    [InlineData("SoftwarePcap", "SoftwarePcap")]
    [InlineData("Json", "Json")]
    public async Task ServerTestTypeEditor_DoesNotTranslateOtherDomains(string key, string label)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var node = new BuildTestReportNodeViewModel { TestType = key };
            vm.Nodes.Clear(); vm.Nodes.Add(node);
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1200, Height = 900, Content = view };
            try
            {
                window.Show(); Layout(window);
                var input = Assert.Single(view.GetVisualDescendants().OfType<TextBox>()
                    .Where(t => ReferenceEquals(t.DataContext, node) && t.Text == label));
                Assert.Equal(key, node.TestType);
                input.Text = "Ремонтная"; Layout(window);
                Assert.Equal("repair", node.TestType);
                input.Text = "custom-server-code"; Layout(window);
                Assert.Equal("custom-server-code", node.TestType);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TranslatedNameColumn_DisplaysLabelButEditsOriginalName(bool register)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            object row = register ? new RegisterDefinition { Name = "PoeMode" } : new BoardRegion { Name = "PoeMode" };
            string Name() => register ? ((RegisterDefinition)row).Name : ((BoardRegion)row).Name;
            var column = new TranslatedNameColumn { Header = "Название", Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
            var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { row } };
            grid.Columns.Add(column);
            var window = new Window { Width = 600, Height = 400, Content = grid };
            try
            {
                window.Show(); Layout(window);
                Assert.Contains("Режим PoE", grid.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
                Assert.Equal("PoeMode", Name());
                grid.SelectedItem = row; grid.CurrentColumn = column;
                Assert.True(grid.BeginEdit()); Layout(window);
                var input = Assert.Single(grid.GetVisualDescendants().OfType<TextBox>().Where(t => t.Text == "PoeMode"));
                input.Text = "Моё название";
                Assert.True(grid.CommitEdit()); Layout(window);
                Assert.Equal("Моё название", Name());
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static string Save(TestViewModel vm) => GraphSerializer.Serialize(vm, "Integrity", DateTimeOffset.UnixEpoch, "Integrity");
    // Serialization intentionally repairs known legacy mode values; check the raw
    // source too so that this safety net cannot mask a newly broken UI binding.
    private static string RawKeys(GraphWorkspaceViewModel graph)
    {
        var values = new List<string>();
        foreach (var node in graph.Nodes)
        {
            foreach (var name in new[] { "Mode", "ParseMode", "ValueType", "PollAction", "ResponseType", "ComparisonType", "ExpectedColor", "TestType" })
                if (node.GetType().GetProperty(name) is { } property) values.Add($"{node.GetType().Name}.{name}={property.GetValue(node)}");
            if (node is ICompositeNodeViewModel composite) values.Add(RawKeys(composite.BodyGraph));
        }
        return string.Join("\n", values);
    }
    private static void Layout(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs();
    }
}
