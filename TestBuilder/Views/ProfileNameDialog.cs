using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TestBuilder.Services;

namespace TestBuilder.Views;

public sealed class ProfileNameDialog : Window
{
    public ProfileNameDialog(GraphProfile profile, bool copy, string suggestedName, Func<string, string?> validate)
    {
        Title = copy ? "Создать копию профиля" : "Переименовать профиль";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var name = new TextBox { Name = "ProfileNameInput", Text = suggestedName, Watermark = "Название проверки" };
        Avalonia.Automation.AutomationProperties.SetName(name, "Название профиля");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        error.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("DangerTextBrush"));
        name.TextChanged += (_, _) => error.IsVisible = false;
        var cancel = new Button { Content = "Отмена", MinWidth = 100 };
        var accept = new Button { Content = copy ? "Создать копию" : "Переименовать", MinWidth = 140 };
        void Submit()
        {
            var value = name.Text?.Trim() ?? "";
            error.Text = validate(value);
            error.IsVisible = error.Text != null;
            if (!error.IsVisible) Close(value);
        }
        cancel.Click += (_, _) => Close();
        accept.Click += (_, _) => Submit();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key == Key.Enter) { e.Handled = true; Submit(); }
        };
        Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = profile.DisplayName, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = "Название проверки" },
                name, error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0), Spacing = 12, Children = { cancel, accept }
                }
            }
        };
    }
}
