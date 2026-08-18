using Avalonia.Controls;
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

    public LogPanelView()
    {
        InitializeComponent();
        LogScrollViewer.ScrollChanged += OnLogScrollChanged;
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
        if (_shouldAutoScroll)
            Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Background);
    }

    private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isProgrammaticScroll)
            return;

        _shouldAutoScroll = IsScrolledToBottom();
    }

    private bool IsScrolledToBottom()
    {
        var maxOffset = Math.Max(0, LogScrollViewer.Extent.Height - LogScrollViewer.Viewport.Height);
        return maxOffset - LogScrollViewer.Offset.Y <= BottomThreshold;
    }

    private void ScrollToBottom()
    {
        var maxOffset = Math.Max(0, LogScrollViewer.Extent.Height - LogScrollViewer.Viewport.Height);
        _isProgrammaticScroll = true;
        LogScrollViewer.Offset = LogScrollViewer.Offset.WithY(maxOffset);
        _isProgrammaticScroll = false;
        _shouldAutoScroll = true;
    }

    public void Dispose()
    {
        LogScrollViewer.ScrollChanged -= OnLogScrollChanged;

        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged -= OnLogEntriesChanged;
    }
}
