using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
using Nodify;
using System;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Views.TestViews;

public partial class GraphEditorView : UserControl, IDisposable
{
    public static readonly StyledProperty<NodeViewModel?> HelpNodeProperty =
        AvaloniaProperty.Register<GraphEditorView, NodeViewModel?>(nameof(HelpNode));

    public static readonly StyledProperty<bool> IsNodeHelpOpenProperty =
        AvaloniaProperty.Register<GraphEditorView, bool>(nameof(IsNodeHelpOpen));

    private readonly IDisposable _visibilitySubscription;
    private readonly IDisposable _themeSubscription;
    private readonly IDisposable _dataContextSubscription;
    private TestViewModel? _viewModel;
    private LabelNodeViewModel? _resizingLabel;
    private Control? _labelResizeHandle;
    private Point _labelResizeStartPoint;
    private double _labelResizeStartWidth;
    private double _labelResizeStartHeight;
    internal NodeHelpWindow? HelpWindow { get; private set; }
    internal ContextMenu? NodeRunMenu { get; private set; }
    private bool _isDisposed;
    private bool _fitPending;
    private bool _fitScheduled;

    public GraphEditorView()
    {
        InitializeComponent();

        Editor.AddHandler(DragDrop.DropEvent, OnDropNode);
        Editor.LayoutUpdated += OnEditorLayoutUpdated;
        Editor.TemplateApplied += OnEditorTemplateApplied;

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

    public NodeViewModel? HelpNode
    {
        get => GetValue(HelpNodeProperty);
        set => SetValue(HelpNodeProperty, value);
    }

    public bool IsNodeHelpOpen
    {
        get => GetValue(IsNodeHelpOpenProperty);
        set => SetValue(IsNodeHelpOpenProperty, value);
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
        HelpWindow?.Close();
        NodeRunMenu?.Close();

        _visibilitySubscription.Dispose();
        _themeSubscription.Dispose();
        _dataContextSubscription.Dispose();
        DetachViewModel();
        Editor.RemoveHandler(DragDrop.DropEvent, OnDropNode);
        Editor.LayoutUpdated -= OnEditorLayoutUpdated;
        Editor.TemplateApplied -= OnEditorTemplateApplied;
        RemoveHandler(KeyDownEvent, OnKeyDown);
        Editor.RemoveHandler(PointerPressedEvent, OnEditorPointerPressed);
    }

    private void OnEditorTemplateApplied(object? sender, Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        // NodifyAvalonia 6.6 sets a local ZIndex of 0 on this host even when
        // DisplayConnectionsOnTop is false, overriding XAML styles. Lower it
        // after the template finishes so existing wires cannot cover ports.
        var host = e.NameScope.Find<Control>("PART_ConnectionsHost");
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isDisposed && host != null) host.ZIndex = -1;
        }, DispatcherPriority.Loaded);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        // Let text fields handle selection/copy, including read-only fields during a run.
        if (e.Source is TextBox) return;

        if ((e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta) && e.Key == Key.Z)
        {
            if (vm.CanUndo) vm.UndoCommand.Execute(null);
            e.Handled = true;
        }
        else if ((e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Y) ||
                 (e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Shift) && e.Key == Key.Z))
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
        if (e.GetCurrentPoint(Editor).Properties.IsRightButtonPressed && DataContext is TestViewModel vm)
        {
            var container = source?.GetSelfAndVisualAncestors().OfType<ItemContainer>().FirstOrDefault();
            if (container?.DataContext is NodeViewModel node && vm.CanConfigureNodeRun(node))
            {
                var only = new MenuItem { Header = "Выполнить только эту ноду" };
                var from = new MenuItem { Header = "Начать с этой ноды" };
                only.Click += (_, _) => vm.ConfigureNodeRun(node, true);
                from.Click += (_, _) => vm.ConfigureNodeRun(node, false);
                NodeRunMenu?.Close();
                NodeRunMenu = new ContextMenu { ItemsSource = new object[] { only, from } };
                NodeRunMenu.Open(container);
                e.Handled = true;
                return;
            }
        }
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
        {
            _viewModel.CurrentGraphOpened += FitCurrentGraph;
            FitCurrentGraph();
        }
    }

    private void DetachViewModel()
    {
        if (_viewModel != null)
            _viewModel.CurrentGraphOpened -= FitCurrentGraph;

        _viewModel = null;
        _fitPending = false;
    }

    private void FitCurrentGraph()
    {
        // A dispatcher callback can run before the first layout or while an ancestor
        // is hidden. Keep the request until Nodify has measured the current graph.
        _fitPending = true;
        Editor.InvalidateArrange();
    }

    private void OnEditorLayoutUpdated(object? sender, EventArgs e)
    {
        if (_isDisposed || !_fitPending || _fitScheduled)
            return;

        // Nodify updates its viewport during layout too. Fitting from inside
        // LayoutUpdated can use the previous viewport size on the first show.
        _fitScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _fitScheduled = false;
            FitAfterLayout();
        }, DispatcherPriority.Loaded);
    }

    private void FitAfterLayout()
    {
        if (_isDisposed || !_fitPending || !Editor.IsEffectivelyVisible ||
            !Editor.IsMeasureValid || !Editor.IsArrangeValid ||
            Editor.Bounds.Width <= 0 || Editor.Bounds.Height <= 0)
            return;

        if (_viewModel?.Nodes.Count > 0 &&
            (Editor.ItemsExtent.Width <= 0 || Editor.ItemsExtent.Height <= 0))
            return;

        _fitPending = false;
        Editor.PopAllStates();
        Editor.FitToScreen();
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

    public void OnNodeHelpPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
            return;

        OpenNodeHelp(sender as Control);
        e.Handled = true;
    }

    public void OnProfileFieldDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not AutoCompleteBox field || field.DataContext is not TestViewModel vm) return;
        // In a hidden editor, Avalonia can replay a TextBox change from the previous
        // DataContext after binding the new one. Restore the current document once
        // that queued template work has settled; never replace text being edited.
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(field.DataContext, vm) && !field.IsKeyboardFocusWithin)
                field.SetCurrentValue(AutoCompleteBox.TextProperty,
                    field.Name == "DeviceNameInput" ? vm.DeviceName : vm.ProfileDescription);
        }, DispatcherPriority.Background);
    }

    public void OnProfileFieldLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not AutoCompleteBox field || _viewModel is not { CanEditGraph: true } vm) return;
        // AutoCompleteBox queues TextChanged work. Read the editor synchronously so a
        // Save click or fast focus change cannot commit the previous text instead.
        var text = field.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Text ?? field.Text ?? "";
        field.SetCurrentValue(AutoCompleteBox.TextProperty, text);
        if (field.Name == "DeviceNameInput") vm.DeviceName = text;
        else vm.ProfileDescription = text;
    }

    public void OnProfileFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is AutoCompleteBox { IsDropDownOpen: false })
        {
            Editor.Focus();
            e.Handled = true;
        }
    }

    public void OnZoomInClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Editor.ZoomIn();
    public void OnZoomOutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Editor.ZoomOut();
    public void OnCenterGraphClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var center = Editor.ItemsExtent.Center;
        Editor.ViewportLocation = new Point(center.X - Editor.ViewportSize.Width / 2, center.Y - Editor.ViewportSize.Height / 2);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); if (IsNodeHelpOpen) ShowHelpWindow(); }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    { HelpWindow?.Close(); NodeRunMenu?.Close(); base.OnDetachedFromVisualTree(e); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsNodeHelpOpenProperty)
        {
            if (IsNodeHelpOpen) ShowHelpWindow(); else HelpWindow?.Close();
        }
        else if (change.Property == HelpNodeProperty && IsNodeHelpOpen) ShowHelpWindow();
    }

    private void ShowHelpWindow()
    {
        if (HelpNode == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        if (HelpWindow == null)
        {
            HelpWindow = new NodeHelpWindow();
            HelpWindow.Closed += (_, _) => { HelpWindow = null; IsNodeHelpOpen = false; HelpNode = null; };
            HelpWindow.Show(owner);
        }
        HelpWindow.SetNode(HelpNode);
        HelpWindow.Activate();
    }

    private void OpenNodeHelp(Control? control)
    {
        if (control?.DataContext is not NodeViewModel node || !node.HasHelpText)
            return;

        HelpNode = node;
        IsNodeHelpOpen = true;
    }

    public void OnLabelResizePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not TestViewModel { CanEditGraph: true }) return;
        if (sender is not Control handle) return;
        if (handle.DataContext is not LabelNodeViewModel label) return;
        if (e.GetCurrentPoint(handle).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed) return;

        _resizingLabel = label;
        _labelResizeHandle = handle;
        _labelResizeStartPoint = e.GetPosition(this);
        _labelResizeStartWidth = label.LabelWidth;
        _labelResizeStartHeight = label.LabelHeight;
        e.Pointer.Capture(handle);
        e.Handled = true;
    }

    public void OnLabelResizePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_resizingLabel == null || _labelResizeHandle == null) return;

        var point = e.GetPosition(this);
        var deltaX = point.X - _labelResizeStartPoint.X;
        var deltaY = point.Y - _labelResizeStartPoint.Y;

        _resizingLabel.LabelWidth = _labelResizeStartWidth + deltaX;
        _resizingLabel.LabelHeight = _labelResizeStartHeight + deltaY;
        e.Handled = true;
    }

    public void OnLabelResizePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_labelResizeHandle != null)
            e.Pointer.Capture(null);

        _resizingLabel = null;
        _labelResizeHandle = null;
        e.Handled = true;
    }

    private sealed class AnonymousObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnNext(T value) => onNext(value);
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
