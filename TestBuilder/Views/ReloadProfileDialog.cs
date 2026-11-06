using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TestBuilder.Views;

public sealed class ReloadProfileDialog : Window
{
    public ReloadProfileDialog()
    {
        Title = "Обновить профиль?";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new Button { Content = "Отмена", MinWidth = 100 };
        var reload = new Button { Content = "Загрузить с диска", MinWidth = 150 };
        cancel.Click += (_, _) => Close(false);
        reload.Click += (_, _) => Close(true);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(false); } };
        Opened += (_, _) => cancel.Focus();
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = "В профиле есть несохранённые изменения. Загрузить сохранённую версию и отменить эти изменения?",
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 12, Children = { cancel, reload }
                }
            }
        };
    }
}
