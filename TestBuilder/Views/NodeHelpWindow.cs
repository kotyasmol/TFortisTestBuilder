using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Views;

public sealed class NodeHelpWindow : Window
{
    private readonly TextBox _search = new() { Watermark = "Найти в справке…", Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel _paragraphs = new() { Spacing = 16 };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _title = new() { FontSize = 24, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private NodeViewModel? _node;
    public NodeHelpWindow()
    {
        Title = "Справка по ноде"; Width = 720; Height = 640; MinWidth = 480; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(24), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        _title.Margin = new Thickness(0, 0, 0, 16); root.Children.Add(_title);
        Grid.SetRow(_search, 1); root.Children.Add(_search);
        var scroll = new ScrollViewer { Name = "HelpScroll", Content = _paragraphs, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 16, 0, 0) };
        var copy = new Button { Content = "Копировать", Margin = new Thickness(12, 0, 12, 0) };
        copy.Click += async (_, _) =>
        {
            try { if (_node != null && Clipboard != null) { await Clipboard.SetTextAsync($"{_node.Title}\n\n{_node.HelpText}"); _status.Text = "Скопировано"; } }
            catch { _status.Text = "Не удалось скопировать"; }
        };
        var close = new Button { Content = "✕ Закрыть" }; close.Click += (_, _) => Close();
        actions.Children.Add(_status); Grid.SetColumn(copy, 1); actions.Children.Add(copy); Grid.SetColumn(close, 2); actions.Children.Add(close);
        Grid.SetRow(actions, 3); root.Children.Add(actions); Content = root;
        _search.TextChanged += (_, _) => Refresh();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }
    public void SetNode(NodeViewModel node) { _node = node; _title.Text = node.Title; _search.Text = ""; Refresh(); }
    private void Refresh()
    {
        if (_node == null) return;
        _paragraphs.Children.Clear();
        var query = _search.Text?.Trim() ?? "";
        var paragraphs = new[] { _node.HelpSummary }.Concat(_node.HelpDetails)
            .Where(p => query.Length == 0 || p.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var text in paragraphs) _paragraphs.Children.Add(new SelectableTextBlock
        { Text = text, FontSize = 16, TextWrapping = Avalonia.Media.TextWrapping.Wrap, LineHeight = 25 });
        _status.Text = query.Length == 0 ? "" : paragraphs.Length == 0 ? "Совпадений нет" : $"Найдено разделов: {paragraphs.Length}";
    }
}
