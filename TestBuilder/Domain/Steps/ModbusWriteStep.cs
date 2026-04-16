using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Шаг теста, который записывает указанное значение в регистр тестового контекста.
    /// Используется для подготовки состояния регистров перед проверками или другими шагами.
    /// </summary>
    public class ModbusWriteStep : ITestStep
    {
        private readonly IModbusService _modbusService;
        private readonly byte _slaveId;
        private readonly ushort _address;
        private readonly ushort _value;

        public ModbusWriteStep(
            IModbusService modbusService,
            byte slaveId,
            ushort address,
            ushort value)
        {
            _modbusService = modbusService;
            _slaveId = slaveId;
            _address = address;
            _value = value;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            var ok = await _modbusService.WriteRegisterAsync(
                _slaveId,
                _address,
                _value,
                true,
                cancellationToken);

            return ok ? StepResult.True : StepResult.False;
        }
    }
}
