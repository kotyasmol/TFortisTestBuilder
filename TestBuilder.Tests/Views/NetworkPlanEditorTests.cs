using TestBuilder.Tests.Support;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Styling;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class NetworkPlanEditorTests
{
    private static void Click(Window window, string text) => window.GetVisualDescendants().OfType<Button>()
        .Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static DataGrid Grid(Window window) => window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "NetworkPlanAddresses");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingTemporaryAddressIdentifiesTheRowAndFieldWithoutChangingThePlan(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var settings = AppSettings.Instance;
            var previous = settings.NetworkPlan;
            var oldTheme = Avalonia.Application.Current!.RequestedThemeVariant;
            Avalonia.Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var plan = NetworkTestPlans.TenPorts();
            plan.Addresses.Add(new() { Name = "Новая карта", Ip = "192.168.0.12" });
            settings.NetworkPlan = plan;
            var window = new NetworkPlanWindow { Width = 900 };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Сохранить план"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-network-plan-validation-{dark}.png"));
                Assert.True(window.IsVisible);
                Assert.Same(plan, settings.NetworkPlan);
                var texts = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
                Assert.Contains(texts, t => t.Contains("Строка 11") && t.Contains("Временный адрес"));
            }
            finally { window.Close(); settings.NetworkPlan = previous; Avalonia.Application.Current.RequestedThemeVariant = oldTheme; }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, 900)]
    [InlineData(true, 1200)]
    public async Task AddedCardsHaveValidAddressesAndSaveAsIndependentRows(bool dark, int width)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var oldTheme = Avalonia.Application.Current!.RequestedThemeVariant;
            Avalonia.Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var source = NetworkTestPlans.TenPorts();
            BenchNetworkPlan? saved = null;
            var window = new NetworkPlanWindow(source, plan => saved = plan) { Width = width };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Click(window, "Добавить порт"); Click(window, "Добавить порт");
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(12, Grid(window).ItemsSource.Cast<BenchNetworkAddress>().Count());
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-network-plan-added-{dark}.png"));
                Click(window, "Сохранить план");
                Assert.False(window.IsVisible);
                Assert.NotNull(saved); saved.Validate();
                Assert.Equal(10, source.Addresses.Count);
                Assert.Equal(new[] { "192.168.0.12", "192.168.0.13" }, saved.Addresses.Skip(10).Select(a => a.Ip));
                Assert.Equal(new[] { "192.0.2.12", "192.0.2.13" }, saved.Addresses.Skip(10).Select(a => a.TemporaryIp));
                Assert.All(saved.Addresses.Skip(10), a => Assert.False(a.IncludeInDataTest));
                saved.Addresses[0].Name = "Saved copy";
                Assert.Equal("", source.Addresses[0].Name);
            }
            finally { window.Close(); Avalonia.Application.Current.RequestedThemeVariant = oldTheme; }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SaveCommitsTheLastEditedCellWithoutRequiringAnotherClick()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var source = NetworkTestPlans.TenPorts(); BenchNetworkPlan? saved = null;
            var window = new NetworkPlanWindow(source, plan => saved = plan);
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var grid = Grid(window); grid.SelectedIndex = 0; grid.CurrentColumn = grid.Columns[4]; grid.Focus();
                Assert.True(grid.BeginEdit()); Dispatcher.UIThread.RunJobs();
                var editor = Assert.Single(grid.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible));
                editor.Text = " 192.168.0.40 ";
                Click(window, "Сохранить план");
                Assert.False(window.IsVisible);
                Assert.NotNull(saved);
                Assert.Equal("192.168.0.40", saved.Addresses[0].Ip);
                Assert.Equal("192.168.0.2", source.Addresses[0].Ip);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SaveAddsRowsImmediatelyKeepsDraftChoicesAndCanRecoverFromDiskFailure()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var settings = AppSettings.Instance; var previous = settings.NetworkPlan;
            var oldBindings = settings.DataTestAdapterIds; var oldApplied = settings.AppliedDataTestAdapterIds;
            var source = NetworkTestPlans.TenPorts(); settings.NetworkPlan = source;
            var cards = Enumerable.Range(2, 11).Select(i => new BenchAdapter($"card-{i}", $"Card {i}", "", "Down", $"192.168.0.{i}", 0)).ToArray();
            settings.DataTestAdapterIds = cards.Take(10).ToDictionary(c => c.Ipv4, c => c.Id);
            settings.AppliedDataTestAdapterIds = new() { ["10.1.1.2"] = "previous-card" };
            var bindings = settings.DataTestAdapterIds; var applied = settings.AppliedDataTestAdapterIds;
            var fail = true; var saves = 0;
            var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>(),
                saveSettings: () => { saves++; if (fail) throw new IOException("Нет места на диске"); });
            vm.Rows[0].SelectedAdapter = cards[1]; // An unsaved swap must survive editing the plan.
            vm.Rows[2].SelectedAdapter = null; // An explicit empty choice must also survive.
            var window = new NetworkPlanWindow(source, vm.SaveAddressPlan);
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Click(window, "Добавить порт"); Click(window, "Сохранить план");
                Assert.True(window.IsVisible);
                Assert.Contains("Нет места на диске", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NetworkPlanError").Text);
                Assert.Same(source, settings.NetworkPlan); Assert.Equal(10, vm.Rows.Count);
                Assert.Equal("card-3", vm.Rows[0].SelectedAdapter?.Id); Assert.Null(vm.Rows[2].SelectedAdapter);
                fail = false; Click(window, "Сохранить план");
                Assert.False(window.IsVisible); Assert.Equal(2, saves);
                Assert.Equal(11, settings.NetworkPlan.Addresses.Count); Assert.Equal(11, vm.Rows.Count);
                Assert.Equal("card-3", vm.Rows[0].SelectedAdapter?.Id); Assert.Equal("card-2", vm.Rows[1].SelectedAdapter?.Id);
                Assert.Null(vm.Rows[2].SelectedAdapter);
                Assert.Equal("card-12", vm.Rows[10].SelectedAdapter?.Id);
                Assert.Same(bindings, settings.DataTestAdapterIds); Assert.Same(applied, settings.AppliedDataTestAdapterIds);
                Assert.Equal(10, source.Addresses.Count);
                Assert.Contains("План сохранён", vm.Status);
                Assert.All(cards, c => Assert.Equal("Down", c.Status)); // Saving does not manufacture a link.
            }
            finally { window.Close(); settings.NetworkPlan = previous; settings.DataTestAdapterIds = oldBindings; settings.AppliedDataTestAdapterIds = oldApplied; }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CancelNeverPersistsTheDraft()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var source = NetworkTestPlans.TenPorts(); var saves = 0;
            var window = new NetworkPlanWindow(source, _ => saves++);
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Click(window, "Добавить порт"); Click(window, "Отмена");
                Assert.Equal(0, saves); Assert.Equal(10, source.Addresses.Count); Assert.False(window.IsVisible);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InvalidNumericCellDoesNotSaveAndItsRowCanStillBeDeleted()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var source = NetworkTestPlans.TenPorts(); var saves = 0;
            var window = new NetworkPlanWindow(source, _ => saves++);
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var grid = Grid(window); grid.SelectedIndex = 0; grid.CurrentColumn = grid.Columns[3]; grid.Focus();
                Assert.True(grid.BeginEdit()); Dispatcher.UIThread.RunJobs();
                Assert.Single(grid.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible)).Text = "не число";
                Click(window, "Сохранить план");
                Assert.True(window.IsVisible); Assert.Equal(0, saves);
                Assert.Contains("Мбит/с теста", window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "NetworkPlanError").Text);
                Click(window, "Удалить строку");
                Assert.Equal(9, grid.ItemsSource.Cast<BenchNetworkAddress>().Count());
                Assert.Equal(10, source.Addresses.Count);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public void EditingPlanPreservesTheRememberedIdOfATemporarilyMissingCard()
    {
        var settings = AppSettings.Instance; var previous = settings.NetworkPlan; var bindings = settings.DataTestAdapterIds;
        var source = NetworkTestPlans.TenPorts(); settings.NetworkPlan = source; settings.DataTestAdapterIds = new();
        var all = Enumerable.Range(2, 10).Select(i => new BenchAdapter($"card-{i}", $"Card {i}", "", "Up", $"192.168.0.{i}", 1000)).ToArray();
        var cards = all;
        try
        {
            var vm = new NetworkSetupViewModel(() => cards, () => true, loadPresets: () => Array.Empty<NetworkConfigurationPreset>(), saveSettings: () => { });
            vm.Rows[0].SelectedAdapter = all[1];
            cards = all.Where(c => c.Id != "card-3").ToArray(); vm.RefreshCommand.Execute(null);
            Assert.Null(vm.Rows[0].SelectedAdapter);
            vm.SaveAddressPlan(source);
            Assert.Null(vm.Rows[0].SelectedAdapter);
            cards = all; vm.RefreshCommand.Execute(null);
            Assert.Equal("card-3", vm.Rows[0].SelectedAdapter?.Id);
        }
        finally { settings.NetworkPlan = previous; settings.DataTestAdapterIds = bindings; }
    }
}
