using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class CompareStepTests
{
    [Fact]
    public async Task CheckRegisterRangeStep_ReturnsTrue_WhenValueInRange()
    {
        var registerState = new RegisterState();
        var context = new TestContext(registerState);
        registerState.Update(slaveId: 1, address: 100, value: 3200);
        var step = new CheckRegisterRangeStep(1, 100, 3000, 3300, NullLogger.Instance);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
    }

    [Fact]
    public async Task CheckRegisterRangeStep_ReturnsFalse_WhenValueOutOfRange()
    {
        var registerState = new RegisterState();
        var context = new TestContext(registerState);
        registerState.Update(slaveId: 1, address: 100, value: 3400);
        var step = new CheckRegisterRangeStep(1, 100, 3000, 3300, NullLogger.Instance);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
    }

    [Fact]
    public async Task CheckRegisterRangeStep_ReturnsFalse_WhenRegisterMissing()
    {
        var context = new TestContext(new RegisterState());
        var step = new CheckRegisterRangeStep(1, 100, 3000, 3300, NullLogger.Instance);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
    }

    [Fact]
    public async Task CheckRegisterRangeStep_WhenLiveReadEnabled_ReadsModbusAndUpdatesRegisterState()
    {
        var modbus = new FakeModbusService(3200);
        var registerState = new RegisterState();
        var context = new TestContext(registerState);
        var step = new CheckRegisterRangeStep(
            1,
            100,
            3000,
            3300,
            NullLogger.Instance,
            modbusService: modbus,
            liveRead: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, modbus.ReadCount);
        Assert.True(registerState.TryGet(1, 100, out var value));
        Assert.Equal(3200, value);
    }

    [Fact]
    public async Task CheckRegisterRangeStep_WhenLiveReadFails_ReturnsFalse()
    {
        var modbus = new FakeModbusService();
        var context = new TestContext(new RegisterState());
        var step = new CheckRegisterRangeStep(
            1,
            100,
            3000,
            3300,
            NullLogger.Instance,
            modbusService: modbus,
            liveRead: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(1, modbus.ReadCount);
    }

    [Fact]
    public async Task CheckRegisterRangeStep_RetriesOutOfRangeLiveReadAndStopsAtFirstPassingValue()
    {
        var modbus = new FakeModbusService(45000, 54000, 55000);
        var context = new TestContext(new RegisterState());
        var step = new CheckRegisterRangeStep(1, 1403, 53000, 56000,
            NullLogger.Instance, modbusService: modbus, liveRead: true,
            readAttempts: 3, readIntervalMs: 0);

        Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(2, modbus.ReadCount);
        Assert.True(context.RegisterState.TryGet(1, 1403, out var value));
        Assert.Equal(54000, value);
    }

    [Fact]
    public async Task CheckRegisterRangeStep_FailsAfterConfiguredAttempts()
    {
        var modbus = new FakeModbusService(45000, 46000, 47000);
        var step = new CheckRegisterRangeStep(1, 1403, 53000, 56000,
            NullLogger.Instance, modbusService: modbus, liveRead: true,
            readAttempts: 3, readIntervalMs: 0);

        Assert.Equal(StepResult.False,
            await step.ExecuteAsync(new TestContext(new RegisterState()), CancellationToken.None));
        Assert.Equal(3, modbus.ReadCount);
    }

    private sealed class FakeModbusService : IModbusService
    {
        private readonly ushort[] _values;

        public FakeModbusService(params ushort[] values)
        {
            _values = values;
        }

        public int ReadCount { get; private set; }

        public Task<ushort[]> ReadRegistersAsync(
            byte slaveId,
            ushort address,
            ushort count,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return Task.FromResult(_values.Length == 0
                ? Array.Empty<ushort>()
                : new[] { _values[Math.Min(ReadCount - 1, _values.Length - 1)] });
        }

        public Task<bool> WriteRegisterAsync(
            byte slaveId,
            ushort address,
            ushort value,
            bool verify = true,
            CancellationToken cancellationToken = default)
        {
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
