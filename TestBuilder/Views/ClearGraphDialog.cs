using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace TestBuilder.Views;

public sealed class ClearGraphDialog : Window
{
    public ClearGraphDialog()
    {
        Title = "Очистить граф?";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new Button { Content = "Отмена", MinWidth = 100 };
        var clear = new Button { Content = "Очистить граф", MinWidth = 140 };
        cancel.Click += (_, _) => Close(false);
        clear.Click += (_, _) => Close(true);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
        Opened += (_, _) => cancel.Focus();
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = "Удалить ноды и связи текущего графа? Действие можно отменить кнопкой Undo. Файл профиля изменится только после сохранения.",
                    TextWrapping = TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 12, Children = { cancel, clear }
                }
            }
        };
    }
}
