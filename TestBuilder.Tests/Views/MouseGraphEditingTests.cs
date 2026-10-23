using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class MouseGraphEditingTests
{
    [Fact]
    public async Task MouseControls_SelectCopyDuplicateDeleteAndUndo_AndLockDuringRun()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var a = new DelayNodeViewModel { Location = new Point(100, 100) };
            var b = new DelayNodeViewModel { Location = new Point(650, 100) };
            vm.Nodes.Add(a); vm.Nodes.Add(b);
            var wire = new ConnectionViewModel(a.Output[0], b.Input[0]);
            vm.Connections.Add(wire);
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1400, Height = 900, Content = view };
            try
            {
                window.Show(); Layout();
                Click(view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("MultiSelectNodesToggle")!);
                Assert.True(view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("MultiSelectNodesToggle")!.IsChecked);
                ClickNode(a);
                Assert.Same(a, Assert.Single(vm.SelectedNodes));
                ClickNode(b);
                window.CaptureRenderedFrame()!.Save("/private/tmp/tfortis-mouse-editor.png");
                Assert.Equal(2, vm.SelectedNodes.Count);
                ClickNode(a);
                Assert.Same(b, Assert.Single(vm.SelectedNodes));
                ClickNode(a);
                ClickButton("CopyNodesButton");
                ClickButton("PasteNodesButton");
                Assert.Equal(4, vm.Nodes.Count);
                Assert.Equal(2, vm.Connections.Count);
                ClickButton("DeleteSelectionButton");
                Assert.Equal(2, vm.Nodes.Count);
                Assert.Same(wire, Assert.Single(vm.Connections));
                Click(view.GetVisualDescendants().OfType<Button>().Single(x => x.Command == vm.UndoCommand));
                Assert.Equal(4, vm.Nodes.Count);
                ClickButton("ClearNodeSelectionButton");
                Assert.Empty(vm.SelectedNodes);
                ClickButton("SelectAllNodesButton");
                Assert.Equal(4, vm.SelectedNodes.Count);
                ClickButton("DuplicateNodesButton");
                Assert.Equal(8, vm.Nodes.Count);
                vm.UndoCommand.Execute(null); Layout();
                vm.SelectConnection(wire);
                ClickButton("DeleteSelectionButton");
                Assert.DoesNotContain(wire, vm.Connections);
                vm.UndoCommand.Execute(null); Layout();
                Assert.Contains(wire, vm.Connections);

                // Right-clicking an unselected node targets that node, not stale selection.
                ClickButton("ClearNodeSelectionButton");
                ClickNode(a, MouseButton.Right);
                Assert.Same(a, Assert.Single(vm.SelectedNodes));
                var remove = view.NodeRunMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Удалить"));
                remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                view.NodeRunMenu.Close(); Layout();
                Assert.DoesNotContain(a, vm.Nodes);
                Assert.DoesNotContain(wire, vm.Connections);
                vm.UndoCommand.Execute(null); Layout();
                Assert.Contains(a, vm.Nodes);
                Assert.Contains(wire, vm.Connections);

                var ports = view.GetVisualDescendants().OfType<Connector>().ToArray();
                var sourcePort = ports.Single(c => c.DataContext == wire.Source);
                var targetPort = ports.Single(c => c.DataContext == wire.Target);
                var from = sourcePort.TranslatePoint(new Point(sourcePort.Bounds.Width / 2, sourcePort.Bounds.Height / 2), window)!.Value;
                var to = targetPort.TranslatePoint(new Point(targetPort.Bounds.Width / 2, targetPort.Bounds.Height / 2), window)!.Value;
                var middle = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2);
                window.MouseDown(middle, MouseButton.Right); window.MouseUp(middle, MouseButton.Right); Layout();
                var removeWire = view.NodeRunMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Удалить связь"));
                removeWire.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                view.NodeRunMenu.Close(); Layout();
                Assert.DoesNotContain(wire, vm.Connections);
                vm.UndoCommand.Execute(null); Layout();
                Assert.Contains(wire, vm.Connections);

                vm.IsTestRunning = true; Layout();
                foreach (var name in new[] { "CopyNodesButton", "PasteNodesButton", "DuplicateNodesButton", "DeleteSelectionButton" })
                    Assert.False(view.FindControl<Button>(name)!.IsEffectivelyEnabled);
                var count = vm.Nodes.Count;
                ClickButton("DeleteSelectionButton");
                Assert.Equal(count, vm.Nodes.Count);
                vm.IsTestRunning = false; Layout();
                window.CaptureRenderedFrame()!.Save("/private/tmp/tfortis-mouse-editor.png");
            }
            finally { window.Close(); }

            void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); }
            void ClickButton(string name) => Click(view.FindControl<Button>(name)!);
            void Click(Control control)
            {
                var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout();
            }
            void ClickNode(NodeViewModel node, MouseButton button = MouseButton.Left)
            {
                var control = view.GetVisualDescendants().OfType<ItemContainer>().Single(c => c.DataContext == node);
                var point = control.TranslatePoint(new Point(15, 15), window)!.Value;
                window.MouseDown(point, button); window.MouseUp(point, button); Layout();
            }
        }, CancellationToken.None);
    }
}
