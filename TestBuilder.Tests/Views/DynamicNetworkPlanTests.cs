using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class DynamicNetworkPlanTests
{
    [Fact]
    public void FreshSettingsHaveNoInventedPortsAndEmptyPlanCannotApplySystemAddresses()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var plan = new BenchNetworkPlan(); plan.Validate();
        Assert.Empty(plan.Addresses); Assert.Empty(AppSettings.Load(clean.SettingsFile).NetworkPlan.Addresses);
        Assert.Empty(JsonSerializer.Deserialize<BenchNetworkPlan>("{}")!.Addresses);
        var cards = new[] { Card(0) };
        var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
        Assert.Empty(vm.Rows); Assert.True(vm.HasNoTestPorts); Assert.False(vm.CanApply);
        Assert.Single(vm.ComputerAdapters);
        Assert.Contains("Тестовые порты не настроены", vm.Validation);
        var next = plan.CreateNextAddress(); Assert.Equal("192.168.0.2", next.Ip);
        Assert.Empty(plan.Addresses); // A suggestion does not create a port.
    }

    [Theory]
    [InlineData(12)] [InlineData(24)] [InlineData(48)] [InlineData(300)]
    public void SavedPlansRetainTheirCountAndMetadataWithoutAddingDefaultRows(int count)
    {
        var plan = new NetworkPortBatch(count, 1, "LAN", "Разъём", 375, "10.42.0.2", "172.20.0.2", true)
            .AppendTo(new BenchNetworkPlan { Mask = "255.255.0.0" });
        var json = JsonSerializer.Serialize(plan);
        var restored = JsonSerializer.Deserialize<BenchNetworkPlan>(json)!;
        Assert.Equal(count, restored.Addresses.Count); Assert.Equal(json, JsonSerializer.Serialize(restored));
        Assert.Equal(count / 2, restored.BuildDataTestPairs().Split(Environment.NewLine).Length);
        var old = NetworkTestPlans.TenPorts();
        Assert.Equal(10, JsonSerializer.Deserialize<BenchNetworkPlan>(JsonSerializer.Serialize(old))!.Addresses.Count);
    }

    [Fact]
    public void AddingAnotherPhysicalCardDoesNotInventTestPortsOrChangeExistingBindings()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var cards = Enumerable.Range(0, 12).Select(Card).ToArray();
        var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
        for (var i = 0; i < 12; i++)
        {
            var editor = vm.CreateAdapterSettings("card-" + i);
            Assert.Equal(i + 1, editor.Ports.Count); // Only actual user-created ports plus "not assigned".
            editor.AddPort(); editor.SaveMapping();
        }
        Assert.Equal(12, vm.Rows.Count); Assert.Equal(12, AppSettings.Instance.DataTestAdapterIds.Count);
        var savedPlan = JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan);
        var savedBindings = JsonSerializer.Serialize(AppSettings.Instance.DataTestAdapterIds);
        cards = cards.Append(Card(12)).ToArray(); vm.RefreshCommand.Execute(null);
        Assert.Equal(13, vm.ComputerAdapters.Count); Assert.Equal(12, vm.Rows.Count);
        Assert.Equal(savedPlan, JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan));
        Assert.Equal(savedBindings, JsonSerializer.Serialize(AppSettings.Instance.DataTestAdapterIds));
        var added = vm.CreateAdapterSettings("card-12"); added.AddPort(); added.SaveMapping();
        Assert.Equal(13, vm.Rows.Count); Assert.Equal(14, added.Ports.Count);
    }

    [Fact]
    public void DeletingTheLastPortPersistsEmptyPlanAndRetainsPreviouslyAppliedOwnership()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var vm = new NetworkSetupViewModel(() => new[] { Card(0) }, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
        var editor = vm.CreateAdapterSettings("card-0"); editor.AddPort(); editor.SaveMapping();
        var ip = editor.SelectedPort!.Port!.Ip;
        var applied = new Dictionary<string, string> { [ip] = "card-0" };
        AppSettings.Instance.AppliedDataTestAdapterIds = applied;
        editor.RemovePort(); editor.SaveMapping();
        Assert.Empty(AppSettings.Load(clean.SettingsFile).NetworkPlan.Addresses);
        Assert.Empty(AppSettings.Instance.DataTestAdapterIds); Assert.Same(applied, AppSettings.Instance.AppliedDataTestAdapterIds);
        Assert.Empty(vm.Rows); Assert.False(vm.CanApply);
        Assert.Single(vm.CreateAdapterSettings("card-0").Ports);
    }

    [Fact]
    public void RemovingPortsFromTheWholePlanRollsBackAllSettingsIfSavingFails()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
        var settings = AppSettings.Instance; settings.NetworkPlan = NetworkTestPlans.TenPorts();
        settings.DataTestAdapterIds = new() { ["192.168.0.2"] = "card-0" };
        var plan = settings.NetworkPlan; var bindings = settings.DataTestAdapterIds;
        var vm = new NetworkSetupViewModel(() => new[] { Card(0) }, () => true,
            saveSettings: () => throw new IOException("write failed"), loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
        Assert.Throws<IOException>(() => vm.SaveAddressPlan(new BenchNetworkPlan()));
        Assert.Same(plan, settings.NetworkPlan); Assert.Same(bindings, settings.DataTestAdapterIds);
        Assert.Null(settings.AppliedDataTestAdapterIds);
    }

    [Fact]
    public async Task ClearingAllPortsIsADraftUntilSaveAndRetainsAppliedAddressOwnership()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var settings = AppSettings.Instance; settings.NetworkPlan = NetworkTestPlans.TenPorts();
            settings.DataTestAdapterIds = new() { ["192.168.0.2"] = "card-0" };
            var source = settings.NetworkPlan; var bindings = settings.DataTestAdapterIds;
            var vm = new NetworkSetupViewModel(() => new[] { Card(0) }, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
            var window = new NetworkPlanView(source, vm.SaveAddressPlan);
            try
            {
                window.Show(); Layout(window);
                Press("Удалить все порты");
                Assert.Empty(Find<DataGrid>(window, "NetworkPlanAddresses").ItemsSource.Cast<object>());
                Assert.Same(source, settings.NetworkPlan); Assert.Equal(10, source.Addresses.Count);
                Press("Сохранить план"); Assert.False(window.IsVisible);
                Assert.Empty(settings.NetworkPlan.Addresses); Assert.Empty(settings.DataTestAdapterIds);
                Assert.Same(bindings, settings.AppliedDataTestAdapterIds); Assert.Empty(vm.Rows);
                void Press(string caption) { window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window); }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task EmptyCardAssignmentFormCanCreateAndDeletePortsWithoutPredefinedChoices(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var theme = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var vm = new NetworkSetupViewModel(() => new[] { Card(0) }, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
            var editor = vm.CreateAdapterSettings("card-0");
            var window = new NetworkAdapterSettingsView(editor) { Width = 850, Height = 820 };
            try
            {
                window.Show(); Find<TabControl>(window, "AdapterSettingsTabs").SelectedIndex = 2; Layout(window);
                var ports = Find<ComboBox>(window, "AdapterPlanPort"); Assert.Single(ports.Items);
                Assert.Contains("пока нет", Find<TextBlock>(window, "AdapterPlanSummary").Text);
                Capture("empty");
                Press("Создать тестовый порт"); Assert.Equal(2, ports.Items.Count);
                Find<TextBox>(window, "AdapterPortName").Text = "Медь 1";
                Capture("created");
                Press("Сохранить назначение"); Assert.Single(vm.Rows);
                Press("Удалить выбранный порт"); Assert.Single(ports.Items);
                Press("Сохранить назначение"); Assert.Empty(vm.Rows);
                Assert.Empty(AppSettings.Load(clean.SettingsFile).NetworkPlan.Addresses);
                void Press(string caption) { window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window); }
                void Capture(string step)
                {
                    Layout(window); var folder = Path.Combine(Path.GetTempPath(), "tfortis-dynamic-plan-20261123"); Directory.CreateDirectory(folder);
                    using var frame = WorkspacePageTesting.CaptureRenderedFrame(window); frame!.Save(Path.Combine(folder, step + (dark ? "-dark" : "-light") + ".png"));
                }
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = theme; }
        }, CancellationToken.None);
    }
    private static BenchAdapter Card(int i) => new("card-" + i, "Ethernet " + i, "00112233" + i.ToString("X4"), "Up", "", 1000);
    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Layout(Control window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
}
