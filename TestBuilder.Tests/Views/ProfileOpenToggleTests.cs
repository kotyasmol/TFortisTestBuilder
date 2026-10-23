using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileOpenToggleTests
{
    [Fact]
    public async Task NodeHeaderToggle_IsVisibleUndoable_AndLockedDuringRun()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var start = new StartNodeViewModel { Location = new Point(0, 100) };
            var prep = new DelayNodeViewModel { Location = new Point(350, 100) };
            var end = new EndNodeViewModel { Location = new Point(800, 100) };
            vm.RootGraph.Nodes.Add(start);
            vm.RootGraph.Nodes.Add(prep);
            vm.RootGraph.Nodes.Add(end);
            vm.RootGraph.Connections.Add(new(start.Output[0], prep.Input[0]));
            vm.RootGraph.Connections.Add(new(prep.Output[0], end.Input[0]));
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1400, Height = 850, Content = view };
            try
            {
                window.Show();
                Layout();
                var check = view.GetVisualDescendants().OfType<CheckBox>()
                    .Single(c => c.Name == "ProfileOpenToggle" && c.DataContext == prep);
                Assert.Equal("При открытии профиля", check.Content);
                Assert.True(check.IsEffectivelyVisible);
                Assert.True(check.IsEffectivelyEnabled);
                Assert.True(check.Bounds.Width > 0 && check.Bounds.Height > 0);
                Assert.False(check.IsChecked);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<CheckBox>(),
                    c => c.Name == "ProfileOpenToggle" && c.IsVisible && (c.DataContext == start || c.DataContext == end));

                ClickToggle();
                Assert.True(prep.RunOnlyOnFirstRun);
                vm.UndoCommand.Execute(null);
                Assert.False(check.IsChecked);
                vm.RedoCommand.Execute(null);
                Assert.True(check.IsChecked);

                var options = view.GetVisualDescendants().OfType<Button>()
                    .Single(b => b.Name == "NodeOptionsTrigger" && b.DataContext == prep);
                var flyout = Assert.IsType<Flyout>(options.Flyout);
                flyout.ShowAt(options);
                Layout();
                var panel = Assert.IsType<StackPanel>(flyout.Content);
                var input = panel.GetLogicalDescendants().OfType<TextBox>()
                    .Single(t => t.Name == "CustomNodeNameInput");
                Assert.True(input.IsEffectivelyVisible);
                input.Text = "Ожидание питания";
                Layout();
                Assert.Equal("Ожидание питания", prep.CustomName);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.Text == "Ожидание питания" && t.DataContext == prep);
                vm.UndoCommand.Execute(null);
                Assert.Empty(input.Text!);
                vm.RedoCommand.Execute(null);
                Assert.Equal("Ожидание питания", input.Text);
                input.Text = "";
                Layout();
                Assert.Equal("Задержка", prep.DisplayTitle);
                flyout.Hide();

                vm.IsTestRunning = true;
                Layout();
                Assert.False(check.IsEffectivelyEnabled);
                ClickToggle();
                Assert.True(prep.RunOnlyOnFirstRun);

                vm.IsTestRunning = false;
                Layout();
                Assert.True(check.IsEffectivelyEnabled);
                ClickToggle();
                Assert.False(prep.RunOnlyOnFirstRun);

                void ClickToggle()
                {
                    var point = check.TranslatePoint(new Point(check.Bounds.Width / 2, check.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(point, MouseButton.Left);
                    Layout();
                }
            }
            finally
            {
                window.Close();
            }

            void Layout()
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
            }
        }, CancellationToken.None);
    }
}
