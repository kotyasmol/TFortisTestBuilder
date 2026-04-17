using Avalonia.Controls;
using Avalonia.Input;
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

    public TestView()
    {
        InitializeComponent();
        Editor.AddHandler(DragDrop.DropEvent, OnDropNode);
    }

    // јвтопрокрутка логов вниз
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TestViewModel vm)
            vm.TestingLogger.Entries.CollectionChanged += Entries_CollectionChanged;
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.UIThread.Post(() =>
            {
                //LogScrollViewer?.ScrollToEnd(); --- вернутьс€ позже.
            });
    }

    // Drag and drop Ч нажатие на ноду в панели
    public void OnNodePressed(object? sender, PointerPressedEventArgs e)
    {
        _leftButtonPressed = e.GetCurrentPoint(this).Properties.PointerUpdateKind ==
                             PointerUpdateKind.LeftButtonPressed;
    }

    // Drag and drop Ч движение мыши (как в Calculator)
    public void OnNodeDrag(object? sender, PointerEventArgs e)
    {
        if (_leftButtonPressed && sender is Nodify.Node node && node.DataContext is NodeViewModel vm)
        {
            var nodeType = vm.Title;
            var data = new DataObject();
            data.Set("NodeType", nodeType);
            DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        }
    }

    // Drag and drop Ч курсор вышел за пределы ноды
    public void OnNodeExited(object? sender, PointerEventArgs e)
    {
        _leftButtonPressed = false;
    }

    // Drag and drop Ч отпускание на редакторе
    private void OnDropNode(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("NodeType") is string nodeType && DataContext is TestViewModel vm)
        {
            var location = Editor.GetLocationInsideEditor(e);
            vm.AddNodeAtLocation(nodeType, location);
            e.Handled = true;
        }
    }
}