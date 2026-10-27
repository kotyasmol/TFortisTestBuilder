using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using TestBuilder.Services.Logging;
using System.Linq;
using TestBuilder.ViewModels;

namespace TestBuilder.Views.TestViews;

public partial class LogPanelView : UserControl, IDisposable
{
    private readonly ObservableCollection<LogEntry> _visibleEntries = new();
    private bool _historyPaused;
    private TestViewModel? _currentVm;
    private bool _scrollPending;
    private bool _disposed;
    private bool _scrollBarInteraction;
    private bool Following => FollowLogsToggle?.IsChecked == true;
    private ScrollViewer? Viewer => LogList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    public LogPanelView()
    {
        InitializeComponent();
        LogList.ItemsSource = _visibleEntries;
        LogSelection.Enable(LogList);
        LogList.AddHandler(ScrollViewer.ScrollChangedEvent, OnLogScrollChanged, RoutingStrategies.Bubble);
        LogList.AddHandler(PointerWheelChangedEvent, OnLogWheel, RoutingStrategies.Tunnel);
        LogList.AddHandler(PointerPressedEvent, OnLogPointerPressed, RoutingStrategies.Tunnel);
        LogList.AddHandler(PointerReleasedEvent, OnLogPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        LogList.AddHandler(KeyDownEvent, OnLogKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnClearLogs(object? sender, RoutedEventArgs e)
    {
        _currentVm?.TestingLogger.Clear();
        ResumeFollowing();
    }

    private async void OnCopyLogs(object? sender, RoutedEventArgs e)
        => await LogSelection.CopyAsync(LogList);

    private void OnScrollToBottomClick(object? sender, RoutedEventArgs e) => ResumeFollowing();
    private void OnFollowLogsChanged(object? sender, RoutedEventArgs e)
    {
        if (Following)
        {
            if (_historyPaused) SynchronizeEntries();
            _historyPaused = false;
            RequestScrollToBottom();
        }
        else _historyPaused = true;
    }

    private void ResumeFollowing()
    {
        FollowLogsToggle.IsChecked = true;
        RequestScrollToBottom();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged -= OnLogEntriesChanged;
        _currentVm = DataContext as TestViewModel;
        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged += OnLogEntriesChanged;
        SynchronizeEntries();
        if (FollowLogsToggle != null) ResumeFollowing();
    }

    private void SynchronizeEntries()
    {
        _visibleEntries.Clear();
        if (_currentVm != null)
            foreach (var entry in _currentVm.TestingLogger.Entries) _visibleEntries.Add(entry);
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep a stable reading snapshot: the live logger trims its first row at
        // 1000 entries, which otherwise moves the viewport even without autoscroll.
        if (!Following) return;
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (var i = 0; i < e.NewItems!.Count; i++)
                    _visibleEntries.Insert(e.NewStartingIndex + i, (LogEntry)e.NewItems[i]!);
                break;
            case NotifyCollectionChangedAction.Remove:
                for (var i = 0; i < e.OldItems!.Count; i++) _visibleEntries.RemoveAt(e.OldStartingIndex);
                break;
            default:
                SynchronizeEntries();
                break;
        }
        RequestScrollToBottom();
    }

    private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Virtualization, wrapping and removal of old rows change the extent/offset.
        // Only user input may pause following; layout changes must not do so.
        if (e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0 || e.ViewportDelta.X != 0)
            RequestScrollToBottom();
    }

    private void OnLogWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return;
        FollowLogsToggle.IsChecked = false;

    }

    private void OnLogPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<ScrollBar>().Any() != true) return;
        _scrollBarInteraction = true;
        FollowLogsToggle.IsChecked = false;
    }

    private void OnLogPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_scrollBarInteraction) return;
        _scrollBarInteraction = false;

    }

    private void OnLogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)) return;
        FollowLogsToggle.IsChecked = false;

    }

    private void RequestScrollToBottom()
    {
        if (_disposed || !Following || _scrollPending || _currentVm == null) return;
        _scrollPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !Following) { _scrollPending = false; return; }
            if (_currentVm is { TestingLogger.Entries.Count: > 0 } vm)
                LogList.ScrollIntoView(vm.TestingLogger.Entries[^1]);
            // ScrollIntoView realizes the last row, but a row taller than the viewport
            // may still show its beginning. Finish after layout has measured that row.
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed && Following) Viewer?.ScrollToEnd();
                _scrollPending = false;
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Background);
    }

    public void Dispose()
    {
        _disposed = true;
        LogList.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnLogScrollChanged);
        LogList.RemoveHandler(PointerWheelChangedEvent, OnLogWheel);
        LogList.RemoveHandler(PointerPressedEvent, OnLogPointerPressed);
        LogList.RemoveHandler(PointerReleasedEvent, OnLogPointerReleased);
        LogList.RemoveHandler(KeyDownEvent, OnLogKeyDown);
        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged -= OnLogEntriesChanged;
    }
}
