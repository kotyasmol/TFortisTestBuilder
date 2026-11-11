using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

public sealed class EquipmentCatalogView : UserControl
{
    private TestViewModel? _profile;
    private Action? _refresh;
    private Action? _updateAvailability;
    private bool _attached;

    public EquipmentCatalogView()
    {
        DataContextChanged += (_, _) => BindProfile();
        AttachedToVisualTree += (_, _) => { _attached = true; Subscribe(); _refresh?.Invoke(); };
        DetachedFromVisualTree += (_, _) => { Unsubscribe(); _attached = false; };
    }

    private void BindProfile()
    {
        Unsubscribe();
        _profile = DataContext as TestViewModel;
        _refresh = null;
        _updateAvailability = null;
        Content = null;
        if (_profile != null) Build(_profile);
        Subscribe();
    }

    private void Subscribe()
    {
        if (!_attached || _profile == null) return;
        _profile.EquipmentCatalogChanged -= OnCatalogChanged;
        _profile.EquipmentCatalogChanged += OnCatalogChanged;
        _profile.PropertyChanged -= OnProfileChanged;
        _profile.PropertyChanged += OnProfileChanged;
    }

    private void Unsubscribe()
    {
        if (_profile == null) return;
        _profile.EquipmentCatalogChanged -= OnCatalogChanged;
        _profile.PropertyChanged -= OnProfileChanged;
    }

    private void OnCatalogChanged() => _refresh?.Invoke();
    private void OnProfileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TestViewModel.CanEditQuickActions)) _updateAvailability?.Invoke();
    }

    private void Build(TestViewModel profile)
    {
        var data = new EquipmentCatalogDataView(profile.CatalogOverridesPath, () => profile.CanEditQuickActions);

        var search = new TextBox { Watermark = "Название или код этикетки, например 026", Margin = new Thickness(0, 0, 0, 12) };
        var list = new ListBox { Name = "EquipmentCatalogList", ItemTemplate = new FuncDataTemplate<EquipmentModel>((model, _) =>
            new StackPanel { Spacing = 2, Margin = new Thickness(0, 4), Children =
            {
                new TextBlock { Text = model?.Name, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { FontSize = 12, Opacity = .75, Text = model == null ? "" :
                    $"Этикетка: {model.LabelCode?.ToString("D3") ?? "не задана"} · " +
                    (model.SerialSeries is { } series ? $"Номера: {series.FirstSerialNumber}–{series.LastSerialNumber}" :
                     string.IsNullOrWhiteSpace(model.EffectiveServerDeviceType) ? "Серверный тип не задан" : "Номера выдаёт сервер") }
            } }) };
        var edit = new Button { Content = "Редактировать изделие…" };
        var add = new Button { Content = "Добавить изделие…" };
        var status = new TextBlock { Text = profile.CatalogStatus, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        search.TextChanged += (_, _) => Refresh((list.SelectedItem as EquipmentModel)?.Id);
        list.SelectionChanged += (_, _) => edit.IsEnabled = list.SelectedItem != null && profile.CanEditQuickActions;
        add.Click += async (_, _) => await OpenCardAsync(profile.NewCatalogModel(useProfileName: false), createNew: true);
        edit.Click += async (_, _) =>
        {
            if (list.SelectedItem is EquipmentModel model) await OpenCardAsync(model, createNew: false);
        };

        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(search);
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { add, edit } };
        add.Margin = edit.Margin = new Thickness(0, 0, 8, 4);
        var paths = new SelectableTextBlock { FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = $"Поставка: {EquipmentCatalog.BundledPath}\nЛокальные изменения: {profile.CatalogOverridesPath}" };
        var footer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0), Children = { status, paths, buttons } };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        var tabs = new TabControl { Name = "EquipmentCatalogTabs", Items =
        {
            new TabItem { Header = "Изделия", Content = root },
            new TabItem { Header = "Все данные", Content = data }
        } };
        data.ReloadRequested += (_, _) => profile.ReloadCatalogForViewing();
        data.EditRequested += async model => await OpenCardAsync(model, createNew: false);
        Content = tabs;
        _updateAvailability = () =>
        {
            add.IsEnabled = profile.CanEditQuickActions;
            edit.IsEnabled = list.SelectedItem != null && profile.CanEditQuickActions;
            data.RefreshAvailability();
        };
        _refresh = () =>
        {
            paths.Text = $"Поставка: {EquipmentCatalog.BundledPath}\nЛокальные изменения: {profile.CatalogOverridesPath}";
            Refresh((list.SelectedItem as EquipmentModel)?.Id ?? profile.SelectedEquipmentModel?.Id);
            data.Refresh(profile.CurrentEquipmentCatalog, profile.CatalogStatus, profile.CatalogOverridesPath);
        };
        _refresh();

        async Task OpenCardAsync(EquipmentModel model, bool createNew)
        {
            if (!profile.CanEditQuickActions || TopLevel.GetTopLevel(this) is not Window owner) return;
            try
            {
                var saved = await new CatalogSerialsWindow(profile, model, createNew, selectForProfile: false)
                    .ShowDialog<EquipmentModel?>(owner);
                if (saved == null) return;
                if (!Matches(saved)) search.Text = "";
                Refresh(saved.Id);
                data.Refresh(profile.CurrentEquipmentCatalog, profile.CatalogStatus, profile.CatalogOverridesPath);
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or
                UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
            {
                status.Text = ex.Message;
                status.IsVisible = true;
                data.Refresh(profile.CurrentEquipmentCatalog, ex.Message, profile.CatalogOverridesPath);
            }
        }

        bool Matches(EquipmentModel model) => EquipmentCatalog.Matches(model, search.Text);

        void Refresh(string? selectedId)
        {
            var visible = profile.EquipmentModels.Where(Matches).ToArray();
            list.ItemsSource = visible;
            list.SelectedItem = visible.FirstOrDefault(model => model.Id == selectedId) ?? visible.FirstOrDefault();
            add.IsEnabled = profile.CanEditQuickActions;
            edit.IsEnabled = list.SelectedItem != null && profile.CanEditQuickActions;
            status.Text = profile.CatalogStatus;
            status.IsVisible = profile.HasCatalogStatus;
        }
    }
}
