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
        .UseSkia().WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileClicks_OpenOnFirstClick_AndRepeatedClickKeepsGraphAndEdits(bool connecting)
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
                vm.IsConnecting = connecting;
                Assert.Equal(!connecting, vm.CanChooseProfile);
                if (connecting) Assert.False(vm.CanStartRun);
                var profile = GraphSerializer.ReadProfile(path);
                vm.Profiles.Clear();
                vm.Profiles.Add(profile);
                window.Show();
                Layout(window);
                foreach (var group in vm.EngineerProfileGroups) group.IsExpanded = true;
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
                if (connecting)
                {
                    Assert.NotSame(profile, vm.SelectedProfile);
                    vm.IsConnecting = false;
                    Layout(window);
                    Click();
                }
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
            var small = GraphSerializer.ReadProfile(TestBuilder.Tests.Support.TestProfilePaths.Find("manual_label_printing.json"));
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
                    foreach (var group in vm.EngineerProfileGroups) group.IsExpanded = true;
                Layout(window);
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
                Assert.Equal("Offset graph", vm.CurrentProfileName);
                Assert.Equal("Профиль выбран.", vm.StatusMessage);
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

    [Fact]
    public async Task RegisterHelp_WorksOffline_UpdatesForSlaveAndLoop_AndSurvivesDisconnect()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var registry = SlaveRegistry.Instance;
            var originalSlaves = registry.Slaves.ToArray();
            var wasConnected = registry.IsConnected;
            using var modbus = new ModbusService();
            registry.NotifyConnected(false);
            registry.Slaves.Clear();
            using var write = new ModbusWriteNodeViewModel { SlaveId = 21, Address = 1200 };
            using var check = new CheckRegisterRangeNodeViewModel { SlaveId = 21, Address = 1200 };
            using var wait = new WaitUntilNodeViewModel { SlaveId = 21, Address = 1200 };
            string[] Descriptions() => new[] { write.RegisterDescription, check.RegisterDescription, wait.RegisterDescription };
            try
            {
                Assert.All(Descriptions(), description =>
                {
                    Assert.Contains("PS-2:", description);
                    Assert.Contains("PS-3:", description);
                    Assert.Contains("RW", description);
                    Assert.Contains("Выход AC1", description);
                });
                Assert.Empty(registry.Slaves);
                Assert.False(modbus.IsConnected);
                registry.Slaves.Add(new PS2Model(21, modbus));
                registry.Slaves.Add(new Ps3Model(23, modbus));
                registry.NotifyConnected(true);
                Assert.All(Descriptions(), description => Assert.DoesNotContain("PS-3:", description));
                write.SlaveId = check.SlaveId = wait.SlaveId = 23;
                Assert.All(Descriptions(), description =>
                {
                    Assert.Contains("PS-3:", description);
                    Assert.DoesNotContain("PS-2:", description);
                });
                write.UseCurrentSlaveId = check.UseCurrentSlaveId = wait.UseCurrentSlaveId = true;
                Assert.All(Descriptions(), description => Assert.Contains("PS-2:", description));
                write.UseCurrentSlaveId = check.UseCurrentSlaveId = wait.UseCurrentSlaveId = false;
                registry.NotifyConnected(false);
                Assert.All(Descriptions(), description => Assert.Contains("PS-2:", description));
                registry.Slaves.Clear();
                write.SyncSlaves(); check.SyncSlaves(); wait.SyncSlaves();
                Assert.NotNull(write.SelectedRegister);
                Assert.True(write.ShowRegisterSelector);
                write.Address = check.Address = wait.Address = 65535;
                Assert.All(Descriptions(), description => Assert.Contains("описание отсутствует", description));
                write.Address = check.Address = wait.Address = 1000;
                Assert.All(Descriptions(), description => Assert.Contains("RO — чтение", description));
            }
            finally
            {
                registry.NotifyConnected(false);
                registry.Slaves.Clear();
                foreach (var slave in originalSlaves) registry.Slaves.Add(slave);
                registry.NotifyConnected(wasConnected);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClearGraph_CancelPreservesGraph_ConfirmCanBeUndone_AndRunBlocksPendingClear()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            var node = new ModbusWriteNodeViewModel();
            vm.Nodes.Add(node);
            var count = vm.Nodes.Count;
            var clear = (CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.ClearGraphCommand;
            vm.ConfirmClearGraphAsync = () => Task.FromResult(false);
            await clear.ExecuteAsync(null);
            Assert.Equal(count, vm.Nodes.Count);
            Assert.Contains(node, vm.Nodes);
            vm.ConfirmClearGraphAsync = () => Task.FromResult(true);
            await clear.ExecuteAsync(null);
            Assert.Empty(vm.Nodes);
            vm.UndoCommand.Execute(null);
            Assert.Equal(count, vm.Nodes.Count);
            Assert.Contains(node, vm.Nodes);

            var confirmation = new TaskCompletionSource<bool>();
            vm.ConfirmClearGraphAsync = () => confirmation.Task;
            var pending = clear.ExecuteAsync(null);
            vm.IsTestRunning = true;
            confirmation.SetResult(true);
            await pending;
            Assert.Equal(count, vm.Nodes.Count);
            vm.IsTestRunning = false;
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task HelpCard_CopiesWholeText_AndClosesWithButton()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            using var node = new SelfTestCheckNodeViewModel();
            using var view = new GraphEditorView { DataContext = vm, HelpNode = node, IsNodeHelpOpen = true };
            var window = new Window { Width = 1200, Height = 800, Content = view };
            try
            {
                window.Show();
                Layout(window);
                Layout(view.HelpWindow!);
                Assert.Contains(view.HelpWindow!.GetVisualDescendants().OfType<SelectableTextBlock>(),
                    text => text.Text == node.HelpSummary);
                var search = view.HelpWindow!.GetVisualDescendants().OfType<TextBox>().Single();
                search.Text = "несуществующий-раздел-12345"; Layout(view.HelpWindow!);
                Assert.Empty(view.HelpWindow!.GetVisualDescendants().OfType<SelectableTextBlock>());
                search.Text = ""; Layout(view.HelpWindow!);
                var copy = view.HelpWindow!.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Копировать"));
                copy.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal($"Самопроверка устройства\n\n{node.HelpText}", await view.HelpWindow!.Clipboard!.GetTextAsync());
                var close = view.HelpWindow!.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "✕ Закрыть"));
                close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.False(view.IsNodeHelpOpen);
                Assert.Null(view.HelpNode);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ContextMenu_ZoomsCentersAndCollapsesPalette_WithoutChangingGraph()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            vm.AddNodeAtLocation("Задержка", new Point(200, 100));
            var node = vm.Nodes[0];
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1100, Height = 700, Content = view };
            try
            {
                window.Show(); Layout(window);
                var editor = view.FindControl<NodifyEditor>("Editor")!;
                void Click(string name)
                {
                    if (name != "Свернуть или развернуть палитру")
                    {
                        view.OpenGraphMenu(); Layout(window);
                        var menu = view.NodeRunMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Масштаб"));
                        var title = name.Replace(" масштаб", "");
                        menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, title))
                            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                        view.NodeRunMenu.Close(); Layout(window);
                        return;
                    }
                    var button = view.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == name);
                    var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
                }
                editor.ViewportZoom = 0.5;
                Click("Увеличить масштаб"); Assert.True(editor.ViewportZoom > 0.5);
                var zoom = editor.ViewportZoom;
                Click("Уменьшить масштаб"); Assert.True(editor.ViewportZoom < zoom);
                zoom = editor.ViewportZoom;
                editor.ViewportLocation = new Point(5000, 5000);
                Click("Центрировать граф");
                Assert.Equal(zoom, editor.ViewportZoom);
                Assert.InRange(Math.Abs(editor.ViewportLocation.X + editor.ViewportSize.Width / 2 - editor.ItemsExtent.Center.X), 0, 1);
                var width = editor.Bounds.Width;
                Click("Свернуть или развернуть палитру");
                Assert.True(vm.IsPaletteCollapsed); Assert.True(editor.Bounds.Width > width + 200);
                Click("Свернуть или развернуть палитру"); Assert.False(vm.IsPaletteCollapsed);
                Assert.Same(node, Assert.Single(vm.Nodes)); Assert.Equal(new Point(200, 100), node.Location);
                var title = view.GetVisualDescendants().OfType<AutoCompleteBox>().Single(t => Avalonia.Automation.AutomationProperties.GetName(t) == "Название профиля").GetVisualDescendants().OfType<TextBox>().Single();
                title.Focus(); title.Text = "Проверка после сборки";
                view.GetVisualDescendants().OfType<Button>().First(b => b.IsEnabled).Focus(); Layout(window);
                Assert.Equal("Проверка после сборки", vm.CurrentProfileName);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledSubtest_GraysHeaderBodyAndFields_AndRestoresSelectedColor(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            Application.Current!.RequestedThemeVariant = dark
                ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
            using var modbus = new ModbusService();
            using var vm = new TestViewModel(modbus, new SlaveManager(modbus));
            vm.IsOperatorView = false;
            var model = new SubtestNodeViewModel { NodeColor = "orange" };
            vm.Nodes.Add(model);
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1100, Height = 750, Content = view };
            try
            {
                window.Show();
                Layout(window);
                var node = view.GetVisualDescendants().OfType<Node>()
                    .Single(n => ReferenceEquals(n.DataContext, model));
                var originalHeader = ((ISolidColorBrush)node.HeaderBrush!).Color;
                var originalBody = ((ISolidColorBrush)node.ContentBrush!).Color;
                model.IsEnabled = false;
                Layout(window);
                Assert.Equal(Colors.Gray, ((ISolidColorBrush)node.HeaderBrush!).Color);
                var grayBody = ((ISolidColorBrush)node.ContentBrush!).Color;
                Assert.Equal(grayBody.R, grayBody.G);
                Assert.Equal(grayBody.G, grayBody.B);
                Assert.NotEqual(originalBody, grayBody);
                foreach (var name in new[] { "NodeOptionsTrigger", "ColorPickerTrigger" })
                {
                    var button = node.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
                    Assert.Equal(0, ((ISolidColorBrush)button.Background!).Color.A);
                    Assert.IsType<Avalonia.Controls.Shapes.Path>(button.Content);
                }
                var profileToggle = node.GetVisualDescendants().OfType<CheckBox>()
                    .Single(c => c.Name == "ProfileOpenToggle");
                var enabledToggle = node.GetVisualDescendants().OfType<CheckBox>()
                    .Single(c => c.Content is TextBlock { Text: "Включен" });
                Assert.Equal(enabledToggle.MinHeight, profileToggle.MinHeight);
                Assert.Equal(enabledToggle.FontSize, profileToggle.FontSize);
                Assert.Equal(((ISolidColorBrush)enabledToggle.Background!).Color,
                    ((ISolidColorBrush)profileToggle.Background!).Color);
                Assert.Contains(profileToggle, Assert.IsType<StackPanel>(node.Content).Children);
                Assert.True(profileToggle.TranslatePoint(default, node)!.Value.Y >=
                    enabledToggle.TranslatePoint(new Point(0, enabledToggle.Bounds.Height), node)!.Value.Y);
                var captureFolder = Environment.GetEnvironmentVariable("TESTBUILDER_LAYOUT_CAPTURES");
                if (!string.IsNullOrEmpty(captureFolder))
                {
                    Directory.CreateDirectory(captureFolder);
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(captureFolder, $"inactive-node-{(dark ? "dark" : "light")}.png"));
                }
                vm.IsTestRunning = true;
                Layout(window);
                Assert.Equal(enabledToggle.IsEffectivelyEnabled, profileToggle.IsEffectivelyEnabled);
                Assert.False(profileToggle.IsHitTestVisible);
                Assert.False(profileToggle.Focusable);
                if (!string.IsNullOrEmpty(captureFolder))
                {
                    using var frame = window.CaptureRenderedFrame();
                    frame!.Save(Path.Combine(captureFolder, $"running-node-{(dark ? "dark" : "light")}.png"));
                }
                vm.IsTestRunning = false;
                Layout(window);
                foreach (var field in node.GetVisualDescendants().OfType<TextBox>())
                {
                    var color = ((ISolidColorBrush)field.Background!).Color;
                    Assert.Equal(color.R, color.G);
                    Assert.Equal(color.G, color.B);
                    Assert.True(field.IsEnabled);
                }
                Assert.True(node.GetVisualDescendants().OfType<CheckBox>()
                    .First(c => c.IsChecked == false).IsEnabled);
                model.IsEnabled = true;
                Layout(window);
                Assert.Equal("orange", model.NodeColor);
                Assert.Equal(originalHeader, ((ISolidColorBrush)node.HeaderBrush!).Color);
                Assert.Equal(originalBody, ((ISolidColorBrush)node.ContentBrush!).Color);
            }
            finally { window.Close(); }
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
