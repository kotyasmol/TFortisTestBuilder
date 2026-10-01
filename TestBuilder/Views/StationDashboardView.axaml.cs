using Avalonia.Controls;
using Avalonia.Interactivity;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class StationDashboardView : UserControl
{
    public StationDashboardView() => InitializeComponent();

    private void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel vm)
            vm.OpenSettingsCommand.Execute(null);
    }
}
