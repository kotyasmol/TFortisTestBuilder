using System.Threading.Tasks;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services.Modbus;
using Xunit;
using System.Collections.Generic;

namespace TestBuilder.Tests.MonitoringTests
{
    public class RegisterMonitorTests
    {
        private class FakeModbusService : IModbusService
        {
            private readonly Dictionary<(byte slaveId, ushort address), ushort> _values = new();

            public FakeModbusService()
            {
                // Инициализируем все регистры слейва 1
                for (ushort i = 0; i < 17; i++)
                    _values[(1, (ushort)(1000 + i))] = 0;

                // Подставляем тестовые значения
                _values[(1, 1000)] = 123;  // Current
                _values[(1, 1001)] = 456;  // Voltage
            }

            public Task<ushort[]> ReadRegistersAsync(byte slaveId, ushort address, ushort count)
            {
                var result = new ushort[count];
                for (int i = 0; i < count; i++)
                {
                    _values.TryGetValue((slaveId, (ushort)(address + i)), out var val);
                    result[i] = val;
                }
                return Task.FromResult(result);
            }

            public Task<bool> WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true)
            {
                _values[(slaveId, address)] = value;
                return Task.FromResult(true);
            }
        }

        [Fact]
        public async Task Monitor_Should_Update_RegisterState_Realistic()
        {
            // arrange
            var fakeModbus = new FakeModbusService();
            var slaveManager = new SlaveManager(fakeModbus);

            var el60 = new El60Model(1, fakeModbus);
            slaveManager.Slaves.Add(el60);

            var registerState = new RegisterState();

            // act
            // форсированный опрос слейва
            await el60.PollAsync();

            // обновляем RegisterState вручную
            foreach (var reg in el60.RegisterItems)
                registerState.Update(reg.Name, reg.Value);

            // assert
            Assert.True(registerState.TryGet("Current", out var current));
            Assert.Equal(123, current);

            Assert.True(registerState.TryGet("Voltage", out var voltage));
            Assert.Equal(456, voltage);
        }
    }
}
