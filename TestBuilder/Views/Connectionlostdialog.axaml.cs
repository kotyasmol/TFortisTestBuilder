using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TestBuilder.Views;

public partial class ConnectionLostDialog : Window
{
    public bool ShouldReconnect { get; private set; }

    public ConnectionLostDialog()
    {
        InitializeComponent();
    }

    private void OnReconnect(object? sender, RoutedEventArgs e)
    {
        ShouldReconnect = true;
        Close();
    }

    private void OnDisconnect(object? sender, RoutedEventArgs e)
    {
        ShouldReconnect = false;
        Close();
    }
}