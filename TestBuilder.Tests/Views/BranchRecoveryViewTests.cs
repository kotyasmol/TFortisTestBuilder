using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class BranchRecoveryViewTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task OrdinaryConnectionCanConfirmItsFailedNodeThroughTheContextMenu(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.IsOperatorView = false;
            var (first, _, link) = BranchRecoveryGraphTests.Fill(vm.RootGraph); link.RecoversFailureOf = null;
            var theme = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1400, Height = 1000, Content = view };
            try
            {
                window.Show(); Layout(window);
                var before = GraphSerializer.Serialize(vm, "Ветвление");
                view.OpenGraphMenu(connection: link); Layout(window);
                var submenu = view.NodeRunMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Устраняет ошибку"));
                var choice = submenu.Items.OfType<MenuItem>().Single(m => m.Header!.ToString()!.Contains("Исходная проверка"));
                choice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                view.NodeRunMenu.Close(); Layout(window);
                Assert.Same(first, link.RecoversFailureOf); Assert.Contains(first.DisplayTitle, link.RecoveryDescription);
                var saved = GraphSerializer.Serialize(vm, "Ветвление");
                Assert.NotEqual(before, saved); Layout(window); Assert.Equal(saved, GraphSerializer.Serialize(vm, "Ветвление"));
                vm.UndoCommand.Execute(null); Assert.Null(link.RecoversFailureOf);
                vm.RedoCommand.Execute(null); Assert.Same(first, link.RecoversFailureOf);
                using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-branch-recovery-{dark}.png"));
                GraphSerializer.Deserialize(saved, vm);
                Assert.Single(vm.Connections.Where(c => c.RecoversFailureOf != null));
            }
            finally { view.NodeRunMenu?.Close(); window.Close(); Application.Current.RequestedThemeVariant = theme; }
        }, CancellationToken.None);
    }

    private static void Layout(Window window)
    { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); }
}
