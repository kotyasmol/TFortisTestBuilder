using TestBuilder.Tests.Support;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ComputerNetworkAdaptersTests
{
    private static BenchAdapter[] Cards(int count = 7) => Enumerable.Range(0, count).Select(i =>
        new BenchAdapter("real-" + i, "Ethernet " + i, $"00112233{i:0000}", i % 2 == 0 ? "Up" : "Down", i == 0 ? "192.168.0.2" : "", 1000)
        { Mask = "255.255.255.0", DhcpEnabled = false }).ToArray();

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task PrimaryScreenShowsExistingCardsAndOpensTheirFullSettings(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            AppSettings.Instance.NetworkPlan = NetworkTestPlans.TenPorts();
            var previousTheme = Application.Current!.RequestedThemeVariant; Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var cards = Cards();
            var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
            var view = new NetworkSetupView { DataContext = vm }; var window = new Window { Content = view, Width = 1250, Height = 1000 };
            try
            {
                window.Show(); Layout(window);
                Assert.Equal(10, vm.Rows.Count); Assert.Equal(7, vm.ComputerAdapters.Count);
                var grid = view.FindControl<DataGrid>("NetworkAdaptersGrid")!;
                Assert.Equal(7, grid.ItemsSource.Cast<ComputerNetworkAdapterViewModel>().Count());
                Assert.False(view.FindControl<Expander>("NetworkPlanSection")!.IsExpanded);
                Assert.DoesNotContain(grid.GetVisualDescendants(), c => c is ComboBox);
                Capture(window, "cards-" + dark);
                var beforePlan = JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan);
                var button = grid.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Настроить…"));
                Click(window, button);
                var dialog = Assert.Single(window.OwnedWindows.OfType<NetworkAdapterSettingsWindow>());
                var editor = dialog.Editor; Assert.Equal("real-0", editor.Current.Id);
                dialog.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "AdapterSystemName").Text = "Не сохранено";
                Capture(dialog, "adapter-info-" + dark);
                var tabs = dialog.GetVisualDescendants().OfType<TabControl>().Single(); tabs.SelectedIndex = 1; Layout(dialog);
                Capture(dialog, "adapter-ipv4-" + dark);
                tabs.SelectedIndex = 2; Layout(dialog);
                editor.SelectedPort = editor.Ports.First(p => p.Port != null);
                editor.SelectedPort.Port!.Name = "Медный порт 1"; editor.TestSpeedText = "125";
                var save = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Сохранить назначение"));
                Click(dialog, save); Assert.Contains("сохранены", editor.Status);
                Assert.Equal("Медный порт 1", AppSettings.Load(clean.SettingsFile).NetworkPlan.Addresses[0].Name);
                Assert.Equal(125, AppSettings.Instance.NetworkPlan.Addresses[0].TestMbps);
                Assert.Equal("real-0", AppSettings.Instance.DataTestAdapterIds["192.168.0.2"]);
                Capture(dialog, "adapter-plan-" + dark); dialog.Close();
                Assert.NotEqual(beforePlan, JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan));
                Assert.Equal("Ethernet 0", vm.ComputerAdapters[0].Name); // Typing a name does not change the OS.
                vm.AdapterSearch = cards[4].Mac; Assert.Single(vm.VisibleComputerAdapters);
                vm.AdapterSearch = ""; vm.AdapterFilterIndex = 2; Assert.Equal(3, vm.VisibleComputerAdapters.Count);
                vm.AdapterFilterIndex = 0;
                var kept = vm.ComputerAdapters[0]; cards = cards.Take(3).ToArray(); vm.RefreshCommand.Execute(null);
                Assert.Equal(3, vm.ComputerAdapters.Count); Assert.Same(kept, vm.ComputerAdapters[0]);
                cards = Array.Empty<BenchAdapter>(); vm.RefreshCommand.Execute(null);
                Assert.Empty(vm.ComputerAdapters); Assert.Empty(vm.VisibleComputerAdapters); Assert.True(vm.HasNoComputerAdapters);
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = previousTheme; }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public void PerCardPlanSaveRollsBackOnDiskFailureAndDoesNotAlterAppliedOwnership()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
            AppSettings.Instance.NetworkPlan = NetworkTestPlans.TenPorts();
        var settings = AppSettings.Instance; var cards = Cards(); var fail = true;
        settings.AppliedDataTestAdapterIds = new() { ["192.168.0.2"] = "previous-owner" };
        var applied = settings.AppliedDataTestAdapterIds; var beforePlan = settings.NetworkPlan; var beforeBindings = settings.DataTestAdapterIds;
        var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>(),
            saveSettings: () => { if (fail) throw new IOException("disk full"); });
        var editor = vm.CreateAdapterSettings("real-1"); editor.SelectedPort = editor.Ports[2]; editor.TestSpeedText = "250";
        Assert.Throws<IOException>(() => editor.SaveMapping());
        Assert.Same(beforePlan, settings.NetworkPlan); Assert.Same(beforeBindings, settings.DataTestAdapterIds);
        fail = false; editor.SaveMapping();
        Assert.Equal("real-1", settings.DataTestAdapterIds["192.168.0.3"]); Assert.Same(applied, settings.AppliedDataTestAdapterIds);
        editor.SelectedPort = editor.Ports[0]; editor.SaveMapping();
        Assert.DoesNotContain("real-1", settings.DataTestAdapterIds.Values);
    }

    [Fact]
    public async Task NativeOperationLocksTheBenchAndUsesOnlyTheChosenCard()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
            AppSettings.Instance.NetworkPlan = NetworkTestPlans.TenPorts();
        var cards = Cards(2); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var operations = new NetworkAdapterOperations(() => cards, async (args, token) =>
        { calls++; entered.TrySetResult(); await finish.Task; cards[1] = cards[1] with { Name = "Новое имя" }; return null; }, () => null);
        var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
        var editor = vm.CreateAdapterSettings("real-1", operations); editor.AdapterName = "Новое имя";
        var task = editor.Rename(); await entered.Task;
        Assert.True(vm.IsBusy); Assert.True(editor.IsBusy); Assert.False(editor.CanClose); Assert.False(vm.CanEdit);
        Assert.False(vm.ProbeConnectionsCommand.CanExecute(null));
        await editor.ApplyIpv4(); Assert.Equal(1, calls);
        finish.TrySetResult(); await task;
        Assert.False(vm.IsBusy); Assert.False(editor.IsBusy); Assert.Equal("Новое имя", vm.ComputerAdapters[1].Name);
        Assert.Equal("Ethernet 0", vm.ComputerAdapters[0].Name);
    }

    [Fact]
    public async Task Ipv4FormChecksInputBeforeApplyingAndRendersAtMinimumSize()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            AppSettings.Instance.NetworkPlan = NetworkTestPlans.TenPorts();
            var cards = Cards(2); var calls = new List<string[]>();
            var operations = new NetworkAdapterOperations(() => cards, (args, _) =>
            {
                calls.Add(args.ToArray());
                cards[1] = cards[1] with { Ipv4 = "192.168.0.20", Dns = "1.1.1.1" };
                return Task.FromResult<string?>(null);
            }, () => null);
            var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
            var editor = vm.CreateAdapterSettings("real-1", operations);
            var before = JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan);
            var dialog = new NetworkAdapterSettingsWindow(editor) { Width = 680, Height = 650 };
            try
            {
                dialog.Show();
                var tabs = Find<TabControl>(dialog, "AdapterSettingsTabs"); tabs.SelectedIndex = 1; Layout(dialog);
                var addressMode = Find<ComboBox>(dialog, "AdapterAddressMode");
                addressMode.SelectedItem = editor.Modes.Single(m => m.Key == "static");
                Find<TextBox>(dialog, "AdapterIpv4").Text = "wrong";
                Find<ComboBox>(dialog, "AdapterDnsMode").SelectedItem = editor.Modes.Single(m => m.Key == "static");
                Find<TextBox>(dialog, "AdapterDns").Text = "1.1.1.1";
                var apply = dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Применить IPv4 и DNS"));
                Click(dialog, apply); Assert.Empty(calls); Assert.Contains("корректный адрес", editor.Status);
                Find<TextBox>(dialog, "AdapterIpv4").Text = "192.168.0.20";
                Click(dialog, apply);
                Assert.Equal(2, calls.Count); Assert.All(calls, c => Assert.Contains("name=Ethernet 1", c));
                Assert.Equal("192.168.0.20", vm.ComputerAdapters[1].Ipv4);
                Assert.Contains("применены", editor.Status); Assert.Equal("static", editor.AddressMode.Key);
                Capture(dialog, "adapter-ipv4-minimum-windows-simulated");
                Assert.Equal(before, JsonSerializer.Serialize(AppSettings.Instance.NetworkPlan));
            }
            finally { dialog.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public void WifiIsVisibleButCannotBeAssignedToDataTest()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
            AppSettings.Instance.NetworkPlan = NetworkTestPlans.TenPorts();
        var cards = Cards(2); cards[1] = cards[1] with { NetworkType = System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 };
        var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
        Assert.Equal(2, vm.ComputerAdapters.Count); Assert.Single(vm.TestAdapters);
        Assert.All(vm.Rows, row => Assert.Single(row.Adapters));
        var editor = vm.CreateAdapterSettings("real-1"); var count = editor.Plan.Addresses.Count;
        Assert.False(editor.CanAssignPort); editor.AddPort(); editor.SaveMapping();
        Assert.Equal(count, editor.Plan.Addresses.Count); Assert.DoesNotContain("real-1", AppSettings.Instance.DataTestAdapterIds.Values);
    }

    [Fact]
    public void MovingAPlanAddressRetainsItsPreviouslyManagedAddressForBulkApply()
    {
        using var clean = new FreshUserJourneyTests.CleanWorkstation();
            AppSettings.Instance.NetworkPlan = NetworkTestPlans.TenPorts(); var cards = Cards(2);
        AppSettings.Instance.DataTestAdapterIds = new() { ["192.168.0.2"] = "real-0" };
        AppSettings.Instance.AppliedDataTestAdapterIds = null;
        var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>());
        var editor = vm.CreateAdapterSettings("real-0");
        editor.SelectedPort!.Port!.Ip = "192.168.0.20"; editor.SaveMapping();
        Assert.Equal("real-0", AppSettings.Instance.DataTestAdapterIds["192.168.0.20"]);
        Assert.Equal("real-0", AppSettings.Instance.AppliedDataTestAdapterIds!["192.168.0.2"]);
        Assert.DoesNotContain("192.168.0.20", AppSettings.Instance.AppliedDataTestAdapterIds.Keys);
    }

    private static T Find<T>(Window window, string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Layout(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Window window, Button button)
    {
        button.BringIntoView(); Layout(window);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
    }
    private static void Capture(Window window, string name)
    {
        Layout(window); var folder = Path.Combine(Path.GetTempPath(), "tfortis-real-adapters-20261123"); Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, name + ".png"));
    }
}
