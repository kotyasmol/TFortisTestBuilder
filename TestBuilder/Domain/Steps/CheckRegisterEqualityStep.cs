using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Steps
{
    public class CheckRegisterEqualityStep : ITestStep
    {
        private readonly byte _slaveId;
        private readonly int _address;
        private readonly int _expectedValue;
        private readonly bool _useCurrentSlaveId;
        private readonly IModbusService? _modbusService;
        private readonly bool _liveRead;
        private readonly ILogger _logger;

        public CheckRegisterEqualityStep(
            byte slaveId,
            int address,
            int expectedValue,
            ILogger logger,
            bool useCurrentSlaveId = false,
            IModbusService? modbusService = null,
            bool liveRead = false)
        {
            _slaveId = slaveId;
            _address = address;
            _expectedValue = expectedValue;
            _logger = logger;
            _useCurrentSlaveId = useCurrentSlaveId;
            _modbusService = modbusService;
            _liveRead = liveRead;
        }

        public async Task<StepResult> ExecuteAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            var actualSlaveId = _useCurrentSlaveId ? context.CurrentSlaveId : _slaveId;

            if (actualSlaveId == null)
            {
                _logger.Warning(
                    $"[ШАГ] Проверка равенства -> устройство не задано, адрес {_address}, ожидалось {_expectedValue}.");

                return StepResult.False;
            }

            var read = await ModbusRegisterReadHelper.ReadAsync(
                context,
                _modbusService,
                actualSlaveId.Value,
                _address,
                _liveRead,
                cancellationToken);

            if (!read.Success)
            {
                _logger.Warning(
                    $"[ОШИБКА] Регистр не прочитан. Устройство {actualSlaveId}, адрес {_address}: {read.Error}");

                return StepResult.False;
            }

            var value = read.Value;
            var equal = value == _expectedValue;

            _logger.Info(
                $"[ШАГ] Проверка равенства -> устройство {actualSlaveId}, адрес {_address}, значение {value}, ожидалось {_expectedValue}, источник {(_liveRead ? "live Modbus" : "RegisterState")}.");

            if (!equal)
            {
                _logger.Warning(
                    $"[ОШИБКА] Значение {value} != {_expectedValue}. Устройство {actualSlaveId}, адрес {_address}.");
            }
            else
            {
                _logger.Info($"[OK] Значение {value} == {_expectedValue}.");
            }

            return equal ? StepResult.True : StepResult.False;
        }
    }
}
