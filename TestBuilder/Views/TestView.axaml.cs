using Avalonia.Controls;
using Avalonia.Threading;
using System.Collections.Specialized;
using System.Threading.Tasks;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class TestView : UserControl
{
    private TestViewModel? _viewModel;

    public TestView()
    {
        InitializeComponent();

        _viewModel = new TestViewModel();
        DataContext = _viewModel;

        // Автопрокрутка логов вниз
        if (_viewModel != null)
        {
            _viewModel.TestingLogger.Entries.CollectionChanged += Entries_CollectionChanged;
        }

       
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() =>
            {
                LogScrollViewer?.ScrollToEnd();
            }, DispatcherPriority.Background);
        }
    }
}
