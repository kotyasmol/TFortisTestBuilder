using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class FirstRunSessionTests
{
    [Fact]
    public Task FirstRun_WritesPreparationAndCheckToOneFile_AndNextRunUsesAnotherFile() => InSession(async () =>
    {
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var settings = AppSettings.Instance;
        var oldEnabled = settings.EnableFileLogging;
        var oldFolder = settings.LogFolder;
        var folder = Path.Combine(Path.GetTempPath(), "first-run-log-" + Guid.NewGuid().ToString("N"));
        settings.EnableFileLogging = true;
        settings.LogFolder = folder;
        try
        {
            Chain(vm.RootGraph, PreparationDelay(), new DelayNodeViewModel { Milliseconds = 0 });
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(vm.Station.IsSuccessful);
            var firstPath = Assert.Single(Directory.GetFiles(folder));
            var firstLog = File.ReadAllText(firstPath);
            Assert.Contains("[Testing] Подготовка стенда:", firstLog);
            Assert.Contains("[Testing] Запуск теста:", firstLog);
            Assert.Null(LoggingService.Instance.CurrentLogFilePath);

            await vm.RunGraphCommand.ExecuteAsync(null);
            var secondPath = Assert.Single(Directory.GetFiles(folder).Where(path => path != firstPath));
            var secondLog = File.ReadAllText(secondPath);
            Assert.Contains("[Testing] Запуск теста:", secondLog);
            Assert.DoesNotContain("[Testing] Подготовка стенда:", secondLog);
            Assert.Equal(firstLog, File.ReadAllText(firstPath));
        }
        finally
        {
            LoggingService.Instance.StopFileLogForRun();
            settings.EnableFileLogging = oldEnabled;
            settings.LogFolder = oldFolder;
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    });

    [Fact]
    public Task OpeningProfile_LeavesGraphEditable_FirstRunPrepares_AndReopenDefersAgain() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        Chain(vm.RootGraph, PreparationDelay(), new DelayNodeViewModel { Milliseconds = 1 });
        var path = files.PathFor("profile");
        await vm.SaveGraphToPathAsync(path);
        var before = PreparationStarts(vm);

        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        var prep = vm.Nodes.OfType<DelayNodeViewModel>().First();
        var test = vm.Nodes.OfType<DelayNodeViewModel>().Last();
        Assert.True(vm.CanEditGraph);
        Assert.True(vm.CanStartRun);
        Assert.False(prep.HasExecutionSucceeded);
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);
        Assert.Equal(before, PreparationStarts(vm));

        for (var i = 0; i < 2; i++)
        {
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(vm.Station.IsSuccessful);
            Assert.False(prep.HasExecutionSucceeded);
            Assert.True(test.HasExecutionSucceeded);
            Assert.Contains(vm.Station.Stages, stage => stage.Source == prep && stage.State == StationStageState.Skipped);
            Assert.Equal(i == 0 ? 1 : 0, PreparationStarts(vm));
        }

        // Saving display-only changes does not reopen the document.
        prep.Location = new Point(900, 700); prep.NodeColor = "red";
        await vm.SaveGraphToPathAsync(path);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(0, PreparationStarts(vm));

        await vm.SelectProfileAsync(vm.SelectedProfile, reload: true);
        prep = vm.Nodes.OfType<DelayNodeViewModel>().First();
        test = vm.Nodes.OfType<DelayNodeViewModel>().Last();
        Assert.False(prep.HasExecutionSucceeded);
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);
        Assert.Equal(0, PreparationStarts(vm));
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(1, PreparationStarts(vm));
        Assert.True(test.HasExecutionSucceeded);
    });

    [Fact]
    public Task SavedPreparationEdit_PreparesAtNextStartAndUsesNewVariablesWithoutReopening() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var setup = new SubtestNodeViewModel { RunOnlyOnFirstRun = true };
        Chain(setup.BodyGraph, FixedValue(42));
        Chain(vm.RootGraph, setup,
            new CheckVariableEqualityNodeViewModel { VariableName = "SerialNumber", ExpectedValue = "42" });
        var path = files.PathFor("edited-preparation");
        await vm.SaveGraphToPathAsync(path);
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(vm.Station.IsSuccessful);

        setup = vm.Nodes.OfType<SubtestNodeViewModel>().Single();
        var value = setup.BodyGraph.Nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Single();
        var check = vm.Nodes.OfType<CheckVariableEqualityNodeViewModel>().Single();
        value.FixedSerialNumber = 43;
        check.ExpectedValue = "43";
        var starts = PreparationStarts(vm);
        await vm.SaveGraphCommand.ExecuteAsync(null);

        Assert.True(vm.CanStartRun);
        Assert.True(vm.CanRetryProfilePreparation);
        Assert.Contains("при следующем запуске", vm.StatusMessage);
        Assert.Equal(starts, PreparationStarts(vm)); // Saving must not operate the stand.
        Assert.Same(setup, vm.Nodes.OfType<SubtestNodeViewModel>().Single());
        Assert.True(vm.CanUndo);

        for (var i = 0; i < 2; i++)
        {
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(check.HasExecutionSucceeded);
            Assert.True(vm.Station.IsSuccessful);
            Assert.Equal(i == 0 ? 1 : 0, PreparationStarts(vm));
        }
    });

    [Fact]
    public Task UnsavedPreparationEdit_CannotRunWithPreviousVariables() => InSession(async () =>
    {
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var setup = FixedValue(42); setup.RunOnlyOnFirstRun = true;
        var check = new CheckVariableEqualityNodeViewModel { VariableName = "SerialNumber", ExpectedValue = "42" };
        Chain(vm.RootGraph, setup, check);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(vm.Station.IsSuccessful);
        setup.FixedSerialNumber = 43;

        await vm.RunGraphCommand.ExecuteAsync(null);

        Assert.Equal(1, PreparationStarts(vm));
        Assert.True(check.HasExecutionError);
        Assert.True(vm.Station.HasResult);
        Assert.False(vm.Station.IsSuccessful);
    });

    [Fact]
    public Task RemovingPreparation_DropsItsVariablesOnTheNextRun() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var setup = FixedValue(42); setup.RunOnlyOnFirstRun = true;
        var check = new CheckVariableEqualityNodeViewModel { VariableName = "SerialNumber", ExpectedValue = "42" };
        Chain(vm.RootGraph, setup, check);
        var path = files.PathFor("removed-preparation");
        await vm.SaveGraphToPathAsync(path);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(vm.Station.IsSuccessful);

        Chain(vm.RootGraph, check);
        await vm.SaveGraphCommand.ExecuteAsync(null);
        Assert.True(vm.CanStartRun);
        await vm.RunGraphCommand.ExecuteAsync(null);

        Assert.True(check.HasExecutionError);
        Assert.False(vm.Station.IsSuccessful);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SavedNodeEdit_ChangesTheNextRunInTheSameDocument(bool nested) => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var value = FixedValue(42);
        var check = new CheckVariableEqualityNodeViewModel { VariableName = "SerialNumber", ExpectedValue = "42" };
        if (nested)
        {
            var body = new SubtestNodeViewModel();
            Chain(body.BodyGraph, value);
            Chain(vm.RootGraph, body, check);
        }
        else Chain(vm.RootGraph, value, check);
        await vm.SaveGraphToPathAsync(files.PathFor("edited-node"));
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(vm.Station.IsSuccessful);

        value.FixedSerialNumber = 43;
        await vm.SaveGraphCommand.ExecuteAsync(null);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(check.HasExecutionError);
        Assert.False(vm.Station.IsSuccessful);

        check.ExpectedValue = "43";
        await vm.SaveGraphCommand.ExecuteAsync(null);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(check.HasExecutionSucceeded);
        Assert.True(vm.Station.IsSuccessful);
    });

    [Fact]
    public Task ReloadProfile_CancelKeepsUnsavedEdits_AndSavedEditsNeedNoConfirmation() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var value = FixedValue(42);
        Chain(vm.RootGraph, value);
        var path = files.PathFor("reload-edits");
        await vm.SaveGraphToPathAsync(path);
        value.FixedSerialNumber = 43;
        var confirmations = 0;
        vm.ConfirmReloadProfileAsync = () => { confirmations++; return Task.FromResult(false); };

        await vm.ReloadProfileCommand.ExecuteAsync(null);
        Assert.Equal(1, confirmations);
        Assert.Same(value, vm.Nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Single());
        Assert.Equal(43, value.FixedSerialNumber);

        await vm.SaveGraphCommand.ExecuteAsync(null);
        Assert.True(vm.CanUndo); // An undo history is not the same as unsaved edits.
        await vm.ReloadProfileCommand.ExecuteAsync(null);
        Assert.Equal(1, confirmations);
        Assert.NotSame(value, vm.Nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Single());
        Assert.Equal(43, vm.Nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Single().FixedSerialNumber);

        vm.Nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Single().FixedSerialNumber = 44;
        vm.ConfirmReloadProfileAsync = () => { confirmations++; return Task.FromResult(true); };
        await vm.ReloadProfileCommand.ExecuteAsync(null);
        Assert.Equal(2, confirmations);
        Assert.Equal(43, vm.Nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Single().FixedSerialNumber);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ReloadButton_ReadsChangedFileAndDefersPreparationUntilStart(bool operatorMode) => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var value = FixedValue(42); value.RunOnlyOnFirstRun = true;
        Chain(vm.RootGraph, value, new CheckVariableEqualityNodeViewModel { VariableName = "SerialNumber", ExpectedValue = "42" });
        var path = files.PathFor("reload-file");
        await vm.SaveGraphToPathAsync(path);
        await vm.SelectProfileAsync(vm.SelectedProfile, reload: true);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(vm.Station.IsSuccessful);
        var starts = PreparationStarts(vm);
        var saved = File.ReadAllText(path);
        File.WriteAllText(path, saved.Replace("\"fixedSerialNumber\": 42", "\"fixedSerialNumber\": 43")
            .Replace("\"expectedValue\": \"42\"", "\"expectedValue\": \"43\""));
        vm.ConfirmReloadProfileAsync = () => throw new InvalidOperationException("No local edits to discard");
        vm.IsOperatorView = operatorMode;
        Control screen = operatorMode ? new StationDashboardView { DataContext = vm } : new GraphEditorView { DataContext = vm };
        var window = new Window { Content = screen, Width = 1500, Height = 950 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var reload = screen.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, vm.ReloadProfileCommand));
            Assert.True(reload.IsEffectivelyEnabled);
            var point = reload.TranslatePoint(new Point(reload.Bounds.Width / 2, reload.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await vm.ReloadProfileCommand.ExecutionTask!;

            Assert.Equal(starts, PreparationStarts(vm));
            Assert.Equal(43, vm.Nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Single().FixedSerialNumber);
            Assert.False(vm.Nodes.OfType<CheckVariableEqualityNodeViewModel>().Single().HasExecutionSucceeded);
            Assert.False(vm.Station.HasResult);
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(vm.Station.IsSuccessful);
            Assert.Equal(1, PreparationStarts(vm));

            vm.IsTestRunning = true;
            Assert.False(vm.ReloadProfileCommand.CanExecute(null));
            await vm.ReloadProfileCommand.ExecuteAsync(null);
            Assert.Equal(1, PreparationStarts(vm));
            vm.IsTestRunning = false;
        }
        finally { window.Close(); (screen as IDisposable)?.Dispose(); }
    });

    [Fact]
    public Task SwitchingProfilesAndReturning_PreparesOnFirstStartOfEachProfile() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        Chain(vm.RootGraph, PreparationDelay(), new DelayNodeViewModel { Milliseconds = 0 });
        var firstPath = files.PathFor("first"); var secondPath = files.PathFor("second");
        await vm.SaveGraphToPathAsync(firstPath);
        await vm.SaveGraphToPathAsync(secondPath);
        foreach (var path in new[] { firstPath, secondPath, firstPath })
        {
            var before = PreparationStarts(vm);
            await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
            Assert.False(vm.Nodes.OfType<DelayNodeViewModel>().First().HasExecutionSucceeded);
            Assert.False(vm.Nodes.OfType<DelayNodeViewModel>().Last().HasExecutionSucceeded);
            Assert.Equal(before, PreparationStarts(vm));
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Equal(1, PreparationStarts(vm));
            Assert.True(vm.Station.IsSuccessful);
        }
    });

    [Fact]
    public Task InvalidParallelSerialNode_BlocksAutomaticPreparationAndCleanup() => InSession(async () =>
    {
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var prep = PreparationDelay();
        var fork = new DelayNodeViewModel { Milliseconds = 0 };
        Chain(vm.RootGraph, prep, fork);
        var end = vm.RootGraph.Nodes.OfType<EndNodeViewModel>().Single();
        var serial = new GetSerialNumberFromServerNodeViewModel();
        var sibling = new DelayNodeViewModel { Milliseconds = 0 };
        var cleanup = new SubtestNodeViewModel { Name = "Аварийное завершение", RunOnFailure = true };
        Chain(cleanup.BodyGraph, new DelayNodeViewModel { Milliseconds = 0 });
        foreach (var node in new NodeViewModel[] { serial, sibling, cleanup }) vm.RootGraph.Nodes.Add(node);
        vm.RootGraph.Connections.Remove(vm.RootGraph.Connections.Single(c => ReferenceEquals(c.Source.Parent, fork)));
        vm.RootGraph.Connections.Add(new(fork.Output[0], serial.Input[0]));
        vm.RootGraph.Connections.Add(new(fork.Output[0], sibling.Input[0]));
        vm.RootGraph.Connections.Add(new(serial.TrueOut, end.Input[0]));
        vm.RootGraph.Connections.Add(new(sibling.Output[0], end.Input[0]));

        await vm.RunGraphCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, PreparationStarts(vm));
        Assert.False(prep.HasExecutionSucceeded);
        Assert.False(vm.NeedsStandInspection);
        Assert.True(vm.CanStartRun);
        Assert.Equal("Проверка не запущена", vm.Station.Headline);
        Assert.Equal(TestErrorCode.ParallelGraph, vm.Station.ErrorCode);
        var error = Assert.Single(vm.TestingLogger.Entries.Where(entry => entry.IsFailureSummary));
        Assert.Equal(TestErrorCode.ParallelGraph, error.Code);
        Assert.Contains("Получение серийного номера", error.Message);
        Assert.DoesNotContain(vm.TestingLogger.Entries, entry => entry.Message.StartsWith("[ШАГ] Cleanup '"));
        Assert.DoesNotContain(vm.TestingLogger.Entries, entry => entry.Message.StartsWith("Запуск теста:"));
    });

    [Fact]
    public Task FailedDeviceRuns_DoNotRepeatSuccessfulPreparation() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        Chain(vm.RootGraph, PreparationDelay(), new CheckVariableEqualityNodeViewModel { VariableName = "missing-device-data" });
        var path = files.PathFor("failed-device");
        await vm.SaveGraphToPathAsync(path);
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        var prep = vm.Nodes.OfType<DelayNodeViewModel>().Single();
        for (var i = 0; i < 2; i++)
        {
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.False(vm.Station.IsSuccessful);
            Assert.False(prep.HasExecutionSucceeded);
            Assert.False(vm.NeedsStandInspection);
            Assert.Equal(i == 0 ? 1 : 0, PreparationStarts(vm));
        }
    });

    [Fact]
    public Task FailedPreparation_BlocksDeviceChecksAndNextStartTriesAgain() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var failedPrep = new CheckVariableEqualityNodeViewModel { VariableName = "missing", RunOnlyOnFirstRun = true };
        Chain(vm.RootGraph, failedPrep, new DelayNodeViewModel { Milliseconds = 0 });
        var path = files.PathFor("failed-preparation");
        await vm.SaveGraphToPathAsync(path);
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        failedPrep = vm.Nodes.OfType<CheckVariableEqualityNodeViewModel>().Single();
        var test = vm.Nodes.OfType<DelayNodeViewModel>().Single();
        Assert.False(failedPrep.HasExecutionError);
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);

        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(1, PreparationStarts(vm));
        Assert.True(failedPrep.HasExecutionError);
        Assert.True(vm.CanStartRun);
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);

        failedPrep.VariableName = "OperatorName";
        failedPrep.ExpectedValue = App.StartupUserName;
        failedPrep.ComparisonType = VariableComparisonType.String;
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(1, PreparationStarts(vm));
        Assert.True(test.HasExecutionSucceeded);
        Assert.True(vm.Station.IsSuccessful);
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public Task FailedArpDuringPreparation_RespectsSavedErrorPolicies(bool stopSetup, bool stopArp, bool retryOnly) => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        const string reason = "ARP access denied in test";
        var setup = new SubtestNodeViewModel { RunOnlyOnFirstRun = true, StopOnError = true };
        var arp = new ClearArpCacheNodeViewModel
        {
            RunArpdBat = false,
            Command = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
            Arguments = OperatingSystem.IsWindows()
                ? $"/d /c \"echo {reason} 1>&2 & exit /b 0\""
                : $"-c \"echo {reason} >&2\"",
            FailOnError = true
        };
        Chain(setup.BodyGraph, arp, new DelayNodeViewModel { Milliseconds = 0 });
        Chain(vm.RootGraph, setup, PreparationDelay(), new DelayNodeViewModel { Milliseconds = 0 });
        var path = files.PathFor("failed-arp-preparation");
        await vm.SaveGraphToPathAsync(path);
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        setup = Assert.Single(vm.Nodes.OfType<SubtestNodeViewModel>());
        setup.StopOnError = stopSetup;
        Assert.Single(setup.BodyGraph.Nodes.OfType<ClearArpCacheNodeViewModel>()).FailOnError = stopArp;
        await vm.SaveGraphToPathAsync(path);
        await vm.ReloadProfileCommand.ExecuteAsync(null);
        vm.IsOperatorView = retryOnly;

        if (retryOnly)
        {
            Assert.True(vm.CanRetryProfilePreparation);
            await vm.RetryProfilePreparationCommand.ExecuteAsync(null);
        }
        else await vm.RunGraphCommand.ExecuteAsync(null);

        setup = Assert.Single(vm.Nodes.OfType<SubtestNodeViewModel>());
        Assert.Equal(stopSetup, setup.StopOnError);
        Assert.Equal(stopArp, Assert.Single(setup.BodyGraph.Nodes.OfType<ClearArpCacheNodeViewModel>()).FailOnError);
        var blocked = stopSetup && stopArp;
        if (blocked || retryOnly)
        {
            Assert.True(setup.HasExecutionError);
            Assert.True(Assert.Single(setup.BodyGraph.Nodes.OfType<ClearArpCacheNodeViewModel>()).HasExecutionError);
            Assert.Equal(!stopArp, Assert.Single(setup.BodyGraph.Nodes.OfType<DelayNodeViewModel>()).HasExecutionSucceeded);
        }
        Assert.Equal(!blocked && retryOnly, vm.Nodes.OfType<DelayNodeViewModel>().First().HasExecutionSucceeded);
        Assert.Equal(!blocked && !retryOnly, vm.Nodes.OfType<DelayNodeViewModel>().Last().HasExecutionSucceeded);
        Assert.Equal(1, PreparationStarts(vm));
        Assert.Equal(blocked, vm.Station.NeedsAttention);
        Assert.Equal(!blocked && !retryOnly, vm.Station.HasResult);
        Assert.True(vm.CanStartRun);
        Assert.Contains(vm.TestingLogger.Entries, entry => entry.Message.Contains(reason, StringComparison.Ordinal));
        if (!blocked)
        {
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(vm.Station.IsSuccessful);
            Assert.Equal(0, PreparationStarts(vm));
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CancelledPreparation_BlocksDeviceChecksAndCanBeRetriedFromUi(bool operatorMode) => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var prep = PreparationDelay(); prep.Milliseconds = 1000;
        Chain(vm.RootGraph, prep, new DelayNodeViewModel { Milliseconds = 0 });
        var path = files.PathFor("cancelled-preparation");
        await vm.SaveGraphToPathAsync(path);
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        var running = vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(vm.IsTestRunning);
        vm.StopTestCommand.Execute(null);
        await running;
        vm.IsOperatorView = operatorMode;
        prep = vm.Nodes.OfType<DelayNodeViewModel>().First();
        var test = vm.Nodes.OfType<DelayNodeViewModel>().Last();
        Assert.False(prep.HasExecutionSucceeded);
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);
        prep.Milliseconds = 0;
        var starts = PreparationStarts(vm);
        Assert.True(vm.CanStartRun);
        Assert.True(vm.CanRetryProfilePreparation);
        vm.NeedsStandInspection = true;
        Assert.False(vm.RetryProfilePreparationCommand.CanExecute(null));
        var blockedStarts = PreparationStarts(vm);
        await vm.RetryProfilePreparationCommand.ExecuteAsync(null);
        Assert.Equal(blockedStarts, PreparationStarts(vm));
        vm.NeedsStandInspection = false;
        vm.IsNetworkSetupBusy = true;
        Assert.False(vm.RetryProfilePreparationCommand.CanExecute(null));
        vm.IsNetworkSetupBusy = false;
        Assert.False(vm.IsStopping);
        var source = vm.RootGraph;
        Control screen = operatorMode ? new StationDashboardView { DataContext = vm } : new GraphEditorView { DataContext = vm };
        var window = new Window { Content = screen, Width = 1400, Height = 950 };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var retry = screen.GetVisualDescendants().OfType<Button>()
                .Single(b => ReferenceEquals(b.Command, vm.RetryProfilePreparationCommand));
            Assert.True(retry.IsEffectivelyVisible); Assert.True(retry.IsEffectivelyEnabled);
            var point = retry.TranslatePoint(new Point(retry.Bounds.Width / 2, retry.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            Assert.NotNull(vm.RetryProfilePreparationCommand.ExecutionTask);
            await vm.RetryProfilePreparationCommand.ExecutionTask!;
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(retry.IsEffectivelyVisible); Assert.True(retry.IsEffectivelyEnabled);
            Assert.True(prep.HasExecutionSucceeded);

            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
            await vm.RetryProfilePreparationCommand.ExecutionTask!;
            Assert.Equal(starts + 2, PreparationStarts(vm));
            Assert.False(test.HasExecutionSucceeded);
        }
        finally { window.Close(); (screen as IDisposable)?.Dispose(); }
        Assert.Same(source, vm.RootGraph);
        Assert.Same(prep, vm.Nodes.OfType<DelayNodeViewModel>().First());
        Assert.Equal(0, prep.Milliseconds); // unsaved change survives retry
        Assert.True(vm.CanStartRun);
        Assert.False(test.HasExecutionSucceeded);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(0, PreparationStarts(vm));
        Assert.True(test.HasExecutionSucceeded);
        Assert.True(vm.Station.IsSuccessful);
    });

    [Fact]
    public Task ManualSingleNode_AlwaysExecutesAndDoesNotConsumeOrResetPreparation() => InSession(async () =>
    {
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var prep = PreparationDelay(); var test = new DelayNodeViewModel { Milliseconds = 0 };
        Chain(vm.RootGraph, prep, test);
        var starts = PreparationStarts(vm);
        vm.ConfigureNodeRun(prep, true);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(prep.HasExecutionSucceeded);
        Assert.False(test.HasExecutionSucceeded);
        Assert.Equal(starts, PreparationStarts(vm));

        await vm.RetryProfilePreparationCommand.ExecuteAsync(null);
        Assert.True(prep.HasExecutionSucceeded);
        Assert.Equal(starts + 1, PreparationStarts(vm));
        vm.ConfigureNodeRun(prep, true);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(prep.HasExecutionSucceeded);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.False(prep.HasExecutionSucceeded);
        Assert.True(test.HasExecutionSucceeded);
        Assert.Equal(0, PreparationStarts(vm));
    });

    [Fact]
    public Task PreparationVariables_SeedEveryFreshRun_WithoutPreparationReportRows() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var previousStandId = AppSettings.Instance.StandId;
        AppSettings.Instance.StandId = "test-preparation";
        try
        {
            var prep = new SubtestNodeViewModel { Name = "SetupMustNotEnterDeviceReport", RunOnlyOnFirstRun = true };
            Chain(prep.BodyGraph, FixedValue(42));
            var check = new CheckVariableEqualityNodeViewModel { VariableName = "SerialNumber", ExpectedValue = "42" };
            var overwrite = FixedValue(43);
            var report = new BuildTestReportNodeViewModel();
            var noSetupReport = new ParseResponseNodeViewModel
            {
                SourceVariableName = "TestReportText", ParseMode = "Regex",
                Selector = @"\A(?![\s\S]*SetupMustNotEnterDeviceReport)[\s\S]*\z", OutputVariableName = "VerifiedReport"
            };
            Chain(vm.RootGraph, prep, check, overwrite, report, noSetupReport);
            var path = files.PathFor("preparation-variables");
            await vm.SaveGraphToPathAsync(path);
            await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
            check = vm.Nodes.OfType<CheckVariableEqualityNodeViewModel>().Single();
            noSetupReport = vm.Nodes.OfType<ParseResponseNodeViewModel>().Single();
            for (var i = 0; i < 2; i++)
            {
                await vm.RunGraphCommand.ExecuteAsync(null);
                Assert.True(check.HasExecutionSucceeded); // The preceding run changed SerialNumber to 43.
                Assert.True(noSetupReport.HasExecutionSucceeded);
                Assert.Equal(i == 0 ? 1 : 0, PreparationStarts(vm));
            }
        }
        finally { AppSettings.Instance.StandId = previousStandId; }
    });

    [Fact]
    public Task InvalidPreparationAfterPassedDevice_ClearsPreviousDeviceResult() => InSession(async () =>
    {
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var ordinary = new DelayNodeViewModel { Milliseconds = 0 };
        var misplaced = new DelayNodeViewModel { Milliseconds = 0 };
        Chain(vm.RootGraph, ordinary, misplaced);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(vm.Station.IsSuccessful);
        misplaced.RunOnlyOnFirstRun = true;

        Assert.True(vm.CanStartRun);
        await vm.RunGraphCommand.ExecuteAsync(null);

        Assert.False(vm.Station.HasResult);
        Assert.False(vm.Station.IsSuccessful);
        Assert.True(vm.Station.NeedsAttention);
        Assert.Contains("подряд после", vm.StatusMessage);
    });

    [Fact]
    public Task DisconnectedModbusPreparation_RemainsPendingAndRunsNoOperations() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        Chain(vm.RootGraph, new ModbusWriteNodeViewModel { RunOnlyOnFirstRun = true }, new DelayNodeViewModel { Milliseconds = 0 });
        var path = files.PathFor("pending-modbus");
        await vm.SaveGraphToPathAsync(path);
        var starts = PreparationStarts(vm);
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.False(vm.IsTestRunning);
        Assert.False(vm.IsConnected);
        Assert.False(vm.Station.HasResult);
        Assert.Contains("Подключите стенд", vm.StatusMessage);
        Assert.All(vm.Nodes, node => Assert.False(node.HasExecutionSucceeded));
        Assert.Equal(starts, PreparationStarts(vm));
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task PortOpeningDoesNotReadDevices_ProfileChecksBeforePreparation_ThenWaitsForStart(bool matches) => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var port = new FakePort();
        var devices = new FakeDevices { Identity = matches ? (ushort)77 : (ushort)78 };
        using var vm = new TestViewModel(port, new SlaveManager(devices)) { IsOperatorView = false,
            AvailableStandPorts = () => new[] { "COM2", "COM1", "COM3" } };
        await vm.ToggleConnectionCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "COM1", "COM2" }, port.Opened);
        Assert.True(vm.IsConnected);
        Assert.Equal("COM2", vm.SelectedPort);
        Assert.Equal(0, devices.Reads);
        Assert.False(vm.IsMonitoringActive);
        Chain(vm.RootGraph, PreparationDelay(), new DelayNodeViewModel { Milliseconds = 0 });
        vm.RootGraph.Hardware = new() { BaudRate = 19200, Devices = new() { new() {
            Name = "Custom", SlaveId = 42, IdentificationValue = 77,
            Registers = new() { new() { Address = 123 } } } } };
        var path = files.PathFor("hardware");
        await vm.SaveGraphToPathAsync(path);
        var previousRequestedPort = AppSettings.Instance.StandPort;
        AppSettings.Instance.StandPort = "COM2";
        try
        {
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        Assert.True(vm.IsConnected);
        Assert.Equal(2, port.Opened.Count); // no release/reopen or fallback to COM3
        Assert.Equal(19200, port.ConfiguredBaudRate);
        Assert.True(devices.Reads > 0);
        var prep = vm.Nodes.OfType<DelayNodeViewModel>().First();
        var test = vm.Nodes.OfType<DelayNodeViewModel>().Last();
        Assert.False(prep.HasExecutionSucceeded);
        Assert.False(test.HasExecutionSucceeded);
        Assert.Equal(matches, vm.CanStartRun);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(matches, test.HasExecutionSucceeded);
        Assert.Equal(matches ? 1 : 0, PreparationStarts(vm));
        if (!matches)
        {
            Assert.True(vm.CanRetryProfilePreparation);
            await vm.RetryProfilePreparationCommand.ExecuteAsync(null);
            Assert.False(prep.HasExecutionSucceeded);
            Assert.False(vm.CanStartRun);
            devices.Identity = 77;
            await vm.RetryProfilePreparationCommand.ExecuteAsync(null);
            Assert.True(prep.HasExecutionSucceeded);
            Assert.False(test.HasExecutionSucceeded);
            Assert.True(vm.CanStartRun);
            Assert.Equal(2, port.Opened.Count);
        }
        await vm.ToggleConnectionCommand.ExecuteAsync(null);
        }
        finally { AppSettings.Instance.StandPort = previousRequestedPort; }
    });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task ProfileSearch_FindsMatchingStandWithoutStartingPreparation(bool connectFirst, bool manual) => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var port = new FakePort();
        var devices = new FakeDevices { IdentityForRead = () => port.Opened.LastOrDefault() == "COM3" ? (ushort)77 : (ushort)78 };
        var previousPort = AppSettings.Instance.StandPort;
        AppSettings.Instance.StandPort = manual ? "COM2" : "";
        using var vm = new TestViewModel(port, new SlaveManager(devices)) {
            IsOperatorView = false, AvailableStandPorts = () => new[] { "COM1", "COM2", "COM3" } };
        try
        {
            if (connectFirst) await vm.ToggleConnectionCommand.ExecuteAsync(null);
            Chain(vm.RootGraph, PreparationDelay(), new DelayNodeViewModel { Milliseconds = 0 });
            vm.RootGraph.Hardware = new() { Devices = new() { new() {
                Name = "Stand", SlaveId = 42, IdentificationValue = 77,
                Registers = new() { new() { Address = 123 } } } } };
            var path = files.PathFor("port-fallback");
            await vm.SaveGraphToPathAsync(path);
            await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
            if (!connectFirst) await vm.ToggleConnectionCommand.ExecuteAsync(null);
            Assert.Equal(manual ? "COM2" : "COM3", vm.SelectedPort);
            Assert.Equal(!manual, vm.IsMonitoringActive);
            Assert.False(vm.Nodes.OfType<DelayNodeViewModel>().First().HasExecutionSucceeded);
            Assert.False(vm.Nodes.OfType<DelayNodeViewModel>().Last().HasExecutionSucceeded);
            Assert.Equal(0, PreparationStarts(vm));
            Assert.Equal(!manual, vm.CanStartRun);
            if (manual) Assert.DoesNotContain("COM3", port.Opened);
            else Assert.Equal(connectFirst ? new[] { "COM1", "COM2", "COM1", "COM3" } : new[] { "COM1", "COM2", "COM3" }, port.Opened);
        }
        finally { if (vm.IsConnected) await vm.ToggleConnectionCommand.ExecuteAsync(null); AppSettings.Instance.StandPort = previousPort; }
    });

    [Fact]
    public Task CorporateLauncherArguments_AllowPreparationAndTestAtFirstStart() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        App.ConfigureStartupArguments(new[] { "test-session", "Тестовый", "оператор", "46" });
        try
        {
            using var vm = CreateViewModel(bus);
            Chain(vm.RootGraph, PreparationDelay(), new DelayNodeViewModel { Milliseconds = 0 });
            var path = files.PathFor("corporate-launcher");
            await vm.SaveGraphToPathAsync(path);
            await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
            Assert.False(vm.Nodes.OfType<DelayNodeViewModel>().First().HasExecutionSucceeded);
            Assert.False(vm.Nodes.OfType<DelayNodeViewModel>().Last().HasExecutionSucceeded);
            Assert.True(vm.CanStartRun);
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Equal(1, PreparationStarts(vm));
            Assert.True(vm.Station.IsSuccessful);
            App.ConfigureStartupArguments(new[] { "--unknown" });
            await vm.SelectProfileAsync(vm.SelectedProfile, reload: true);
            Assert.False(vm.RunGraphCommand.CanExecute(null));
            Assert.False(vm.RetryProfilePreparationCommand.CanExecute(null));
            Assert.Equal(App.StartupOptions.Error, vm.StationReadiness);
            Assert.Equal(App.StartupOptions.Error, vm.StatusMessage);
        }
        finally { App.ConfigureStartupArguments(Array.Empty<string>()); }
    });

    private sealed class FakePort : ModbusService
    {
        public List<string> Opened { get; } = new();
        public int ConfiguredBaudRate;
        public override Task<bool> ConnectAsync(string port, int baudRate, System.IO.Ports.Parity parity, int dataBits, System.IO.Ports.StopBits stopBits)
        {
            Opened.Add(port); IsConnected = port != "COM1";
            return Task.FromResult(IsConnected);
        }
        public override Task ConfigureLineAsync(int baudRate, System.IO.Ports.Parity parity, int dataBits, System.IO.Ports.StopBits stopBits)
        { ConfiguredBaudRate = baudRate; return Task.CompletedTask; }
    }
    private sealed class FakeDevices : IModbusService
    {
        public ushort Identity;
        public Func<ushort>? IdentityForRead;
        public int Reads;
        public Task<ushort[]> ReadRegistersAsync(byte slave, ushort address, ushort count, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(Enumerable.Repeat(address == 0 ? IdentityForRead?.Invoke() ?? Identity : (ushort)0, count).ToArray());
        }
        public Task<bool> WriteRegisterAsync(byte slave, ushort address, ushort value, bool verify = true, CancellationToken ct = default) => throw new InvalidOperationException("Unexpected write");
        public Task<bool> CheckPortAsync(CancellationToken ct = default) => throw new InvalidOperationException("Unexpected probe");
        public void SubscribeRegister(byte slave, ushort address, Action<ushort[]> callback) { }
    }

    private static TestViewModel CreateViewModel(ModbusService bus) => new(bus, new SlaveManager(bus)) { IsOperatorView = false };
    private static DelayNodeViewModel PreparationDelay() => new() { Milliseconds = 1, RunOnlyOnFirstRun = true };
    private static GetSerialNumberFromServerNodeViewModel FixedValue(int value) => new() { UseFixedSerialNumber = true, FixedSerialNumber = value };
    private static int PreparationStarts(TestViewModel vm) => vm.TestingLogger.Entries.Count(entry => entry.Message.StartsWith("Подготовка стенда:", StringComparison.Ordinal));

    private static async Task InSession(Func<Task> test)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () => { await test(); return true; }, CancellationToken.None);
    }

    private static void Chain(GraphWorkspaceViewModel graph, params NodeViewModel[] steps)
    {
        graph.Clear();
        NodeViewModel previous = new StartNodeViewModel(); graph.Nodes.Add(previous);
        foreach (var step in steps.Append<NodeViewModel>(new EndNodeViewModel()))
        {
            graph.Nodes.Add(step);
            graph.Connections.Add(new(previous.Output[0], step.Input[0]));
            previous = step;
        }
    }

    private sealed class ProfileFiles : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "profile-open-" + Guid.NewGuid().ToString("N"));
        private readonly string _previousFolder = AppSettings.Instance.GraphsFolder;
        public ProfileFiles()
        {
            Directory.CreateDirectory(_folder);
            AppSettings.Instance.GraphsFolder = _folder;
        }
        public string PathFor(string name) => Path.Combine(_folder, name + ".json");
        public void Dispose()
        {
            AppSettings.Instance.GraphsFolder = _previousFolder;
            Directory.Delete(_folder, recursive: true);
        }
    }
}
