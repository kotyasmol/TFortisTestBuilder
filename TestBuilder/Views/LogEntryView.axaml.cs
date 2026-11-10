using Avalonia.Controls;
using Avalonia.Interactivity;
using TestBuilder.Services.Logging;

namespace TestBuilder.Views;

public partial class LogEntryView : UserControl
{
    public LogEntryView() => InitializeComponent();

    private void OnOpenErrorHelp(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LogEntry { Code: { } code }) UserGuideWindow.ShowErrorHelp(this, code);
    }
}
