using System.Collections.ObjectModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using Xunit.Abstractions;

namespace TestBuilder.Tests.StepTests;

// Run inside the existing checkout on an idle Windows stand with a known
// connected pair. No application, graph, configuration, report POST or printer.
[Collection(AppSettingsTestCollection.Name)]
public class DataTestThroughputHardwareTests(ITestOutputHelper output)
{
    [ThroughputHardwareTheory]
    [InlineData(100)]
    [InlineData(1000)]
    [Trait("Category", "Hardware")]
    public async Task ThreeFullSamplesConfirmUniqueDeliveryInBothDirections(int mbps)
    {
        var pair = Environment.GetEnvironmentVariable("TFORTIS_DATATEST_THROUGHPUT_PAIR")!.Split(',');
        Assert.Equal(2, pair.Length);
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        output.WriteLine($"ПК {Environment.MachineName}; .NET {Environment.Version}; цель {mbps} Мбит/с; {pair[0]} ↔ {pair[1]}");
        for (var run = 1; run <= 3; run++)
        {
            output.WriteLine($"Прогон {run}/3");
            var context = new TestContext(new RegisterState());
            var step = new RunDataTestStep(new OutputLogger(output), "SoftwarePcap", 1, 1514, 43962,
                30000, mbps, 5000, 500, 100, 0, 2, true,
                new[] { new DataTestPortConfig("Контроль пропускной способности", pair[0].Trim(), pair[1].Trim(), mbps) },
                "ThroughputProbe", true, allowGigabit: true);
            Assert.Equal(StepResult.True, await step.ExecuteAsync(context, stop.Token));
            foreach (var direction in new[] { "Forward", "Reverse" })
            {
                var prefix = $"ThroughputProbe.Port0.{direction}";
                Assert.True(context.GetVariable<bool>(prefix + ".Passed"));
                Assert.Equal(1, context.GetVariable<int>(prefix + ".AttemptCount"));
                Assert.Equal(RunDataTestStep.CalculateExpectedPackets(mbps, 1514, 5000),
                    context.GetVariable<int>(prefix + ".TransmittedPackets"));
                Assert.Equal(context.GetVariable<int>(prefix + ".TransmittedPackets"),
                    context.GetVariable<int>(prefix + ".ReceivedPackets"));
                Assert.True(context.GetVariable<double>(prefix + ".ActualTxMbps") >= mbps * 0.98);
                Assert.Equal(128, context.GetVariable<int>(prefix + ".CaptureLength"));
            }
        }
    }

    private sealed class ThroughputHardwareTheoryAttribute : TheoryAttribute
    {
        public ThroughputHardwareTheoryAttribute()
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TFORTIS_DATATEST_THROUGHPUT_PAIR")))
                Skip = "Задайте TFORTIS_DATATEST_THROUGHPUT_PAIR=IPv4,IPv4 на свободном Windows-стенде с известным соединением карт.";
        }
    }

    private sealed class OutputLogger(ITestOutputHelper output) : ILogger
    {
        public string Category => "ThroughputProbe";
        public ObservableCollection<LogEntry> Entries { get; } = new();
        public void Log(LogLevel level, string message) => output.WriteLine($"{level}: {message}");
        public void Trace(string message) => Log(LogLevel.Trace, message);
        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message) => Log(LogLevel.Warning, message);
        public void Error(string message) => Log(LogLevel.Error, message);
        public void Clear() { }
    }
}
