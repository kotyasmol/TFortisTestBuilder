using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TestBuilder.Models;
using TestBuilder.Presentation;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class BoardPreviewView : UserControl
{
    private TestViewModel? _vm;
    private BoardDesign? _design;
    private readonly Dictionary<string, string> _states = new(StringComparer.Ordinal);
    private bool _attached;
    internal bool AllowExpand { set => ExpandButton.IsVisible = value; }
    internal string DisplayedState(string id) => _states.GetValueOrDefault(id, "Не проверен");

    public BoardPreviewView()
    {
        InitializeComponent();
        BoardSurface.State = DisplayedState;
        BoardSurface.ViewChanged += UpdateZoom;
        BoardSurface.RegionSelected += _ => UpdateSelection();
        DataContextChanged += (_, _) => { if (_attached) BindModel(); };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); _attached = true; BindModel();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        if (_vm != null) _vm.BoardChanged -= RefreshBoard;
        _vm = null; _design = null; _states.Clear(); BoardSurface.Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    private void BindModel()
    {
        if (_vm != null) _vm.BoardChanged -= RefreshBoard;
        _vm = DataContext as TestViewModel;
        if (_vm != null) _vm.BoardChanged += RefreshBoard;
        RefreshBoard();
    }

    private void RefreshBoard()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => { if (_attached) RefreshBoard(); }); return; }
        var design = _vm?.IsBoardProfile == true ? _vm.RootGraph.Board : null;
        if (!ReferenceEquals(design, _design)) { BoardSurface.SelectedId = null; BoardSurface.ResetView(); }
        _design = design;
        _states.Clear();
        if (design != null && _vm != null)
            foreach (var region in design.Regions) _states[region.Id] = _vm.GetBoardRegionState(region.Id);
        var hasImage = !string.IsNullOrEmpty(design?.ImageBase64);
        try
        {
            BoardSurface.SetDesign(design ?? new BoardDesign());
            EmptyMessage.Text = "В профиле нет изображения платы. Добавьте его в редакторе через кнопку «Плата».";
        }
        catch (Exception ex)
        {
            hasImage = false; EmptyMessage.Text = "Не удалось показать изображение платы: " + ex.Message;
        }
        EmptyMessage.IsVisible = !hasImage;
        BoardSurface.IsVisible = hasImage;
        ZoomInButton.IsEnabled = ZoomOutButton.IsEnabled = FitButton.IsEnabled = hasImage;
        UpdateZoom(); UpdateSelection();
    }

    private void UpdateSelection()
    {
        var region = _design?.Regions.FirstOrDefault(r => r.Id == BoardSurface.SelectedId);
        ElementText.Text = region == null ? "Нажмите на элемент, чтобы увидеть результат." :
            $"{region.Id} · {RussianUiText.Display(region.Name)} · {DisplayedState(region.Id)}";
        ToolTip.SetTip(ElementText, ElementText.Text);
    }

    private void UpdateZoom()
    {
        ZoomText.Text = $"{BoardSurface.Zoom * 100:0}%";
        ZoomOutButton.IsEnabled = BoardSurface.IsVisible && BoardSurface.Zoom > 1;
        ZoomInButton.IsEnabled = BoardSurface.IsVisible && BoardSurface.Zoom < 8;
    }
    private void ZoomIn(object? sender, RoutedEventArgs e) => BoardSurface.ZoomIn();
    private void ZoomOut(object? sender, RoutedEventArgs e) => BoardSurface.ZoomOut();
    private void Fit(object? sender, RoutedEventArgs e) => BoardSurface.ResetView();
    private void Expand(object? sender, RoutedEventArgs e)
    {
        if (_vm == null || TopLevel.GetTopLevel(this) is not Window owner) return;
        var existing = owner.OwnedWindows.OfType<BoardPreviewWindow>().FirstOrDefault();
        if (existing != null) existing.Activate();
        else new BoardPreviewWindow(_vm).Show(owner);
    }
}
