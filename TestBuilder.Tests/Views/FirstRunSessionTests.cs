using Avalonia;
using Avalonia.Headless;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class FirstRunSessionTests
{
    [Fact]
    public Task OpeningProfile_PreparesBeforeTesting_RepeatRunsSkip_AndExplicitReopenPreparesAgain() => InSession(async () =>
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
        Assert.True(prep.HasExecutionSucceeded);
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);
        Assert.Equal(before + 1, PreparationStarts(vm));

        for (var i = 0; i < 2; i++)
        {
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.True(vm.Station.IsSuccessful);
            Assert.False(prep.HasExecutionSucceeded);
            Assert.True(test.HasExecutionSucceeded);
            Assert.Contains(vm.Station.Stages, stage => stage.Source == prep && stage.State == StationStageState.Skipped);
        }
        Assert.Equal(before + 1, PreparationStarts(vm));

        // Saving display-only changes does not reopen the document.
        prep.Location = new Point(900, 700); prep.NodeColor = "red";
        await vm.SaveGraphToPathAsync(path);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(before + 1, PreparationStarts(vm));

        await vm.ReloadProfileCommand.ExecuteAsync(null);
        prep = vm.Nodes.OfType<DelayNodeViewModel>().First();
        test = vm.Nodes.OfType<DelayNodeViewModel>().Last();
        Assert.True(prep.HasExecutionSucceeded);
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);
        Assert.Equal(before + 2, PreparationStarts(vm));
    });

    [Fact]
    public Task SwitchingProfilesAndReturning_RunsPreparationForEveryOpening() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        Chain(vm.RootGraph, PreparationDelay(), new DelayNodeViewModel { Milliseconds = 0 });
        var firstPath = files.PathFor("first"); var secondPath = files.PathFor("second");
        await vm.SaveGraphToPathAsync(firstPath);
        await vm.SaveGraphToPathAsync(secondPath);
        var before = PreparationStarts(vm);
        foreach (var path in new[] { firstPath, secondPath, firstPath })
        {
            await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
            Assert.True(vm.Nodes.OfType<DelayNodeViewModel>().First().HasExecutionSucceeded);
            Assert.False(vm.Nodes.OfType<DelayNodeViewModel>().Last().HasExecutionSucceeded);
        }
        Assert.Equal(before + 3, PreparationStarts(vm));
    });

    [Fact]
    public Task FailedDeviceRuns_DoNotRepeatSuccessfulOpeningPreparation() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        Chain(vm.RootGraph, PreparationDelay(), new CheckVariableEqualityNodeViewModel { VariableName = "missing-device-data" });
        var path = files.PathFor("failed-device");
        await vm.SaveGraphToPathAsync(path);
        await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        var starts = PreparationStarts(vm);
        var prep = vm.Nodes.OfType<DelayNodeViewModel>().Single();
        for (var i = 0; i < 2; i++)
        {
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.False(vm.Station.IsSuccessful);
            Assert.False(prep.HasExecutionSucceeded);
            Assert.False(vm.NeedsStandInspection);
        }
        Assert.Equal(starts, PreparationStarts(vm));
    });

    [Fact]
    public Task FailedPreparation_BlocksDeviceChecksAndRetriesUntilSuccess() => InSession(async () =>
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
        var starts = PreparationStarts(vm);
        Assert.True(failedPrep.HasExecutionError);
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);

        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(starts + 1, PreparationStarts(vm));
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);

        failedPrep.VariableName = "OperatorName";
        failedPrep.ExpectedValue = App.StartupUserName;
        failedPrep.ComparisonType = VariableComparisonType.String;
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(starts + 2, PreparationStarts(vm));
        Assert.True(test.HasExecutionSucceeded);
        Assert.True(vm.Station.IsSuccessful);
    });

    [Fact]
    public Task CancelledPreparation_BlocksDeviceChecksAndCanBeRetried() => InSession(async () =>
    {
        using var files = new ProfileFiles();
        using var bus = new ModbusService();
        using var vm = CreateViewModel(bus);
        var prep = PreparationDelay(); prep.Milliseconds = 1000;
        Chain(vm.RootGraph, prep, new DelayNodeViewModel { Milliseconds = 0 });
        var path = files.PathFor("cancelled-preparation");
        await vm.SaveGraphToPathAsync(path);
        var opening = vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
        Assert.True(vm.IsTestRunning);
        vm.StopTestCommand.Execute(null);
        await opening;
        prep = vm.Nodes.OfType<DelayNodeViewModel>().First();
        var test = vm.Nodes.OfType<DelayNodeViewModel>().Last();
        Assert.False(prep.HasExecutionSucceeded);
        Assert.False(test.HasExecutionSucceeded);
        Assert.False(vm.Station.HasResult);
        prep.Milliseconds = 0;
        var starts = PreparationStarts(vm);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.Equal(starts + 1, PreparationStarts(vm));
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

        Assert.True(await vm.PrepareOpenedProfileAsync());
        Assert.Equal(starts + 1, PreparationStarts(vm));
        vm.ConfigureNodeRun(prep, true);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.True(prep.HasExecutionSucceeded);
        await vm.RunGraphCommand.ExecuteAsync(null);
        Assert.False(prep.HasExecutionSucceeded);
        Assert.True(test.HasExecutionSucceeded);
        Assert.Equal(starts + 1, PreparationStarts(vm));
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
            var starts = PreparationStarts(vm);
            check = vm.Nodes.OfType<CheckVariableEqualityNodeViewModel>().Single();
            noSetupReport = vm.Nodes.OfType<ParseResponseNodeViewModel>().Single();
            for (var i = 0; i < 2; i++)
            {
                await vm.RunGraphCommand.ExecuteAsync(null);
                Assert.True(check.HasExecutionSucceeded); // The preceding run changed SerialNumber to 43.
                Assert.True(noSetupReport.HasExecutionSucceeded);
            }
            Assert.Equal(starts, PreparationStarts(vm));
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

    private static TestViewModel CreateViewModel(ModbusService bus) => new(bus, new SlaveManager(bus)) { IsOperatorView = false };
    private static DelayNodeViewModel PreparationDelay() => new() { Milliseconds = 1, RunOnlyOnFirstRun = true };
    private static GetSerialNumberFromServerNodeViewModel FixedValue(int value) => new() { UseFixedSerialNumber = true, FixedSerialNumber = value };
    private static int PreparationStarts(TestViewModel vm) => vm.TestingLogger.Entries.Count(entry => entry.Message.StartsWith("Подготовка стенда при открытии профиля:", StringComparison.Ordinal));

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
