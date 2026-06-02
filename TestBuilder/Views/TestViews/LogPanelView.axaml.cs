using Avalonia.Controls;
using TestBuilder.ViewModels;

namespace TestBuilder.Views.TestViews;

public partial class LogPanelView : UserControl
{
    public LogPanelView()
    {
        InitializeComponent();
    }

    private void OnClearLogs(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is TestViewModel vm)
            vm.TestingLogger.Clear();
    }
}
