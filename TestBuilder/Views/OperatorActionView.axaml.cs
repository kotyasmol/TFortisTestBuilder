using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TestBuilder.Views;

public partial class OperatorActionView : WorkspacePage
{
    public bool Confirmed { get; private set; }
    public string InputText => InputBox.Text ?? "";

    public OperatorActionView(string message, bool requestInput = false)
    {
        DataContext = new OperatorActionViewModel(message);
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

public record OperatorActionViewModel(string Message);
