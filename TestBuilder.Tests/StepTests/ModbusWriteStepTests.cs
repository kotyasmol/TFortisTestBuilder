using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class ModbusWriteStepTests
{
    [Theory]
    [InlineData((byte)21, "Реле нагревателя")]
    [InlineData((byte)23, "Реле нагревателя 1")]
    public async Task RegisterLogs_InParallelBranchUseActualSlaveModelAndStableNames(byte slave, string name)
    {
        var modbus = new FakeModbusService();
        var ps2 = new PS2Model(21, modbus);
        var ps3 = new Ps3Model(23, modbus);
        var root = new TestContext(new RegisterState())
        {
            ModbusRegisters = new ModbusRegisterCatalog(new SlaveModelBase[] { ps2, ps3 })
        };
        var context = root.CreateParallelBranch(CancellationToken.None);
        context.CurrentSlaveId = slave;
        // A later model refresh must not rename registers in a running branch.
        ps2.RegisterItems.Clear();
        ps3.RegisterItems.Clear();
        var logger = new RecordingLogger();
        modbus.EnqueueRead(0);
        modbus.EnqueueRead(1);
        var write = new ModbusWriteStep(modbus, logger, 99, 1215, 1,
            useCurrentSlaveId: true, verifyWrite: true);
        var range = new CheckRegisterRangeStep(99, 1215, 0, 0, logger, useCurrentSlaveId: true);
        var wait = new WaitUntilStep(99, 1215, 1, 1000, logger, useCurrentSlaveId: true);

        Assert.Equal(StepResult.True, await write.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(StepResult.False, await range.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(StepResult.True, await wait.ExecuteAsync(context, CancellationToken.None));

        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Contains($"устройство {slave}", entry.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"адрес 1215 ({name})", entry.Message);
        });
        Assert.Equal(1, modbus.WriteCount);
        Assert.Equal(2, modbus.ReadCount);
    }

    [Fact]
    public void UnknownRegisterOrSlave_DoesNotBorrowDescriptionFromAnotherDevice()
    {
        var catalog = new ModbusRegisterCatalog(new[] { new Ps3Model(23, new FakeModbusService()) });

        Assert.Equal("1215 (описание неизвестно)", catalog.FormatAddress(99, 1215));
        Assert.Equal("9999 (описание неизвестно)", catalog.FormatAddress(23, 9999));
        Assert.Equal("1215 (описание неизвестно)", catalog.FormatAddress(null, 1215));
    }

    [Fact]
    public async Task ExecuteAsync_WhenVerificationDisabled_DoesNotReadBackRegister()
    {
        var modbus = new FakeModbusService();
        var step = new ModbusWriteStep(modbus, NullLogger.Instance, 1, 100, 42);

        var result = await step.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, modbus.WriteCount);
        Assert.Equal(0, modbus.ReadCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenVerificationEnabled_RetriesUntilValueMatches()
    {
        var modbus = new FakeModbusService();
        modbus.EnqueueRead(41);
        modbus.EnqueueRead(41);
        modbus.EnqueueRead(42);
        var step = new ModbusWriteStep(modbus, NullLogger.Instance, 1, 100, 42, verifyWrite: true);

        var result = await step.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, modbus.WriteCount);
        Assert.Equal(3, modbus.ReadCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenVerificationConfirmsValue_UpdatesRegisterState()
    {
        var modbus = new FakeModbusService();
        modbus.EnqueueRead(42);
        var context = CreateContext();
        context.RegisterState.Update(1, 100, 0);
        var writeStep = new ModbusWriteStep(modbus, NullLogger.Instance, 1, 100, 42, verifyWrite: true);

        var writeResult = await writeStep.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, writeResult);
        Assert.True(context.RegisterState.TryGet(1, 100, out var value));
        Assert.Equal(42, value);
    }

    [Fact]
    public async Task ExecuteAsync_WhenVerificationReadThrows_RetriesUntilValueMatches()
    {
        var modbus = new FakeModbusService();
        modbus.EnqueueReadFailure();
        modbus.EnqueueRead(42);
        var step = new ModbusWriteStep(modbus, NullLogger.Instance, 1, 100, 42, verifyWrite: true);

        var result = await step.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, modbus.WriteCount);
        Assert.Equal(2, modbus.ReadCount);
    }

    [Fact]
    public async Task ExecuteAsync_WhenVerificationEnabledAndValueNeverMatches_ReturnsFalse()
    {
        var modbus = new FakeModbusService();
        modbus.EnqueueRead(41);
        modbus.EnqueueRead(40);
        modbus.EnqueueRead(39);
        var logger = new RecordingLogger();
        var step = new ModbusWriteStep(modbus, logger, 1, 100, 42, verifyWrite: true);

        var result = await step.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(1, modbus.WriteCount);
        Assert.Equal(3, modbus.ReadCount);
        Assert.Contains("Ожидалось 42, прочитано 39", logger.Entries[^1].Message);
    }

    private static TestContext CreateContext()
    {
        return new TestContext(new RegisterState());
    }

    private sealed class RecordingLogger : ILogger
    {
        public string Category => "Test";
        public System.Collections.ObjectModel.ObservableCollection<LogEntry> Entries { get; } = new();
        public void Log(LogLevel level, string message) => Entries.Add(new LogEntry(DateTime.UtcNow, level, Category, message));
        public void Trace(string message) => Log(LogLevel.Trace, message);
        public void Debug(string message) => Log(LogLevel.Debug, message);
        public void Info(string message) => Log(LogLevel.Info, message);
        public void Warning(string message) => Log(LogLevel.Warning, message);
        public void Error(string message) => Log(LogLevel.Error, message);
        public void Clear() => Entries.Clear();
    }

    private sealed class FakeModbusService : IModbusService
    {
        private readonly Queue<object> _reads = new();

        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }

        public void EnqueueRead(ushort value)
        {
            _reads.Enqueue(new[] { value });
        }

        public void EnqueueReadFailure()
        {
            _reads.Enqueue(new InvalidOperationException("Read failed"));
        }

        public Task<ushort[]> ReadRegistersAsync(
            byte slaveId,
            ushort address,
            ushort count,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (_reads.Count == 0)
            {
                return Task.FromResult(Array.Empty<ushort>());
            }

            var read = _reads.Dequeue();

            if (read is Exception ex)
            {
                throw ex;
            }

            return Task.FromResult((ushort[])read);
        }

        public Task<bool> WriteRegisterAsync(
            byte slaveId,
            ushort address,
            ushort value,
            bool verify = true,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            return Task.FromResult(true);
        }

        public Task<bool> CheckPortAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback)
        {
        }
    }
}
