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
    private TestViewModel? _currentVm;

    public TestView()
    {
        InitializeComponent();
        Editor.AddHandler(DragDrop.DropEvent, OnDropNode);

        // —брасываем состо€ние editor'а когда вкладка становитс€ видимой снова.
        // Avalonia не уничтожает контент TabItem при переключении Ч он просто скрываетс€.
        // ≈сли при уходе с вкладки editor захватил мышь или осталс€ в состо€нии
        // Selecting/Panning, клики по коннекторам перестают работать.
        this.GetObservable(IsVisibleProperty).Subscribe(isVisible =>
        {
            if (isVisible)
                Editor.PopAllStates();
        });
    }

    // ѕодписка на глобальные KeyDown (Delete дл€ удалени€ нод)
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.KeyDown += OnWindowKeyDown;
    }

    // ќтписка при удалении из visual tree
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel != null)
            topLevel.KeyDown -= OnWindowKeyDown;

        // —брасываем незавершЄнное соединение на случай если view уничтожаетс€
        _currentVm?.PendingConnection.Reset();
    }

    // Delete удал€ет выделенные ноды
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && DataContext is TestViewModel vm)
        {
            vm.DeleteSelectedNodesCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ”правление подпиской на лог Ч с корректной отпиской от предыдущего VM
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // ќтписываемс€ от предыдущего VM чтобы не было утечки подписок
        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged -= Entries_CollectionChanged;

        _currentVm = DataContext as TestViewModel;

        if (_currentVm != null)
            _currentVm.TestingLogger.Entries.CollectionChanged += Entries_CollectionChanged;
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
            Dispatcher.UIThread.Post(() =>
            {
                //LogScrollViewer?.ScrollToEnd();
            });
    }

    // Drag and drop Ч фиксируем нажатие левой кнопки
    public void OnNodePressed(object? sender, PointerPressedEventArgs e)
    {
        _leftButtonPressed = e.GetCurrentPoint(this).Properties.PointerUpdateKind ==
                             PointerUpdateKind.LeftButtonPressed;
    }

    // Drag and drop Ч начинаем перетаскивание ноды
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

    // Drag and drop Ч сбрасываем флаг нажати€
    public void OnNodeExited(object? sender, PointerEventArgs e)
    {
        _leftButtonPressed = false;
    }

    // Drag and drop Ч принимаем ноду на холст
    private void OnDropNode(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("NodeType") is string nodeType && DataContext is TestViewModel vm)
        {
            var location = Editor.GetLocationInsideEditor(e);
            vm.AddNodeAtLocation(nodeType, location);
            e.Handled = true;
        }
    }

    //  нопка "ќчистить граф" Ч удал€ет все ноды через стандартный механизм Delete
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