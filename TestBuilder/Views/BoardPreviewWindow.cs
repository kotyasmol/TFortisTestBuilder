using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class BoardPreviewWindow : Window
{
    public BoardPreviewWindow(TestViewModel vm)
    {
        Title = "Плата — результаты проверки";
        Width = 1280; Height = 850; MinWidth = 760; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowState = WindowState.Maximized;
        DataContext = vm;
        this.Bind(BackgroundProperty, new DynamicResourceExtension("AppBackgroundBrush"));
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(16) };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 12) };
        var stage = new TextBlock { FontSize = 18, VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        stage.Bind(TextBlock.TextProperty, new Binding("Station.CurrentStage"));
        stage.Bind(IsVisibleProperty, new Binding("!Station.HasResult"));
        var result = new TextBlock { FontSize = 18, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        result.Bind(TextBlock.TextProperty, new Binding("Station.Headline"));
        result.Bind(IsVisibleProperty, new Binding("Station.HasResult"));
        var duration = new TextBlock { FontSize = 13 };
        duration.Bind(TextBlock.TextProperty, new Binding("Station.DurationText"));
        duration.Bind(IsVisibleProperty, new Binding("Station.HasDuration"));
        var status = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        status.Children.Add(stage); status.Children.Add(result); status.Children.Add(duration); header.Children.Add(status);
        var back = new Button { Content = "Вернуться к пульту" }; back.Click += (_, _) => Close();
        Grid.SetColumn(back, 1); header.Children.Add(back); root.Children.Add(header);
        var attention = new Border { Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12),
            Child = new TextBlock { Text = "Требуется действие оператора. Вернитесь к пульту, чтобы прочитать задание.", TextWrapping = Avalonia.Media.TextWrapping.Wrap } };
        attention.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ChangedSoftBrush"));
        attention.Bind(IsVisibleProperty, new Binding("Station.IsAwaitingAction"));
        Grid.SetRow(attention, 1); root.Children.Add(attention);
        var preview = new BoardPreviewView { AllowExpand = false };
        Grid.SetRow(preview, 2); root.Children.Add(preview); Content = root;
        Opened += (_, _) => back.Focus();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }
}
