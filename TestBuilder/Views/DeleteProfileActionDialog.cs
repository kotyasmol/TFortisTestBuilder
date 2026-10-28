using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TestBuilder.Views;

public sealed class DeleteProfileActionDialog : Window
{
    public DeleteProfileActionDialog(string actionName, bool registerIndicator = false)
    {
        Title = registerIndicator ? "Удалить индикатор?" : "Удалить кнопку?";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new Button { Content = "Отмена", MinWidth = 100 };
        var delete = new Button { Content = "Удалить", MinWidth = 100 };
        cancel.Click += (_, _) => Close(false);
        delete.Click += (_, _) => Close(true);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(false); } };
        Opened += (_, _) => cancel.Focus();
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = registerIndicator ? $"Удалить индикатор «{actionName}» из профиля?" : $"Удалить кнопку «{actionName}» и её графы из профиля?",
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 12, Children = { cancel, delete }
                }
            }
        };
    }
}
