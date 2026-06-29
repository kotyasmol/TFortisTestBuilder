using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class BuildMacFromSerialStep : ITestStep
    {
        private readonly ILogger _logger;
        private readonly string _serialVariableName;
        private readonly int _serialOffset;
        private readonly string _macPrefix;
        private readonly string _serialShortVariableName;
        private readonly string _macVariableName;
        private readonly bool _failOnError;

        public BuildMacFromSerialStep(
            ILogger logger,
            string serialVariableName,
            int serialOffset,
            string macPrefix,
            string serialShortVariableName,
            string macVariableName,
            bool failOnError)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serialVariableName = string.IsNullOrWhiteSpace(serialVariableName) ? "SerialNumber" : serialVariableName.Trim();
            _serialOffset = serialOffset;
            _macPrefix = string.IsNullOrWhiteSpace(macPrefix) ? "C0:11:A6:20" : macPrefix.Trim();
            _serialShortVariableName = string.IsNullOrWhiteSpace(serialShortVariableName) ? "SerialShort" : serialShortVariableName.Trim();
            _macVariableName = string.IsNullOrWhiteSpace(macVariableName) ? "Dut.NewMac" : macVariableName.Trim();
            _failOnError = failOnError;
        }

        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!context.Variables.TryGetValue(_serialVariableName, out var rawSerial))
            {
                return Task.FromResult(Fail(context, $"Переменная серийного номера '{_serialVariableName}' не найдена."));
            }

            if (!TryParseInt(rawSerial, out var serial))
            {
                return Task.FromResult(Fail(context, $"Серийный номер '{rawSerial}' не является целым числом."));
            }

            var serialShort = serial - _serialOffset;
            if (serialShort < 0 || serialShort > 0xFFFF)
            {
                return Task.FromResult(Fail(
                    context,
                    $"Короткий серийный номер {serialShort} вне диапазона 0..65535. Serial={serial}, offset={_serialOffset}."));
            }

            string normalizedPrefix;
            try
            {
                normalizedPrefix = NormalizeMacPrefix(_macPrefix);
            }
            catch (FormatException ex)
            {
                return Task.FromResult(Fail(context, ex.Message));
            }

            var high = (serialShort >> 8) & 0xFF;
            var low = serialShort & 0xFF;
            var mac = $"{normalizedPrefix}:{high:X2}:{low:X2}";

            context.SetVariable(_serialShortVariableName, serialShort);
            context.SetVariable(_macVariableName, mac);
            context.SetVariable("BuildMac.SerialNumber", serial);
            context.SetVariable("BuildMac.SerialOffset", _serialOffset);
            context.SetVariable("BuildMac.SerialShort", serialShort);
            context.SetVariable("BuildMac.Mac", mac);
            context.SetVariable("BuildMac.Success", true);
            context.SetVariable("BuildMac.Error", string.Empty);

            _logger.Info($"[OK] MAC рассчитан из серийного номера: serial={serial}, short={serialShort}, mac={mac}.");
            return Task.FromResult(StepResult.True);
        }

        private StepResult Fail(TestContext context, string error)
        {
            context.SetVariable("BuildMac.Success", false);
            context.SetVariable("BuildMac.Error", error);
            _logger.Warning($"[ОШИБКА] MAC не рассчитан: {error}");
            return _failOnError ? StepResult.False : StepResult.True;
        }

        private static bool TryParseInt(object value, out int parsed)
        {
            if (value is int i)
            {
                parsed = i;
                return true;
            }

            if (value is long l && l >= int.MinValue && l <= int.MaxValue)
            {
                parsed = (int)l;
                return true;
            }

            var text = value switch
            {
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);
        }

        private static string NormalizeMacPrefix(string value)
        {
            var hex = new string(value.Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

            if (hex.Length != 8)
            {
                throw new FormatException($"Префикс MAC должен содержать 4 байта, например C0:11:A6:20. Факт: '{value}'.");
            }

            return string.Join(":", Enumerable.Range(0, 4).Select(i => hex.Substring(i * 2, 2)));
        }
    }
}
