using Avalonia.Controls;
using Avalonia.Interactivity;
using TestBuilder.ViewModels;
using Avalonia.Input;

namespace TestBuilder.Views;

public partial class StationDashboardView : UserControl
{
    internal ContextMenu? ProfileMenu { get; private set; }

    public StationDashboardView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OpenProfileMenu, RoutingStrategies.Tunnel);
    }

    private void OpenProfileMenu(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed || DataContext is not TestViewModel vm) return;
        var element = this.InputHitTest(e.GetPosition(this)) as Control;
        while (element != null)
        {
            if (element.DataContext is StationProfileOptionViewModel option)
            {
                ProfileMenu?.Close();
                ProfileMenu = new ContextMenu { ItemsSource = new[] { ProfileMenuItems.Current(vm, option.Profile) } };
                ProfileMenu.Open(element);
                e.Handled = true;
                return;
            }
            element = element.Parent as Control;
        }
    }

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
