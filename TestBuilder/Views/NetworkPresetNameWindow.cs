using Avalonia;
using Avalonia.Controls;

namespace TestBuilder.Views;

public sealed class NetworkPresetNameWindow : Window
{
    public NetworkPresetNameWindow()
    {
        Title = "Сохранить набор сети"; Width = 520; Height = 220; CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Название набора" });
        var input = new TextBox { MaxLength = 100 }; panel.Children.Add(input);
        var save = new Button { Content = "Сохранить" };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(input.Text)) Close(input.Text.Trim()); };
        panel.Children.Add(save); Content = panel;
    }
}
