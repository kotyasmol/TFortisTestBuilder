using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TestBuilder.Views;

public partial class OperatorActionDialog : Window
{
    public bool Confirmed { get; private set; }

    public OperatorActionDialog(string message)
    {
        DataContext = new OperatorActionDialogModel(message);
        InitializeComponent();
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close();
    }
}

public record OperatorActionDialogModel(string Message);
