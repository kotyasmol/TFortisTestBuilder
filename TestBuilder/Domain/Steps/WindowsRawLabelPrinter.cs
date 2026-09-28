using System;
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
    void Write(byte[] bytes);
    void EndDocument();
    PrinterJobState? ReadJob(uint jobId);
    void ReleaseJob(uint jobId);
    void DeleteJob(uint jobId);
}

internal sealed class WindowsRawLabelPrinter : IRawLabelPrinter
{
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
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            session = _open(printerName);
            CheckPrinter(session.ReadPrinter());
            cancellationToken.ThrowIfCancellationRequested();
            jobId = session.StartDocument();
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
                if ((job.Value.Status & (0x80 | 0x1000)) != 0)
                {
                    // TSC's port monitor reports COMPLETE (sent), not PRINTED.
                    // Keep this distinction visible; neither is an optical check of a label.
                    var completion = (job.Value.Status & 0x80) != 0 ? "Printed" : "SentToPrinter";
                    session.ReleaseJob(jobId);
                    return new RawLabelPrintResult(true, 0, string.Empty, jobId, completion);
                }
                Thread.Sleep(_pollMs);
            }
        }
        catch (OperationCanceledException)
        {
            if (session != null && jobId != 0)
            {
                try { session.DeleteJob(jobId); } catch { /* Caller already reports cancellation/timeout. */ }
            }
            throw;
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (session != null && jobId != 0)
            {
                try { session.DeleteJob(jobId); }
                catch (Exception cleanup) { message += $" Не удалось отменить задание {jobId}: {cleanup.Message}"; }
            }
            return new RawLabelPrintResult(false, 3, message, jobId);
        }
        finally { session?.Dispose(); }
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
        // JOB_CONTROL_RETAIN: keep this job after printing so polling can observe
        // PRINTED. This changes neither the printer nor any other queued job.
        Check(SetJob(_handle, id, 0, IntPtr.Zero, 8), "SetJob(RETAIN)");
        return id;
    }

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
        Check(SetJob(_handle, id, 0, IntPtr.Zero, 5), "SetJob(DELETE)");
        // A retained completed job can stay in DELETING until retention is released.
        // Cancel first, so releasing cannot restart a pending print job.
        if (!SetJob(_handle, id, 0, IntPtr.Zero, 9) && Marshal.GetLastWin32Error() != 87)
            throw Error("SetJob(RELEASE after DELETE)");
    }
    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        if (_documentOpen) AbortPrinter(_handle);
        ClosePrinter(_handle);
        _handle = IntPtr.Zero;
    }
    private static void Check(bool success, string operation) { if (!success) throw Error(operation); }
    private static Exception Error(string operation)
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
