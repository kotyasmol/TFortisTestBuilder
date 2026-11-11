using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal sealed class EquipmentCatalogWindow : Window
{
    public EquipmentCatalogWindow(TestViewModel profile)
    {
        Title = "Справочник изделий";
        Width = 560;
        Height = 550;
        MinWidth = 440;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

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
        var close = new Button { Content = "Закрыть", IsCancel = true };
        var status = new TextBlock { Text = profile.CatalogStatus, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

        search.TextChanged += (_, _) => Refresh((list.SelectedItem as EquipmentModel)?.Id);
        list.SelectionChanged += (_, _) => edit.IsEnabled = list.SelectedItem != null;
        add.Click += async (_, _) => await OpenCardAsync(profile.NewCatalogModel(useProfileName: false), createNew: true);
        edit.Click += async (_, _) =>
        {
            if (list.SelectedItem is EquipmentModel model) await OpenCardAsync(model, createNew: false);
        };
        close.Click += (_, _) => Close();

        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(search);
        Grid.SetRow(list, 1);
        root.Children.Add(list);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, Children = { add, edit, close } };
        add.Margin = edit.Margin = new Thickness(0, 0, 8, 4);
        var paths = new SelectableTextBlock { FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Text = $"Поставка: {EquipmentCatalog.BundledPath}\nЛокальные изменения: {profile.CatalogOverridesPath}" };
        var footer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0), Children = { status, paths, buttons } };
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        Refresh(profile.SelectedEquipmentModel?.Id);

        async Task OpenCardAsync(EquipmentModel model, bool createNew)
        {
            if (!profile.CanEditQuickActions) return;
            var saved = await new CatalogSerialsWindow(profile, model, createNew, selectForProfile: false)
                .ShowDialog<EquipmentModel?>(this);
            if (saved == null) return;
            if (!Matches(saved)) search.Text = "";
            Refresh(saved.Id);
        }

        bool Matches(EquipmentModel model) => EquipmentCatalog.Matches(model, search.Text);

        void Refresh(string? selectedId)
        {
            var visible = profile.EquipmentModels.Where(Matches).ToArray();
            list.ItemsSource = visible;
            list.SelectedItem = visible.FirstOrDefault(model => model.Id == selectedId) ?? visible.FirstOrDefault();
            edit.IsEnabled = list.SelectedItem != null;
            status.Text = profile.CatalogStatus;
            status.IsVisible = profile.HasCatalogStatus;
        }
    }
}
