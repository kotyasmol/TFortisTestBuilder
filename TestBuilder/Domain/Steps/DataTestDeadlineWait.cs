using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace TestBuilder.Domain.Steps;

// One timer per sender, used only between queues. Per-frame 100 Mbps pacing
// retains its QPC loop. No process-wide timer resolution or thread priority changes.
internal sealed class DataTestDeadlineWait : IDisposable
{
    internal const double SpinTailMs = 0.25;
    private const uint CreateHighResolutionTimer = 0x2;
    private const uint TimerModifyStateAndSynchronize = 0x00100002;
    private readonly SafeWaitHandle? _timer;

    internal DataTestDeadlineWait()
    {
        if (!OperatingSystem.IsWindows()) return;
        var timer = CreateWaitableTimerExW(IntPtr.Zero, null, CreateHighResolutionTimer, TimerModifyStateAndSynchronize);
        if (timer.IsInvalid) timer.Dispose();
        else _timer = timer;
    }

    internal void Wait(double deadlineMs, Func<double> elapsedMs, CancellationToken token) =>
        Wait(deadlineMs, elapsedMs, Pause, token);

    internal static void Wait(double deadlineMs, Func<double> elapsedMs, Action<double> pause,
        CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var remaining = deadlineMs - elapsedMs();
            if (remaining <= 0) return;
            if (remaining > SpinTailMs + 0.1) pause(Math.Min(20, remaining - SpinTailMs));
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
