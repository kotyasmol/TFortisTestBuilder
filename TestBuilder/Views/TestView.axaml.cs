using Avalonia;
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

    // ѕодписка на KeyDown окна при по€влении в дереве
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.KeyDown += OnWindowKeyDown;
    }

    // ќтписка при уходе из дерева
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.KeyDown -= OnWindowKeyDown;
    }

    // Delete Ч удал€ем выделенные ноды
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && DataContext is TestViewModel vm)
        {
            vm.DeleteSelectedNodesCommand.Execute(null);
            e.Handled = true;
        }
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

    //  нопка ќчистить Ч выдел€ем все и удал€ем через тот же механизм что и Delete
    public void OnClearGraph(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not TestViewModel vm)
            return;

        Editor.SelectAll();

        Dispatcher.UIThread.Post(() =>
        {
            vm.DeleteSelectedNodesCommand.Execute(null);
        }, DispatcherPriority.Background);
    }
}