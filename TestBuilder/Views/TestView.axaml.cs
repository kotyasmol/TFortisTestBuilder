using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using System;
using System.Collections.Specialized;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class TestView : UserControl
{
    private TestViewModel? _currentVm;

    public TestView()
    {
        InitializeComponent();

        var topLevel = TopLevel.GetTopLevel(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.KeyDown += OnWindowKeyDown;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.KeyDown -= OnWindowKeyDown;
        _currentVm?.PendingConnection.Reset();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        if (e.Key == Key.Delete)
        {
            if (vm.SelectedNodes.Count > 0)
                vm.DeleteSelectedNodesCommand.Execute(null);
            else if (vm.SelectedConnection != null)
                vm.DeleteSelectedConnection();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && vm.SelectedConnection != null)
        {
            vm.SelectConnection(null);
            e.Handled = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged -= Entries_CollectionChanged;
        _currentVm = DataContext as TestViewModel;
        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged += Entries_CollectionChanged;
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.UIThread.Post(() => { });
    }
}