using Avalonia.Controls;
using Avalonia.Interactivity;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public partial class PingMonitorView : UserControl
{
    public PingMonitorView() => InitializeComponent();
    private async void OnConfigure(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TestViewModel vm && TopLevel.GetTopLevel(this) is Window owner)
            await vm.ConfigurePingCommand.ExecuteAsync(owner);
    }
}
