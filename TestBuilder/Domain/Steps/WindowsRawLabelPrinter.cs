using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace TestBuilder.Domain.Steps;

internal readonly record struct PrinterQueueState(uint Status, uint Attributes);
internal readonly record struct PrinterJobState(uint Status, string Description);

internal interface IRawPrinterSession : IDisposable
{
    PrinterQueueState ReadPrinter();
    uint StartDocument();
    void RetainJob(uint jobId);
    void Write(byte[] bytes);
    void EndDocument();
    PrinterJobState? ReadJob(uint jobId);
    void ReleaseJob(uint jobId);
    void DeleteJob(uint jobId);
}

internal sealed class WindowsRawLabelPrinter : IRawLabelPrinter
{
    internal static WindowsRawLabelPrinter Shared { get; } = new();
    private const uint Completed = 0x80 | 0x1000;
    private const uint Deleted = 0x100;
    private readonly ConcurrentDictionary<string, UncertainJob> _uncertainJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly record struct UncertainJob(uint Id, string CleanupError);
    private readonly Func<string, IRawPrinterSession> _open;
    private readonly int _pollMs;
    public WindowsRawLabelPrinter() : this(name => new NativePrinterSession(name)) { }
    internal WindowsRawLabelPrinter(Func<string, IRawPrinterSession> open, int pollMs = 100)
    {
        _open = open;
        _pollMs = Math.Max(1, pollMs);
    }

    public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken cancellationToken)
    {
        IRawPrinterSession? session = null;
        uint jobId = 0;
        _uncertainJobs.TryGetValue(printerName, out var previous);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            session = _open(printerName);
            cancellationToken.ThrowIfCancellationRequested();
            if (previous.Id != 0)
            {
                var previousStatus = session.ReadJob(previous.Id);
                if (previousStatus != null && (previousStatus.Value.Status & (Completed | Deleted)) == 0)
                    return new(false, 7, $"{previous.CleanupError}. Проверьте очередь печати.", previous.Id, "Unknown");
                _uncertainJobs.TryRemove(printerName, out _);
                previous = default;
            }
            CheckPrinter(session.ReadPrinter());
            cancellationToken.ThrowIfCancellationRequested();
            jobId = session.StartDocument();
            cancellationToken.ThrowIfCancellationRequested();
            session.RetainJob(jobId);
            cancellationToken.ThrowIfCancellationRequested();
            session.Write(bytes);
            cancellationToken.ThrowIfCancellationRequested();
            session.EndDocument();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckPrinter(session.ReadPrinter());
                var job = session.ReadJob(jobId);
                if (job == null)
                    throw new InvalidOperationException($"Задание {jobId} исчезло без подтверждения печати.");
                // Retained jobs cannot disappear between polls after successful printing.
                const uint failures = 0x1 | 0x2 | 0x4 | 0x20 | 0x40 | 0x100 | 0x200;
                if ((job.Value.Status & failures) != 0)
                    throw new InvalidOperationException($"Задание {jobId}: ошибка/пауза/отмена (0x{job.Value.Status:X}); {job.Value.Description}");
                if ((job.Value.Status & Completed) != 0)
                {
                    // TSC's port monitor reports COMPLETE (sent), not PRINTED.
                    // Keep this distinction visible; neither is an optical check of a label.
                    var completion = (job.Value.Status & 0x80) != 0 ? "Printed" : "SentToPrinter";
                    session.ReleaseJob(jobId);
                    return new RawLabelPrintResult(true, 0, string.Empty, jobId, completion);
                }
                cancellationToken.WaitHandle.WaitOne(_pollMs);
            }
        }
        catch (OperationCanceledException)
        {
            if (session != null && jobId != 0)
            {
                var cleanup = TryDeleteJob(printerName, session, jobId);
                if (cleanup != null)
                    throw new OperationCanceledException(cleanup.Message, cleanup, cancellationToken);
            }
            throw;
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (previous.Id != 0)
                return new(false, 7, $"Не удалось проверить прежнее задание {previous.Id}: {message}", previous.Id, "Unknown");
            var status = string.Empty;
            if (session != null && jobId != 0)
            {
                var cleanup = TryDeleteJob(printerName, session, jobId);
                if (cleanup != null)
                {
                    message += $" {cleanup.Message}";
                    status = "Unknown";
                }
            }
            return new RawLabelPrintResult(false, 3, message, jobId, status);
        }
        finally { session?.Dispose(); }
    }

    private Exception? TryDeleteJob(string printerName, IRawPrinterSession session, uint jobId)
    {
        try
        {
            session.DeleteJob(jobId);
            return null;
        }
        catch (Exception error)
        {
            var message = $"Не удалось подтвердить отмену задания {jobId}: {error.Message}";
            _uncertainJobs[printerName] = new(jobId, message);
            return new InvalidOperationException(message, error);
        }
    }

    private static void CheckPrinter(PrinterQueueState state)
    {
        if ((state.Attributes & 0x400) != 0)
            throw new InvalidOperationException("Принтер в режиме «Работать автономно». Включите принтер и проверьте USB-подключение.");
        const uint errors = 0x1 | 0x2 | 0x8 | 0x10 | 0x80 | 0x800 | 0x1000 | 0x40000 | 0x100000 | 0x400000;
        if ((state.Status & errors) != 0)
            throw new InvalidOperationException($"Принтер недоступен, приостановлен или требует вмешательства (статус 0x{state.Status:X}).");
    }
}

internal sealed class NativePrinterSession : IRawPrinterSession
{
    private IntPtr _handle;
    private bool _documentOpen;
    internal NativePrinterSession(string name)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("RAW-печать поддерживается только в Windows.");
        Check(OpenPrinter(name, out _handle, IntPtr.Zero), "OpenPrinter");
    }

    public PrinterQueueState ReadPrinter()
    {
        GetPrinter(_handle, 2, IntPtr.Zero, 0, out var needed);
        if (needed == 0) throw Error("GetPrinter");
        var buffer = Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            Check(GetPrinter(_handle, 2, buffer, needed, out _), "GetPrinter");
            var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
            return new PrinterQueueState(info.Status, info.Attributes);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public uint StartDocument()
    {
        var id = StartDocPrinter(_handle, 1, new DocInfo1 { DocumentName = "TFortis labels", DataType = "RAW" });
        if (id == 0) throw Error("StartDocPrinter");
        _documentOpen = true;
        return id;
    }

    public void RetainJob(uint id) => Check(SetJob(_handle, id, 0, IntPtr.Zero, 8), "SetJob(RETAIN)");

    public void Write(byte[] bytes)
    {
        Check(StartPagePrinter(_handle), "StartPagePrinter");
        Check(WritePrinter(_handle, bytes, bytes.Length, out var written), "WritePrinter");
        if (written != bytes.Length) throw new InvalidOperationException($"WritePrinter принял {written} из {bytes.Length} байт.");
        Check(EndPagePrinter(_handle), "EndPagePrinter");
    }

    public void EndDocument()
    {
        Check(EndDocPrinter(_handle), "EndDocPrinter");
        _documentOpen = false;
    }

    public PrinterJobState? ReadJob(uint id)
    {
        GetJob(_handle, id, 1, IntPtr.Zero, 0, out var needed);
        if (needed == 0)
        {
            if (Marshal.GetLastWin32Error() == 87) return null;
            throw Error("GetJob");
        }
        var buffer = Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            if (!GetJob(_handle, id, 1, buffer, needed, out _))
            {
                if (Marshal.GetLastWin32Error() == 87) return null;
                throw Error("GetJob");
            }
            var info = Marshal.PtrToStructure<JobInfo1>(buffer);
            return new PrinterJobState(info.Status, Marshal.PtrToStringUni(info.StatusText) ?? string.Empty);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void ReleaseJob(uint id) => Check(SetJob(_handle, id, 0, IntPtr.Zero, 9), "SetJob(RELEASE)");
    public void DeleteJob(uint id)
    {
        if (_documentOpen && AbortPrinter(_handle)) _documentOpen = false;
        if (!SetJob(_handle, id, 0, IntPtr.Zero, 5))
        {
            var deleteError = Error("SetJob(DELETE)");
            if (deleteError.NativeErrorCode == 87 && ReadJob(id) == null) return;
            throw deleteError;
        }
        // A retained completed job can stay in DELETING until retention is released.
        // Cancel first, so releasing cannot restart a pending print job.
        if (!SetJob(_handle, id, 0, IntPtr.Zero, 9) && Marshal.GetLastWin32Error() != 87)
            throw Error("SetJob(RELEASE after DELETE)");

        var deadline = Environment.TickCount64 + 2000;
        while (ReadJob(id) is { } state && (state.Status & 0x100) == 0)
        {
            if (Environment.TickCount64 >= deadline)
                throw new InvalidOperationException($"Задание {id} всё ещё в очереди Windows.");
            Thread.Sleep(100);
        }
    }
    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        if (_documentOpen) AbortPrinter(_handle);
        ClosePrinter(_handle);
        _handle = IntPtr.Zero;
    }
    private static void Check(bool success, string operation) { if (!success) throw Error(operation); }
    private static Win32Exception Error(string operation)
    {
        var code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, $"{operation}: Win32 {code}: {new Win32Exception(code).Message}");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string DocumentName = string.Empty;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string DataType = string.Empty;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo2
    {
        public IntPtr ServerName, PrinterName, ShareName, PortName, DriverName, Comment, Location;
        public IntPtr DevMode, SepFile, PrintProcessor, DataType, Parameters, SecurityDescriptor;
        public uint Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status, Jobs, AveragePpm;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobInfo1
    {
        public uint JobId;
        public IntPtr PrinterName, MachineName, UserName, Document, DataType, StatusText;
        public uint Status, Priority, Position, TotalPages, PagesPrinted;
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool AbortPrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetPrinter(IntPtr handle, uint level, IntPtr info, uint size, out uint needed);
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint StartDocPrinter(IntPtr handle, int level, [In] DocInfo1 info);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] bytes, int count, out int written);
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetJob(IntPtr handle, uint id, uint level, IntPtr info, uint size, out uint needed);
    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetJob(IntPtr handle, uint id, uint level, IntPtr info, uint command);
}
