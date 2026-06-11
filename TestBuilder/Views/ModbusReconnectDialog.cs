using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TestBuilder.Views;

public sealed class ModbusReconnectDialog : Window
{
    private readonly TextBlock _messageText;
    private readonly Button _closeButton;

    public ModbusReconnectDialog()
    {
        Title = "Проблема связи Modbus";
        Width = 420;
        Height = 190;
        MinWidth = 420;
        MinHeight = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;

        _messageText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        };

        _closeButton = new Button
        {
            Content = "Понятно",
            HorizontalAlignment = HorizontalAlignment.Right,
            IsEnabled = false,
            MinWidth = 100
        };
        _closeButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "Связь со стендом потеряна",
                    FontSize = 16,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                _messageText,
                _closeButton
            }
        };
    }

    public void SetMessage(string message, bool canClose)
    {
        _messageText.Text = message;
        _closeButton.IsEnabled = canClose;
    }
}
