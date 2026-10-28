using Avalonia;
using Avalonia.Controls;

namespace TestBuilder.Views;

public sealed class NetworkAdapterNameWindow : Window
{
    public NetworkAdapterNameWindow(string currentName)
    {
        Title = "Название сетевой карты Windows";
        Width = 560; Height = 220; CanResize = false;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Название карты Windows (нужны права администратора)" });
        var input = new TextBox { Text = currentName }; panel.Children.Add(input);
        var save = new Button { Content = "Переименовать" };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) Close(input.Text.Trim()); };
        panel.Children.Add(save); Content = panel;
    }
}
