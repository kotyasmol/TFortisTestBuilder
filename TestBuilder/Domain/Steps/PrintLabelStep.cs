using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Domain.Steps
{
    public sealed class PrintLabelStep : ITestStep
    {
        private readonly ILogger _logger;
        private readonly string _printerName;
        private readonly string _deviceName;
        private readonly int _deviceType;
        private readonly string _serialVariableName;
        private readonly string _macVariableName;
        private readonly int _copies;
        private readonly bool _includeMac;
        private readonly bool _equipmentFieldUse;
        private readonly int _equipmentType;
        private readonly string _equipmentText;
        private readonly bool _failOnPrinterError;

        public PrintLabelStep(
            ILogger logger,
            string printerName,
            string deviceName,
            int deviceType,
            string serialVariableName,
            string macVariableName,
            int copies,
            bool includeMac,
            bool equipmentFieldUse,
            int equipmentType,
            string equipmentText,
            bool failOnPrinterError)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _printerName = printerName?.Trim() ?? string.Empty;
            _deviceName = deviceName?.Trim() ?? string.Empty;
            _deviceType = deviceType;
            _serialVariableName = string.IsNullOrWhiteSpace(serialVariableName) ? "SerialShort" : serialVariableName.Trim();
            _macVariableName = string.IsNullOrWhiteSpace(macVariableName) ? "Dut.NewMac" : macVariableName.Trim();
            _copies = Math.Max(1, copies);
            _includeMac = includeMac;
            _equipmentFieldUse = equipmentFieldUse;
            _equipmentType = equipmentType;
            _equipmentText = equipmentText ?? string.Empty;
            _failOnPrinterError = failOnPrinterError;
        }

        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var serial = GetVariableText(context, _serialVariableName);
            var mac = _includeMac ? GetVariableText(context, _macVariableName) : string.Empty;
            var zpl = BuildZpl(serial, mac);
            var fullZpl = Repeat(zpl, _copies);

            context.SetVariable("PrintLabel.Copies", _copies);
            context.SetVariable("PrintLabel.PrinterName", _printerName);
            context.SetVariable("PrintLabel.Zpl", fullZpl);

            if (string.IsNullOrWhiteSpace(_printerName))
            {
                return Task.FromResult(Fail(context, 3, "Имя принтера не задано."));
            }

            try
            {
                var bytes = Encoding.ASCII.GetBytes(fullZpl);

                if (!RawPrinterHelper.SendBytesToPrinter(_printerName, bytes, out var errorCode, out var error))
                {
                    return Task.FromResult(Fail(context, errorCode, error));
                }

                context.SetVariable("PrintLabel.Success", true);
                context.SetVariable("PrintLabel.ErrorCode", 0);
                context.SetVariable("PrintLabel.Error", string.Empty);
                _logger.Info($"[OK] Этикетка отправлена на принтер '{_printerName}', копий {_copies}.");
                return Task.FromResult(StepResult.True);
            }
            catch (Exception ex)
            {
                return Task.FromResult(Fail(context, 3, ex.Message));
            }
        }

        private string BuildZpl(string serial, string mac)
        {
            var sn = _equipmentFieldUse
                ? $"{serial}-{_equipmentType}{_equipmentText}"
                : serial;
            var barcode = _includeMac
                ? $"{_deviceType};{sn};{mac}"
                : $"{_deviceType};{sn}";

            var builder = new StringBuilder();
            builder.AppendLine("^XA");
            builder.AppendLine("^PW600");
            builder.AppendLine("^LL320");
            builder.AppendLine($"^FO30,30^A0N,34,34^FD{EscapeZpl(_deviceName)}^FS");
            builder.AppendLine($"^FO30,85^A0N,28,28^FDSN: {EscapeZpl(sn)}^FS");

            if (_includeMac)
            {
                builder.AppendLine($"^FO30,125^A0N,28,28^FDMAC: {EscapeZpl(mac)}^FS");
            }

            builder.AppendLine($"^FO30,180^BY2^BCN,80,Y,N,N^FD{EscapeZpl(barcode)}^FS");
            builder.AppendLine("^XZ");
            return builder.ToString();
        }

        private StepResult Fail(TestContext context, int errorCode, string error)
        {
            context.SetVariable("PrintLabel.Success", false);
            context.SetVariable("PrintLabel.ErrorCode", errorCode);
            context.SetVariable("PrintLabel.Error", error);
            _logger.Warning($"[ОШИБКА] Этикетка не напечатана: {error}");
            return _failOnPrinterError ? StepResult.False : StepResult.True;
        }

        private static string GetVariableText(TestContext context, string variableName)
        {
            return context.Variables.TryGetValue(variableName, out var value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
        }

        private static string Repeat(string value, int count)
        {
            var builder = new StringBuilder();

            for (var i = 0; i < count; i++)
            {
                builder.Append(value);
            }

            return builder.ToString();
        }

        private static string EscapeZpl(string value)
        {
            return value.Replace("^", string.Empty).Replace("~", string.Empty);
        }

        private static class RawPrinterHelper
        {
            [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

            [DllImport("winspool.drv", SetLastError = true)]
            private static extern bool ClosePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOC_INFO_1 docInfo);

            [DllImport("winspool.drv", SetLastError = true)]
            private static extern bool EndDocPrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", SetLastError = true)]
            private static extern bool StartPagePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", SetLastError = true)]
            private static extern bool EndPagePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", SetLastError = true)]
            private static extern bool WritePrinter(IntPtr hPrinter, byte[] bytes, int count, out int written);

            public static bool SendBytesToPrinter(string printerName, byte[] bytes, out int errorCode, out string error)
            {
                errorCode = 0;
                error = string.Empty;

                if (!OperatingSystem.IsWindows())
                {
                    errorCode = 3;
                    error = "RAW-печать поддерживается только в Windows.";
                    return false;
                }

                if (!OpenPrinter(printerName, out var printer, IntPtr.Zero))
                {
                    errorCode = 3;
                    error = $"could not open printer: {Marshal.GetLastWin32Error()}";
                    return false;
                }

                try
                {
                    var doc = new DOC_INFO_1
                    {
                        pDocName = "TFortis label",
                        pDataType = "RAW"
                    };

                    if (!StartDocPrinter(printer, 1, doc))
                    {
                        errorCode = 2;
                        error = $"couldn't create job: {Marshal.GetLastWin32Error()}";
                        return false;
                    }

                    try
                    {
                        if (!StartPagePrinter(printer))
                        {
                            errorCode = 1;
                            error = $"could not start printer: {Marshal.GetLastWin32Error()}";
                            return false;
                        }

                        try
                        {
                            if (!WritePrinter(printer, bytes, bytes.Length, out var written) || written != bytes.Length)
                            {
                                errorCode = 4;
                                error = $"wrong number of bytes: {written}/{bytes.Length}";
                                return false;
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

                    return true;
                }
                finally
                {
                    ClosePrinter(printer);
                }
            }

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private sealed class DOC_INFO_1
            {
                public string pDocName = string.Empty;
                public string pOutputFile = string.Empty;
                public string pDataType = string.Empty;
            }
        }
    }
}
