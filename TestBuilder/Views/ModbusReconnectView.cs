using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TestBuilder.Views;

public sealed class ModbusReconnectView : WorkspacePage
{
    private readonly TextBlock _messageText;
    private readonly Button _closeButton;

    public ModbusReconnectView()
    {
        MaxWidth = 720;
        Title = "Проблема связи Modbus";

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
