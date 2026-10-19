using System;
using System.Linq;
using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TestBuilder.Services.Help;

namespace TestBuilder.Views;

public sealed class GuideArticleView : StackPanel
{
    public GuideArticleView() { Spacing = 22; }
    public int ShowArticle(GuideArticle article, string query = "")
    {
        Children.Clear();
        bool Match(string text) => query.Length == 0 || text.Contains(query, StringComparison.OrdinalIgnoreCase);
        var count = 0;
        if (Match(article.Summary)) { Children.Add(Paragraph(article.Summary)); count++; }
        foreach (var block in article.Blocks)
        {
            var matches = block.Items.Select((text, index) => (text, index)).Where(p => Match(p.text) || (query.Length > 0 && Match(block.Heading))).ToArray();
            if (matches.Length == 0) continue;
            var section = new StackPanel { Spacing = 12 };
            section.Children.Add(new TextBlock { Text = block.Heading, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
            foreach (var (text, index) in matches)
            {
                if (block.Kind == "steps")
                {
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("32,*") };
                    var number = new TextBlock { Text = (index + 1).ToString(), FontSize = 16, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 2, 0, 0) };
                    number.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("PrimaryTextBrush"));
                    row.Children.Add(number);
                    var paragraph = Paragraph(text); Grid.SetColumn(paragraph, 1); row.Children.Add(paragraph); section.Children.Add(row);
                }
                else section.Children.Add(Paragraph(text));
                count++;
            }
            Children.Add(section);
        }
        return count;
    }
    private static SelectableTextBlock Paragraph(string text) => new() { Text = text, FontSize = 16, LineHeight = 25, TextWrapping = TextWrapping.Wrap };
}
