using Avalonia.Controls;
using Avalonia.Input;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Views.TestViews;

public partial class NodePaletteView : UserControl
{
    private bool _leftButtonPressed;

    public NodePaletteView()
    {
        InitializeComponent();
    }

    public void OnNodePressed(object? sender, PointerPressedEventArgs e)
    {
        _leftButtonPressed =
            e.GetCurrentPoint(this).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed;
    }

    public void OnNodeDrag(object? sender, PointerEventArgs e)
    {
        if (!_leftButtonPressed)
            return;

        if (sender is not Control control)
            return;

        if (control.DataContext is not NodeViewModel vm)
            return;

        var data = new DataObject();
        data.Set("NodeType", vm.Title);

        DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);

        _leftButtonPressed = false;
    }

    public void OnNodeExited(object? sender, PointerEventArgs e)
    {
        _leftButtonPressed = false;
    }
}