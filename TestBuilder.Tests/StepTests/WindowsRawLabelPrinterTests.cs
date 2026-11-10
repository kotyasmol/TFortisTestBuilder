using TestBuilder.Domain.Steps;

namespace TestBuilder.Tests.StepTests;

public class WindowsRawLabelPrinterTests
{
    [Theory]
    [InlineData(0u, 0x400u)]
    [InlineData(0x80u, 0u)]
    [InlineData(0x10u, 0u)]
    public void OfflineOrPaperOutDoesNotEnqueue(uint status, uint attributes)
    {
        var session = new Session { Queue = new(status, attributes) };
        var result = Print(session);
        Assert.False(result.Success);
        Assert.False(session.Started);
        Assert.Empty(session.Deleted);
        Assert.True(session.Disposed);
    }

    [Fact]
    public void PrintingWaitsUntilCompletion()
    {
        var session = new Session();
        session.States.Enqueue(new(0x10, "printing"));
        session.States.Enqueue(new(0x2080, "printed and retained"));
        var result = Print(session);
        Assert.True(result.Success);
        Assert.Equal(42u, result.JobId);
        Assert.Equal("Printed", result.CompletionStatus);
        Assert.Equal(2, session.Reads);
        Assert.Equal(new[] { 42u }, session.Released);
        Assert.Empty(session.Deleted);
    }

    [Fact]
    public void DriverCompleteIsReportedAsSentRatherThanPrinted()
    {
        var session = new Session();
        session.States.Enqueue(new(0x3000, "complete and retained"));
        var result = Print(session);
        Assert.True(result.Success);
        Assert.Equal("SentToPrinter", result.CompletionStatus);
        Assert.Equal(new[] { 42u }, session.Released);
    }

    [Theory]
    [InlineData(0x2u)]
    [InlineData(0x20u)]
    [InlineData(0x40u)]
    [InlineData(0x100u)]
    public void JobFailureCancelsOnlyOwnedJob(uint status)
    {
        var session = new Session();
        session.States.Enqueue(new(status, "failure"));
        Assert.False(Print(session).Success);
        Assert.Equal(new[] { 42u }, session.Deleted);
        Assert.Empty(session.Released);
    }

    [Fact]
    public void DisappearedJobIsNotReportedAsPrinted()
    {
        var session = new Session();
        session.States.Enqueue(null);
        var result = Print(session);
        Assert.False(result.Success);
        Assert.Contains("без подтверждения", result.Error);
    }

    [Fact]
    public void EndDocumentFailureCancelsOwnedJob()
    {
        var session = new Session { EndError = true };
        Assert.False(Print(session).Success);
        Assert.Equal(new[] { 42u }, session.Deleted);
        Assert.True(session.Disposed);
    }

    [Fact]
    public void CancellationWhileWaitingDeletesOwnedJobAndClosesSession()
    {
        using var stop = new CancellationTokenSource();
        var session = new Session { OnRead = stop.Cancel };
        Assert.ThrowsAny<OperationCanceledException>(() =>
            new WindowsRawLabelPrinter(_ => session, 1).Print("test", new byte[] { 1 }, stop.Token));
        Assert.Equal(new[] { 42u }, session.Deleted);
        Assert.True(session.Disposed);
    }

    [Fact]
    public void FailedDeletionBlocksNextJobWhilePriorJobIsStillActive()
    {
        using var stop = new CancellationTokenSource();
        var first = new Session { OnRead = stop.Cancel, DeleteError = true };
        var retry = new Session();
        retry.States.Enqueue(new(0x10, "still printing"));
        var later = new Session();
        later.States.Enqueue(null);
        later.States.Enqueue(new(0x80, "printed"));
        var sessions = new Queue<Session>([first, retry, later]);
        var printer = new WindowsRawLabelPrinter(_ => sessions.Dequeue(), 1);

        var error = Assert.Throws<OperationCanceledException>(() => printer.Print("test", [1], stop.Token));
        Assert.Contains("Не удалось подтвердить отмену задания 42", error.Message);
        Assert.Equal(new[] { 42u }, first.Deleted);
        Assert.True(first.Disposed);
        var result = printer.Print("TEST", [1], default);
        Assert.False(result.Success);
        Assert.Equal("Unknown", result.CompletionStatus);
        Assert.Contains("Delete failed", result.Error);
        Assert.False(retry.Started);
        Assert.True(printer.Print("test", [1], default).Success);
    }

    [Fact]
    public void RetentionFailureCancelsTheJobThatWasAlreadyCreated()
    {
        var session = new Session { RetainError = true };
        Assert.False(Print(session).Success);
        Assert.Equal(new[] { 42u }, session.Deleted);
    }

    private static RawLabelPrintResult Print(Session session) =>
        new WindowsRawLabelPrinter(_ => session, 1).Print("test", new byte[] { 1 }, CancellationToken.None);

    private sealed class Session : IRawPrinterSession
    {
        public PrinterQueueState Queue;
        public Queue<PrinterJobState?> States { get; } = new();
        public List<uint> Deleted { get; } = new();
        public List<uint> Released { get; } = new();
        public Action? OnRead;
        public bool Started, Disposed, EndError, DeleteError, RetainError;
        public int Reads;
        public PrinterQueueState ReadPrinter() => Queue;
        public uint StartDocument() { Started = true; return 42; }
        public void RetainJob(uint id) { if (RetainError) throw new IOException("Retain failed"); }
        public void Write(byte[] bytes) { }
        public void EndDocument() { if (EndError) throw new IOException("EndDoc failed"); }
        public PrinterJobState? ReadJob(uint id)
        {
            Assert.Equal(42u, id);
            Reads++;
            OnRead?.Invoke();
            return States.Count > 0 ? States.Dequeue() : new PrinterJobState(0, "waiting");
        }
        public void ReleaseJob(uint id) => Released.Add(id);
        public void DeleteJob(uint id)
        {
            Deleted.Add(id);
            if (DeleteError) throw new IOException("Delete failed");
        }
        public void Dispose() => Disposed = true;
    }
}
