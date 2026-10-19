using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TestBuilder.Services.Help;

namespace TestBuilder.Views;

public sealed class UserGuideWindow : Window
{
    public static string ReadGuide() => UserGuide.PlainText();
    public UserGuideWindow()
    {
        Title = "Инструкция TFortis TestBuilder"; Width = 1120; Height = 820; MinWidth = 850; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var search = new TextBox { Watermark = "Найти в инструкции", Margin = new Thickness(0, 0, 0, 12) };
        var list = new ListBox { ItemsSource = UserGuide.All, Name = "GuideArticles" };
        list.ItemTemplate = new FuncDataTemplate<GuideArticle>((article, _) =>
        {
            var entry = new StackPanel { Spacing = 3 };
            var category = new TextBlock { Text = article?.Group, FontSize = 11 };
            category.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("TextSecondaryBrush"));
            entry.Children.Add(category);
            entry.Children.Add(new TextBlock { Text = article?.Title, MaxWidth = 235, TextWrapping = TextWrapping.Wrap });
            return entry;
        });
        var title = new TextBlock { FontSize = 26, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var body = new GuideArticleView();
        var content = new StackPanel { Spacing = 20, Margin = new Thickness(28, 16, 28, 32), MaxWidth = 800, HorizontalAlignment = HorizontalAlignment.Stretch };
        content.Children.Add(title); content.Children.Add(body);
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is GuideArticle article) { title.Text = article.Title; body.ShowArticle(article); scroll.Offset = default; } };
        search.TextChanged += (_, _) => {
            var matches = UserGuide.Search(search.Text ?? "");
            list.ItemsSource = matches; list.SelectedIndex = matches.Length > 0 ? 0 : -1;
            if (matches.Length == 0) { title.Text = "Ничего не найдено"; body.Children.Clear(); body.Children.Add(new TextBlock { Text = "Попробуйте другое слово: профиль, регистр, этикетка.", TextWrapping = TextWrapping.Wrap }); }
        };
        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var copy = new Button { Content = "Копировать статью" };
        copy.Click += async (_, _) => {
            try { if (Clipboard != null && list.SelectedItem is GuideArticle a) { await Clipboard.SetTextAsync(a.Title + "\n\n" + a.BodyText); status.Text = "Статья скопирована"; } }
            catch { status.Text = "Не удалось скопировать"; }
        };
        var save = new Button { Content = "Сохранить для печати…" };
        save.Click += async (_, _) => {
            try {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                    Title = "Сохранить полную инструкцию", SuggestedFileName = "TFortis-инструкция.html", DefaultExtension = "html",
                    FileTypeChoices = new[] { new FilePickerFileType("Инструкция для браузера и печати") { Patterns = new[] { "*.html" } } }
                });
                if (file == null) return;
                await using var stream = await file.OpenWriteAsync(); stream.SetLength(0);
                await using var writer = new StreamWriter(stream); await writer.WriteAsync(UserGuide.ExportHtml());
                status.Text = "Откройте файл в браузере для печати или сохранения в PDF.";
            } catch (Exception error) { status.Text = "Не удалось сохранить: " + error.Message; }
        };
        var close = new Button { Content = "Закрыть" }; close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(copy); buttons.Children.Add(save); buttons.Children.Add(close);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) }; footer.Children.Add(status); footer.Children.Add(buttons);
        var sidebar = new DockPanel { Margin = new Thickness(0, 0, 12, 0) }; DockPanel.SetDock(search, Dock.Top); sidebar.Children.Add(search); sidebar.Children.Add(list);
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("280,*") }; columns.Children.Add(sidebar);
        var paper = new Border { Child = scroll, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        paper.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush")); paper.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("BorderBrush"));
        Grid.SetColumn(paper, 1); columns.Children.Add(paper);
        var root = new DockPanel { Margin = new Thickness(20) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(columns);
        Content = root; list.SelectedIndex = 0;
    }
}
