using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
    private readonly IDisposable _dataContextSubscription;
    private TestViewModel? _viewModel;
    private LabelNodeViewModel? _resizingLabel;
    private Control? _labelResizeHandle;
    private Point _labelResizeStartPoint;
    private double _labelResizeStartWidth;
    private double _labelResizeStartHeight;
    internal NodeHelpWindow? HelpWindow { get; private set; }
    internal ContextMenu? NodeRunMenu { get; private set; }
    private bool _isMultiSelect;
    private Point? _contextPress;
    private Point _pasteLocation;
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
        AddHandler(PointerReleasedEvent, OnSelectionPointerReleased, RoutingStrategies.Tunnel);

        AddHandler(
            PointerPressedEvent,
            OnEditorPointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel,
            handledEventsToo: false);

        _visibilitySubscription = this.GetObservable(IsVisibleProperty).Subscribe(new AnonymousObserver<bool>(isVisible =>
        {
            if (isVisible)
                Editor.PopAllStates();
        }));

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

    private async void OnConfigurePing(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TestViewModel vm && TopLevel.GetTopLevel(this) is Window owner)
            await vm.ConfigurePingCommand.ExecuteAsync(owner);
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TestViewModel { CanEditGraph: true } vm)
        {
            vm.SelectConnection(null);
            SelectAllNodes();
        }
    }

    private void OnClearSelectionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestViewModel vm) return;
        vm.SelectedNodes.Clear();
        vm.SelectConnection(null);
    }

    private void OnCopyNodesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TestViewModel { CanEditGraph: true } vm) vm.CopyNodes();
    }

    private void OnPasteNodesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TestViewModel vm) vm.PasteNodes(_pasteLocation);
    }

    private Point VisiblePasteLocation => new(
        Editor.ViewportLocation.X + Editor.ViewportSize.Width / 4,
        Editor.ViewportLocation.Y + Editor.ViewportSize.Height / 4);

    private void OnDuplicateNodesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestViewModel { CanEditGraph: true } vm || vm.SelectedNodes.Count == 0) return;
        vm.DuplicateNodes();
    }

    private void OnDeleteSelectionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TestViewModel { CanEditGraph: true } vm) return;
        if (vm.SelectedConnection != null) vm.DeleteSelectedConnection();
        else vm.DeleteSelectedNodes();
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        HelpWindow?.Close();
        NodeRunMenu?.Close();

        _visibilitySubscription.Dispose();
        _dataContextSubscription.Dispose();
        DetachViewModel();
        Editor.RemoveHandler(DragDrop.DropEvent, OnDropNode);
        Editor.LayoutUpdated -= OnEditorLayoutUpdated;
        Editor.TemplateApplied -= OnEditorTemplateApplied;
        RemoveHandler(KeyDownEvent, OnKeyDown);
        RemoveHandler(PointerReleasedEvent, OnSelectionPointerReleased);
        RemoveHandler(PointerPressedEvent, OnEditorPointerPressed);
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
        if ((e.Source as Control)?.GetSelfAndVisualAncestors().Any(c => c is TextBox) == true) return;

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
        else if ((e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta) && e.Key == Key.C)
        {
            vm.CopyNodes();
            e.Handled = true;
        }
        else if ((e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta) && e.Key == Key.V)
        {
            vm.PasteNodes(VisiblePasteLocation);
            e.Handled = true;
        }
    }

    private void OnSelectionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right && _contextPress is { } press)
        {
            _contextPress = null;
            var delta = e.GetPosition(Editor) - press;
            if (delta.X * delta.X + delta.Y * delta.Y <= 16)
                OpenGraphMenu(pasteLocation: Editor.GetLocationInsideEditor(e));
        }
        if (!ReferenceEquals(e.Pointer.Captured, this)) return;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var source = e.Source as Control;
        // Intercept before Nodify's own pointer state starts an exclusive selection/drag.
        if (source?.GetSelfAndVisualAncestors().Contains(Editor) != true) return;
        var nodeContainer = source?.GetSelfAndVisualAncestors().OfType<ItemContainer>().FirstOrDefault();
        if (nodeContainer != null && e.GetCurrentPoint(Editor).Properties.IsLeftButtonPressed &&
            DataContext is TestViewModel nodeVm)
            nodeVm.SelectConnection(null);
        if (_isMultiSelect &&
            e.GetCurrentPoint(Editor).Properties.IsLeftButtonPressed &&
            DataContext is TestViewModel { CanEditGraph: true } selectionVm &&
            nodeContainer?.DataContext is NodeViewModel selectedNode)
        {
            selectionVm.SelectConnection(null);
            e.Pointer.Capture(this);
            if (!selectionVm.SelectedNodes.Remove(selectedNode)) selectionVm.SelectedNodes.Add(selectedNode);
            e.Handled = true;
            return;
        }
        if (e.GetCurrentPoint(Editor).Properties.IsRightButtonPressed && DataContext is TestViewModel vm)
        {
            // Keep the standard text-field context menu available for editing values.
            if (source?.GetSelfAndVisualAncestors().Any(c => c is TextBox) == true) return;
            var wire = source?.GetSelfAndVisualAncestors().OfType<BaseConnection>().FirstOrDefault();
            if (wire?.DataContext is ConnectionViewModel connection)
            {
                if (vm.CanEditGraph) vm.SelectConnection(connection);
                OpenGraphMenu(connection: connection, pasteLocation: Editor.GetLocationInsideEditor(e));
                e.Handled = true;
                return;
            }
            if (nodeContainer?.DataContext is NodeViewModel node)
            {
                if (vm.CanEditGraph)
                {
                    vm.SelectConnection(null);
                    if (!vm.SelectedNodes.Contains(node))
                    {
                        vm.SelectedNodes.Clear();
                        vm.SelectedNodes.Add(node);
                    }
                }
                OpenGraphMenu(node, pasteLocation: Editor.GetLocationInsideEditor(e));
                e.Handled = true;
                return;
            }
            // A click opens the menu; dragging keeps Nodify's existing panning gesture.
            _contextPress = e.GetPosition(Editor);
        }
        while (source != null)
        {
            if (source is ComboBox) { e.Handled = true; return; }
            source = source.Parent as Control;
        }
    }

    internal void OpenGraphMenu(NodeViewModel? node = null, ConnectionViewModel? connection = null, Point? pasteLocation = null)
    {
        if (DataContext is not TestViewModel vm) return;
        Editor.Focus();
        _pasteLocation = pasteLocation ?? VisiblePasteLocation;
        MenuItem Action(string title, EventHandler<RoutedEventArgs> handler, bool enabled = true)
        {
            var item = new MenuItem { Header = title, IsEnabled = enabled };
            item.Click += handler;
            return item;
        }
        MenuItem Command(string title, System.Windows.Input.ICommand command) =>
            new() { Header = title, Command = command };
        var items = new System.Collections.Generic.List<object>();
        if (connection != null)
            items.Add(Action("Удалить связь", (_, _) =>
            {
                if (vm.CanEditGraph) vm.DeleteConnection(connection);
            }, vm.CanEditGraph));
        items.Add(Action(_isMultiSelect ? "✓ Выбирать несколько" : "Выбирать несколько",
            (_, _) => { if (vm.CanEditGraph) _isMultiSelect = !_isMultiSelect; }, vm.CanEditGraph));
        items.Add(Action("Выделить всё", OnSelectAllClick, vm.CanEditGraph));
        items.Add(Action("Снять выделение", OnClearSelectionClick));
        items.Add(new Separator());
        items.Add(Action("Копировать", OnCopyNodesClick, vm.CanEditGraph && vm.SelectedNodes.Count > 0));
        items.Add(Action("Вставить", OnPasteNodesClick, vm.CanPaste));
        items.Add(Action("Дублировать", OnDuplicateNodesClick, vm.CanEditGraph && vm.SelectedNodes.Count > 0));
        items.Add(Action("Удалить", OnDeleteSelectionClick, vm.CanEditGraph &&
            (vm.SelectedNodes.Count > 0 || vm.SelectedConnection != null)));
        items.Add(Command("Отменить", vm.UndoCommand));
        items.Add(Command("Повторить", vm.RedoCommand));
        items.Add(Command("Очистить граф", vm.ClearGraphCommand));
        items.Add(new Separator());
        items.Add(Command("Сохранить профиль", vm.SaveGraphCommand));
        items.Add(Command("Назад к родительскому графу", vm.GoBackGraphCommand));
        items.Add(new MenuItem { Header = "Масштаб", ItemsSource = new object[]
        {
            Action("Увеличить", OnZoomInClick), Action("Уменьшить", OnZoomOutClick),
            Action("Центрировать граф", OnCenterGraphClick)
        }});
        var runItems = new System.Collections.Generic.List<object>
        {
            Command("Запустить", vm.RunGraphCommand), Command("Пауза", vm.PauseTestCommand),
            Command("Продолжить", vm.ResumeTestCommand), Command("Остановить", vm.StopTestCommand),
            Command("С начала", vm.ClearPartialRunCommand)
        };
        if (node != null)
        {
            runItems.Add(new Separator());
            runItems.Add(Action("Выполнить только эту ноду", (_, _) => vm.ConfigureNodeRun(node, true), vm.CanConfigureNodeRun(node)));
            runItems.Add(Action("Начать с этой ноды", (_, _) => vm.ConfigureNodeRun(node, false), vm.CanConfigureNodeRun(node)));
        }
        items.Add(new MenuItem { Header = "Выполнение", ItemsSource = runItems });
        NodeRunMenu?.Close();
        NodeRunMenu = new ContextMenu { ItemsSource = items };
        NodeRunMenu.Open(Editor);
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
