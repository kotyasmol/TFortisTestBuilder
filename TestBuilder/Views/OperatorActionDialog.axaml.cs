using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TestBuilder.Views;

public partial class OperatorActionDialog : Window
{
    public bool Confirmed { get; private set; }
    public string InputText => InputBox.Text ?? "";

    public OperatorActionDialog(string message, bool requestInput = false)
    {
        DataContext = new OperatorActionDialogModel(message);
        InitializeComponent();
        InputBox.IsVisible = requestInput;
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
