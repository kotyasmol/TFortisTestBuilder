using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using TestBuilder.Converters;
using TestBuilder.Presentation;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using TestBuilder.Models;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class BoardWindow : Window
{
    private readonly TestViewModel _vm;
    private readonly bool _editing;
    private BoardDesign _draft;
    private readonly BoardCanvas _canvas = new();
    private readonly DataGrid _regions = new() { AutoGenerateColumns = false, MinHeight = 180, CanUserReorderColumns = false, CanUserResizeColumns = true };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _board = new() { Content = "Этот профиль проверяет плату" };
    private BoardRegion? _clipboardRegion;
    public BoardWindow(TestViewModel vm, bool editing)
    {
        _vm = vm; _editing = editing; _draft = TestViewModel.CopyDefinition(vm.RootGraph.Board ?? new BoardDesign());
        Title = editing ? "Редактор платы" : "Результаты на плате"; Width = 1200; Height = 800; MinWidth = 720; MinHeight = 500;
        _board.IsChecked = vm.IsBoardProfile; _board.IsEnabled = editing;
        _canvas.DrawEnabled = editing;
        _canvas.RegionDrawn += region =>
        {
            region.Id = "element" + (_draft.Regions.Count + 1);
            while (_draft.Regions.Any(r => r.Id == region.Id)) region.Id += "_";
            _draft.Regions.Add(region);
            Refresh();
            _canvas.SelectedId = region.Id;
            _regions.SelectedItem = region;
            _canvas.Focus();
        };
        _canvas.State = vm.GetBoardRegionState;
        _canvas.RegionChanged += region =>
        {
            Refresh(); // BoardRegion is a plain JSON model; refresh the coordinate cells after dragging.
            _regions.SelectedItem = region;
            _regions.ScrollIntoView(region, null);
        };
        foreach (var name in editing ? new[] { "Id", "Name", "X", "Y", "Width", "Height" } : new[] { "Id", "Name" })
            _regions.Columns.Add(new DataGridTextColumn { Header = name switch { "Id" => "Код", "Name" => "Название", "Width" => "Ширина", "Height" => "Высота", _ => name }, Binding = new Binding(name) { Mode = BindingMode.TwoWay, Converter = name == "Name" ? new RussianUiTextConverter() : null, StringFormat = name is "Id" or "Name" ? null : "{0:0.####}" }, Width = editing ? new DataGridLength(name is "Id" or "Name" ? 115 : 65) : new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = name is "Id" or "Name" ? 95 : 58 });
        _regions.IsReadOnly = !editing;
        if (!editing) _regions.Columns.Add(new DataGridTextColumn { Header = "Результат", Binding = new Binding("Id") { Converter = new FuncValueConverter<string, string>(id => vm.GetBoardRegionState(id ?? "")) }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        _regions.SelectionChanged += (_, _) => { _canvas.SelectedId = (_regions.SelectedItem as BoardRegion)?.Id; _canvas.InvalidateVisual(); };
        _canvas.RegionSelected += region => { _regions.SelectedItem = region; _regions.ScrollIntoView(region, null); };
        _regions.CellEditEnded += (_, _) => _canvas.InvalidateVisual();
        var root = new DockPanel { Margin = new Thickness(16) };
        var top = new StackPanel { Spacing = 8 }; top.Children.Add(_board);
        top.Children.Add(new TextBlock { Text = editing ? "Загрузите фото и обведите элемент мышью. Перетащите область за середину, чтобы передвинуть её, или за угловой маркер, чтобы изменить размер. На свободном месте можно нарисовать новую область. Правая кнопка перемещает изображение, колесо меняет масштаб. Координаты и размер можно уточнить в таблице. В ноде нажмите шестерёнку «Настройки ноды» и укажите код области в поле «Элементы платы через запятую». Один элемент можно связать с несколькими проверками." : "Серый — не проверен; жёлтый — проверяется; зелёный — все связанные проверки пройдены; красный — отказ. Непривязанный элемент не получает результат. Колесо мыши — масштаб; правая кнопка — перемещение; щелчок — выбор элемента.", TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel();
        if (editing)
        {
            buttons.Children.Add(Button("Загрузить изображение", async () => {
                try {
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Изображение платы", AllowMultiple = false, FileTypeFilter = new[] { FilePickerFileTypes.ImageAll } });
                    if (files.Count == 0) return;
                    using var input = await files[0].OpenReadAsync();
                    if (input.Length > 10_000_000) throw new InvalidOperationException("Максимальный размер изображения — 10 МБ.");
                    using var bytes = new MemoryStream(); await input.CopyToAsync(bytes);
                    BoardImage.Validate(bytes.ToArray());
                    _draft.ImageBase64 = Convert.ToBase64String(bytes.ToArray()); Refresh();
                } catch (Exception ex) { _status.Text = "Изображение не загружено: " + ex.Message; }
            }));
            buttons.Children.Add(Button("Добавить область", () => { _draft.Regions.Add(new BoardRegion { Id = Guid.NewGuid().ToString("N")[..8], X = .1, Y = .1 }); Refresh(); }));
            buttons.Children.Add(Button("Удалить область", DeleteSelectedRegion));
            buttons.Children.Add(Button("Применить к профилю", () => { try { if (!_regions.CommitEdit()) throw new InvalidOperationException("Исправьте координаты области."); _vm.ApplyBoard(_draft, _board.IsChecked == true); Close(); } catch (Exception ex) { _status.Text = ex.Message; } }));
        }
        if (!editing) buttons.Children.Add(Button("Показать целиком", _canvas.ResetView));
        buttons.Children.Add(Button(editing ? "Отмена" : "Закрыть", Close)); top.Children.Add(buttons);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var split = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*") };
        split.Children.Add(_canvas);
        var tablePanel = new DockPanel { MinWidth = 0 };
        var tableHeader = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 0, 0, 6) };
        tableHeader.Children.Add(new TextBlock { Text = editing ? "Элементы платы" : "Результаты проверки", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        tableHeader.Children.Add(new TextBlock { Text = editing ? "Ctrl+C — копировать · Ctrl+V — вставить · Delete — удалить" : "", Opacity = .75, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(tableHeader, Dock.Top); tablePanel.Children.Add(tableHeader);
        var tableScroll = new ScrollViewer { Content = _regions, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        tablePanel.Children.Add(tableScroll);
        Grid.SetColumn(tablePanel, 1); split.Children.Add(tablePanel); root.Children.Add(split);
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Content = root; vm.BoardChanged += OnBoardChanged; Closed += (_, _) => { vm.BoardChanged -= OnBoardChanged; _canvas.Dispose(); }; Refresh();
    }
    private void OnBoardChanged() { if (!_editing) { _draft = _vm.RootGraph.Board ?? new BoardDesign(); Refresh(); } else _canvas.InvalidateVisual(); }
    private void Refresh()
    {
        try { var selected = (_regions.SelectedItem as BoardRegion)?.Id; _canvas.SetDesign(_draft); _regions.ItemsSource = null; _regions.ItemsSource = _draft.Regions; if (selected != null) _regions.SelectedItem = _draft.Regions.FirstOrDefault(r => r.Id == selected); _status.Text = _editing ? "После применения сохраните профиль в редакторе графов." : "Элементов: " + _draft.Regions.Count + " · " + string.Join(" · ", _draft.Regions.GroupBy(r => _vm.GetBoardRegionState(r.Id)).Select(g => g.Key + ": " + g.Count())); }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
    private static Button Button(string title, Action action) { var b = new Button { Content = title, Margin = new Thickness(0, 4, 8, 4) }; b.Click += (_, _) => action(); return b; }
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_editing || e.Source is TextBox) return;
        var modifiers = e.KeyModifiers;
        if ((modifiers == KeyModifiers.Control || modifiers == KeyModifiers.Meta) && e.Key == Key.C)
        {
            if (_regions.SelectedItem is BoardRegion selected) _clipboardRegion = CopyRegion(selected);
            e.Handled = true;
        }
        else if ((modifiers == KeyModifiers.Control || modifiers == KeyModifiers.Meta) && e.Key == Key.V)
        {
            if (_clipboardRegion is { } copied) PasteRegion(copied);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            DeleteSelectedRegion();
            e.Handled = true;
        }
    }
    private static BoardRegion CopyRegion(BoardRegion region) => new() { Id = region.Id, Name = region.Name, X = region.X, Y = region.Y, Width = region.Width, Height = region.Height };
    private void PasteRegion(BoardRegion source)
    {
        var baseId = string.IsNullOrWhiteSpace(source.Id) ? "element" : source.Id;
        var id = baseId + "_копия";
        for (var suffix = 2; _draft.Regions.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)); suffix++) id = baseId + "_копия" + suffix;
        var copy = CopyRegion(source);
        copy.Id = id;
        copy.Name = string.IsNullOrWhiteSpace(source.Name) ? "Элемент" : source.Name + " (копия)";
        copy.X = Math.Min(.95, source.X + .02);
        copy.Y = Math.Min(.95, source.Y + .02);
        copy.Width = Math.Min(source.Width, 1 - copy.X);
        copy.Height = Math.Min(source.Height, 1 - copy.Y);
        _draft.Regions.Add(copy);
        Refresh();
        _canvas.SelectedId = copy.Id;
        _regions.SelectedItem = copy;
        _regions.ScrollIntoView(copy, null);
        _canvas.InvalidateVisual();
    }
    private void DeleteSelectedRegion()
    {
        if (_regions.SelectedItem is not BoardRegion region) return;
        _draft.Regions.Remove(region);
        Refresh();
        _canvas.SelectedId = (_regions.SelectedItem as BoardRegion)?.Id;
        _canvas.InvalidateVisual();
    }
}

public sealed class BoardCanvas : Control, IDisposable
{
    private Bitmap? _image;
    private string _imageData = "";
    private BoardDesign _design = new();
    private Point? _start;
    private Point? _end;
    private Point? _panStart;
    private Point? _pressPoint;
    private BoardRegion? _movingRegion;
    private Rect _movingRegionStart;
    private BoardRegion? _resizingRegion;
    private Rect _resizingRegionStart;
    private ResizeCorner? _resizeCorner;
    private bool _panned;
    private Vector _offset;
    private double _zoom = 1;
    private enum ResizeCorner { TopLeft, TopRight, BottomRight, BottomLeft }
    public double Zoom => _zoom;
    public event Action? ViewChanged;
    public string? SelectedId { get; set; }
    public event Action<BoardRegion>? RegionSelected;
    public event Action<BoardRegion>? RegionChanged;
    public BoardCanvas() { ClipToBounds = true; }
    public void ResetView() { _zoom = 1; _offset = default; InvalidateVisual(); ViewChanged?.Invoke(); }
    public void ZoomIn() => ZoomAt(1.25, new Point(Bounds.Width / 2, Bounds.Height / 2));
    public void ZoomOut() => ZoomAt(1 / 1.25, new Point(Bounds.Width / 2, Bounds.Height / 2));
    private void ZoomAt(double factor, Point anchor)
    {
        var old = _zoom; _zoom = Math.Clamp(_zoom * factor, 1, 8);
        var point = anchor - new Point(Bounds.Width / 2, Bounds.Height / 2);
        _offset = point + (_offset - point) * (_zoom / old);
        if (_zoom == 1) _offset = default;
        InvalidateVisual(); ViewChanged?.Invoke();
    }
    public bool DrawEnabled { get; set; }
    public Func<string, string>? State { get; set; }
    public event Action<BoardRegion>? RegionDrawn;
    public void SetDesign(BoardDesign design)
    {
        _design = design;
        if (_imageData != design.ImageBase64)
        {
            Bitmap? next = null;
            if (design.ImageBase64.Length > 0)
            {
                var bytes = Convert.FromBase64String(design.ImageBase64); BoardImage.Validate(bytes);
                using var data = SkiaSharp.SKData.CreateCopy(bytes);
                using var codec = SkiaSharp.SKCodec.Create(data);
                using var stream = new MemoryStream(bytes);
                next = Bitmap.DecodeToWidth(stream, Math.Min(codec.Info.Width, 4096));
            }
            _image?.Dispose(); _image = next; _imageData = design.ImageBase64;
            ResetView();
        }
        InvalidateVisual();
    }
    private Rect ImageRect
    {
        get
        {
            var ratio = _image == null ? 1.4 : _image.Size.Width / _image.Size.Height;
            var w = Math.Min(Bounds.Width, Bounds.Height * ratio) * _zoom; var h = w / ratio;
            return new Rect((Bounds.Width - w) / 2 + _offset.X, (Bounds.Height - h) / 2 + _offset.Y, w, h);
        }
    }
    public override void Render(DrawingContext dc)
    {
        var rect = ImageRect;
        dc.FillRectangle(Brushes.DimGray, rect);
        if (_image != null) dc.DrawImage(_image, new Rect(_image.Size), rect);
        foreach (var region in _design.Regions)
        {
            var color = State?.Invoke(region.Id) switch { "Годен" => Colors.LimeGreen, "Ошибка" => Colors.Red, "Проверяется" => Colors.Gold, _ => Colors.Gray };
            var box = new Rect(rect.X + region.X * rect.Width, rect.Y + region.Y * rect.Height, Math.Max(1, region.Width * rect.Width), Math.Max(1, region.Height * rect.Height));
            dc.DrawRectangle(new SolidColorBrush(color, .25), new Pen(region.Id == SelectedId ? Brushes.Cyan : new SolidColorBrush(color), region.Id == SelectedId ? 3 : 1.5), box);
            if (DrawEnabled && region.Id == SelectedId)
            {
                const double handleSize = 12;
                foreach (var corner in Enum.GetValues<ResizeCorner>())
                {
                    var point = CornerPoint(box, corner);
                    dc.DrawRectangle(Brushes.White, new Pen(Brushes.Cyan, 2), new Rect(point.X - handleSize / 2, point.Y - handleSize / 2, handleSize, handleSize));
                }
            }
            if (region.Id != SelectedId && (box.Width < 65 || box.Height < 25)) continue;
            var label = new FormattedText(region.Id, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Brushes.White);
            dc.FillRectangle(Brushes.Black, new Rect(box.TopLeft, new Size(label.Width + 4, label.Height)));
            dc.DrawText(label, box.TopLeft + new Vector(2, 0));
        }
        if (_start is { } a && _end is { } b) dc.DrawRectangle(null, new Pen(Brushes.Cyan, 2), new Rect(a, b).Normalize());
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var p = e.GetPosition(this);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) { _panStart = p; e.Pointer.Capture(this); e.Handled = true; return; }
        if (!ImageRect.Contains(p) || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (!DrawEnabled) { _pressPoint = _panStart = p; _panned = false; e.Pointer.Capture(this); e.Handled = true; return; }
        var hit = HitRegion(p);
        if (DrawEnabled && hit != null && hit.Id == SelectedId && HitResizeCorner(p, hit) is { } corner)
        {
            _resizingRegion = hit;
            _resizingRegionStart = new Rect(hit.X, hit.Y, hit.Width, hit.Height);
            _resizeCorner = corner;
            _pressPoint = p;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
        if (hit is { } region)
        {
            SelectedId = region.Id;
            RegionSelected?.Invoke(region);
            _movingRegion = region;
            _movingRegionStart = new Rect(region.X, region.Y, region.Width, region.Height);
            _pressPoint = p;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        _start = _end = p; e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e); var p = e.GetPosition(this);
        if (_resizingRegion is { } resizing && _resizeCorner is { } corner && _pressPoint is { } resizeStart)
        {
            var image = ImageRect;
            if (image.Width <= 0 || image.Height <= 0) return;
            var dx = (p.X - resizeStart.X) / image.Width;
            var dy = (p.Y - resizeStart.Y) / image.Height;
            const double minSize = .002;
            var left = _resizingRegionStart.Left;
            var top = _resizingRegionStart.Top;
            var right = _resizingRegionStart.Right;
            var bottom = _resizingRegionStart.Bottom;
            if (corner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft)
                left = Math.Clamp(_resizingRegionStart.Left + dx, 0, right - minSize);
            else
                right = Math.Clamp(_resizingRegionStart.Right + dx, left + minSize, 1);
            if (corner is ResizeCorner.TopLeft or ResizeCorner.TopRight)
                top = Math.Clamp(_resizingRegionStart.Top + dy, 0, bottom - minSize);
            else
                bottom = Math.Clamp(_resizingRegionStart.Bottom + dy, top + minSize, 1);
            resizing.X = left;
            resizing.Y = top;
            resizing.Width = right - left;
            resizing.Height = bottom - top;
            InvalidateVisual();
            return;
        }
        if (_movingRegion is { } moving && _pressPoint is { } moveStart)
        {
            var image = ImageRect;
            if (image.Width <= 0 || image.Height <= 0) return;
            var dx = (p.X - moveStart.X) / image.Width;
            var dy = (p.Y - moveStart.Y) / image.Height;
            moving.X = Math.Clamp(_movingRegionStart.X + dx, 0, Math.Max(0, 1 - moving.Width));
            moving.Y = Math.Clamp(_movingRegionStart.Y + dy, 0, Math.Max(0, 1 - moving.Height));
            InvalidateVisual();
            return;
        }
        if (_panStart is { } previous)
        {
            if (_pressPoint is { } pressed && !_panned && Math.Abs(p.X - pressed.X) + Math.Abs(p.Y - pressed.Y) < 4) return;
            _panned = true; _offset += p - previous; _panStart = p; InvalidateVisual(); return;
        }
        if (_start == null)
        {
            var region = HitRegion(p);
            ToolTip.SetTip(this, region == null ? null : region.Id + " · " + RussianUiText.Display(region.Name) + " · " + State?.Invoke(region.Id));
            var hoveredCorner = DrawEnabled && region != null && region.Id == SelectedId ? HitResizeCorner(p, region) : null;
            Cursor = hoveredCorner is { } resize ? new Cursor(CursorFor(resize)) : DrawEnabled && region != null ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
            return;
        }
        var r = ImageRect;
        _end = new Point(Math.Clamp(p.X, r.Left, r.Right), Math.Clamp(p.Y, r.Top, r.Bottom)); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!DrawEnabled && !_panned && _pressPoint is { } pressed)
        {
            var region = HitRegion(pressed);
            if (region != null) { SelectedId = region.Id; RegionSelected?.Invoke(region); }
        }
        if (_movingRegion is { } moved)
        {
            var changed = moved.X != _movingRegionStart.X || moved.Y != _movingRegionStart.Y;
            _movingRegion = null;
            _pressPoint = null;
            if (changed) RegionChanged?.Invoke(moved);
        }
        else if (_resizingRegion is { } resized)
        {
            var changed = resized.X != _resizingRegionStart.X || resized.Y != _resizingRegionStart.Y || resized.Width != _resizingRegionStart.Width || resized.Height != _resizingRegionStart.Height;
            _resizingRegion = null;
            _resizeCorner = null;
            _pressPoint = null;
            if (changed) RegionChanged?.Invoke(resized);
        }
        else if (_start is { } a && _end is { } b)
        {
            var box = new Rect(a, b).Normalize(); var r = ImageRect;
            if (box.Width > 4 && box.Height > 4 && r.Width > 0 && r.Height > 0)
                RegionDrawn?.Invoke(new BoardRegion { X = (box.X - r.X) / r.Width, Y = (box.Y - r.Y) / r.Height, Width = box.Width / r.Width, Height = box.Height / r.Height });
        }
        _start = _end = _panStart = _pressPoint = null; _movingRegion = _resizingRegion = null; _resizeCorner = null; _panned = false; e.Pointer.Capture(null); InvalidateVisual();
    }
    private BoardRegion? HitRegion(Point point)
    {
        var r = ImageRect; if (r.Width <= 0 || r.Height <= 0) return null;
        return _design.Regions.LastOrDefault(x => new Rect(r.X + x.X*r.Width, r.Y + x.Y*r.Height, x.Width*r.Width, x.Height*r.Height).Inflate(2).Contains(point));
    }
    private ResizeCorner? HitResizeCorner(Point point, BoardRegion region)
    {
        var r = ImageRect;
        var box = new Rect(r.X + region.X * r.Width, r.Y + region.Y * r.Height, region.Width * r.Width, region.Height * r.Height);
        const double hitRadius = 9;
        foreach (var corner in Enum.GetValues<ResizeCorner>())
        {
            var p = CornerPoint(box, corner);
            if (new Rect(p.X - hitRadius, p.Y - hitRadius, hitRadius * 2, hitRadius * 2).Contains(point)) return corner;
        }
        return null;
    }
    private static Point CornerPoint(Rect box, ResizeCorner corner) => corner switch
    {
        ResizeCorner.TopLeft => box.TopLeft,
        ResizeCorner.TopRight => box.TopRight,
        ResizeCorner.BottomRight => box.BottomRight,
        _ => box.BottomLeft
    };
    private static StandardCursorType CursorFor(ResizeCorner corner) => corner is ResizeCorner.TopLeft or ResizeCorner.BottomRight
        ? StandardCursorType.TopLeftCorner : StandardCursorType.TopRightCorner;
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(Math.Pow(1.2, e.Delta.Y), e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        _start = _end = _panStart = _pressPoint = null; _movingRegion = _resizingRegion = null; _resizeCorner = null; _panned = false;
        base.OnPointerCaptureLost(e); InvalidateVisual();
    }
    public void Dispose() { _image?.Dispose(); _image = null; _imageData = ""; }
}
