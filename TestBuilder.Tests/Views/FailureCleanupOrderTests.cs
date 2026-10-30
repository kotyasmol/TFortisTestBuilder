using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class FailureCleanupOrderTests
{
    [Fact]
    public async Task ContinuedPrintFailure_CompletesNormalPath_WithoutRepeatingEmergencyActions()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.NewProfileCommand.Execute(null);
            var sub = new SubtestNodeViewModel { Name = "Печать", StopOnError = false };
            var print = new PrintLabelNodeViewModel { UseManualSerialNumber = true, ManualSerialNumber = "12345678", PrinterName = "" };
            sub.BodyGraph.Nodes.Add(print);
            sub.BodyGraph.Connections.Add(new(sub.BodyGraph.Nodes.OfType<StartNodeViewModel>().Single().Output[0], print.In));
            sub.BodyGraph.Connections.Add(new(print.TrueOut, sub.BodyGraph.Nodes.OfType<EndNodeViewModel>().Single().Input[0]));
            var start = new StartNodeViewModel(); var end = new EndNodeViewModel();
            var cleanup = Cleanup("Аварийные действия");
            foreach (var node in new NodeViewModel[] { start, sub, end, cleanup }) vm.RootGraph.Nodes.Add(node);
            vm.RootGraph.Connections.Add(new(start.Output[0], sub.Input[0]));
            vm.RootGraph.Connections.Add(new(sub.Output[0], end.Input[0]));
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(end.HasExecutionSucceeded);
            Assert.False(vm.Station.IsSuccessful);
            Assert.DoesNotContain(vm.TestingLogger.Entries, entry => entry.Message.StartsWith("[ШАГ] Cleanup '"));
            Assert.False(vm.IsTestRunning); Assert.False(vm.NeedsStandInspection);
            return true;
        }, default);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailureAndStop_RunExplicitOrder_AndContinueAfterFailedCleanup(bool cancel)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.NewProfileCommand.Execute(null);
            var first = Cleanup("Первый", fails: true); first.FailureOrder = 1;
            var second = Cleanup("Второй"); second.FailureOrder = 2;
            var disabled = Cleanup("Отключённый"); disabled.FailureOrder = 1; disabled.IsEnabled = false;
            var appended = Cleanup("Новый без номера");
            var start = new StartNodeViewModel(); var end = new EndNodeViewModel();
            NodeViewModel main = cancel ? new DelayNodeViewModel { Milliseconds = 10000 }
                : new CheckVariableEqualityNodeViewModel { VariableName = "missing" };
            // Deliberately reverse collection and screen order relative to the queue.
            first.Location = new Point(1000, 1000); second.Location = new Point(0, 0);
            foreach (var node in new[] { start, main, second, disabled, appended, first, end }) vm.RootGraph.Nodes.Add(node);
            vm.RootGraph.Connections.Add(new(start.Output[0], main.Input[0]));
            vm.RootGraph.Connections.Add(new(main.Output[0], end.Input[0]));
            var run = vm.RunGraphCommand.ExecuteAsync(null);
            if (cancel) { await Task.Delay(40); vm.StopTestCommand.Execute(null); }
            await run;
            var starts = vm.TestingLogger.Entries.Select(e => e.Message)
                .Where(m => m.StartsWith("[ШАГ] Cleanup '") && m.EndsWith("' начат.")).ToArray();
            Assert.Equal(new[] { "[ШАГ] Cleanup 'Первый' начат.", "[ШАГ] Cleanup 'Второй' начат.", "[ШАГ] Cleanup 'Новый без номера' начат." }, starts);
            Assert.Contains(vm.TestingLogger.Entries, e => e.Message == "[OK] Cleanup 'Второй' завершён.");
            Assert.False(vm.IsTestRunning);
            Assert.True(vm.NeedsStandInspection); // The failed first action is not concealed by later success.
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LegacyOrder_Copy_RoundTrip_AndInvalidInputPreserveGraph()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var a = Cleanup("A"); var b = Cleanup("B"); var c = Cleanup("C");
            var start = new StartNodeViewModel(); var end = new EndNodeViewModel();
            foreach (var n in new NodeViewModel[] { start, a, b, c, end }) vm.RootGraph.Nodes.Add(n);
            vm.RootGraph.Connections.Add(new(start.Output[0], end.Input[0]));
            var legacy = GraphSerializer.Serialize(vm, "Legacy");
            Assert.DoesNotContain("failureOrder", legacy);
            Assert.Equal(new[] { a, b, c }, FailureCleanupOrder.GetNodes(vm.RootGraph));
            vm.ApplyFailureCleanupOrder(new[] { c, a, b });
            var json = GraphSerializer.Serialize(vm, "Ordered");
            Assert.Contains("Subtest (cleanup order v1)", json);
            using var copy = new TestViewModel(bus, new SlaveManager(bus));
            GraphSerializer.Deserialize(json, copy);
            Assert.Equal(new[] { "C", "A", "B" }, FailureCleanupOrder.GetNodes(copy.RootGraph).Select(n => n.Name));
            var cloned = Assert.IsType<SubtestNodeViewModel>(c.Clone());
            Assert.Equal(c.FailureOrder, cloned.FailureOrder);
            copy.RootGraph.Nodes.Add(cloned);
            Assert.Equal(new[] { "C", "C", "A", "B" }, FailureCleanupOrder.GetNodes(copy.RootGraph).Select(n => n.Name));
            // Equal priorities (e.g. a copied node) retain their source order; new actions append.
            var fresh = Cleanup("Fresh"); copy.RootGraph.Nodes.Insert(1, fresh);
            Assert.Same(fresh, FailureCleanupOrder.GetNodes(copy.RootGraph).Last());
            c.RunOnFailure = false;
            Assert.Equal(new[] { a, b }, FailureCleanupOrder.GetNodes(vm.RootGraph));
            c.RunOnFailure = true;
            Assert.Same(c, FailureCleanupOrder.GetNodes(vm.RootGraph).First());

            var before = copy.RootGraph.Nodes.ToArray();
            foreach (int? invalid in new int?[] { 0, -1, null })
            {
                var broken = JsonNode.Parse(json)!;
                broken["nodes"]!.AsArray().OfType<JsonObject>().First(n => n["failureOrder"] != null)["failureOrder"] = invalid;
                Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(broken.ToJsonString(), copy));
                Assert.Equal(before, copy.RootGraph.Nodes);
            }
            Assert.Throws<InvalidOperationException>(() => vm.ApplyFailureCleanupOrder(new[] { a, a, c }));
            Assert.Equal(new[] { c, a, b }, FailureCleanupOrder.GetNodes(vm.RootGraph));
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MouseEditor_CancelApplyUndoAndRunLock(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var a = Cleanup("Аварийное выключение стенда"); var b = Cleanup("Отключённый подтест"); var c = Cleanup("Отчёт при ошибке");
            b.IsEnabled = false;
            foreach (var n in new[] { a, b, c }) vm.RootGraph.Nodes.Add(n);
            var originalNodes = vm.RootGraph.Nodes.ToArray();
            using var view = new GraphEditorView { DataContext = vm };
            var owner = new Window { Width = 1300, Height = 850, Content = view };
            try
            {
                owner.Show(); Layout(owner);
                var toolbar = view.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Порядок аварийных действий");
                Assert.True(toolbar.IsEffectivelyVisible); Assert.True(toolbar.IsEffectivelyEnabled);
                for (var apply = 0; apply < 2; apply++)
                {
                    var task = vm.ConfigureFailureCleanupCommand.ExecuteAsync(owner);
                    Layout(owner);
                    var dialog = Assert.Single(owner.OwnedWindows.OfType<FailureCleanupOrderWindow>());
                    Layout(dialog);
                    var draft = Assert.IsType<FailureCleanupOrderViewModel>(dialog.DataContext);
                    var row = draft.Items.Single(i => i.Node == c);
                    var from = RowPoint(dialog, row, 0.5);
                    var to = RowPoint(dialog, draft.Items[0], 0.1);
                    dialog.MouseDown(from, MouseButton.Left);
                    dialog.MouseMove(to, RawInputModifiers.LeftMouseButton); Layout(dialog);
                    Assert.True(row.IsDragging);
                    Assert.True(draft.Items[0].DropBefore);
                    Assert.Equal(new[] { a, b, c }, draft.BuildResult()); // Commit only on release.
                    dialog.MouseUp(to, MouseButton.Left); Layout(dialog);
                    Assert.Equal(new[] { c, a, b }, draft.BuildResult());
                    Assert.Equal(new[] { 1, 2, 3 }, draft.Items.Select(i => i.Number));
                    Assert.Null(c.FailureOrder); // Draft must not mutate the live document.
                    Assert.True(draft.Items.Last().IsDisabled);
                    Assert.All(draft.Items, i => Assert.False(i.IsDragging || i.DropBefore || i.DropAfter));
                    var folder = Environment.GetEnvironmentVariable("TFORTIS_CLEANUP_ORDER_CAPTURES");
                    if (folder != null) { Directory.CreateDirectory(folder); using var frame = dialog.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, $"order-{dark}.png")); }
                    Click(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, apply == 1 ? "Применить к профилю" : "Отмена")));
                    await task;
                    if (apply == 0) Assert.All(new[] { a, b, c }, n => Assert.Null(n.FailureOrder));
                }
                Assert.Equal(new[] { c, a, b }, FailureCleanupOrder.GetNodes(vm.RootGraph, includeDisabled: true));
                Assert.Equal(originalNodes, vm.RootGraph.Nodes);
                vm.UndoCommand.Execute(null);
                Assert.All(new[] { a, b, c }, n => Assert.Null(n.FailureOrder));
                vm.RedoCommand.Execute(null);
                Assert.Equal(1, c.FailureOrder); Assert.Equal(2, a.FailureOrder); Assert.Equal(3, b.FailureOrder);
                vm.IsTestRunning = true; Layout(owner);
                Assert.False(toolbar.IsEffectivelyEnabled);
                Assert.False(vm.ConfigureFailureCleanupCommand.CanExecute(owner));
                vm.ApplyFailureCleanupOrder(new[] { a, b, c });
                Assert.Equal(1, c.FailureOrder);
                vm.IsTestRunning = false;
                vm.IsOperatorView = true; Layout(owner);
                Assert.False(toolbar.IsEffectivelyVisible);
                Assert.False(vm.ConfigureFailureCleanupCommand.CanExecute(owner));
                vm.IsOperatorView = false; vm.IsActionGraphEditor = true;
                Assert.False(vm.ConfigureFailureCleanupCommand.CanExecute(owner));
            }
            finally { foreach (var d in owner.OwnedWindows.ToArray()) d.Close(); owner.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Drag_DownAndUp_AutoScrolls_AndOutsideOrEscapeCancels()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var nodes = Enumerable.Range(1, 10).Select(i => Cleanup($"Действие {i}")).ToArray();
            var draft = new FailureCleanupOrderViewModel(nodes);
            var window = new FailureCleanupOrderWindow(draft);
            try
            {
                window.Show(); Layout(window);
                var scroll = window.FindControl<ScrollViewer>("OrderScroll")!;
                var viewport = new Rect(scroll.TranslatePoint(default, window)!.Value, scroll.Bounds.Size);
                var top = new Point(viewport.Center.X, viewport.Top + 3);
                var bottom = new Point(viewport.Center.X, viewport.Bottom - 3);
                var item = draft.Items[0];
                var from = RowPoint(window, item, 0.5);
                // A click or small accidental motion must not change the list.
                window.MouseDown(from, MouseButton.Left); window.MouseMove(from + new Vector(2, 1), RawInputModifiers.LeftMouseButton);
                window.MouseUp(from + new Vector(2, 1), MouseButton.Left); Layout(window);
                Assert.Equal(nodes, draft.BuildResult());
                // Dropping over the footer cancels; it must not activate Apply/Cancel.
                window.MouseDown(from, MouseButton.Left); window.MouseMove(bottom, RawInputModifiers.LeftMouseButton); Layout(window);
                var outside = new Point(viewport.Center.X, window.Bounds.Height - 8);
                window.MouseMove(outside, RawInputModifiers.LeftMouseButton); window.MouseUp(outside, MouseButton.Left); Layout(window);
                Assert.True(window.IsVisible); Assert.Equal(nodes, draft.BuildResult());
                window.MouseDown(from, MouseButton.Left); window.MouseMove(bottom, RawInputModifiers.LeftMouseButton); Layout(window);
                window.KeyPress(Key.Escape, RawInputModifiers.None); window.KeyRelease(Key.Escape, RawInputModifiers.None);
                window.MouseUp(bottom, MouseButton.Left); Layout(window);
                Assert.True(window.IsVisible); Assert.Equal(nodes, draft.BuildResult());
                Assert.All(draft.Items, i => Assert.False(i.IsDragging || i.DropBefore || i.DropAfter));

                window.MouseDown(RowPoint(window, item, 0.5), MouseButton.Left);
                window.MouseMove(bottom, RawInputModifiers.LeftMouseButton); Layout(window);
                for (var i = 0; i < 70 && scroll.Offset.Y < scroll.Extent.Height - scroll.Viewport.Height; i++)
                { await Task.Delay(40); Layout(window); }
                Assert.True(scroll.Offset.Y > 0);
                Assert.True(draft.Items[^1].DropAfter);
                window.MouseUp(bottom, MouseButton.Left); Layout(window);
                Assert.Same(item, draft.Items[^1]);
                Assert.Equal(Enumerable.Range(1, 10), draft.Items.Select(i => i.Number));

                window.MouseDown(RowPoint(window, item, 0.5), MouseButton.Left);
                window.MouseMove(top, RawInputModifiers.LeftMouseButton); Layout(window);
                for (var i = 0; i < 70 && scroll.Offset.Y > 0; i++) { await Task.Delay(40); Layout(window); }
                Assert.Equal(0, scroll.Offset.Y);
                Assert.True(draft.Items[0].DropBefore);
                window.MouseUp(top, MouseButton.Left); Layout(window);
                Assert.Equal(nodes, draft.BuildResult());
                Assert.All(nodes, n => Assert.Null(n.FailureOrder));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static SubtestNodeViewModel Cleanup(string name, bool fails = false)
    {
        var sub = new SubtestNodeViewModel { Name = name, RunOnFailure = true };
        var start = sub.BodyGraph.Nodes.OfType<StartNodeViewModel>().Single();
        var end = sub.BodyGraph.Nodes.OfType<EndNodeViewModel>().Single();
        if (fails)
        {
            var fail = new CheckVariableEqualityNodeViewModel { VariableName = "missing" };
            sub.BodyGraph.Nodes.Add(fail);
            sub.BodyGraph.Connections.Add(new(start.Output[0], fail.Input[0]));
        }
        else sub.BodyGraph.Connections.Add(new(start.Output[0], end.Input[0]));
        return sub;
    }

    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); }
    private static Point RowPoint(Window window, FailureCleanupItemViewModel item, double y)
    {
        var row = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "CleanupOrderRow" && ReferenceEquals(g.DataContext, item));
        return row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height * y), window)!.Value;
    }
    private static void Click(Window window, Button button)
    {
        var p = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); Layout(window);
    }
}
