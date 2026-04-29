using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Nodify;
using System;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Views.TestViews;

public partial class GraphEditorView : UserControl
{
    public GraphEditorView()
    {
        InitializeComponent();

        Editor.AddHandler(DragDrop.DropEvent, OnDropNode);

        Editor.AddHandler(
            PointerPressedEvent,
            OnEditorPointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: false);

        this.GetObservable(IsVisibleProperty).Subscribe(new AnonymousObserver<bool>(isVisible =>
        {
            if (isVisible)
                Editor.PopAllStates();
        }));

        Application.Current!.GetObservable(Application.RequestedThemeVariantProperty)
            .Subscribe(new AnonymousObserver<ThemeVariant>(_ => UpdateEditorBackground()));
    }

    public void SelectAllNodes() => Editor.SelectAll();

    private void UpdateEditorBackground()
    {
        var isDark = Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;
        var brushKey = isDark ? "SmallGridBrush" : "SmallGridBrushLight";
        if (this.Resources.TryGetResource(brushKey, ActualThemeVariant, out var brush) && brush is IBrush b)
            Editor.Background = b;
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