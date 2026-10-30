using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;
using TestBuilder.Services;

namespace TestBuilder.Domain.Steps
{
    public enum DeviceLabelModel { PswUpsBox8x2Pro, Psw2G6FPlus }

    internal readonly record struct RawLabelPrintResult(bool Success, int ErrorCode, string Error, uint JobId = 0, string CompletionStatus = "Printed")
    {
        public static RawLabelPrintResult Ok() => new(true, 0, string.Empty);

        public static RawLabelPrintResult Fail(int errorCode, string error) =>
            new(false, errorCode, error);
    }

    internal interface IRawLabelPrinter
    {
        RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken cancellationToken);
    }

    public sealed class PrintLabelStep : ITestStep
    {
        private const int DefaultPrinterTimeoutMs = 30000;
        private readonly ILogger _logger;
        private readonly string _printerName;
        private readonly string _serialVariableName;
        private readonly string _serialShortVariableName;
        private readonly string _macVariableName;
        private readonly bool _useManualSerialNumber;
        private readonly string _manualSerialNumber;
        private readonly string _manualMacAddress;
        private readonly int _copies;
        private readonly bool _useQtProZplFormat;
        private readonly bool _failOnPrinterError;
        private readonly IRawLabelPrinter _printer;
        private readonly int _printerTimeoutMs;
        private readonly DeviceLabelModel _labelModel;
        private readonly LabelTemplateDefinition _template;
        private string DeviceName => _template.Name;
        private int DeviceType => _template.DeviceCode;
        private int SerialOffset => _template.SerialOffset;

        public PrintLabelStep(
            ILogger logger,
            string printerName,
            string serialVariableName,
            string serialShortVariableName,
            string macVariableName,
            bool useManualSerialNumber,
            string manualSerialNumber,
            string manualMacAddress,
            int copies,
            bool useQtProZplFormat,
            bool failOnPrinterError,
            DeviceLabelModel labelModel = DeviceLabelModel.PswUpsBox8x2Pro,
            LabelTemplateDefinition? template = null)
            : this(
                logger,
                printerName,
                serialVariableName,
                serialShortVariableName,
                macVariableName,
                useManualSerialNumber,
                manualSerialNumber,
                manualMacAddress,
                copies,
                useQtProZplFormat,
                failOnPrinterError,
                new WindowsRawLabelPrinter(),
                DefaultPrinterTimeoutMs,
                labelModel, template)
        {
        }

        internal PrintLabelStep(
            ILogger logger,
            string printerName,
            string serialVariableName,
            string serialShortVariableName,
            string macVariableName,
            bool useManualSerialNumber,
            string manualSerialNumber,
            string manualMacAddress,
            int copies,
            bool useQtProZplFormat,
            bool failOnPrinterError,
            IRawLabelPrinter printer,
            int printerTimeoutMs,
            DeviceLabelModel labelModel = DeviceLabelModel.PswUpsBox8x2Pro,
            LabelTemplateDefinition? template = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _printerName = printerName?.Trim() ?? string.Empty;
            _serialVariableName = string.IsNullOrWhiteSpace(serialVariableName)
                ? "SerialNumber"
                : serialVariableName.Trim();
            _serialShortVariableName = string.IsNullOrWhiteSpace(serialShortVariableName)
                ? "SerialShort"
                : serialShortVariableName.Trim();
            _macVariableName = string.IsNullOrWhiteSpace(macVariableName)
                ? "Dut.default_mac"
                : macVariableName.Trim();
            _useManualSerialNumber = useManualSerialNumber;
            _manualSerialNumber = manualSerialNumber?.Trim() ?? string.Empty;
            _manualMacAddress = manualMacAddress?.Trim() ?? string.Empty;
            _copies = Math.Max(1, copies);
            _template = (template ?? LabelTemplateCatalog.Legacy(useQtProZplFormat ? labelModel.ToString() : "SerialOnlyEpl")).Copy();
            _template.Validate();
            _useQtProZplFormat = _template.UseShortSerial;
            _failOnPrinterError = failOnPrinterError;
            _printer = printer ?? throw new ArgumentNullException(nameof(printer));
            _printerTimeoutMs = Math.Max(1, printerTimeoutMs);
            _labelModel = labelModel;
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetDiagnostics(context);
            context.SetVariable("PrintLabel.Copies", _copies);
            context.SetVariable("PrintLabel.PrinterName", _printerName);
            context.SetVariable("PrintLabel.Language", _template.Language);
            context.SetVariable("PrintLabel.Template", _template.Id);

            if (string.IsNullOrWhiteSpace(_printerName))
            {
                return Fail(context, 3, "Имя принтера не задано.");
            }

            string serial;
            string serialSource;

            if (_useManualSerialNumber)
            {
                if (string.IsNullOrWhiteSpace(_manualSerialNumber))
                {
                    return Fail(context, 6, "Введите серийный номер в поле 'Серийник вручную'.");
                }

                serial = _manualSerialNumber;
                serialSource = "Manual";
            }
            else
            {
                if (!context.Variables.TryGetValue(_serialVariableName, out var rawSerial))
                {
                    return Fail(
                        context,
                        6,
                        $"Переменная серийного номера '{_serialVariableName}' не найдена.");
                }

                serial = rawSerial?.ToString()?.Trim() ?? string.Empty;
                serialSource = _serialVariableName;
            }

            if (string.IsNullOrWhiteSpace(serial))
            {
                return Fail(context, 6, "Серийный номер для печати пуст.");
            }

            if (!serial.All(character => character is >= '0' and <= '9'))
            {
                return Fail(context, 6, "Серийный номер должен содержать только цифры 0-9.");
            }

            string singleLabel;
            string language;
            string logDescription;

            if (_useQtProZplFormat)
            {
                int fullSerial;
                int shortSerial;
                string mac = "";
                string shortSerialSource;
                string macSource;

                if (_useManualSerialNumber)
                {
                    if (!LabelPrintContent.TryResolveSerial(_template, serial, out fullSerial, out shortSerial, out var serialError))
                    {
                        return Fail(context, 6, serialError);
                    }

                    if (_template.RequiresMac && !LabelPrintContent.TryNormalizeMac(_manualMacAddress, out mac))
                    {
                        return Fail(
                            context,
                            6,
                            "Введите реальный MAC в поле 'MAC вручную', например C0:11:A6:20:01:AC.");
                    }

                    shortSerialSource = "Manual";
                    macSource = "Manual";
                }
                else
                {
                    if (!int.TryParse(serial, NumberStyles.None, CultureInfo.InvariantCulture, out fullSerial))
                    {
                        return Fail(context, 6, $"Серийный номер '{serial}' слишком большой или имеет неверный формат.");
                    }

                    if (!TryReadIntVariable(context, _serialShortVariableName, out shortSerial))
                    {
                        return Fail(
                            context,
                            6,
                            $"Переменная короткого серийного номера '{_serialShortVariableName}' не найдена или не является целым числом.");
                    }

                    if (shortSerial < 0 || shortSerial > _template.ShortSerialMax)
                    {
                        return Fail(context, 6, $"Короткий серийный номер {shortSerial} вне диапазона 0..{_template.ShortSerialMax}.");
                    }

                    if (fullSerial != SerialOffset + shortSerial)
                    {
                        return Fail(
                            context,
                            6,
                            $"Серийники не согласованы: {_serialVariableName}={fullSerial}, " +
                            $"{_serialShortVariableName}={shortSerial}.");
                    }

                    if (_template.RequiresMac && (!context.Variables.TryGetValue(_macVariableName, out var rawMac) ||
                        !LabelPrintContent.TryNormalizeMac(rawMac?.ToString() ?? string.Empty, out mac)))
                    {
                        return Fail(
                            context,
                            6,
                            $"Переменная MAC '{_macVariableName}' не найдена или содержит некорректный адрес.");
                    }

                    shortSerialSource = _serialShortVariableName;
                    macSource = _macVariableName;
                }

                var barcode = _template.Barcode(shortSerial);
                singleLabel = LabelPrintContent.Render(_template, fullSerial.ToString(CultureInfo.InvariantCulture), shortSerial, mac);
                language = _template.Language;
                logDescription =
                    $"этикеток {DeviceName}: serial={fullSerial}, SN={shortSerial:D5}, MAC={mac}, barcode={barcode}";

                context.SetVariable("PrintLabel.Template", _template.Id);
                context.SetVariable("PrintLabel.FullSerial", fullSerial);
                context.SetVariable("PrintLabel.SerialShort", shortSerial);
                context.SetVariable("PrintLabel.DeviceName", DeviceName);
                context.SetVariable("PrintLabel.DeviceType", DeviceType);
                context.SetVariable("PrintLabel.Mac", mac);
                context.SetVariable("PrintLabel.MacSource", macSource);
                context.SetVariable("PrintLabel.SerialShortSource", shortSerialSource);
                context.SetVariable("PrintLabel.Barcode", barcode);
            }
            else
            {
                singleLabel = LabelPrintContent.Render(_template, serial, 0, "");
                language = _template.Language;
                logDescription = $"серийного номера {serial}";
            }

            var printData = Repeat(singleLabel, _copies);
            var bytes = GetPrinterEncoding().GetBytes(printData);

            context.SetVariable("PrintLabel.Serial", serial);
            context.SetVariable("PrintLabel.SerialSource", serialSource);
            context.SetVariable("PrintLabel.Language", language);
            context.SetVariable("PrintLabel.SingleCommand", singleLabel);
            context.SetVariable("PrintLabel.Epl", language == "EPL" ? printData : string.Empty);
            context.SetVariable("PrintLabel.Zpl", language == "ZPL" ? printData : string.Empty);
            context.SetVariable("PrintLabel.RawData", printData);
            context.SetVariable("PrintLabel.Bytes", bytes.Length);

            _logger.Info($"[ШАГ] Печать {logDescription} на '{_printerName}', экземпляров: {_copies}.");

            using var printCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var printTask = Task.Run(
                () => _printer.Print(_printerName, bytes, printCts.Token),
                CancellationToken.None);
            var timeoutTask = Task.Delay(_printerTimeoutMs, cancellationToken);
            var completed = await Task.WhenAny(printTask, timeoutTask).ConfigureAwait(false);

            if (completed != printTask)
            {
                printCts.Cancel();
                ObserveLatePrintTask(printTask);
                cancellationToken.ThrowIfCancellationRequested();
                context.SetVariable("PrintLabel.TimedOut", true);
                return Fail(context, 5, $"Задание печати не завершено за {_printerTimeoutMs} мс; запрошена отмена задания.");
            }

            RawLabelPrintResult printResult;
            try
            {
                printResult = await printTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Fail(context, 3, ex.Message);
            }

            context.SetVariable("PrintLabel.JobId", printResult.JobId);
            context.SetVariable("PrintLabel.CompletionStatus", printResult.Success ? printResult.CompletionStatus : string.Empty);
            if (!printResult.Success)
            {
                return Fail(context, printResult.ErrorCode, printResult.Error);
            }

            context.SetVariable("PrintLabel.Success", true);
            context.SetVariable("PrintLabel.ErrorCode", 0);
            context.SetVariable("PrintLabel.Error", string.Empty);
            var completionText = printResult.CompletionStatus == "SentToPrinter"
                ? "передано принтеру; драйвер не подтверждает физический выход этикетки"
                : "Windows сообщает PRINTED";
            context.AddReportEntry("Печать этикеток", true, $"{completionText}; {_copies} шт.; {_printerName}; задание {printResult.JobId}");
            _logger.Info($"[OK] Задание {printResult.JobId}: {completionText}; '{_printerName}', этикеток: {_copies}.");
            return StepResult.True;
        }

        internal static string BuildEpl(string serial)
        {
            var template = LabelTemplateCatalog.Defaults().Single(t => t.Id == "SerialOnlyEpl");
            return LabelPrintContent.Render(template, serial, 0, "");
        }
        internal static string BuildPswUpsBox8x2ProZpl(int shortSerial, string mac) => BuildLegacy("PswUpsBox8x2Pro", shortSerial, mac);
        internal static string BuildPsw2G6FPlusZpl(int shortSerial, string mac) => BuildLegacy("Psw2G6FPlus", shortSerial, mac);
        private static string BuildLegacy(string id, int shortSerial, string mac)
        {
            var template = LabelTemplateCatalog.Defaults().Single(t => t.Id == id);
            if (shortSerial < 0 || shortSerial > template.ShortSerialMax) throw new ArgumentOutOfRangeException(nameof(shortSerial));
            return template.Render((template.SerialOffset + shortSerial).ToString(CultureInfo.InvariantCulture), shortSerial, mac);
        }

        private static bool TryReadIntVariable(TestContext context, string variableName, out int value)
        {
            value = 0;
            if (!context.Variables.TryGetValue(variableName, out var rawValue) || rawValue == null)
            {
                return false;
            }

            if (rawValue is int intValue)
            {
                value = intValue;
                return true;
            }

            if (rawValue is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
            {
                value = (int)longValue;
                return true;
            }

            return int.TryParse(
                rawValue.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        private StepResult Fail(TestContext context, int errorCode, string error)
        {
            context.SetVariable("PrintLabel.Success", false);
            context.SetVariable("PrintLabel.ErrorCode", errorCode);
            context.SetVariable("PrintLabel.Error", error);
            context.AddReportEntry("Печать этикеток", false, error);
            _logger.Warning($"[ОШИБКА] Печать этикеток не подтверждена: {error}");
            return _failOnPrinterError ? StepResult.False : StepResult.True;
        }

        private static void ResetDiagnostics(TestContext context)
        {
            context.SetVariable("PrintLabel.Copies", 0);
            context.SetVariable("PrintLabel.JobId", 0u);
            context.SetVariable("PrintLabel.CompletionStatus", string.Empty);
            context.SetVariable("PrintLabel.PrinterName", string.Empty);
            context.SetVariable("PrintLabel.Serial", string.Empty);
            context.SetVariable("PrintLabel.SerialSource", string.Empty);
            context.SetVariable("PrintLabel.Template", string.Empty);
            context.SetVariable("PrintLabel.FullSerial", 0);
            context.SetVariable("PrintLabel.SerialShort", 0);
            context.SetVariable("PrintLabel.DeviceName", string.Empty);
            context.SetVariable("PrintLabel.DeviceType", 0);
            context.SetVariable("PrintLabel.Mac", string.Empty);
            context.SetVariable("PrintLabel.MacSource", string.Empty);
            context.SetVariable("PrintLabel.SerialShortSource", string.Empty);
            context.SetVariable("PrintLabel.Barcode", string.Empty);
            context.SetVariable("PrintLabel.Success", false);
            context.SetVariable("PrintLabel.TimedOut", false);
            context.SetVariable("PrintLabel.ErrorCode", 0);
            context.SetVariable("PrintLabel.Error", string.Empty);
            context.SetVariable("PrintLabel.Language", string.Empty);
            context.SetVariable("PrintLabel.SingleCommand", string.Empty);
            context.SetVariable("PrintLabel.Epl", string.Empty);
            context.SetVariable("PrintLabel.Zpl", string.Empty);
            context.SetVariable("PrintLabel.RawData", string.Empty);
            context.SetVariable("PrintLabel.Bytes", 0);
        }

        private static string Repeat(string value, int count)
        {
            var builder = new StringBuilder(value.Length * count);
            for (var index = 0; index < count; index++)
            {
                builder.Append(value);
            }

            return builder.ToString();
        }

        private static Encoding GetPrinterEncoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251);
        }

        private static void ObserveLatePrintTask(Task<RawLabelPrintResult> task)
        {
            _ = task.ContinueWith(
                completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

}
