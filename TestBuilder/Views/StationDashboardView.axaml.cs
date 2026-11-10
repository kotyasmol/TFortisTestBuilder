using Avalonia.Controls;
using Avalonia.Interactivity;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class StationDashboardView : UserControl
{
    public StationDashboardView() => InitializeComponent();

    private void OpenErrorHelp(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TestViewModel vm && vm.Station.ErrorCode is { } code)
            UserGuideWindow.ShowErrorHelp(this, code);
    }

    private void OpenSettings(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel vm)
            vm.OpenSettingsCommand.Execute(null);
    }
}
