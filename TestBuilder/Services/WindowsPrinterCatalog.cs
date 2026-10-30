using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Services;

internal interface IPrinterCatalog
{
    Task<IReadOnlyList<string>> GetPrintersAsync(CancellationToken token);
}

/// <summary>Installed local queues and the current user's network printer connections.</summary>
internal sealed class WindowsPrinterCatalog : IPrinterCatalog
{
    private readonly object _gate = new();
    private Task<IReadOnlyList<string>>? _pending;

    public async Task<IReadOnlyList<string>> GetPrintersAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Список принтеров доступен в Windows.");
        token.ThrowIfCancellationRequested();
        Task<IReadOnlyList<string>> pending;
        lock (_gate)
        {
            // EnumPrinters is synchronous and cannot be cancelled. Reuse a slow call
            // after timeout rather than starting more workers on repeated refreshes.
            if (_pending == null || _pending.IsCompleted) _pending = Task.Run(Enumerate);
            pending = _pending;
        }
        return await pending.WaitAsync(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> Enumerate()
    {
        const uint flags = 0x2 | 0x4; // PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS.
        const int insufficientBuffer = 122;
        // Level 4 reads the Windows cache, without opening every remote queue.
        // https://learn.microsoft.com/windows/win32/printdocs/enumprinters
        uint size = 0;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (size > 16 * 1024 * 1024) throw new InvalidOperationException("Слишком большой список принтеров Windows.");
            var buffer = size == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (!EnumPrinters(flags, null, 4, buffer, size, out var needed, out var returned))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == insufficientBuffer && needed > size) { size = needed; continue; }
                    throw new Win32Exception(error);
                }
                var stride = Marshal.SizeOf<PrinterInfo4>();
                if (returned > size / stride) throw new InvalidOperationException("Некорректный ответ службы печати Windows.");
                var names = new List<string>();
                for (var i = 0; i < returned; i++)
                {
                    var info = Marshal.PtrToStructure<PrinterInfo4>(IntPtr.Add(buffer, checked(i * stride)));
                    var name = Marshal.PtrToStringUni(info.PrinterName);
                    if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
                }
                return names;
            }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidOperationException("Список принтеров изменился во время чтения. Нажмите «Обновить».");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr PrinterName;
        public IntPtr ServerName;
        public uint Attributes;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr buffer,
        uint size, out uint needed, out uint returned);
}
