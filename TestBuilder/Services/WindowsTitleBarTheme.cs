using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace TestBuilder.Services;

internal static class WindowsTitleBarTheme
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        _initialized = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            // Opened can fire again if an existing window is hidden and shown.
            window.ActualThemeVariantChanged -= OnThemeChanged;
            window.ActualThemeVariantChanged += OnThemeChanged;
            window.Closed -= OnClosed;
            window.Closed += OnClosed;
            ScheduleUpdate(window);
        });
    }

    private static void OnThemeChanged(object? sender, EventArgs e)
    {
        if (sender is Window window) ScheduleUpdate(window);
    }

    private static void OnClosed(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        window.ActualThemeVariantChanged -= OnThemeChanged;
        window.Closed -= OnClosed;
    }

    private static void ScheduleUpdate(Window window) => Dispatcher.UIThread.Post(() =>
    {
        if (window.IsVisible) Apply(window);
    }, DispatcherPriority.Loaded);

    private static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;
        var handle = window.TryGetPlatformHandle();
        if (handle?.Handle is not { } hwnd || hwnd == IntPtr.Zero || handle.HandleDescriptor != "HWND") return;

        var dark = window.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
        // Attribute 19 was used by Windows 10 before attribute 20 became available.
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) < 0)
            DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            SetColor(hwnd, 35, "SurfaceBrush", window.ActualThemeVariant);
            SetColor(hwnd, 36, "TextPrimaryBrush", window.ActualThemeVariant);
        }

        // Refresh the native frame without moving, resizing or activating the window.
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0037);
        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, 0x0501);
    }

    private static void SetColor(IntPtr hwnd, int attribute, string resource, ThemeVariant variant)
    {
        if (Application.Current?.TryGetResource(resource, variant, out var value) != true ||
            value is not ISolidColorBrush brush) return;
        var color = brush.Color.R | (brush.Color.G << 8) | (brush.Color.B << 16);
        DwmSetWindowAttribute(hwnd, attribute, ref color, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRegion, uint flags);
}
