using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace TestBuilder.Services;

// Keep helper descendants owned by the run even if the direct BAT/EXE exits first.
internal sealed class WindowsProcessJob : IDisposable
{
    private readonly SafeFileHandle _handle;
    public WindowsProcessJob()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось создать группу внешней программы.");
        var info = new ExtendedLimitInformation(); info.BasicLimitInformation.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
        var size = Marshal.SizeOf<ExtendedLimitInformation>(); var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (!SetInformationJobObject(_handle, 9, pointer, (uint)size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось настроить остановку внешней программы.");
        }
        catch { _handle.Dispose(); throw; }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    public void Attach(Process process)
    {
        if (!AssignProcessToJobObject(_handle, process.Handle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось взять внешнюю программу под управление. Запуск прерван.");
    }

    // Attach before any browser code can create children. Attaching after Process.Start
    // leaves a race in which renderer processes can escape ownership.
    public Process Start(ProcessStartInfo info)
    {
        if (info.UseShellExecute || info.RedirectStandardOutput || info.RedirectStandardError ||
            info.RedirectStandardInput || info.Arguments.Length != 0)
            throw new ArgumentException("Managed browser launch requires ArgumentList and no redirected streams.", nameof(info));
        var command = new StringBuilder(QuoteArgument(info.FileName));
        foreach (var argument in info.ArgumentList) command.Append(' ').Append(QuoteArgument(argument));
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
        if (!CreateProcess(info.FileName, command, IntPtr.Zero, IntPtr.Zero, false,
                0x00000004 | 0x08000000, IntPtr.Zero, // CREATE_SUSPENDED | CREATE_NO_WINDOW
                string.IsNullOrEmpty(info.WorkingDirectory) ? null : info.WorkingDirectory,
                ref startup, out var native))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось запустить браузер.");
        Process? process = null;
        try
        {
            if (!AssignProcessToJobObject(_handle, native.Process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось взять браузер под управление. Запуск прерван.");
            process = Process.GetProcessById((int)native.ProcessId);
            if (ResumeThread(native.Thread) == uint.MaxValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось продолжить запуск браузера.");
            return process;
        }
        catch (Exception ex)
        {
            // The process is still suspended if attachment or resume failed.
            TerminateProcess(native.Process, 1);
            var exited = WaitForSingleObject(native.Process, 3000) == 0;
            process?.Dispose();
            if (!exited)
                throw new Http.BrowserCleanupException("Не удалось завершить приостановленный процесс браузера после ошибки запуска.", ex);
            throw;
        }
        finally { CloseHandle(native.Thread); CloseHandle(native.Process); }
    }

    public uint ActiveProcessCount
    {
        get
        {
            if (!QueryInformationJobObject(_handle, 1, out var info,
                    (uint)Marshal.SizeOf<BasicAccountingInformation>(), IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось проверить завершение группы браузера.");
            return info.ActiveProcesses;
        }
    }

    public async Task TerminateAndWaitAsync(TimeSpan timeout)
    {
        // Also run when the direct parent exited: its descendants still belong to this job.
        if (ActiveProcessCount == 0) return;
        if (!TerminateJobObject(_handle, 1))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Не удалось остановить группу браузера.");
        var clock = Stopwatch.StartNew();
        while (ActiveProcessCount != 0)
        {
            if (clock.Elapsed >= timeout)
                throw new TimeoutException("Процессы браузера не завершились за отведённое время.");
            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    internal static string QuoteArgument(string value)
    {
        var quoted = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            quoted.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            quoted.Append(character);
            slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }

    public void Dispose() => _handle.Dispose();
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public uint Size;
        public string? Reserved, Desktop, Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedPointer, StandardInput, StandardOutput, StandardError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicAccountingInformation
    {
        public long TotalUserTime, TotalKernelTime, ThisPeriodTotalUserTime, ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount, TotalProcesses, ActiveProcesses, TotalTerminatedProcesses;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass, IntPtr information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcess(string applicationName, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment, string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int informationClass, out BasicAccountingInformation information, uint size, IntPtr returnLength);
}
