using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed partial class HardwareEditorWindow
{
    private readonly ListBox _catalogList = new() { Name = "EquipmentCatalogList" };
    private readonly TextBox _catalogSearch = new() { Watermark = "Найти оборудование по названию" };
    private System.Collections.Generic.IReadOnlyList<EquipmentTemplate> _catalog = Array.Empty<EquipmentTemplate>();

    private Control CreateCatalogPage()
    {
        var root = new DockPanel { Margin = new Thickness(8, 16) };
        var top = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 12) };
        top.Children.Add(new TextBlock { Text = "Готовое оборудование", FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        top.Children.Add(new TextBlock { Text = "Выберите устройство и добавьте в профиль. Собственное оборудование создаётся один раз и доступно для других профилей на этом компьютере.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        top.Children.Add(_catalogSearch);
        var actions = new WrapPanel();
        var add = Button("Добавить выбранное в профиль", () =>
        {
            try
            {
                CommitGrid();
                if (_catalogList.SelectedItem is not EquipmentTemplate template) return;
                AddDevice(EquipmentLibrary.Instantiate(template, _devices.Select(d => d.SlaveId)));
                _tabs.SelectedIndex = 1;
                _status.Text = "Устройство добавлено. Проверьте slave-адрес, затем примените состав к профилю.";
            }
            catch (Exception ex) { _status.Text = ex.Message; }
        });
        add.Name = "AddCatalogEquipment"; actions.Children.Add(add);
        actions.Children.Add(Button("Новое оборудование…", async () => await EditTemplateAsync(null,
            new DeviceDefinition { Name = "", Registers = new() { new RegisterDefinition() } })));
        var edit = Button("Редактировать своё…", async () =>
        {
            if (_catalogList.SelectedItem is EquipmentTemplate { IsBuiltIn: false } entry)
                await EditTemplateAsync(entry.Id, entry.Device);
        });
        actions.Children.Add(edit);
        var copy = Button("Создать копию…", async () =>
        {
            if (_catalogList.SelectedItem is EquipmentTemplate entry)
            {
                var device = TestViewModel.CopyDefinition(entry.Device); device.Name += " (копия)";
                await EditTemplateAsync(null, device);
            }
        });
        actions.Children.Add(copy);
        top.Children.Add(actions);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        _catalogList.ItemTemplate = new FuncDataTemplate<EquipmentTemplate>((entry, _) =>
        {
            var row = new StackPanel { Spacing = 4, Margin = new Thickness(8) };
            row.Children.Add(new TextBlock { Text = entry?.Display, FontSize = 16, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            row.Children.Add(new TextBlock { Text = entry?.Details, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            return row;
        });
        void SelectionChanged()
        {
            add.IsEnabled = copy.IsEnabled = _catalogList.SelectedItem != null;
            edit.IsEnabled = _catalogList.SelectedItem is EquipmentTemplate { IsBuiltIn: false };
        }
        _catalogList.SelectionChanged += (_, _) => SelectionChanged();
        _catalogSearch.TextChanged += (_, _) => FilterCatalog();
        root.Children.Add(_catalogList); ReloadCatalog(); SelectionChanged();
        return root;
    }
    private void FilterCatalog()
    {
        var selected = (_catalogList.SelectedItem as EquipmentTemplate)?.Id;
        var entries = _catalog.Where(e => e.Device.Name.Contains(_catalogSearch.Text?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
        _catalogList.ItemsSource = entries;
        _catalogList.SelectedItem = entries.FirstOrDefault(e => e.Id == selected) ?? entries.FirstOrDefault();
    }
    private void ReloadCatalog(string? selected = null)
    {
        try
        {
            _catalog = _library.Load(); FilterCatalog();
            if (selected != null) _catalogList.SelectedItem = _catalog.FirstOrDefault(e => e.Id == selected);
        }
        catch (Exception ex) { _status.Text = "Не удалось прочитать каталог: " + ex.Message; }
    }
    private async Task EditTemplateAsync(string? id, DeviceDefinition source)
    {
        try
        {
            var result = await new EquipmentTemplateWindow(_library, source, id).ShowDialog<EquipmentTemplate?>(this);
            if (result == null) return;
            _catalogSearch.Text = ""; ReloadCatalog(result.Id); _tabs.SelectedIndex = 0;
            _status.Text = "Оборудование сохранено в каталог. Теперь его можно добавить в профиль.";
        }
        catch (Exception ex) { _status.Text = ex.Message; }
    }
}
