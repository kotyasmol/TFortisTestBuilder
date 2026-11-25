global using TestBuilder.Tests.Support;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using TestBuilder.Views;

namespace TestBuilder.Tests.Support;

internal static class WorkspacePageTesting
{
    private static readonly ConditionalWeakTable<WorkspacePage, Window> Windows = new();
    // Legacy editor tests still exercise their actual page, now inside the production navigation host.
    public static void Show(this WorkspacePage page)
    {
        var width = double.IsNaN(page.Width) ? 1100 : page.Width;
        var height = double.IsNaN(page.Height) ? 800 : page.Height;
        page.Width = double.NaN; page.Height = double.NaN;
        var window = new Window { Content = new Border(), Width = width, Height = height };
        Windows.Add(page, window);
        page.Closed += (_, _) => { window.Close(); Windows.Remove(page); };
        window.Show(); _ = page.ShowAsync(window);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }
    public static void MouseDown(Control view, Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None) => HeadlessWindowExtensions.MouseDown(view.Owner(), point + (Vector)view.TranslatePoint(default, view.Owner())!.Value, button, modifiers);
    public static void MouseUp(Control view, Point point, MouseButton button, RawInputModifiers modifiers = RawInputModifiers.None) => HeadlessWindowExtensions.MouseUp(view.Owner(), point + (Vector)view.TranslatePoint(default, view.Owner())!.Value, button, modifiers);
    public static void MouseMove(Control view, Point point, RawInputModifiers modifiers = RawInputModifiers.None) => HeadlessWindowExtensions.MouseMove(view.Owner(), point + (Vector)view.TranslatePoint(default, view.Owner())!.Value, modifiers);
    public static void KeyPress(Control view, Key key, RawInputModifiers modifiers) => HeadlessWindowExtensions.KeyPress(view.Owner(), key, modifiers);
    public static void KeyRelease(Control view, Key key, RawInputModifiers modifiers) => HeadlessWindowExtensions.KeyRelease(view.Owner(), key, modifiers);
    public static void KeyPressQwerty(Control view, PhysicalKey key, RawInputModifiers modifiers) => HeadlessWindowExtensions.KeyPressQwerty(view.Owner(), key, modifiers);
    public static void Show(this ContentControl view) { if (view is Window window) window.Show(); else ((WorkspacePage)view).Show(); }
    public static void Close(this ContentControl view) { if (view is Window window) window.Close(); else ((WorkspacePage)view).Close(); }
    public static Window Owner(this Control view) => view as Window ?? TopLevel.GetTopLevel(view) as Window ?? throw new InvalidOperationException("Page not shown");
    public static WriteableBitmap? CaptureRenderedFrame(Control view) => HeadlessWindowExtensions.CaptureRenderedFrame(view.Owner());
    public static IReadOnlyList<WorkspacePage> OpenPages(this Control view)
    {
        var window = view as Window ?? TopLevel.GetTopLevel(view) as Window;
        return window == null ? Array.Empty<WorkspacePage>() : WorkspacePageHost.Find(window)?.Pages ?? Array.Empty<WorkspacePage>();
    }
}
