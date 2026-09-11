using System.Text;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class PrintLabelStepTests
{
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
            "\r\nN\r\nq354\r\nI8,C,001\r\n" +
            "B107,24,0,1,2,1,47,N,\"3200428\"\r\n" +
            "A107,94,0,3,1,1,N,\"3200428\"\r\n" +
            "P1,1\r\n";
        Assert.Equal(StepResult.True, result);
        Assert.Equal(1, printer.Calls);
        Assert.Equal("TSC TE310", printer.PrinterName);
        Assert.Equal(string.Concat(Enumerable.Repeat(singleLabel, 4)), printer.GetText());
        Assert.Equal(4, CountOccurrences(printer.GetText(), "P1,1"));
        Assert.Equal(4, context.GetVariable<int>("PrintLabel.Copies"));
        Assert.Equal("3200428", context.GetVariable<string>("PrintLabel.Serial"));
        Assert.Equal("EPL", context.GetVariable<string>("PrintLabel.Language"));
        Assert.True(context.GetVariable<bool>("PrintLabel.Success"));
        Assert.False(context.GetVariable<bool>("PrintLabel.TimedOut"));
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

    private static PrintLabelStep CreateStep(
        IRawLabelPrinter printer,
        int copies,
        int timeoutMs = 1000) =>
        new(
            NullLogger.Instance,
            "TSC TE310",
            "SerialNumber",
            copies,
            failOnPrinterError: true,
            printer,
            timeoutMs);

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

        public RawLabelPrintResult Print(string printerName, byte[] bytes)
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

        public RawLabelPrintResult Print(string printerName, byte[] bytes)
        {
            Thread.Sleep(_delay);
            return RawLabelPrintResult.Ok();
        }
    }
}
