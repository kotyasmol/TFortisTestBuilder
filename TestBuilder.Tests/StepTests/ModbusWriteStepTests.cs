using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class ModbusWriteStepTests
{
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
        var checkStep = new CheckRegisterEqualityStep(1, 100, 42, NullLogger.Instance);

        var writeResult = await writeStep.ExecuteAsync(context, CancellationToken.None);
        var checkResult = await checkStep.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, writeResult);
        Assert.Equal(StepResult.True, checkResult);
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
        var step = new ModbusWriteStep(modbus, NullLogger.Instance, 1, 100, 42, verifyWrite: true);

        var result = await step.ExecuteAsync(CreateContext(), CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(1, modbus.WriteCount);
        Assert.Equal(3, modbus.ReadCount);
    }

    private static TestContext CreateContext()
    {
        return new TestContext(new RegisterState());
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
