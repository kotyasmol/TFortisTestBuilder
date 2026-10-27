using System;
using System.Collections.Generic;
using System.Linq;
using System.IO.Ports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class DiscoverySettingsWindow : Window
{
    public DiscoverySettingsWindow()
    {
        Title = "Параметры открытия COM-порта"; Width = 580; Height = 460;
        var draft = TestViewModel.CopyDefinition(AppSettings.Instance.Discovery);
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel { Spacing = 6 };
        var inputs = new List<TextBox>();
        foreach (var (label, property) in new[] { ("Скорость, бод", "BaudRate"), ("Биты данных", "DataBits") })
        {
            top.Children.Add(new TextBlock { Text = label });
            var box = new TextBox(); box.Bind(TextBox.TextProperty, new Binding(property) { Source = draft, Mode = BindingMode.TwoWay });
            top.Children.Add(box); inputs.Add(box);
        }
        var parity = new ComboBox { ItemsSource = Enum.GetValues<Parity>(), SelectedItem = draft.Parity };
        var stop = new ComboBox { ItemsSource = new[] { StopBits.One, StopBits.Two, StopBits.OnePointFive }, SelectedItem = draft.StopBits };
        top.Children.Add(new TextBlock { Text = "Чётность" }); top.Children.Add(parity);
        top.Children.Add(new TextBlock { Text = "Стоп-биты" }); top.Children.Add(stop);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(status);
        var save = new Button { Content = "Сохранить параметры" };
        save.Click += (_, _) =>
        {
            try
            {
                if (inputs.Any(DataValidationErrors.GetHasErrors)) throw new InvalidOperationException("Исправьте значения в полях.");
                draft.Parity = (Parity)parity.SelectedItem!; draft.StopBits = (StopBits)stop.SelectedItem!;
                if (draft.BaudRate is < 300 or > 1000000 || draft.DataBits is < 5 or > 8)
                    throw new InvalidOperationException("Проверьте скорость и число бит данных.");
                var previous = AppSettings.Instance.Discovery;
                AppSettings.Instance.Discovery = draft;
                try { AppSettings.Instance.SaveChecked(); } catch { AppSettings.Instance.Discovery = previous; throw; }
                Close();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        footer.Children.Add(save); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        Content = root;
    }
}
