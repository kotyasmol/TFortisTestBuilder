using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Specialized;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class TestView : UserControl
{
    public TestView()
    {
        InitializeComponent();
    }

    // Автопрокрутка логов вниз
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is TestViewModel vm)
        {
            vm.TestingLogger.Entries.CollectionChanged += Entries_CollectionChanged;
        }
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() =>
            {
                //LogScrollViewer?.ScrollToEnd(); --- вернуться позже. Пока не работает, возможно из-за структуры разметки.
            });
        }
    }
}
