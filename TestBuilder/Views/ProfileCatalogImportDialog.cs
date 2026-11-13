using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Services;
using System.Text.Json;

namespace TestBuilder.Views;

public sealed class ProfileCatalogImportDialog : Window
{
    public ProfileCatalogImportDialog(CatalogImportConflict conflict)
    {
        Title = "Карточка изделия отличается"; Width = 780; MaxHeight = 720;
        SizeToContent = SizeToContent.Height; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var cancel = new Button { Content = "Отмена" };
        var local = new Button { Content = "Карточка этого компьютера" };
        var incoming = new Button { Content = "Карточка из профиля" };
        cancel.Click += (_, _) => Close(CatalogImportChoice.Cancel);
        local.Click += (_, _) => Close(CatalogImportChoice.Local);
        incoming.Click += (_, _) => Close(CatalogImportChoice.Profile);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(CatalogImportChoice.Cancel); } };
        Opened += (_, _) => cancel.Focus();
        var grid = new Grid { ColumnDefinitions = new("*,*") };
        var left = new TextBlock { Margin = new(0, 0, 20, 0), Text = "На этом компьютере\n\n" + ProfileCatalogLink.Describe(conflict.Local), TextWrapping = TextWrapping.Wrap };
        var right = new TextBlock { Text = "В импортируемом JSON\n\n" + ProfileCatalogLink.Describe(conflict.Profile), TextWrapping = TextWrapping.Wrap };
        StackPanel Column(TextBlock summary, EquipmentModel card) => new() { Spacing = 10, Children =
        {
            summary, new Expander { Header = "Все поля карточки (JSON)", HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new SelectableTextBlock { Text = JsonSerializer.Serialize(card, EquipmentCatalog.JsonOptions), TextWrapping = TextWrapping.Wrap } }
        }};
        var leftColumn = Column(left, conflict.Local); var rightColumn = Column(right, conflict.Profile);
        Grid.SetColumn(rightColumn, 1); grid.Children.Add(leftColumn); grid.Children.Add(rightColumn);
        foreach (var button in new[] { cancel, local, incoming }) button.Margin = new Thickness(0, 0, 10, 8);
        Content = new StackPanel { Margin = new(24), Spacing = 18, Children =
        {
            new TextBlock { Text = "Выберите карточку для импортируемого профиля.", TextWrapping = TextWrapping.Wrap },
            new ScrollViewer { MaxHeight = 410, Content = grid },
            new TextBlock { Text = "«Карточка из профиля» обновит местный справочник для всех профилей этого изделия. «Карточка этого компьютера» сохранит местные данные и запишет их резервную копию в импортируемый JSON.", TextWrapping = TextWrapping.Wrap },
            new WrapPanel { Orientation = Orientation.Horizontal, Children = { cancel, local, incoming } }
        }};
    }
}

public sealed class ProfileDependenciesWindow : Window
{
    public ProfileDependenciesWindow(string text)
    {
        Title = "Зависимости профиля"; Width = 760; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Content = new ScrollViewer { Margin = new Thickness(24), Content = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap } };
    }
}
