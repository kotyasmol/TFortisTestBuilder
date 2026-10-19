using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Views;
namespace TestBuilder.Tests.Views;
[Collection("Graph editor UI")]
public class BoardViewsTests
{
    [Fact] public async Task BoardHighlightDemo_RunsWithoutHardware_AndShowsAllResultStates()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        // The non-generic overload does not await a Task-returning callback.
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles/board_highlight_demo.json"));
            GraphSerializer.Deserialize(File.ReadAllText(path), vm);
            Assert.True(vm.IsBoardProfile);
            Assert.Equal(6, vm.RootGraph.Board!.Regions.Count);
            Assert.Equal(8, vm.RootGraph.Connections.Count);
            foreach (var delay in vm.RootGraph.Nodes.OfType<DelayNodeViewModel>()) delay.Milliseconds = 1;
            var checking = new HashSet<string>();
            vm.BoardChanged += () =>
            {
                foreach (var region in vm.RootGraph.Board.Regions)
                    if (vm.GetBoardRegionState(region.Id) == "Проверяется") checking.Add(region.Id);
            };
            var window = new BoardWindow(vm, false);
            try
            {
                window.Show();
                Assert.True(vm.RunGraphCommand.CanExecute(null));
                await vm.RunGraphCommand.ExecuteAsync(null);
                foreach (var id in new[] { "R1", "R2", "C2", "D1" })
                {
                    Assert.Contains(id, checking);
                    Assert.Equal("Годен", vm.GetBoardRegionState(id));
                }
                Assert.Contains("C1", checking);
                Assert.Equal("Ошибка", vm.GetBoardRegionState("C1"));
                Assert.Equal("Не проверен", vm.GetBoardRegionState("U1"));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var output = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS") ?? Path.Combine(Path.GetTempPath(), "tfortis-board-ui");
                Directory.CreateDirectory(output);
                frame.Save(Path.Combine(output, "BoardHighlightDemo.png"));
            }
            finally { window.Content = null; window.Close(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact] public async Task BoardRegionCombinesAllChecks_AndDoesNotMarkSkippedOrInterruptedAsPassed()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var first = new CheckRegisterRangeNodeViewModel { BoardElements = "R1" };
            var second = new CheckRegisterRangeNodeViewModel { BoardElements = "R1" };
            var disabled = new SubtestNodeViewModel { BoardElements = "R2", IsEnabled = false };
            vm.RootGraph.Nodes.Add(first); vm.RootGraph.Nodes.Add(second); vm.RootGraph.Nodes.Add(disabled);
            var context = new TestContext(new RegisterState());
            var a = new TestNode(null, first); var b = new TestNode(null, second);
            Assert.Equal("Не привязан", vm.GetBoardRegionState("R3"));
            Assert.Equal("Не проверен", vm.GetBoardRegionState("R1"));
            await vm.NodeStartedAsync(a, context, default);
            Assert.Equal("Проверяется", vm.GetBoardRegionState("R1"));
            await vm.NodeFinishedAsync(a, context, default);
            Assert.Equal("Не проверен", vm.GetBoardRegionState("R1"));
            await vm.NodeCompletedAsync(a, StepResult.True, context, default);
            Assert.Equal("Не проверен", vm.GetBoardRegionState("R1"));
            await vm.NodeCompletedAsync(b, StepResult.False, context, default);
            Assert.Equal("Ошибка", vm.GetBoardRegionState("R1"));
            await vm.NodeCompletedAsync(a, StepResult.True, context, default);
            Assert.Equal("Ошибка", vm.GetBoardRegionState("R1"));
            await vm.NodeCompletedAsync(b, StepResult.True, context, default);
            Assert.Equal("Годен", vm.GetBoardRegionState("R1"));
            await vm.NodeCompletedAsync(new TestNode(null, disabled), StepResult.True, context, default);
            Assert.Equal("Не проверен", vm.GetBoardRegionState("R2"));
            vm.NotifyProfileMetadataChanged();
            Assert.Equal("Не проверен", vm.GetBoardRegionState("R1"));
            return true;
        }, CancellationToken.None);
    }

    [Fact] public async Task SettingsOpensEmbeddedGuide_AndNewEditorsRender()
    {
        using var session=HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(()=>
        {
            using var bus=new ModbusService();using var vm=new TestViewModel(bus,new SlaveManager(bus)){IsOperatorView=false};
            var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../profiles/RTL_POE_board.json"));GraphSerializer.Deserialize(File.ReadAllText(path),vm);
            var guide=UserGuideWindow.ReadGuide();Assert.Contains("Запустить готовую проверку",guide);Assert.Contains("Modbus",guide);Assert.True(TestBuilder.Services.Help.UserGuide.All.Count>=50);
            var windows=new Window[]{new HardwareEditorWindow(vm),new BoardWindow(vm,true),new BoardWindow(vm,false),new UserGuideWindow(),new MainWindow { Width=1100, Height=680 }};
            foreach(var window in windows)
            {
                try{window.Show();Dispatcher.UIThread.RunJobs();window.UpdateLayout();window.InvalidateVisual();AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);Dispatcher.UIThread.RunJobs();Assert.True(window.Bounds.Width>0);
                    var bitmap=window.CaptureRenderedFrame();if(bitmap!=null){var dir=Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS")??Path.Combine(Path.GetTempPath(),"tfortis-board-ui");Directory.CreateDirectory(dir);bitmap.Save(Path.Combine(dir,window.GetType().Name+(window.Title?.Contains("Редактор")==true?"-edit":"")+".png"));bitmap.Dispose();}}
                finally{window.Content = null; window.Close();AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);Dispatcher.UIThread.RunJobs();}
            }
            var settings=new SettingsView{DataContext=new SettingsViewModel()};var host=new Window{Content=settings};
            try{host.Show();Dispatcher.UIThread.RunJobs();var open=Assert.Single(settings.GetVisualDescendants().OfType<Button>().Where(b=>Equals(b.Content,"Полная инструкция")));open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();var opened=Assert.Single(host.OwnedWindows.OfType<UserGuideWindow>());Assert.True(opened.IsVisible);opened.Close();}
            finally{host.Content = null; host.Close();AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);Dispatcher.UIThread.RunJobs();}
        },CancellationToken.None);
    }
}
