using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Nodify;
using System;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Views.TestViews;

public partial class GraphEditorView : UserControl, IDisposable
{
    private readonly IDisposable _visibilitySubscription;
    private readonly IDisposable _themeSubscription;
    private readonly IDisposable _dataContextSubscription;
    private TestViewModel? _viewModel;
    private bool _isDisposed;

    public GraphEditorView()
    {
        InitializeComponent();

        Editor.AddHandler(DragDrop.DropEvent, OnDropNode);

        this.AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Editor.AddHandler(
            PointerPressedEvent,
            OnEditorPointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: false);

        _visibilitySubscription = this.GetObservable(IsVisibleProperty).Subscribe(new AnonymousObserver<bool>(isVisible =>
        {
            if (isVisible)
                Editor.PopAllStates();
        }));

        _themeSubscription = Application.Current!.GetObservable(Application.RequestedThemeVariantProperty)
            .Subscribe(new AnonymousObserver<ThemeVariant?>(_ => UpdateEditorBackground()));

        _dataContextSubscription = this.GetObservable(DataContextProperty)
            .Subscribe(new AnonymousObserver<object?>(OnDataContextChanged));
    }

    public void SelectAllNodes() => Editor.SelectAll();

    private void UpdateEditorBackground()
    {
        if (this.Resources.TryGetResource("SmallGridBrush", ActualThemeVariant, out var brush) && brush is IBrush b)
            Editor.Background = b;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        _visibilitySubscription.Dispose();
        _themeSubscription.Dispose();
        _dataContextSubscription.Dispose();
        DetachViewModel();
        Editor.RemoveHandler(DragDrop.DropEvent, OnDropNode);
        RemoveHandler(KeyDownEvent, OnKeyDown);
        Editor.RemoveHandler(PointerPressedEvent, OnEditorPointerPressed);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Z)
        {
            if (vm.CanUndo) vm.UndoCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Y)
        {
            if (vm.CanRedo) vm.RedoCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.C)
        {
            vm.CopyNodes();
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.V)
        {
            vm.PasteNodes();
            e.Handled = true;
        }
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Control;
        while (source != null)
        {
            if (source is ComboBox) { e.Handled = true; return; }
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

    private void OnDataContextChanged(object? dataContext)
    {
        DetachViewModel();

        _viewModel = dataContext as TestViewModel;

        if (_viewModel != null)
            _viewModel.CurrentGraphOpened += FitCurrentGraph;
    }

    private void DetachViewModel()
    {
        if (_viewModel != null)
            _viewModel.CurrentGraphOpened -= FitCurrentGraph;

        _viewModel = null;
    }

    private void FitCurrentGraph()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Editor.PopAllStates();
            Editor.FitToScreen();
        });
    }

    public void OnConnectionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        if (sender is not BaseConnection conn) return;
        if (conn.DataContext is not ConnectionViewModel connection) return;
        if (e.GetCurrentPoint(conn).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;
        vm.SelectConnection(connection);
        e.Handled = true;
    }

    public void OnConnectionDisconnect(object? sender, ConnectionEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        if (sender is not BaseConnection conn) return;
        if (conn.DataContext is not ConnectionViewModel connection) return;
        vm.DeleteConnection(connection);
        e.Handled = true;
    }

    private sealed class AnonymousObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
