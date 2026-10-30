using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Views;

public partial class FailureCleanupOrderWindow : Window
{
    private readonly DispatcherTimer _scrollTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private FailureCleanupItemViewModel? _pressedItem;
    private IPointer? _pointer;
    private Point _pressPosition;
    private Point _position;
    private bool _dragging;

    public FailureCleanupOrderWindow() : this(new FailureCleanupOrderViewModel(Array.Empty<SubtestNodeViewModel>())) { }
    public FailureCleanupOrderWindow(FailureCleanupOrderViewModel draft)
    {
        InitializeComponent();
        DataContext = draft;
        _scrollTimer.Tick += (_, _) => ScrollWhileDragging();
        Deactivated += (_, _) => CancelDrag();
        Closed += (_, _) => CancelDrag();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (_pressedItem != null) CancelDrag(); else Close();
        };
    }

    private FailureCleanupOrderViewModel Draft => (FailureCleanupOrderViewModel)DataContext!;

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ||
            sender is not Control { DataContext: FailureCleanupItemViewModel item } || Draft.Items.Count < 2) return;
        CancelDrag();
        _pressedItem = item;
        _pressPosition = _position = e.GetPosition(this);
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressedItem == null || e.Pointer != _pointer) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { CancelDrag(); return; }
        _position = e.GetPosition(this);
        var delta = _position - _pressPosition;
        if (!_dragging && delta.X * delta.X + delta.Y * delta.Y < 36) return;
        _dragging = true;
        _pressedItem.IsDragging = true;
        _scrollTimer.Start();
        UpdateDropTarget();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressedItem == null || e.Pointer != _pointer || e.InitialPressMouseButton != MouseButton.Left) return;
        _position = e.GetPosition(this);
        var item = _pressedItem;
        var slot = _dragging ? FindDropSlot() : -1;
        var from = Draft.Items.IndexOf(item);
        CancelDrag();
        if (slot >= 0 && from >= 0) Draft.MoveTo(item, slot > from ? slot - 1 : slot);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelDrag();
    }

    private Rect ViewportBounds => new(OrderScroll.TranslatePoint(default, this) ?? default, OrderScroll.Bounds.Size);

    private int FindDropSlot()
    {
        if (!ViewportBounds.Contains(_position)) return -1;
        foreach (var row in OrderItems.GetVisualDescendants().OfType<Grid>().Where(g => g.Name == "CleanupOrderRow"))
        {
            if (row.DataContext is not FailureCleanupItemViewModel item || row.TranslatePoint(default, this) is not { } origin) continue;
            if (_position.Y < origin.Y + row.Bounds.Height / 2) return Draft.Items.IndexOf(item);
        }
        return Draft.Items.Count;
    }

    private void UpdateDropTarget()
    {
        foreach (var item in Draft.Items) { item.DropBefore = false; item.DropAfter = false; }
        var slot = FindDropSlot();
        var from = _pressedItem == null ? -1 : Draft.Items.IndexOf(_pressedItem);
        if (slot < 0 || from < 0 || slot == from || slot == from + 1) return;
        if (slot < Draft.Items.Count) Draft.Items[slot].DropBefore = true;
        else Draft.Items[^1].DropAfter = true;
    }

    private void ScrollWhileDragging()
    {
        var bounds = ViewportBounds;
        if (!_dragging || !bounds.Contains(_position)) return;
        var delta = _position.Y < bounds.Top + 32 ? -20 : _position.Y > bounds.Bottom - 32 ? 20 : 0;
        var offset = Math.Clamp(OrderScroll.Offset.Y + delta, 0, Math.Max(0, OrderScroll.Extent.Height - OrderScroll.Viewport.Height));
        if (offset == OrderScroll.Offset.Y) return;
        OrderScroll.Offset = new Vector(OrderScroll.Offset.X, offset);
        UpdateLayout();
        UpdateDropTarget();
    }

    private void CancelDrag()
    {
        _scrollTimer.Stop();
        var pointer = _pointer;
        _pointer = null;
        _pressedItem = null;
        _dragging = false;
        foreach (var item in Draft.Items) { item.IsDragging = false; item.DropBefore = false; item.DropAfter = false; }
        if (pointer?.Captured == this) pointer.Capture(null);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    private void OnApply(object? sender, RoutedEventArgs e) => Close(((FailureCleanupOrderViewModel)DataContext!).BuildResult());
}
