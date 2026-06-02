using TestBuilder.Domain.Monitoring;

namespace TestBuilder.Tests.MonitoringTests;

public class RegisterMonitorTests
{
    [Fact]
    public void RegisterState_Should_Update_And_Read_BySlaveAndAddress()
    {
        var registerState = new RegisterState();

        registerState.Update(slaveId: 1, address: 1000, value: 42);
        registerState.Update(slaveId: 2, address: 1000, value: 77);

        Assert.True(registerState.TryGet(slaveId: 1, address: 1000, out var firstValue));
        Assert.True(registerState.TryGet(slaveId: 2, address: 1000, out var secondValue));
        Assert.Equal(42, firstValue);
        Assert.Equal(77, secondValue);
    }

    [Fact]
    public void RegisterState_Should_ReturnFalse_WhenValueMissing()
    {
        var registerState = new RegisterState();

        var found = registerState.TryGet(slaveId: 1, address: 1000, out var value);

        Assert.False(found);
        Assert.Equal(0, value);
    }
}
