using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TestBuilder.Views;

/// <summary>A draft editor displayed inside its owner's workspace, never a native window.</summary>
public class WorkspacePage : UserControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<WorkspacePage, string>(nameof(Title), "");
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public event EventHandler? Opened;
    public event EventHandler? Closed;
    public event EventHandler<CancelEventArgs>? Closing;
    private TaskCompletionSource<object?>? _completion;
    private bool _closed;
    internal WorkspacePageHost? Host { get; private set; }
    protected IStorageProvider StorageProvider => TopLevel.GetTopLevel(this)?.StorageProvider ?? throw new InvalidOperationException("Редактор не открыт.");
    protected IClipboard? Clipboard => TopLevel.GetTopLevel(this)?.Clipboard;

    public WorkspacePage() { Focusable = true; }
    public async Task<T> ShowAsync<T>(Control owner)
    {
        if (_closed) throw new InvalidOperationException("Редактор уже закрыт.");
        if (_completion != null) throw new InvalidOperationException("Редактор уже открыт.");
        var window = owner as Window ?? TopLevel.GetTopLevel(owner) as Window ?? throw new InvalidOperationException("Нет рабочего окна.");
        var completion = _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Host = WorkspacePageHost.For(window);
            Host.Push(this, owner);
            Opened?.Invoke(this, EventArgs.Empty);
            var result = await completion.Task;
            return result is T value ? value : default!;
        }
        catch
        {
            Close();
            throw;
        }
    }
    public async Task ShowAsync(Control owner) => await ShowAsync<object?>(owner);
    public void Close() => Close(null);
    public void Close(object? result)
    {
        if (_closed || !CanClose()) return;
        Complete(result);
    }
    internal bool CanClose()
    {
        var args = new CancelEventArgs(); OnClosing(args); return !args.Cancel;
    }
    protected virtual void OnClosing(CancelEventArgs args) => Closing?.Invoke(this, args);
    protected virtual void OnClosed(EventArgs args) => Closed?.Invoke(this, args);
    internal void Complete(object? result)
    {
        if (_closed) return;
        _closed = true;
        var completion = _completion;
        _completion = null;
        Host?.Remove(this); Host = null;
        IsVisible = false;
        try { OnClosed(EventArgs.Empty); }
        finally { completion?.TrySetResult(result); }
    }
}

/// <summary>One navigation stack per application window; previous pages keep their drafts.</summary>
internal sealed class WorkspacePageHost : Grid
{
    private static readonly ConditionalWeakTable<Window, WorkspacePageHost> Hosts = new();
    private readonly Window _window;
    private readonly Control _workspace;
    private readonly bool _workspaceVisible;
    private readonly bool _workspaceEnabled;
    private readonly Grid _surface;
    private readonly TextBlock _title;
    private readonly List<(WorkspacePage Page, IInputElement? Focus)> _pages = new();
    internal WorkspacePage? Current => _pages.Count == 0 ? null : _pages[^1].Page;
    internal IReadOnlyList<WorkspacePage> Pages => _pages.ConvertAll(frame => frame.Page);
    internal static WorkspacePageHost For(Window window)
    {
        if (Hosts.TryGetValue(window, out var existing)) return existing;
        var host = new WorkspacePageHost(window);
        // Publish before attachment: OnAttached handlers may themselves open a help page.
        Hosts.Add(window, host);
        window.Content = null; host.Children.Insert(0, host._workspace); window.Content = host;
        return host;
    }
    internal static WorkspacePageHost? Find(Control source) => (source as Window ?? TopLevel.GetTopLevel(source) as Window) is { } window ? Find(window) : null;
    internal static WorkspacePageHost? Find(Window window) => Hosts.TryGetValue(window, out var host) ? host : null;

    private WorkspacePageHost(Window window)
    {
        _window = window;
        _workspace = window.Content as Control ?? new ContentControl { Content = window.Content };
        _workspaceVisible = _workspace.IsVisible; _workspaceEnabled = _workspace.IsEnabled;
        _surface = new Grid { Name = "WorkspacePageSurface", RowDefinitions = new("Auto,*"), IsVisible = false };
        _surface.Bind(BackgroundProperty, new DynamicResourceExtension("AppBackgroundBrush"));
        var back = new Button { Name = "WorkspacePageBack", Content = "Назад", Margin = new(0, 0, 16, 0) };
        back.Click += (_, _) => Current?.Close();
        _title = new TextBlock { Name = "WorkspacePageTitle", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var heading = new Grid { ColumnDefinitions = new("Auto,*") }; heading.Children.Add(back); SetColumn(_title, 1); heading.Children.Add(_title);
        var header = new Border { Padding = new(16, 8), BorderThickness = new(0, 0, 0, 1), Child = heading };
        header.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush"));
        header.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));
        _surface.Children.Add(header); Children.Add(_surface);
        _surface.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!e.Handled && e.Key == Key.Escape) { Current?.Close(); e.Handled = true; }
        });
        window.Closing += (_, args) => { if (_pages.Exists(frame => !frame.Page.CanClose())) args.Cancel = true; };
        window.Closed += (_, _) => { while (Current is { } page) page.Complete(null); Hosts.Remove(window); };
    }
    internal void Push(WorkspacePage page, Control source)
    {
        // Hidden callers cannot open a second chain over an unrelated draft.
        if (Current != null && source != _window && !ReferenceEquals(source, Current) && !Current.IsVisualAncestorOf(source))
            throw new InvalidOperationException("Сначала вернитесь из текущего редактора.");
        ClosePopups(Current ?? _workspace);
        if (Current is { } previous) previous.IsVisible = false;
        _pages.Add((page, _window.FocusManager?.GetFocusedElement()));
        page.IsVisible = true; SetRow(page, 1); _surface.Children.Add(page);
        _workspace.IsEnabled = false; _workspace.IsVisible = false; _surface.IsVisible = true;
        _title.Text = page.Title;
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(Current, page) && !(_window.FocusManager?.GetFocusedElement() is Control focused && page.IsVisualAncestorOf(focused))) page.Focus();
        });
    }
    private static void ClosePopups(Control root)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>().Prepend(root).ToArray())
        {
            if (control is ComboBox combo) combo.IsDropDownOpen = false;
            if (control is AutoCompleteBox autoComplete) autoComplete.IsDropDownOpen = false;
            if (control is Control element) { element.ContextMenu?.Close(); element.ContextFlyout?.Hide(); }
            if (control is Button button) button.Flyout?.Hide();
        }
    }
    internal void Remove(WorkspacePage page)
    {
        var index = _pages.FindIndex(frame => ReferenceEquals(frame.Page, page));
        if (index < 0) return;
        // A parent may close while a child is open (e.g. profile unload); cancel descendants first.
        while (_pages.Count > index + 1) Current!.Complete(null);
        var focus = _pages[index].Focus;
        _pages.RemoveAt(index); _surface.Children.Remove(page);
        if (Current is { } previous) { previous.IsVisible = true; _title.Text = previous.Title; }
        else { _surface.IsVisible = false; _workspace.IsVisible = _workspaceVisible; _workspace.IsEnabled = _workspaceEnabled; }
        Dispatcher.UIThread.Post(() =>
        {
            if (_window.IsVisible && focus is Control control && ReferenceEquals(TopLevel.GetTopLevel(control), _window) && control.IsEffectivelyVisible && control.IsEffectivelyEnabled)
                control.Focus();
        });
    }
}
