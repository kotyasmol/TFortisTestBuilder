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
        Title = "Поиск стенда до выбора профиля"; Width = 760; Height = 740;
        var draft = TestViewModel.CopyDefinition(AppSettings.Instance.Discovery);
        var root = new DockPanel { Margin = new Thickness(20) };
        var top = new StackPanel { Spacing = 6 };
        var inputs = new List<TextBox>();
        foreach (var (label, property) in new[] { ("Скорость, бод", "BaudRate"), ("Биты данных", "DataBits"),
            ("Адреса slave через запятую (включая чётные)", "SlaveAddresses"), ("Пауза между устройствами, мс", "DelayMs") })
        {
            top.Children.Add(new TextBlock { Text = label });
            var box = new TextBox(); box.Bind(TextBox.TextProperty, new Binding(property) { Source = draft, Mode = BindingMode.TwoWay });
            top.Children.Add(box); inputs.Add(box);
        }
        var parity = new ComboBox { ItemsSource = Enum.GetValues<Parity>(), SelectedItem = draft.Parity };
        var stop = new ComboBox { ItemsSource = new[] { StopBits.One, StopBits.Two, StopBits.OnePointFive }, SelectedItem = draft.StopBits };
        top.Children.Add(new TextBlock { Text = "Чётность" }); top.Children.Add(parity);
        top.Children.Add(new TextBlock { Text = "Стоп-биты" }); top.Children.Add(stop);
        top.Children.Add(new TextBlock { Text = "Искать оборудование из каталога:", Margin = new Thickness(0, 10, 0, 6) });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var choices = new List<(string Id, CheckBox Box)>();
        var list = new StackPanel { Spacing = 6 };
        var status = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        try
        {
            foreach (var entry in new EquipmentLibrary().Load())
            {
                var box = new CheckBox { Content = entry.Display,
                    IsChecked = draft.TemplateIds == null ? entry.IsBuiltIn : draft.TemplateIds.Contains(entry.Id) };
                choices.Add((entry.Id, box)); list.Children.Add(box);
            }
        }
        catch (Exception ex) { status.Text = ex.Message; }
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        footer.Children.Add(status);
        var save = new Button { Content = "Сохранить настройки поиска" };
        save.Click += (_, _) =>
        {
            try
            {
                if (inputs.Any(DataValidationErrors.GetHasErrors)) throw new InvalidOperationException("Исправьте значения в полях.");
                draft.Parity = (Parity)parity.SelectedItem!; draft.StopBits = (StopBits)stop.SelectedItem!;
                draft.TemplateIds = choices.Where(c => c.Box.IsChecked == true).Select(c => c.Id).ToList();
                draft.Validate();
                var library = new EquipmentLibrary().Load();
                if (library.Where(e => draft.TemplateIds.Contains(e.Id)).Any(e => e.Device.ExpectedIdentificationValues.Count == 0))
                    throw new InvalidOperationException("Для поиска выбранного оборудования задайте значения идентификации в каталоге.");
                var previous = AppSettings.Instance.Discovery;
                AppSettings.Instance.Discovery = draft;
                try { AppSettings.Instance.SaveChecked(); } catch { AppSettings.Instance.Discovery = previous; throw; }
                Close();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        footer.Children.Add(save); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = list }); Content = root;
    }
}
