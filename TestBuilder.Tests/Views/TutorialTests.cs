using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nodify;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class TutorialTests
{
    static string Output => Environment.GetEnvironmentVariable("TFORTIS_TUTORIAL_ARTIFACTS") ?? Path.Combine(Path.GetTempPath(), "tfortis-tutorial-ui");

    [Theory]
    [InlineData(false, 1320)]
    [InlineData(true, 1100)]
    public async Task UserCanCompleteBothLessons_WithRealClicksAndConnections(bool dark, int width)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var previousCompleted = AppSettings.Instance.TutorialCompletedVersion;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var window = new TutorialWindow { Width = width, Height = dark ? 780 : 850 };
            try
            {
                window.Show(); Layout(window);
                await At(window, TutorialStep.Model);
                Capture(window, dark, "model");
                ClickTarget(window); await At(window, TutorialStep.Profile);
                ClickTarget(window); await At(window, TutorialStep.Start);
                var original = File.ReadAllText(window.Model.SelectedProfile!.FilePath);
                var folder = Path.GetDirectoryName(window.Model.SelectedProfile.FilePath)!;
                ClickTarget(window); await At(window, TutorialStep.Confirm);
                Capture(window, dark, "operator");
                ClickTarget(window); await At(window, TutorialStep.Result);
                Assert.True(window.Model.Station.HasDuration);
                Capture(window, dark, "result");
                Click(window, Named(window, "TutorialEditor")); await At(window, TutorialStep.Rename);
                var name = window.Target!.GetVisualDescendants().OfType<TextBox>().First();
                name.Focus(); name.Text = "Мой учебный профиль"; window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); Layout(window);
                await At(window, TutorialStep.Parameter);
                Capture(window, dark, "parameter");
                Assert.IsType<TextBox>(window.Target).Text = "500";
                await At(window, TutorialStep.SaveCopy);
                ClickTarget(window); await At(window, TutorialStep.New);
                Assert.Equal(2, Directory.GetFiles(folder, "*.json").Length);
                Assert.Equal(original, File.ReadAllText(Path.Combine(folder, "intro.json")));
                ClickTarget(window); await At(window, TutorialStep.Device);
                var device = window.Target!.GetVisualDescendants().OfType<TextBox>().First();
                device.Focus(); device.Text = "Учебная плата"; window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                await At(window, TutorialStep.HideProfiles); ClickTarget(window);
                await At(window, TutorialStep.AddStart);
                Drop(window, "Старт"); await At(window, TutorialStep.AddAction);
                Drop(window, "Действие оператора"); await At(window, TutorialStep.AddDelay);
                Drop(window, "Задержка"); await At(window, TutorialStep.AddEnd);
                Drop(window, "Конец"); await At(window, TutorialStep.ActionMessage);
                var message = Assert.IsType<TextBox>(window.Target);
                message.Focus(); message.Text = "Проверьте индикатор";
                await Task.Delay(150); Layout(window);
                Assert.Equal(TutorialStep.ActionMessage, window.Step); // Do not advance while the operator is still typing.
                Capture(window, dark, "message");
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                await At(window, TutorialStep.Help); ClickTarget(window);
                await At(window, TutorialStep.CloseHelp);
                var help = Assert.Single(window.OwnedWindows.OfType<NodeHelpWindow>());
                Layout(help); Capture(help, dark, "help");
                Click(help, help.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "✕ Закрыть")));
                await At(window, TutorialStep.ConnectStart);
                await Task.Delay(150); Layout(window);
                Capture(window, dark, "connections");
                AssertNoNodeOverlap(window);
                var action = window.Model.Nodes.OfType<OperatorActionNodeViewModel>().Single();
                var delay = window.Model.Nodes.OfType<DelayNodeViewModel>().Single();
                var end = window.Model.Nodes.OfType<EndNodeViewModel>().Single();
                Connect(window, window.Model.Nodes.OfType<StartNodeViewModel>().Single().Output[0], action.In);
                await At(window, TutorialStep.ConnectSuccess);
                Connect(window, action.TrueOut, delay.Input[0]);
                await At(window, TutorialStep.ConnectEnd);
                if (dark) delay.Milliseconds = 0;
                Connect(window, delay.Output[0], end.Input[0]);
                await At(window, TutorialStep.ConnectFailure);
                Capture(window, dark, "failure-connection");
                Connect(window, action.FalseOut, end.Input[0]);
                await At(window, TutorialStep.Name);
                var newName = window.Target!.GetVisualDescendants().OfType<TextBox>().First();
                newName.Focus(); newName.Text = "Пробная проверка"; window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                await At(window, TutorialStep.Save);
                ClickTarget(window); await At(window, TutorialStep.Run);
                ClickTarget(window); await At(window, TutorialStep.ConfirmGraph);
                var prompt = Assert.Single(window.OwnedWindows.OfType<OperatorActionDialog>());
                Layout(prompt); Capture(prompt, dark, "own-prompt");
                Assert.Contains(prompt.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Проверьте индикатор");
                Click(prompt, prompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, dark ? "Отмена" : "Продолжить")));
                await At(window, TutorialStep.Done);
                Assert.Equal("Учебная плата · Пробная проверка", window.Model.CurrentProfileName);
                Assert.Equal(3, Directory.GetFiles(folder, "*.json").Length);
                Assert.Equal(TutorialSession.Version, AppSettings.Instance.TutorialCompletedVersion);
                Capture(window, dark, "done");
                window.Close(); Assert.False(Directory.Exists(folder));
            }
            finally
            {
                window.Close(); Application.Current.RequestedThemeVariant = previousTheme;
                AppSettings.Instance.TutorialCompletedVersion = previousCompleted; AppSettings.Instance.Save();
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TrainingCannotTouchHardwareOutsideProfilesOrProductionLog()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var training = new TutorialSession(); var vm = training.Model;
            var external = Path.Combine(Path.GetTempPath(), "training-external-" + Guid.NewGuid() + ".json");
            var settings = AppSettings.Instance;
            var oldFolder = settings.GraphsFolder; var oldLog = settings.LogFolder; var oldEnable = settings.EnableFileLogging;
            var logFolder = Path.Combine(Path.GetTempPath(), "training-log-" + Guid.NewGuid());
            try
            {
                Assert.Single(vm.Profiles); Assert.All(vm.Profiles, p => Assert.StartsWith(training.DirectoryPath, p.FilePath));
                Assert.False(vm.ToggleConnectionCommand.CanExecute(null)); Assert.False(vm.ConnectStationCommand.CanExecute(null));
                await vm.ToggleConnectionCommand.ExecuteAsync(null); Assert.False(vm.IsConnected);
                vm.SelectedProfile = vm.Profiles.Single(); vm.IsOperatorView = false;
                await vm.SaveGraphToPathAsync(external); Assert.False(File.Exists(external));
                vm.Nodes.Add(new ModbusWriteNodeViewModel());
                await vm.RunGraphCommand.ExecuteAsync(null); Assert.Contains("В обучении", vm.StatusMessage); Assert.False(vm.IsTestRunning);
                vm.NewProfileCommand.Execute(null);
                var start = new StartNodeViewModel(); var end = new EndNodeViewModel();
                vm.Nodes.Add(start); vm.Nodes.Add(end); vm.Connections.Add(new(start.Output[0], end.Input[0]));
                settings.LogFolder = logFolder; settings.EnableFileLogging = true;
                var realLog = LoggingService.Instance.StartFileLogForRun("Контрольный журнал");
                var before = File.ReadAllText(realLog!);
                await vm.RunGraphCommand.ExecuteAsync(null);
                Assert.True(vm.Station.IsSuccessful);
                Assert.Equal(realLog, LoggingService.Instance.CurrentLogFilePath);
                Assert.Equal(before, File.ReadAllText(realLog!));
                Assert.Equal(oldFolder, settings.GraphsFolder);
            }
            finally
            {
                settings.LogFolder = oldLog; settings.EnableFileLogging = oldEnable;
                LoggingService.Instance.StopFileLogForRun(); if (Directory.Exists(logFolder)) Directory.Delete(logFolder, true);
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FirstLaunchOfferIsPersisted_AndSettingsCanReplay()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var settings = AppSettings.Instance; var oldOffered = settings.TutorialOfferedVersion;
            var owner = new MainWindow();
            try
            {
                settings.TutorialOfferedVersion = 0; owner.Show(); Layout(owner);
                var offer = TutorialLauncher.OfferOnFirstLaunchAsync(owner);
                Layout(owner); var welcome = owner.OwnedWindows.Single();
                Capture(welcome, false, "welcome");
                Click(welcome, Named(welcome, "SkipTutorial")); await offer;
                Assert.False(TutorialSession.ShouldOffer(settings));
                var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
                Assert.False(TutorialSession.ShouldOffer(restored));
                await TutorialLauncher.OfferOnFirstLaunchAsync(owner); Assert.Empty(owner.OwnedWindows);
                var vm = (TestBuilder.ViewModels.MainWindowViewModel)owner.DataContext!;
                vm.OpenSettingsCommand.Execute(null); Layout(owner);
                var replay = owner.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Интерактивное обучение"));
                Click(owner, replay); Layout(owner);
                var tutorial = Assert.Single(owner.OwnedWindows.OfType<TutorialWindow>());
                await At(tutorial, TutorialStep.Model);
                tutorial.Close();
            }
            finally { owner.Close(); settings.TutorialOfferedVersion = oldOffered; settings.Save(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ClosingDuringOperatorWaitCancelsRun_AndRestartRecreatesOnlyTrainingFiles()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var window = new TutorialWindow(); window.Show();
            try
            {
                await At(window, TutorialStep.Model); ClickTarget(window); await At(window, TutorialStep.Profile);
                ClickTarget(window); await At(window, TutorialStep.Start);
                var firstFolder = Path.GetDirectoryName(window.Model.SelectedProfile!.FilePath)!;
                Click(window, Named(window, "TutorialRestart")); await At(window, TutorialStep.Model);
                Assert.False(Directory.Exists(firstFolder));
                ClickTarget(window); await At(window, TutorialStep.Profile); ClickTarget(window); await At(window, TutorialStep.Start);
                var folder = Path.GetDirectoryName(window.Model.SelectedProfile!.FilePath)!;
                ClickTarget(window); await At(window, TutorialStep.Confirm);
                var run = window.Model.RunGraphCommand.ExecutionTask!;
                window.Close(); await run; await Task.Delay(150); Layout(window);
                Assert.False(window.IsVisible); Assert.False(Directory.Exists(folder));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    static void Drop(TutorialWindow window, string type)
    {
        var editor = window.GetVisualDescendants().OfType<NodifyEditor>().Single();
        var data = new DataObject(); data.Set("NodeType", type);
        editor.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, editor, new Point(80, 80), KeyModifiers.None));
        Layout(window);
    }

    static void Connect(TutorialWindow window, ConnectorViewModel from, ConnectorViewModel to)
    {
        var editor = window.GetVisualDescendants().OfType<NodifyEditor>().Single();
        var output = editor.GetVisualDescendants().OfType<Connector>().Single(c => ReferenceEquals(c.DataContext, from));
        var input = editor.GetVisualDescendants().OfType<Connector>().Single(c => ReferenceEquals(c.DataContext, to));
        var outputThumb = output.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Connector");
        var inputThumb = input.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Connector");
        var a = outputThumb.TranslatePoint(new Point(outputThumb.Bounds.Width / 2, outputThumb.Bounds.Height / 2), window)!.Value;
        var b = inputThumb.TranslatePoint(new Point(inputThumb.Bounds.Width / 2, inputThumb.Bounds.Height / 2), window)!.Value;
        // Existing wires must not intercept a connector, including the return path into Delay.
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(a));
        Assert.Contains(output, hit.GetVisualAncestors().OfType<Connector>());
        window.MouseMove(a); window.MouseDown(a, MouseButton.Left);
        window.MouseMove((a + b) / 2); window.MouseMove(b); window.MouseUp(b, MouseButton.Left); Layout(window);
    }

    static void AssertNoNodeOverlap(TutorialWindow w)
    {
        var boxes = w.GetVisualDescendants().OfType<ItemContainer>().Where(c => c.DataContext is NodeViewModel)
            .Select(c => new Rect(((NodeViewModel)c.DataContext!).Location, c.Bounds.Size)).ToArray();
        for (var i = 0; i < boxes.Length; i++) for (var j = i + 1; j < boxes.Length; j++) Assert.False(boxes[i].Intersects(boxes[j]));
    }
    static async Task At(TutorialWindow w, TutorialStep step)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (w.Step != step && DateTime.UtcNow < until) { await Task.Delay(30); Layout(w); }
        Layout(w); w.UpdateHighlight();
        if (w.Step != step) Capture(w, true, "failed-" + step);
        Assert.True(w.Step == step, $"Expected {step}, actual {w.Step}; {w.Model.StatusMessage}");
        if (w.Target != null) Assert.True(w.HighlightBounds.Width > 0 && w.HighlightBounds.Height > 0);
    }
    static void ClickTarget(TutorialWindow w)
    {
        Assert.NotNull(w.Target);
        if (w.Step is TutorialStep.SaveCopy or TutorialStep.Save or TutorialStep.Run)
        {
            var graph = w.GetVisualDescendants().OfType<TestBuilder.Views.TestViews.GraphEditorView>().Single();
            graph.OpenGraphMenu(); Layout(w);
            var menu = graph.NodeRunMenu!;
            var item = w.Step == TutorialStep.Run
                ? menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Выполнение"))
                    .Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Запустить"))
                : menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Сохранить профиль"));
            Assert.True(item.Command!.CanExecute(null));
            item.Command.Execute(null);
            menu.Close(); Layout(w);
            return;
        }
        Click(w, w.Target!);
    }
    static Button Named(Window w, string name) => w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);
    static void Click(Window w, Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left); Layout(w);
    }
    static void Layout(Window w) { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    static void Capture(Window w, bool dark, string name)
    { Directory.CreateDirectory(Output); Layout(w); using var frame = w.CaptureRenderedFrame(); Assert.NotNull(frame); frame.Save(Path.Combine(Output, $"{(dark ? "dark" : "light")}-{name}.png")); }
}
