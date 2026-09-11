using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.Specialized;
using TestBuilder.ViewModels;

namespace TestBuilder.Views.TestViews;

public partial class LogPanelView : UserControl, IDisposable
{
    private const double BottomThreshold = 4;
    private TestViewModel? _currentVm;
    private bool _shouldAutoScroll = true;
    private bool _isProgrammaticScroll;
    private bool _scrollPending;

    public LogPanelView()
    {
        InitializeComponent();
        LogList.AddHandler(ScrollViewer.ScrollChangedEvent, OnLogScrollChanged, RoutingStrategies.Bubble);
    }

    private void OnClearLogs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is TestViewModel vm)
        {
            _shouldAutoScroll = true;
            vm.TestingLogger.Clear();
            ScrollToBottom();
        }
    }

    private async void OnCopyLogs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not TestViewModel vm || vm.TestingLogger.Entries.Count == 0)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            return;

        var text = string.Join(Environment.NewLine, vm.TestingLogger.Entries);
        await clipboard.SetTextAsync(text);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged -= OnLogEntriesChanged;

        _currentVm = DataContext as TestViewModel;

        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged += OnLogEntriesChanged;

        _shouldAutoScroll = true;
        ScrollToBottom();
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_shouldAutoScroll && !_scrollPending)
        {
            _scrollPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _scrollPending = false;
                if (_shouldAutoScroll)
                    ScrollToBottom();
            }, DispatcherPriority.Background);
        }
    }

    private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isProgrammaticScroll || e.Source is not ScrollViewer viewer)
            return;

        // New rows change the extent without the operator scrolling up.
        if (e.ExtentDelta.Y == 0 && e.OffsetDelta.Y != 0)
        {
            var maxOffset = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
            _shouldAutoScroll = maxOffset - viewer.Offset.Y <= BottomThreshold;
        }
    }

    private void ScrollToBottom()
    {
        _isProgrammaticScroll = true;
        if (_currentVm is { TestingLogger.Entries.Count: > 0 } vm)
            LogList.ScrollIntoView(vm.TestingLogger.Entries[^1]);
        _isProgrammaticScroll = false;
        _shouldAutoScroll = true;
    }

    public void Dispose()
    {
        LogList.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnLogScrollChanged);

        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged -= OnLogEntriesChanged;
    }
}
