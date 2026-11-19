using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace TestBuilder.Domain.Steps;

// One timer per sender. The caller selects a queue or per-packet QPC tail.
// No process-wide timer resolution or thread priority changes.
internal sealed class DataTestDeadlineWait : IDisposable
{
    internal const double SpinTailMs = 0.25;
    internal const double PacketSpinTailMs = 0.03;
    private const uint CreateHighResolutionTimer = 0x2;
    private const uint TimerModifyStateAndSynchronize = 0x00100002;
    private readonly SafeWaitHandle? _timer;
    private readonly Action<double> _pause;
    internal bool Available => _timer != null;

    internal DataTestDeadlineWait()
    {
        _pause = Pause;
        if (!OperatingSystem.IsWindows()) return;
        var timer = CreateWaitableTimerExW(IntPtr.Zero, null, CreateHighResolutionTimer, TimerModifyStateAndSynchronize);
        if (timer.IsInvalid) timer.Dispose();
        else _timer = timer;
    }

    internal void Wait(double deadlineMs, Func<double> elapsedMs, CancellationToken token,
        double spinTailMs = SpinTailMs, double minimumSleepMs = 0.1) =>
        Wait(deadlineMs, elapsedMs, _pause, token, spinTailMs, minimumSleepMs);

    internal static void Wait(double deadlineMs, Func<double> elapsedMs, Action<double> pause,
        CancellationToken token, double spinTailMs = SpinTailMs, double minimumSleepMs = 0.1)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var remaining = deadlineMs - elapsedMs();
            if (remaining <= 0) return;
            if (remaining > spinTailMs + minimumSleepMs) pause(Math.Min(20, remaining - spinTailMs));
            else Thread.SpinWait(16);
        }
    }

    private void Pause(double milliseconds)
    {
        if (_timer != null)
        {
            var due = -Math.Max(1, (long)(milliseconds * 10000));
            if (SetWaitableTimer(_timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                if (WaitForSingleObject(_timer, 50) == 0) return;
            }
        }
        // Older Windows without high-resolution timers: yield without assuming
        // that Sleep(1) wakes within a queue window. Actual oversleep stays timed.
        Thread.Yield();
    }

    public void Dispose() => _timer?.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        IntPtr completion, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
