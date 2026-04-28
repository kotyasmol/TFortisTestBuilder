using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Nodify;
using System;
using System.Collections.Specialized;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Views;

public partial class TestView : UserControl
{
    private bool _leftButtonPressed;
    private TestViewModel? _currentVm;

    public TestView()
    {
        InitializeComponent();

        Editor.AddHandler(DragDrop.DropEvent, OnDropNode);

        Editor.AddHandler(
            PointerPressedEvent,
            OnEditorPointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: false);

        this.GetObservable(IsVisibleProperty).Subscribe(isVisible =>
        {
            if (isVisible)
                Editor.PopAllStates();
        });

        Application.Current!.GetObservable(Application.RequestedThemeVariantProperty)
            .Subscribe(_ => UpdateEditorBackground());
    }

    private void UpdateEditorBackground()
    {
        var isDark = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
        var brushKey = isDark ? "SmallGridBrush" : "SmallGridBrushLight";
        if (this.Resources.TryGetResource(brushKey, ActualThemeVariant, out var brush) && brush is Avalonia.Media.IBrush b)
            Editor.Background = b;
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

    public void OnNodePressed(object? sender, PointerPressedEventArgs e)
    {
        _leftButtonPressed =
            e.GetCurrentPoint(this).Properties.PointerUpdateKind ==
            PointerUpdateKind.LeftButtonPressed;
    }

    public void OnNodeDrag(object? sender, PointerEventArgs e)
    {
        if (_leftButtonPressed && sender is Nodify.Node node && node.DataContext is NodeViewModel vm)
        {
            var data = new DataObject();
            data.Set("NodeType", vm.Title);
            DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        }
    }

    public void OnNodeExited(object? sender, PointerEventArgs e)
    {
        _leftButtonPressed = false;
    }

    public void OnConnectionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        if (sender is not BaseConnection connectionControl) return;
        if (connectionControl.DataContext is not ConnectionViewModel connection) return;
        var point = e.GetCurrentPoint(connectionControl);
        if (point.Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        vm.SelectConnection(connection);
        e.Handled = true;
    }

    public void OnConnectionDisconnect(object? sender, ConnectionEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        if (sender is not BaseConnection connectionControl) return;
        if (connectionControl.DataContext is not ConnectionViewModel connection) return;
        vm.DeleteConnection(connection);
        e.Handled = true;
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Control;
        while (source != null)
        {
            if (source is ComboBox)
            {
                e.Handled = true;
                return;
            }
            source = source.Parent as Control;
        }
    }

    private void OnDropNode(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("NodeType") is string nodeType && DataContext is TestViewModel vm)
        {
            var location = Editor.GetLocationInsideEditor(e);
            vm.AddNodeAtLocation(nodeType, location);
            e.Handled = true;
        }
    }

    public void OnClearGraph(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        Editor.SelectAll();
        Dispatcher.UIThread.Post(() =>
        {
            vm.DeleteSelectedNodesCommand.Execute(null);
        }, DispatcherPriority.Background);
    }
}