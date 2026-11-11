using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class EquipmentCatalogDataTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task AllDataShowsEffectiveCatalogAndOpeningItDoesNotChangeProfileOrFiles(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var folder = new ProfileCopyTests.ProfileFolder();
            var model = EquipmentCatalog.Load(folder.Path).Resolve("psw-2g8f-plus-box", null)!;
            EquipmentCatalog.SaveModel(folder.Path, model with
            {
                IsFullOverride = true, Aliases = new[] { "Fixture 026" },
                LabelCabinets = new() { [1] = "CustomCase" },
                SerialSeries = model.SerialSeries! with { FailureReportSerialNumber = 2600001 },
                ExtensionData = new() { ["fixtureData"] = JsonSerializer.SerializeToElement("keep-me") }
            });
            using var bus = new ModbusService();
            using var profile = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var beforeProfile = GraphSerializer.Serialize(profile, profile.CurrentProfileName);
            var beforeCatalog = File.ReadAllBytes(EquipmentCatalog.LocalPath(folder.Path));
            var window = new EquipmentCatalogWindow(profile);
            try
            {
                window.Show(); Layout(window);
                MainTabs(window).SelectedIndex = 1; Layout(window);
                var models = Grid(window, "CatalogDataModels");
                Assert.True(models.IsReadOnly);
                Assert.Equal(profile.EquipmentModels.Count, models.ItemsSource.Cast<object>().Count());
                Search(window).Text = "CustomCase"; Layout(window);
                var row = Assert.IsType<EquipmentCatalogDataView.CatalogRow>(Assert.Single(models.ItemsSource.Cast<object>()));
                Assert.Equal(model.Id, row.Model.Id);
                Assert.Equal("Локальная карточка", row.Source);
                Assert.Equal("2600001", row.First);
                var fields = Grid(window, "CatalogDataDetails").ItemsSource.Cast<EquipmentCatalogDataView.CatalogField>().ToArray();
                Assert.Contains(fields, field => field.Name == "Шкаф комплектации 1" && field.Value == "CustomCase");
                Assert.Contains(fields, field => field.Name == "Номер для аварийного отчёта" && field.Value == "2600001");
                Assert.Contains(fields, field => field.Name == "Дополнительное поле изделия: fixtureData" && field.Value == "keep-me");
                window.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-all-catalog-data-{dark}.png"));
                window.GetVisualDescendants().OfType<TabControl>().Single(tab => tab.Name != "EquipmentCatalogTabs").SelectedIndex = 1;
                Layout(window);
                var json = window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "CatalogDataJson");
                Assert.True(json.IsReadOnly);
                using var document = JsonDocument.Parse(json.Text!);
                var all = document.RootElement.GetProperty("models").EnumerateArray().ToArray();
                Assert.Equal(profile.EquipmentModels.Count, all.Length); // Search must not filter the full JSON.
                var effective = all.Single(item => item.GetProperty("id").GetString() == model.Id);
                Assert.Equal("CustomCase", effective.GetProperty("labelCabinets").GetProperty("1").GetString());
                Assert.Equal("keep-me", effective.GetProperty("fixtureData").GetString());
                Assert.False(document.RootElement.TryGetProperty("sources", out _));
                var edit = window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "EditCatalogDataModel");
                Assert.True(edit.IsEnabled);
                edit.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Layout(window);
                var card = Assert.Single(window.OwnedWindows.OfType<CatalogSerialsWindow>());
                Layout(card);
                Assert.Equal(model.Name, Field(card, "Название изделия").Text);
                card.Close(); Layout(window);
                Assert.Equal(beforeProfile, GraphSerializer.Serialize(profile, profile.CurrentProfileName));
                Assert.Equal(beforeCatalog, File.ReadAllBytes(EquipmentCatalog.LocalPath(folder.Path)));
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = ThemeVariant.Default; }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RefreshUsesLocalEditsAndRetainsLastReadableCatalogIfFileIsInvalid()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var folder = new ProfileCopyTests.ProfileFolder();
            using var bus = new ModbusService();
            using var profile = new TestViewModel(bus, new SlaveManager(bus));
            var before = GraphSerializer.Serialize(profile, profile.CurrentProfileName);
            var window = new EquipmentCatalogWindow(profile);
            window.Show(); Layout(window);
            try
            {
                MainTabs(window).SelectedIndex = 1; Layout(window);
                Search(window).Text = "026"; Layout(window);
                var model = EquipmentCatalog.Load(folder.Path).Resolve("psw-2g8f-plus-box", null)!;
                EquipmentCatalog.SaveModel(folder.Path, model with { IsFullOverride = true, LabelCabinets = new() { [0] = "UpdatedCase" } });
                Reload(window);
                var row = Assert.IsType<EquipmentCatalogDataView.CatalogRow>(Assert.Single(Grid(window, "CatalogDataModels").ItemsSource.Cast<object>()));
                Assert.Equal("UpdatedCase", row.Model.LabelCabinets![0]);
                File.WriteAllText(EquipmentCatalog.LocalPath(folder.Path), "invalid JSON");
                Reload(window);
                row = Assert.IsType<EquipmentCatalogDataView.CatalogRow>(Assert.Single(Grid(window, "CatalogDataModels").ItemsSource.Cast<object>()));
                Assert.Equal("UpdatedCase", row.Model.LabelCabinets![0]);
                var status = window.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Name == "CatalogDataStatus");
                Assert.True(status.IsVisible);
                Assert.Contains("последние прочитанные", status.Text);
                Assert.Equal(before, GraphSerializer.Serialize(profile, profile.CurrentProfileName));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FilledSerialFieldsKeepTheirExplanationsVisible()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var folder = new ProfileCopyTests.ProfileFolder();
            using var bus = new ModbusService();
            using var profile = new TestViewModel(bus, new SlaveManager(bus));
            var model = profile.EquipmentModels.Single(item => item.Id == "psw-2g8f-plus-box");
            var card = new CatalogSerialsWindow(profile, model);
            card.Show(); Layout(card);
            try
            {
                card.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 1; Layout(card);
                var first = Field(card, "Стартовый серийный номер");
                Assert.Equal("2600001", first.Text);
                Assert.Contains("Не номер текущего устройства", AutomationProperties.GetHelpText(first));
                Assert.Contains("2600001 − 2600000 = 00001", AutomationProperties.GetHelpText(Field(card, "База короткого номера")));
                Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text?.Contains("Карточка не создаёт тип на сервере") == true && text.IsEffectivelyVisible);
                card.CaptureRenderedFrame()!.Save(Path.Combine(Path.GetTempPath(), "tfortis-catalog-field-help.png"));
            }
            finally { card.Close(); }
        }, CancellationToken.None);
    }

    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static TabControl MainTabs(Window window) => window.GetVisualDescendants().OfType<TabControl>().Single(tab => tab.Name == "EquipmentCatalogTabs");
    private static DataGrid Grid(Window window, string name) => window.GetVisualDescendants().OfType<DataGrid>().Single(grid => grid.Name == name);
    private static TextBox Search(Window window) => window.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "CatalogDataSearch");
    private static TextBox Field(Window window, string caption) => window.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == caption);
    private static void Reload(Window window)
    {
        window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "ReloadCatalogData")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Layout(window);
    }
}
