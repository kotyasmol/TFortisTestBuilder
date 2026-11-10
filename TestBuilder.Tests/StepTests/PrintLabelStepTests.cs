using System.Text;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class PrintLabelStepTests
{
    [Theory]
    [InlineData("Profile product", true)]
    [InlineData("Bad^XZ", false)]
    public async Task DeviceNameComesFromContext_AndCannotInjectCommands(string name, bool valid)
    {
        var context = new TestContext(new RegisterState()) { DeviceModel = name, ProfileName = "Run without SFP" };
        Assert.Equal(name, context.CreateParallelBranch(CancellationToken.None).DeviceModel);
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var step = CreateStep(printer, 1, manualSerial: "3200428", manualMacAddress: "C0:11:A6:20:01:AC", useQtProZplFormat: true);
        Assert.Equal(valid ? StepResult.True : StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(valid ? 1 : 0, printer.Calls);
        if (valid)
        {
            Assert.Contains("^FDProfile product^FS", Encoding.UTF8.GetString(printer.Bytes));
            Assert.DoesNotContain("Run without SFP", Encoding.UTF8.GetString(printer.Bytes));
        }
    }

    [Fact]
    public async Task DriverCompleteKeepsHonestCompletionStatusInReport()
    {
        var context = new TestContext(new RegisterState());
        var printer = new CapturingPrinter(new RawLabelPrintResult(true, 0, "", 42, "SentToPrinter"));
        Assert.Equal(StepResult.True, await CreateStep(printer, 1, manualSerial: "612447")
            .ExecuteAsync(context, CancellationToken.None));
        Assert.Equal("SentToPrinter", context.GetVariable<string>("PrintLabel.CompletionStatus"));
        Assert.Contains("драйвер не подтверждает", Assert.Single(context.ReportEntries).Value);
    }

    [Fact]
    public async Task TimeoutCancelsBackgroundPrinterWork()
    {
        var context = new TestContext(new RegisterState());
        var printer = new CancellablePrinter();
        Assert.Equal(StepResult.False, await CreateStep(printer, 1, 50, manualSerial: "612447")
            .ExecuteAsync(context, CancellationToken.None));
        Assert.True(await printer.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(Assert.Single(context.ReportEntries).IsSuccess);
        Assert.Equal("Unknown", context.GetVariable<string>("PrintLabel.CompletionStatus"));
    }

    [Fact]
    public async Task ExternalCancellationIsPropagatedAndCancelsPrinterWork()
    {
        var context = new TestContext(new RegisterState());
        var printer = new CancellablePrinter();
        using var stop = new CancellationTokenSource();
        var task = CreateStep(printer, 1, 5000, manualSerial: "612447").ExecuteAsync(context, stop.Token);
        await printer.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(await printer.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task RetryCannotSubmitToQueueWhileTimedOutDriverIsStillRunning()
    {
        using var printer = new BlockedPrinter();
        var first = new TestContext(new RegisterState());
        var pending = CreateStep(printer, 1, 50, manualSerial: "612447").ExecuteAsync(first, default);
        try
        {
            await printer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(StepResult.False, await pending.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("Unknown", first.GetVariable<string>("PrintLabel.CompletionStatus"));

            var retry = new TestContext(new RegisterState());
            Assert.Equal(StepResult.False, await CreateStep(printer, 1, manualSerial: "612447").ExecuteAsync(retry, default));
            Assert.Equal("Pending", retry.GetVariable<string>("PrintLabel.CompletionStatus"));
            Assert.Equal(1, printer.Calls);
        }
        finally
        {
            printer.Release.Set();
            await printer.Exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class BlockedPrinter : IRawLabelPrinter, IDisposable
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public int Calls { get; private set; }

        public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token)
        {
            Calls++;
            Entered.SetResult(true);
            try { Release.Wait(); }
            finally { Exited.SetResult(true); }
            return RawLabelPrintResult.Ok();
        }

        public void Dispose() => Release.Dispose();
    }

    private sealed class CancellablePrinter : IRawLabelPrinter
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token)
        {
            Started.TrySetResult(true);
            token.WaitHandle.WaitOne();
            Cancelled.TrySetResult(true);
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException();
        }
    }

    public PrintLabelStepTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_PrintsFullSerialAsTextAndCode128FourTimes()
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 3200428);
        var step = CreateStep(printer, copies: 4);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        const string singleLabel =
            "\r\nN\r\nZB\r\nq354\r\nI8,C,001\r\n" +
            "B65,24,0,1B,2,1,47,N,\"3200428\"\r\n" +
            "A107,94,0,3,1,1,N,\"3200428\"\r\n" +
            "P1,1\r\n";
        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, printer.Calls);
        Assert.Equal("TSC TE310", printer.PrinterName);
        Assert.Equal(string.Concat(Enumerable.Repeat(singleLabel, 4)), printer.GetText());
        Assert.Equal(4, CountOccurrences(printer.GetText(), "P1,1"));
        Assert.Equal(4, context.GetVariable<int>("PrintLabel.Copies"));
        Assert.Equal("3200428", context.GetVariable<string>("PrintLabel.Serial"));
        Assert.Equal("SerialNumber", context.GetVariable<string>("PrintLabel.SerialSource"));
        Assert.Equal("EPL", context.GetVariable<string>("PrintLabel.Language"));
        Assert.True(context.GetVariable<bool>("PrintLabel.Success"));
        Assert.False(context.GetVariable<bool>("PrintLabel.TimedOut"));
    }

    [Fact]
    public async Task ExecuteAsync_PrintsManualSerialWithoutContextVariable()
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        var step = CreateStep(printer, copies: 4, manualSerial: "3200999");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, printer.Calls);
        Assert.Equal(8, CountOccurrences(printer.GetText(), "\"3200999\""));
        Assert.Equal(4, CountOccurrences(printer.GetText(), "P1,1"));
        Assert.Equal("3200999", context.GetVariable<string>("PrintLabel.Serial"));
        Assert.Equal("Manual", context.GetVariable<string>("PrintLabel.SerialSource"));
    }

    [Theory]
    [InlineData("3200428")]
    [InlineData("00428")]
    public async Task ExecuteAsync_PrintsQtProZplLabelFourTimes(string serial)
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        var step = CreateStep(
            printer,
            copies: 4,
            manualSerial: serial,
            manualMacAddress: "C0:11:A6:20:01:AC",
            useQtProZplFormat: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        const string singleLabel =
            "^XA^MD10^FO486,22^A0N,36,24^FDPSW+UPS-Box 8x2Pro^FS" +
            "^FO486,59^A0N,25,20^FDMAC: C0:11:A6:20:01:AC^FS" +
            "^FO486,85^A0N,25,20^FDSN: 3200428^FS" +
            "^FO524,112^BY2^BCN,34,N,N,N^FD>;00320042>68^FS^XZ ";
        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, printer.Calls);
        Assert.Equal(string.Concat(Enumerable.Repeat(singleLabel, 4)), printer.GetText());
        Assert.Equal(4, CountOccurrences(printer.GetText(), "^XZ"));
        Assert.Equal("ZPL", context.GetVariable<string>("PrintLabel.Language"));
        Assert.Equal("PswUpsBox8x2Pro", context.GetVariable<string>("PrintLabel.Template"));
        Assert.Equal(3200428, context.GetVariable<int>("PrintLabel.FullSerial"));
        Assert.Equal(428, context.GetVariable<int>("PrintLabel.SerialShort"));
        Assert.Equal("C0:11:A6:20:01:AC", context.GetVariable<string>("PrintLabel.Mac"));
        Assert.Equal("003200428", context.GetVariable<string>("PrintLabel.Barcode"));
        Assert.Equal(string.Empty, context.GetVariable<string>("PrintLabel.Epl"));
        Assert.Equal(string.Concat(Enumerable.Repeat(singleLabel, 4)), context.GetVariable<string>("PrintLabel.Zpl"));
    }

    [Fact]
    public async Task ExecuteAsync_PrintsQtProValuesObtainedByPreviousTestSteps()
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 3200428);
        context.SetVariable("SerialShort", 428);
        context.SetVariable("Dut.default_mac", "c0-11-a6-20-01-ac");
        context.SetVariable("SetMac.Timestamp", 1790000123L);
        var step = CreateStep(printer, copies: 4, useQtProZplFormat: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.True, result);
        Assert.Contains("MAC: C0:11:A6:20:01:AC", printer.GetText());
        Assert.Contains("SN: 3200428", printer.GetText());
        Assert.Equal("Dut.default_mac", context.GetVariable<string>("PrintLabel.MacSource"));
        Assert.Equal("SerialShort", context.GetVariable<string>("PrintLabel.SerialShortSource"));
        Assert.DoesNotContain("1790000123", printer.GetText());
    }

    [Theory]
    [InlineData(241, 154, 2)]
    [InlineData(295, 145, 2)]
    [InlineData(295, 154, 3)]
    public async Task Code128OutsideThePaperOrQuietZoneNeverReachesThePrinter(int width, int height, int module)
    {
        var template = LabelTemplateCatalog.Defaults()[0];
        template.WidthDots = width;
        template.HeightDots = height;
        template.BarcodeModuleDots = module;
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        var step = new PrintLabelStep(NullLogger.Instance, "TSC TE310", "", "", "", true, "3200428",
            "C0:11:A6:20:01:AC", 1, true, true, printer, 30000, template: template);

        Assert.Contains("не помещается", LabelPreview.FromInput(template, "3200428", "C0:11:A6:20:01:AC").Message);
        Assert.Equal(StepResult.False, await step.ExecuteAsync(context, default));
        Assert.Contains("не помещается", context.GetVariable<string>("PrintLabel.Error"));
        Assert.Equal(0, printer.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotCalculateMissingQtProMac()
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 3200428);
        context.SetVariable("SerialShort", 428);
        var step = CreateStep(printer, copies: 4, useQtProZplFormat: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(0, printer.Calls);
        Assert.Contains("Dut.default_mac", context.GetVariable<string>("PrintLabel.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsQtProSerialOutsideMacRange()
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        var step = CreateStep(
            printer,
            copies: 4,
            manualSerial: "3299999",
            manualMacAddress: "C0:11:A6:20:01:AC",
            useQtProZplFormat: true);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(0, printer.Calls);
        Assert.Equal("ZPL", context.GetVariable<string>("PrintLabel.Language"));
        Assert.Contains("3200000..3265535", context.GetVariable<string>("PrintLabel.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNonNumericManualSerial()
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        var step = CreateStep(printer, copies: 4, manualSerial: "320A999");

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(0, printer.Calls);
        Assert.Contains("только цифры", context.GetVariable<string>("PrintLabel.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotPrintWhenSerialIsMissing()
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        var step = CreateStep(printer, copies: 4);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(0, printer.Calls);
        Assert.False(context.GetVariable<bool>("PrintLabel.Success"));
        Assert.Contains("SerialNumber", context.GetVariable<string>("PrintLabel.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsFalseWhenSpoolerRejectsJob()
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Fail(4, "write failed"));
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", "3200428");
        var step = CreateStep(printer, copies: 4);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.Equal(1, printer.Calls);
        Assert.Equal(4, context.GetVariable<int>("PrintLabel.ErrorCode"));
        Assert.Equal("write failed", context.GetVariable<string>("PrintLabel.Error"));
    }

    [Fact]
    public async Task ExecuteAsync_TimesOutWithoutBlockingCaller()
    {
        var printer = new DelayedPrinter(TimeSpan.FromMilliseconds(100));
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", 3200428);
        var step = CreateStep(printer, copies: 4, timeoutMs: 10);

        var result = await step.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(StepResult.False, result);
        Assert.True(context.GetVariable<bool>("PrintLabel.TimedOut"));
        Assert.Equal(5, context.GetVariable<int>("PrintLabel.ErrorCode"));
    }

    [Theory]
    [InlineData("600428")]
    [InlineData("00428")]
    public async Task Psw2G6FPlusPrintsFullSerialAndDocumentedBarcode(string serial)
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        var step = CreateStep(printer, 4, manualSerial: serial, manualMacAddress: "C0:11:A6:06:01:AC",
            useQtProZplFormat: true, labelModel: DeviceLabelModel.Psw2G6FPlus);
        Assert.Equal(StepResult.True, await step.ExecuteAsync(context, CancellationToken.None));
        const string expected = "^XA^MD10^FO486,22^A0N,36,24^FDPSW-2G6F+^FS" +
            "^FO486,59^A0N,25,20^FDMAC: C0:11:A6:06:01:AC^FS" +
            "^FO486,85^A0N,25,20^FDSN: 600428^FS" +
            "^FO524,112^BY2^BCN,34,N,N,N^FD>;00060042>68^FS^XZ ";
        Assert.Equal(string.Concat(Enumerable.Repeat(expected, 4)), printer.GetText());
        Assert.Equal(600428, context.GetVariable<int>("PrintLabel.FullSerial"));
        Assert.Equal(6, context.GetVariable<int>("PrintLabel.DeviceType"));
        Assert.Equal("Psw2G6FPlus", context.GetVariable<string>("PrintLabel.Template"));
    }

    [Theory]
    [InlineData(3300579, "C0:11:A6:21:02:43", true)]
    [InlineData(3200579, "C0:11:A6:20:02:43", false)]
    [InlineData(3300579, "C0:11:A6:21:02:44", false)]
    public async Task CatalogLabelUsesSelectedModelInsteadOfCopiedUpsTemplate(int serial, string mac, bool prints)
    {
        var model = new EquipmentModel("box-pro", "PSW+Box 8x2Pro")
        {
            LabelCode = 33,
            LabelVariants = new() { [1] = "DIN" },
            SerialSeries = new() { ServerDeviceType = "Box", FirstSerialNumber = 3300001,
                LastSerialNumber = 3365535, SerialOffset = 3300000, MacPrefix = "C0:11:A6:21" }
        };
        var context = new TestContext(new RegisterState()) { ProductionModel = model, LabelVariant = 1 };
        context.SetVariable("SerialNumber", serial);
        context.SetVariable("SerialShort", 42);
        context.SetVariable("Dut.default_mac", mac);
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var step = CreateStep(printer, copies: 1, useQtProZplFormat: true);

        Assert.Equal(prints ? StepResult.True : StepResult.False, await step.ExecuteAsync(context, default));
        Assert.Equal(prints ? 1 : 0, printer.Calls);
        if (prints)
        {
            Assert.Contains("^FDPSW+Box 8x2Pro^FS", printer.GetText());
            Assert.Contains("^FDSN: 3300579^FS", printer.GetText());
            Assert.DoesNotContain("TYPE:", printer.GetText());
            Assert.Contains("^FD>;10330057>69^FS", printer.GetText());
        }
    }

    [Theory]
    [InlineData(600428, true)]
    [InlineData(3200428, false)]
    [InlineData(665536, false)]
    public async Task Psw2G6FPlusValidatesContextSerialsBeforePrinting(int fullSerial, bool valid)
    {
        var printer = new CapturingPrinter(RawLabelPrintResult.Ok());
        var context = new TestContext(new RegisterState());
        context.SetVariable("SerialNumber", fullSerial);
        context.SetVariable("SerialShort", 428);
        context.SetVariable("Dut.default_mac", "C0:11:A6:06:01:AC");
        var step = CreateStep(printer, 4, useQtProZplFormat: true, labelModel: DeviceLabelModel.Psw2G6FPlus);
        Assert.Equal(valid ? StepResult.True : StepResult.False, await step.ExecuteAsync(context, CancellationToken.None));
        Assert.Equal(valid ? 1 : 0, printer.Calls);
    }

    private static PrintLabelStep CreateStep(
        IRawLabelPrinter printer,
        int copies,
        int timeoutMs = 1000,
        string manualSerial = "",
        string manualMacAddress = "",
        bool useQtProZplFormat = false,
        DeviceLabelModel labelModel = DeviceLabelModel.PswUpsBox8x2Pro) =>
        new(
            NullLogger.Instance,
            "TSC TE310",
            "SerialNumber",
            "SerialShort",
            "Dut.default_mac",
            !string.IsNullOrEmpty(manualSerial),
            manualSerial,
            manualMacAddress,
            copies,
            useQtProZplFormat,
            failOnPrinterError: true,
            printer,
            timeoutMs,
            labelModel);

    private static int CountOccurrences(string value, string needle) =>
        value.Split(needle, StringSplitOptions.None).Length - 1;

    private sealed class CapturingPrinter : IRawLabelPrinter
    {
        private readonly RawLabelPrintResult _result;

        public CapturingPrinter(RawLabelPrintResult result)
        {
            _result = result;
        }

        public int Calls { get; private set; }
        public string PrinterName { get; private set; } = string.Empty;
        public byte[] Bytes { get; private set; } = Array.Empty<byte>();

        public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken cancellationToken)
        {
            Calls++;
            PrinterName = printerName;
            Bytes = bytes.ToArray();
            return _result;
        }

        public string GetText() => Encoding.GetEncoding(1251).GetString(Bytes);
    }

    private sealed class DelayedPrinter : IRawLabelPrinter
    {
        private readonly TimeSpan _delay;

        public DelayedPrinter(TimeSpan delay)
        {
            _delay = delay;
        }

        public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken cancellationToken)
        {
            Thread.Sleep(_delay);
            return RawLabelPrintResult.Ok();
        }
    }
}
