using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    internal readonly record struct RawLabelPrintResult(bool Success, int ErrorCode, string Error)
    {
        public static RawLabelPrintResult Ok() => new(true, 0, string.Empty);

        public static RawLabelPrintResult Fail(int errorCode, string error) =>
            new(false, errorCode, error);
    }

    internal interface IRawLabelPrinter
    {
        RawLabelPrintResult Print(string printerName, byte[] bytes);
    }

    public sealed class PrintLabelStep : ITestStep
    {
        private const int DefaultPrinterTimeoutMs = 10000;
        private const int LabelWidthDots = 354;
        private const string PswUpsBox8x2ProName = "PSW+UPS-Box 8x2Pro";
        private const int PswUpsBox8x2ProDeviceType = 32;
        private const int PswUpsBox8x2ProSerialOffset = 3200000;

        private readonly ILogger _logger;
        private readonly string _printerName;
        private readonly string _serialVariableName;
        private readonly bool _useManualSerialNumber;
        private readonly string _manualSerialNumber;
        private readonly int _copies;
        private readonly bool _useQtProZplFormat;
        private readonly bool _failOnPrinterError;
        private readonly IRawLabelPrinter _printer;
        private readonly int _printerTimeoutMs;

        public PrintLabelStep(
            ILogger logger,
            string printerName,
            string serialVariableName,
            bool useManualSerialNumber,
            string manualSerialNumber,
            int copies,
            bool useQtProZplFormat,
            bool failOnPrinterError)
            : this(
                logger,
                printerName,
                serialVariableName,
                useManualSerialNumber,
                manualSerialNumber,
                copies,
                useQtProZplFormat,
                failOnPrinterError,
                new WindowsRawLabelPrinter(),
                DefaultPrinterTimeoutMs)
        {
        }

        internal PrintLabelStep(
            ILogger logger,
            string printerName,
            string serialVariableName,
            bool useManualSerialNumber,
            string manualSerialNumber,
            int copies,
            bool useQtProZplFormat,
            bool failOnPrinterError,
            IRawLabelPrinter printer,
            int printerTimeoutMs)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _printerName = printerName?.Trim() ?? string.Empty;
            _serialVariableName = string.IsNullOrWhiteSpace(serialVariableName)
                ? "SerialNumber"
                : serialVariableName.Trim();
            _useManualSerialNumber = useManualSerialNumber;
            _manualSerialNumber = manualSerialNumber?.Trim() ?? string.Empty;
            _copies = Math.Max(1, copies);
            _useQtProZplFormat = useQtProZplFormat;
            _failOnPrinterError = failOnPrinterError;
            _printer = printer ?? throw new ArgumentNullException(nameof(printer));
            _printerTimeoutMs = Math.Max(1, printerTimeoutMs);
        }

        public async Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResetDiagnostics(context);
            context.SetVariable("PrintLabel.Copies", _copies);
            context.SetVariable("PrintLabel.PrinterName", _printerName);
            context.SetVariable("PrintLabel.Language", _useQtProZplFormat ? "ZPL" : "EPL");
            context.SetVariable("PrintLabel.Template", _useQtProZplFormat ? "PswUpsBox8x2Pro" : "SerialOnlyEpl");

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
                if (!TryResolvePswUpsBox8x2ProSerial(serial, out var fullSerial, out var shortSerial, out var serialError))
                {
                    return Fail(context, 6, serialError);
                }

                var mac = BuildPswUpsBox8x2ProMac(shortSerial);
                var barcode = $"{PswUpsBox8x2ProDeviceType:D3}{shortSerial:D5}";
                singleLabel = BuildPswUpsBox8x2ProZpl(shortSerial, mac);
                language = "ZPL";
                logDescription =
                    $"этикеток {PswUpsBox8x2ProName}: serial={fullSerial}, SN={shortSerial:D5}, MAC={mac}, barcode={barcode}";

                context.SetVariable("PrintLabel.Template", "PswUpsBox8x2Pro");
                context.SetVariable("PrintLabel.FullSerial", fullSerial);
                context.SetVariable("PrintLabel.SerialShort", shortSerial);
                context.SetVariable("PrintLabel.DeviceName", PswUpsBox8x2ProName);
                context.SetVariable("PrintLabel.DeviceType", PswUpsBox8x2ProDeviceType);
                context.SetVariable("PrintLabel.Mac", mac);
                context.SetVariable("PrintLabel.Barcode", barcode);
            }
            else
            {
                singleLabel = BuildEpl(serial);
                language = "EPL";
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

            var printTask = Task.Run(
                () => _printer.Print(_printerName, bytes),
                CancellationToken.None);
            var timeoutTask = Task.Delay(_printerTimeoutMs, cancellationToken);
            var completed = await Task.WhenAny(printTask, timeoutTask).ConfigureAwait(false);

            if (completed != printTask)
            {
                ObserveLatePrintTask(printTask);
                cancellationToken.ThrowIfCancellationRequested();
                context.SetVariable("PrintLabel.TimedOut", true);
                return Fail(context, 5, $"Принтер не ответил за {_printerTimeoutMs} мс.");
            }

            RawLabelPrintResult printResult;
            try
            {
                printResult = await printTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Fail(context, 3, ex.Message);
            }

            if (!printResult.Success)
            {
                return Fail(context, printResult.ErrorCode, printResult.Error);
            }

            context.SetVariable("PrintLabel.Success", true);
            context.SetVariable("PrintLabel.ErrorCode", 0);
            context.SetVariable("PrintLabel.Error", string.Empty);
            _logger.Info($"[OK] В очередь принтера '{_printerName}' отправлено этикеток: {_copies}.");
            return StepResult.True;
        }

        internal static string BuildEpl(string serial)
        {
            var barcodeWidth = CalculateBarcodeWidth(serial);
            var barcodeX = Math.Max(0, LabelWidthDots / 2 - barcodeWidth / 2);
            var textWidth = 20 * serial.Length;
            var textX = Math.Max(0, LabelWidthDots / 2 - textWidth / 2);

            return string.Concat(
                "\r\n",
                "N\r\n",
                $"q{LabelWidthDots}\r\n",
                "I8,C,001\r\n",
                $"B{barcodeX},24,0,1,2,1,47,N,\"{serial}\"\r\n",
                $"A{textX},94,0,3,1,1,N,\"{serial}\"\r\n",
                "P1,1\r\n");
        }

        internal static string BuildPswUpsBox8x2ProZpl(int shortSerial, string mac)
        {
            if (shortSerial is < 0 or > 0xFFFF)
            {
                throw new ArgumentOutOfRangeException(nameof(shortSerial), "Короткий серийный номер должен быть в диапазоне 0..65535.");
            }

            var nameOffset = (int)(640 - PswUpsBox8x2ProName.Length * 9 * 0.9);
            var barcode = $"{PswUpsBox8x2ProDeviceType:D3}{shortSerial:D5}";

            return string.Concat(
                "^XA^MD10^FO",
                nameOffset.ToString(CultureInfo.InvariantCulture),
                ",35^A0,36,25^FD",
                PswUpsBox8x2ProName,
                "^FS^FO510,70^A0,25,20^FDMAC: ",
                mac,
                "^FS^FO510,95^A0,25,20^FDSN: ",
                shortSerial.ToString("D5", CultureInfo.InvariantCulture),
                "^FS^FO510,117^BY2^BCN,50,N,N,N^FD>:",
                barcode,
                "^FS^XZ ");
        }

        private static bool TryResolvePswUpsBox8x2ProSerial(
            string serial,
            out int fullSerial,
            out int shortSerial,
            out string error)
        {
            fullSerial = 0;
            shortSerial = 0;
            error = string.Empty;

            if (!int.TryParse(serial, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                error = $"Серийный номер '{serial}' слишком большой или имеет неверный формат.";
                return false;
            }

            if (parsed <= 0xFFFF)
            {
                shortSerial = parsed;
                fullSerial = PswUpsBox8x2ProSerialOffset + shortSerial;
                return true;
            }

            shortSerial = parsed - PswUpsBox8x2ProSerialOffset;
            if (shortSerial is < 0 or > 0xFFFF)
            {
                error =
                    $"Для {PswUpsBox8x2ProName} введите полный серийник " +
                    $"{PswUpsBox8x2ProSerialOffset}..{PswUpsBox8x2ProSerialOffset + 0xFFFF} " +
                    "или короткий номер 0..65535.";
                return false;
            }

            fullSerial = parsed;
            return true;
        }

        private static string BuildPswUpsBox8x2ProMac(int shortSerial)
        {
            var high = (shortSerial >> 8) & 0xFF;
            var low = shortSerial & 0xFF;
            return $"C0:11:A6:{PswUpsBox8x2ProDeviceType:X2}:{high:X2}:{low:X2}";
        }

        private static int CalculateBarcodeWidth(string serial)
        {
            var baseWidth = serial.Length switch
            {
                12 or 14 => 17.0,
                19 => 16.3,
                15 => 17.7,
                20 => 15.0,
                _ => 20.0
            };
            var nonDigits = serial.Count(character => !char.IsDigit(character));
            if (serial.Length == 14 && nonDigits == 8)
            {
                return 370;
            }

            return (int)(serial.Length * baseWidth + nonDigits * 1.7 * baseWidth);
        }

        private StepResult Fail(TestContext context, int errorCode, string error)
        {
            context.SetVariable("PrintLabel.Success", false);
            context.SetVariable("PrintLabel.ErrorCode", errorCode);
            context.SetVariable("PrintLabel.Error", error);
            _logger.Warning($"[ОШИБКА] Этикетки не напечатаны: {error}");
            return _failOnPrinterError ? StepResult.False : StepResult.True;
        }

        private static void ResetDiagnostics(TestContext context)
        {
            context.SetVariable("PrintLabel.Copies", 0);
            context.SetVariable("PrintLabel.PrinterName", string.Empty);
            context.SetVariable("PrintLabel.Serial", string.Empty);
            context.SetVariable("PrintLabel.SerialSource", string.Empty);
            context.SetVariable("PrintLabel.Template", string.Empty);
            context.SetVariable("PrintLabel.FullSerial", 0);
            context.SetVariable("PrintLabel.SerialShort", 0);
            context.SetVariable("PrintLabel.DeviceName", string.Empty);
            context.SetVariable("PrintLabel.DeviceType", 0);
            context.SetVariable("PrintLabel.Mac", string.Empty);
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

    internal sealed class WindowsRawLabelPrinter : IRawLabelPrinter
    {
        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DocInfo1 docInfo);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, byte[] bytes, int count, out int written);

        public RawLabelPrintResult Print(string printerName, byte[] bytes)
        {
            if (!OperatingSystem.IsWindows())
            {
                return RawLabelPrintResult.Fail(3, "RAW-печать поддерживается только в Windows.");
            }

            if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
            {
                var win32Error = Marshal.GetLastWin32Error();
                return RawLabelPrintResult.Fail(3, $"Не удалось открыть принтер '{printerName}' (Win32 {win32Error}).");
            }

            try
            {
                var document = new DocInfo1
                {
                    DocumentName = "TFortis labels",
                    DataType = "RAW"
                };

                if (!StartDocPrinter(printer, 1, document))
                {
                    return RawLabelPrintResult.Fail(2, $"Не удалось создать задание печати (Win32 {Marshal.GetLastWin32Error()}).");
                }

                try
                {
                    if (!StartPagePrinter(printer))
                    {
                        return RawLabelPrintResult.Fail(1, $"Не удалось начать RAW-страницу (Win32 {Marshal.GetLastWin32Error()}).");
                    }

                    try
                    {
                        if (!WritePrinter(printer, bytes, bytes.Length, out var written))
                        {
                            return RawLabelPrintResult.Fail(4, $"WritePrinter завершился ошибкой Win32 {Marshal.GetLastWin32Error()}.");
                        }

                        if (written != bytes.Length)
                        {
                            return RawLabelPrintResult.Fail(4, $"Принтер принял {written} из {bytes.Length} байт.");
                        }
                    }
                    finally
                    {
                        EndPagePrinter(printer);
                    }
                }
                finally
                {
                    EndDocPrinter(printer);
                }

                return RawLabelPrintResult.Ok();
            }
            finally
            {
                ClosePrinter(printer);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class DocInfo1
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string DocumentName = string.Empty;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string? OutputFile;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string DataType = string.Empty;
        }
    }
}
