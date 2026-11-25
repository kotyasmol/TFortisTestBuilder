using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class NetworkAdapterSettingsView : WorkspacePage
{
    public NetworkAdapterSettingsViewModel Editor { get; }
    public NetworkAdapterSettingsView(NetworkAdapterSettingsViewModel editor)
    {
        MaxWidth = 1100;
        Editor = editor; DataContext = editor;
        Title = "Настройка сетевой карты";

        var root = new DockPanel { Margin = new Thickness(20) };
        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        var status = new TextBlock { Name = "AdapterSettingsStatus", TextWrapping = TextWrapping.Wrap }; status.Bind(TextBlock.TextProperty, new Binding("Status")); bottom.Children.Add(status);
        var close = Button("Закрыть", () => { Close(); return Task.CompletedTask; }); close.Bind(IsEnabledProperty, new Binding("CanClose")); bottom.Children.Add(close);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        var name = new TextBlock { FontSize = 22, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        name.Bind(TextBlock.TextProperty, new Binding("Current.Name")); top.Children.Add(name);
        var support = new TextBlock { TextWrapping = TextWrapping.Wrap }; support.Bind(TextBlock.TextProperty, new Binding("NativeSupport")); top.Children.Add(support);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var tabs = new TabControl { Name = "AdapterSettingsTabs" };

        var info = Panel();
        var details = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap }; details.Bind(SelectableTextBlock.TextProperty, new Binding("ConnectionDetails")); info.Children.Add(details);
        info.Children.Add(Button("Обновить данные", () => { Editor.Reload(); return Task.CompletedTask; }));
        var systemName = Text("AdapterName", "AdapterSystemName"); systemName.Bind(IsEnabledProperty, new Binding("CanChangeSystem"));
        Field(info, "Имя карты в системе", systemName);
        var rename = Button("Переименовать карту", Editor.Rename); rename.Bind(IsEnabledProperty, new Binding("CanChangeSystem")); info.Children.Add(rename);
        info.Children.Add(Button("Системные свойства сети…", () => { NetworkSetupViewModel.OpenSystemNetworkSettings(); return Task.CompletedTask; }));
        info.Children.Add(new TextBlock { Text = "В системных свойствах доступны включение карты, IPv6, скорость/дуплекс и параметры драйвера. Выберите эту карту по имени и MAC.", TextWrapping = TextWrapping.Wrap });

        var ipv4 = Panel();
        ipv4.Bind(IsEnabledProperty, new Binding("CanChangeSystem"));
        ipv4.Children.Add(new TextBlock { Text = "Применение меняет настройки только этой карты. Ручная настройка IPv4 заменяет её текущие статические IPv4-адреса; IPv6 не меняется.", TextWrapping = TextWrapping.Wrap });
        Field(ipv4, "Адрес IPv4", Mode("AddressMode", "AdapterAddressMode"));
        var address = Panel(); address.Bind(IsEnabledProperty, new Binding("ManualAddress"));
        Field(address, "IPv4", Text("Address", "AdapterIpv4")); Field(address, "Маска", Text("Mask", "AdapterIpv4Mask")); Field(address, "Шлюз (можно не задавать)", Text("Gateway", "AdapterGateway")); ipv4.Children.Add(address);
        Field(ipv4, "DNS", Mode("DnsMode", "AdapterDnsMode"));
        var dns = Text("Dns", "AdapterDns"); dns.Bind(IsEnabledProperty, new Binding("ManualDns")); Field(ipv4, "DNS-серверы через запятую", dns);
        var apply = Button("Применить IPv4 и DNS", Editor.ApplyIpv4); apply.Bind(IsEnabledProperty, new Binding("CanChangeSystem")); ipv4.Children.Add(apply);

        var plan = Panel();
        plan.Children.Add(new TextBlock { Text = "Тестовый порт связывает эту сетевую карту с разъёмом проверяемого устройства. Укажите название, адрес и пару; по одному порту на карту. Сохранение не меняет IP системы или существующие ноды DataTest.", TextWrapping = TextWrapping.Wrap });
        var planSummary = new TextBlock { Name = "AdapterPlanSummary", TextWrapping = TextWrapping.Wrap }; planSummary.Bind(TextBlock.TextProperty, new Binding("PlanSummary")); plan.Children.Add(planSummary);
        var ports = new ComboBox { Name = "AdapterPlanPort", ItemsSource = editor.Ports, HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<NetworkPortChoice>((_, _) =>
            { var text = new TextBlock { TextWrapping = TextWrapping.Wrap }; text.Bind(TextBlock.TextProperty, new Binding("Title")); return text; }) };
        ports.Bind(ComboBox.SelectedItemProperty, new Binding("SelectedPort") { Mode = BindingMode.TwoWay }); ports.Bind(IsEnabledProperty, new Binding("CanAssignPort")); Field(plan, "Тестовый порт", ports);
        var add = Button("Создать тестовый порт", () => { Editor.AddPort(); return Task.CompletedTask; }); add.Bind(IsEnabledProperty, new Binding("CanAssignPort")); plan.Children.Add(add);
        var remove = Button("Удалить выбранный порт", () => { Editor.RemovePort(); return Task.CompletedTask; }); remove.Bind(IsEnabledProperty, new Binding("HasPort")); remove.Bind(IsVisibleProperty, new Binding("HasPort")); plan.Children.Add(remove);
        var portFields = Panel(); portFields.Bind(IsEnabledProperty, new Binding("HasPort")); portFields.Bind(IsVisibleProperty, new Binding("HasPort"));
        Field(portFields, "Название порта", Text("SelectedPort.Port.Name", "AdapterPortName"));
        Field(portFields, "Назначение / разъём", Text("SelectedPort.Port.Role", "AdapterPortRole"));
        Field(portFields, "IPv4 для проверки", Text("SelectedPort.Port.Ip", "AdapterPlanIp"));
        Field(portFields, "Временный адрес", Text("SelectedPort.Port.TemporaryIp", "AdapterTemporaryIp"));
        Field(portFields, "Имя пары DataTest", Text("SelectedPort.Port.Pair", "AdapterTestPair"));
        Field(portFields, "Мбит/с теста (1–1000)", Text("TestSpeedText", "AdapterTestSpeed"));
        var include = new CheckBox { Content = "Включить порт в пары DataTest" }; include.Bind(CheckBox.IsCheckedProperty, new Binding("SelectedPort.Port.IncludeInDataTest") { Mode = BindingMode.TwoWay }); portFields.Children.Add(include); plan.Children.Add(portFields);
        var pairHelp = new TextBlock { Text = "У двух портов одной пары укажите одинаковое имя пары и скорость. В ноде DataTest нажмите «Взять пары из плана сети». Подключение карт к разъёмам проверьте по кабелям.", TextWrapping = TextWrapping.Wrap }; pairHelp.Bind(IsVisibleProperty, new Binding("HasPort")); plan.Children.Add(pairHelp);
        Field(plan, "Маска плана стенда", Text("Plan.Mask", "AdapterPlanMask"));
        var save = Button("Сохранить назначение", () => { Editor.SaveMapping(); return Task.CompletedTask; }); save.Bind(IsEnabledProperty, new Binding("CanAssignPort")); save.IsVisible = false; bottom.Children.Insert(1, save);
        tabs.SelectionChanged += (_, _) => save.IsVisible = tabs.SelectedIndex == 2;
        if (!editor.Current.CanUseForDataTest) plan.Children.Add(new TextBlock { Text = "В плане DataTest используются Ethernet-карты. Для Wi-Fi доступны сведения и настройки IPv4.", TextWrapping = TextWrapping.Wrap });
        tabs.Items.Add(new TabItem { Header = "Карта", Content = new ScrollViewer { Content = info } });
        tabs.Items.Add(new TabItem { Header = "IPv4 и DNS", Content = new ScrollViewer { Content = ipv4 } });
        tabs.Items.Add(new TabItem { Header = "Порт и пара DataTest", Content = new ScrollViewer { Content = plan } });
        root.Children.Add(tabs); Content = root;
        Closing += (_, e) => { if (Editor.IsBusy) e.Cancel = true; };

        Button Button(string text, Func<Task> action)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 4, 0, 0) };
            button.Click += async (_, _) =>
            {
                try
                {
                    if (Editor.IsBusy) return;
                    if (text != "Закрыть" && !Editor.CanEdit) throw new InvalidOperationException("Дождитесь завершения текущей операции.");
                    if (text != "Закрыть" && this.GetVisualDescendants().OfType<Control>().Any(c => c.IsEffectivelyVisible && c.IsEffectivelyEnabled && DataValidationErrors.GetHasErrors(c)))
                        throw new InvalidOperationException("Исправьте выделенное поле.");
                    await action();
                }
                catch (Exception ex) { Editor.Status = ex.Message; }
            };
            return button;
        }
        TextBox Text(string path, string name)
        {
            var input = new TextBox { Name = name, HorizontalAlignment = HorizontalAlignment.Stretch };
            input.Bind(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay }); AutomationProperties.SetName(input, name); return input;
        }
        ComboBox Mode(string property, string name)
        {
            var input = new ComboBox { Name = name, ItemsSource = editor.Modes, HorizontalAlignment = HorizontalAlignment.Stretch };
            input.Bind(ComboBox.SelectedItemProperty, new Binding(property) { Mode = BindingMode.TwoWay }); return input;
        }
    }
    private static StackPanel Panel() => new() { Spacing = 10, Margin = new Thickness(0, 12, 10, 0) };
    private static void Field(Panel panel, string label, Control input)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("210,*") };
        row.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        Grid.SetColumn(input, 1); row.Children.Add(input); panel.Children.Add(row);
    }
}
