using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class NetworkPairAdditionTests
{
    private static BenchNetworkPlan InitialPlan() => new NetworkPortBatch(2, 1, "LAN", "Медь", 100,
        "10.42.0.2", "172.20.0.2", true).AppendTo(new());

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InlineActionAppendsTwelvePairsAndPreservesAssignmentsAndExistingNodes(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var settings = AppSettings.Instance; var source = settings.NetworkPlan = InitialPlan();
            var original = JsonSerializer.Serialize(source);
            var bindings = settings.DataTestAdapterIds = new() { ["10.42.0.2"] = "card-0", ["10.42.0.3"] = "card-1" };
            var applied = settings.AppliedDataTestAdapterIds = new(bindings);
            settings.SaveChecked();
            var cards = source.Addresses.Select((p, i) => new BenchAdapter("card-" + i, "Ethernet " + i,
                "00112233000" + i, "Up", p.Ip, 1000)).ToArray();
            var node = new RunDataTestNodeViewModel { PortsText = source.BuildDataTestPairs() };
            var oldPairs = node.PortsText; var saves = 0;
            var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>(),
                saveSettings: () => { settings.SaveChecked(); saves++; });
            var theme = Application.Current!.RequestedThemeVariant;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var view = new NetworkSetupView { DataContext = vm };
            var window = new Window { Width = 1100, Height = 800, Content = view };
            try
            {
                window.Show(); Layout(window);
                Find<Expander>(window, "NetworkPlanSection").IsExpanded = true; Layout(window);
                MouseClick(window, Find<Button>(window, "AddNetworkPairs"));
                var host = Find<ContentControl>(window, "NetworkPairEditorHost");
                var form = host.Content; Assert.NotNull(form); Assert.Empty(window.OwnedWindows);
                Press(window, "AddNetworkPairs"); Assert.Same(form, host.Content);
                Find<NumericUpDown>(window, "BatchCount").Value = 12;
                Find<TextBox>(window, "BatchSpeed").Text = "275"; Layout(window);
                Assert.Contains("24", Find<TextBlock>(window, "BatchSummary").Text);
                Capture(window, "form-" + dark);
                vm.RowSearch = "no match"; vm.RowFilterIndex = 2;
                MouseClick(window, Find<Button>(window, "ConfirmNetworkBatch"));
                Assert.False(host.IsVisible); Assert.Null(host.Content); Assert.Empty(window.OwnedWindows);
                Assert.Equal(1, saves); Assert.Equal(26, settings.NetworkPlan.Addresses.Count);
                Assert.Equal(original, JsonSerializer.Serialize(source));
                Assert.Equal(JsonSerializer.Serialize(source.Addresses), JsonSerializer.Serialize(settings.NetworkPlan.Addresses.Take(2)));
                Assert.Same(bindings, settings.DataTestAdapterIds); Assert.Same(applied, settings.AppliedDataTestAdapterIds);
                Assert.Equal(26, AppSettings.Load(clean.SettingsFile).NetworkPlan.Addresses.Count);
                Assert.Equal(13, settings.NetworkPlan.BuildDataTestPairs().Split(Environment.NewLine).Length);
                Assert.All(settings.NetworkPlan.Addresses.Skip(2), p => Assert.Equal(275, p.TestMbps));
                Assert.Equal("", vm.RowSearch); Assert.Equal(0, vm.RowFilterIndex);
                Assert.Equal("card-0", vm.Rows[0].SelectedAdapter?.Id); Assert.Equal("card-1", vm.Rows[1].SelectedAdapter?.Id);
                Assert.All(vm.Rows.Skip(2), p => Assert.Null(p.SelectedAdapter));
                Assert.Same(vm.Rows[2], Find<DataGrid>(window, "NetworkPlanAdaptersGrid").SelectedItem);
                var addedRow = Find<DataGrid>(window, "NetworkPlanAdaptersGrid").GetVisualDescendants().OfType<DataGridRow>()
                    .Single(r => ReferenceEquals(r.DataContext, vm.Rows[2]));
                Assert.InRange(addedRow.TranslatePoint(new Point(0, addedRow.Bounds.Height), window)!.Value.Y, 0, window.ClientSize.Height);
                Assert.Equal(oldPairs, node.PortsText);
                node.ImportNetworkPlanCommand.Execute(null);
                Assert.Equal(13, node.ParsePorts().Count()); Assert.True(node.AllowGigabit);
                Capture(window, "added-" + dark);
                var saved = File.ReadAllText(clean.SettingsFile);
                Press(window, "AddNetworkPairs"); Press(window, "CancelNetworkBatch");
                Assert.Equal(saved, File.ReadAllText(clean.SettingsFile)); Assert.Equal(1, saves);
            }
            finally { window.Close(); Application.Current.RequestedThemeVariant = theme; }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StalePlanAndWriteFailureDoNotOverwriteSettingsAndTheFormCanBeRetried()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var settings = AppSettings.Instance; settings.NetworkPlan = InitialPlan(); settings.SaveChecked();
            var fail = false;
            var vm = new NetworkSetupViewModel(() => Array.Empty<BenchAdapter>(), () => true,
                loadPresets: () => Array.Empty<NetworkConfigurationPreset>(), saveSettings: () =>
                { if (fail) throw new IOException("disk failed"); settings.SaveChecked(); });
            var window = new Window { Width = 1100, Height = 800, Content = new NetworkSetupView { DataContext = vm } };
            try
            {
                window.Show(); Layout(window); Find<Expander>(window, "NetworkPlanSection").IsExpanded = true; Layout(window);
                Press(window, "AddNetworkPairs");
                var changed = new NetworkPortBatch(2, 3, "SFP", "Оптика", 1000, "10.42.0.4", "172.20.0.4", true).AppendTo(settings.NetworkPlan);
                vm.SaveAddressPlan(changed);
                var current = settings.NetworkPlan; var bindings = settings.DataTestAdapterIds; var applied = settings.AppliedDataTestAdapterIds;
                var saved = File.ReadAllText(clean.SettingsFile);
                Press(window, "ConfirmNetworkBatch");
                Assert.Contains("План изменился", Find<TextBlock>(window, "BatchError").Text);
                Assert.Same(current, settings.NetworkPlan); Assert.Equal(saved, File.ReadAllText(clean.SettingsFile));
                Press(window, "CancelNetworkBatch"); Press(window, "AddNetworkPairs");
                fail = true; Press(window, "ConfirmNetworkBatch");
                Assert.Contains("disk failed", Find<TextBlock>(window, "BatchError").Text);
                Assert.Same(current, settings.NetworkPlan); Assert.Same(bindings, settings.DataTestAdapterIds);
                Assert.Same(applied, settings.AppliedDataTestAdapterIds); Assert.Equal(saved, File.ReadAllText(clean.SettingsFile));
                fail = false; Press(window, "ConfirmNetworkBatch");
                Assert.Equal(6, settings.NetworkPlan.Addresses.Count);
                Assert.Equal(6, AppSettings.Load(clean.SettingsFile).NetworkPlan.Addresses.Count);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EmptyPlanCanAddItsFirstPairAndInvalidInputsDoNotSave()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation(); var saves = 0;
            var settings = AppSettings.Instance; var source = settings.NetworkPlan;
            var vm = new NetworkSetupViewModel(() => Array.Empty<BenchAdapter>(), () => true,
                loadPresets: () => Array.Empty<NetworkConfigurationPreset>(), saveSettings: () => { saves++; settings.SaveChecked(); });
            var window = new Window { Width = 1100, Height = 800, Content = new NetworkSetupView { DataContext = vm } };
            try
            {
                window.Show(); Layout(window); Find<Expander>(window, "NetworkPlanSection").IsExpanded = true; Layout(window);
                Press(window, "AddNetworkPairs");
                Find<NumericUpDown>(window, "BatchCount").Value = 1.5m;
                Press(window, "ConfirmNetworkBatch"); Assert.Equal(0, saves);
                Assert.Contains("целыми", Find<TextBlock>(window, "BatchError").Text);
                Find<NumericUpDown>(window, "BatchCount").Value = 1;
                Find<Expander>(window, "BatchAddressSettings").IsExpanded = true; Layout(window);
                Find<TextBox>(window, "BatchFirstTemporaryIp").Text = "192.168.0.2";
                Press(window, "ConfirmNetworkBatch"); Assert.Equal(0, saves);
                Assert.NotEmpty(Find<TextBlock>(window, "BatchError").Text!);
                Find<TextBox>(window, "BatchFirstTemporaryIp").Text = "192.0.2.2";
                Press(window, "ConfirmNetworkBatch"); Assert.Equal(1, saves);
                Assert.Empty(source.Addresses); Assert.Equal(2, vm.Rows.Count);
                Assert.Single(settings.NetworkPlan.BuildDataTestPairs().Split(Environment.NewLine));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FullEditorCanAppendOneHundredFiftyPairsAndChangeMaskBeforeSavingTheDraft()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var source = InitialPlan(); BenchNetworkPlan? saved = null;
            var window = new NetworkPlanWindow(source, p => saved = p) { Width = 1100, Height = 800 };
            try
            {
                window.Show(); Layout(window);
                window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Добавить пары…"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
                Assert.Empty(window.OwnedWindows);
                Find<NumericUpDown>(window, "BatchCount").Value = 150;
                Find<TextBox>(window, "BatchSpeed").Text = "750";
                Find<Expander>(window, "BatchAddressSettings").IsExpanded = true; Layout(window);
                Find<TextBox>(window, "BatchMask").Text = "255.255.0.0";
                Layout(window); Capture(window, "full-editor-form");
                Press(window, "ConfirmNetworkBatch"); Assert.Null(saved);
                Assert.Equal(2, source.Addresses.Count);
                Assert.Equal(302, Find<DataGrid>(window, "NetworkPlanAddresses").ItemsSource.Cast<object>().Count());
                Assert.Equal("255.255.0.0", Find<TextBox>(window, "NetworkPlanMask").Text);
                window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Сохранить план"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window);
                Assert.NotNull(saved); Assert.Equal(302, saved.Addresses.Count);
                Assert.Equal("255.255.0.0", saved.Mask);
                Assert.Equal(151, saved.BuildDataTestPairs().Split(Environment.NewLine).Length);
                Assert.All(saved.Addresses.Skip(2), p => Assert.Equal(750, p.TestMbps));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
    private static void Press(Window window, string name) { Find<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(window); }
    private static void Layout(Window window) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private static void MouseClick(Window window, Control control)
    {
        control.BringIntoView(); Layout(window);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
    }
    private static void Capture(Window window, string step)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tfortis-network-pairs-20261123"); Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, step + ".png"));
    }
}
