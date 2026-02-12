using System;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services.Modbus;
using Xunit;
using Xunit.Abstractions;

namespace TestBuilder.Tests.MonitoringTests
{
    public class RegisterMonitorRealTests
    {
        private readonly ITestOutputHelper _output;

        public RegisterMonitorRealTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Monitor_Should_Update_RegisterState_RealDevice()
        {
            var modbus = new ModbusService();

            // Подключаемся к COM7
            bool connected = await modbus.ConnectAsync(
                port: "COM8",
                baudRate: 9600,
                parity: System.IO.Ports.Parity.None,
                dataBits: 8,
                stopBits: System.IO.Ports.StopBits.One
            );

            Assert.True(connected, $"Не удалось подключиться к COM8. Ошибка: {modbus.LastError}");

            try
            {
                // Создаем менеджер и EL-60 слейв
                var slaveManager = new SlaveManager(modbus);
                var el60 = new El60Model(1, modbus);
                slaveManager.Slaves.Add(el60);

                var registerState = new RegisterState();

                // Форсируем опрос слейва
                await el60.PollAsync();

                // Обновляем RegisterState
                foreach (var reg in el60.RegisterItems)
                {
                    registerState.Update(reg.Name, reg.Value);
                    _output.WriteLine($"{reg.Name} = {reg.Value}");
                }

                // Пример проверки конкретных регистров
                Assert.True(registerState.TryGet("Current", out var current));
                Assert.True(registerState.TryGet("Voltage", out var voltage));

                _output.WriteLine($"Проверка: Current = {current}, Voltage = {voltage}");
            }
            finally
            {
                await modbus.DisconnectAsync();
            }
        }
    }
}
