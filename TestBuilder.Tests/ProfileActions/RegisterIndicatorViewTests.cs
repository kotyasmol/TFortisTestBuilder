using TestBuilder.Models;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Views;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.ProfileActions;

[Collection("Graph editor UI")]
public class RegisterIndicatorViewTests
{
    [Fact]
    public async Task Cards_RenderInBothThemes_OperatorCannotEdit_DeleteNeedsConfirmationAndCanBeUndone()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            ProfileActionTests.LoadEmpty(vm); vm.RootGraph.Hardware = RegisterIndicatorTests.Hardware();
            vm.RootGraph.RegisterIndicators.Add(RegisterIndicatorTests.Indicator());
            var custom = RegisterIndicatorTests.Indicator(); custom.Color = "#EAB308"; custom.Name = "Питание 2";
            vm.RootGraph.RegisterIndicators.Add(custom); vm.IsQuickActionsCollapsed = false;
            var state = new RegisterState(); state.Update(42, 123, 251);
            foreach (var card in vm.RegisterIndicators) card.Refresh(state, vm.RootGraph.Hardware, true, true);
            var bar = new RegisterIndicatorsView { DataContext = vm };
            var window = new Window { Width = 400, Height = 700, Content = bar };
            try
            {
                window.Show(); Layout(window);
                var cards = bar.GetVisualDescendants().OfType<ContentControl>().Where(c => c.Classes.Contains("registerIndicator")).ToArray();
                Assert.Equal(2, cards.Length);
                var add = bar.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.AddRegisterIndicatorCommand));
                var edit = bar.GetVisualDescendants().OfType<Button>().Where(b => ReferenceEquals(b.Command, vm.EditRegisterIndicatorCommand)).ToArray();
                Assert.True(add.IsEffectivelyVisible); Assert.True(add.IsEnabled); Assert.Equal(2, edit.Length);
                Color? previous = null;
                foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
                {
                    window.RequestedThemeVariant = theme; Layout(window);
                    var color = Assert.IsAssignableFrom<ISolidColorBrush>(cards[0].Background).Color;
                    Assert.NotEqual(previous, color); previous = color;
                    Assert.Equal(theme == Avalonia.Styling.ThemeVariant.Light ? Colors.Black : Colors.White,
                        Assert.IsAssignableFrom<ISolidColorBrush>(cards[0].Foreground).Color);
                    Assert.Equal(Color.Parse("#EAB308"), Assert.IsAssignableFrom<ISolidColorBrush>(cards[1].Background).Color);
                    Assert.Equal(Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(cards[1].Foreground).Color);
                    foreach (var card in cards)
                    {
                        Assert.Contains("alert", card.Classes);
                        Assert.All(card.GetVisualDescendants().OfType<TextBlock>(), t => Assert.Equal(card.Foreground, t.Foreground));
                        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Выше допуска");
                    }
                    Capture(window, $"register-indicators-{theme}.png");
                }
                vm.IsOperatorView = true; Layout(window);
                Assert.True(vm.ShowRegisterIndicators); Assert.False(vm.ShowQuickActions); Assert.False(add.IsEffectivelyVisible);
                Assert.All(edit, b => Assert.False(b.IsEffectivelyVisible));
                Assert.False(vm.DeleteRegisterIndicatorCommand.CanExecute(vm.RegisterIndicators[0]));
                Assert.All(cards, c => Assert.True(c.IsEffectivelyVisible)); Capture(window, "register-indicators-operator.png");
                vm.IsOperatorView = false;
                vm.ConfirmDeleteRegisterIndicatorAsync = _ => Task.FromResult(false);
                await vm.DeleteRegisterIndicatorCommand.ExecuteAsync(vm.RegisterIndicators[0]); Assert.Equal(2, vm.RegisterIndicators.Count);
                var definition = vm.RegisterIndicators[0].Definition;
                vm.ConfirmDeleteRegisterIndicatorAsync = name => { Assert.Equal(definition.Name, name); return Task.FromResult(true); };
                await vm.DeleteRegisterIndicatorCommand.ExecuteAsync(vm.RegisterIndicators[0]); Assert.Single(vm.RegisterIndicators);
                Assert.True(vm.UndoCommand.CanExecute(null)); vm.UndoCommand.Execute(null);
                Assert.Same(definition, vm.RegisterIndicators[0].Definition); Assert.Equal(2, vm.RegisterIndicators.Count);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Editor_RealControlsBindSelectionScalingAndOptionalLimits_WithoutConnection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var window = new RegisterIndicatorEditorWindow(RegisterIndicatorTests.Indicator(), RegisterIndicatorTests.Hardware());
            try
            {
                window.Show(); Layout(window);
                var vm = Assert.IsType<RegisterIndicatorEditorViewModel>(window.DataContext);
                Assert.Same(vm.SelectedDevice, window.FindControl<ComboBox>("DeviceInput")!.SelectedItem);
                var register = window.FindControl<AutoCompleteBox>("RegisterInput")!;
                Assert.Same(vm.SelectedRegister, register.SelectedItem);
                var listButton = window.FindControl<Button>("RegisterListButton")!;
                var point = listButton.TranslatePoint(new Point(18, 18), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
                Assert.True(register.IsDropDownOpen);
                var popup = register.GetVisualDescendants().OfType<Popup>().Single();
                var list = popup.Child!.GetVisualDescendants().OfType<ListBox>().FirstOrDefault() ?? popup.Child as ListBox;
                Assert.NotNull(list); Assert.Equal(2, list.ItemCount);
                list.SelectedItem = vm.Registers[1];
                register.IsDropDownOpen = false;
                window.FindControl<NumericUpDown>("DivisorInput")!.Value = 100; Layout(window);
                Assert.Equal(100, vm.Divisor); Assert.Equal(2, window.FindControl<NumericUpDown>("DecimalsInput")!.Value);
                window.FindControl<TextBox>("NameInput")!.Text = "Выходное напряжение";
                window.FindControl<NumericUpDown>("MinimumInput")!.Value = null;
                window.FindControl<NumericUpDown>("MaximumInput")!.Value = null;
                Layout(window); var result = window.BuildResult();
                Assert.Null(result.Minimum); Assert.Null(result.Maximum); Assert.Equal("Выходное напряжение", result.Name);
                Assert.Equal(ModbusArea.Input, result.Area); Assert.Equal("А", result.Unit);
                Capture(window, "register-indicator-editor.png");
                var text = register.GetVisualDescendants().OfType<TextBox>().Single();
                text.Text = "не существующий регистр";
                Assert.Throws<InvalidOperationException>(() => window.BuildResult());
                Layout(window); Assert.Throws<InvalidOperationException>(() => vm.BuildResult());
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SharedPolling_UpdatesCardsAutomatically_AndDisconnectClearsReadings()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "indicator-poll-" + Guid.NewGuid().ToString("N") + ".json");
            using var port = new FakePort(); var devices = new FakeDevices(); var manager = new SlaveManager(devices);
            using var vm = new TestViewModel(port, manager) { IsOperatorView = false, AvailableStandPorts = () => new[] { "SIM" } };
            try
            {
                ProfileActionTests.LoadEmpty(vm);
                var hardware = RegisterIndicatorTests.Hardware(); hardware.Devices[0].Registers.RemoveAt(1);
                vm.RootGraph.Hardware = hardware; vm.RootGraph.RegisterIndicators.Add(RegisterIndicatorTests.Indicator());
                await vm.SaveGraphToPathAsync(path);
                await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
                Assert.False(vm.RegisterIndicators[0].HasValue);
                await vm.ToggleConnectionCommand.ExecuteAsync(null);
                Assert.True(vm.IsMonitoringActive);
                await Until(() => vm.RegisterIndicators[0].HasValue);
                var card = vm.RegisterIndicators[0];
                Assert.Equal(RegisterIndicatorViewModel.FormatValue(245, 10, 1, "В"), card.ValueText);
                devices.Raw = 260;
                await Until(() => card.IsOutOfRange);
                Assert.Equal(RegisterIndicatorViewModel.FormatValue(260, 10, 1, "В"), card.ValueText);
                Assert.Equal((ushort)260, manager.Slaves.Single().RegisterItems.Single().Value);
                await vm.ToggleConnectionCommand.ExecuteAsync(null);
                Assert.False(card.HasValue); Assert.Equal("—", card.ValueText); Assert.Equal("Нет связи", card.StatusText);
            }
            finally { if (vm.IsConnected) await vm.ToggleConnectionCommand.ExecuteAsync(null); File.Delete(path); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BoundAction_UpdatesWithoutIndicatorCards_AndStopsOnDisconnect()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "action-state-" + Guid.NewGuid().ToString("N") + ".json");
            using var port = new FakePort(); var devices = new FakeDevices { Raw = 1 }; var manager = new SlaveManager(devices);
            using var vm = new TestViewModel(port, manager) { IsOperatorView = false, AvailableStandPorts = () => new[] { "SIM" } };
            try
            {
                ProfileActionTests.LoadEmpty(vm);
                var hardware = ProfileActionStateTests.Hardware(); hardware.Devices[0].Registers.RemoveAt(1);
                hardware.PollIntervalMs = 100;
                vm.RootGraph.Hardware = hardware;
                vm.RootGraph.QuickActions.Add(new() { Display = ProfileActionDisplay.Toggle, StateBinding = ProfileActionStateTests.Binding() });
                await vm.SaveGraphToPathAsync(path);
                await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
                Assert.Empty(vm.RegisterIndicators);
                Assert.False(vm.QuickActions[0].CanRun);
                await vm.ToggleConnectionCommand.ExecuteAsync(null);
                await Until(() => vm.QuickActions[0].IsStateKnown && vm.QuickActions[0].IsOn);
                devices.Raw = 0;
                await Until(() => vm.QuickActions[0].IsStateKnown && !vm.QuickActions[0].IsOn);
                devices.Raw = 2;
                await Until(() => !vm.QuickActions[0].IsStateKnown);
                Assert.False(vm.QuickActions[0].CanRun);
                await vm.ToggleConnectionCommand.ExecuteAsync(null);
                Assert.False(vm.QuickActions[0].CanRun);
                Assert.Contains("Нет связи", vm.QuickActions[0].DetailText);
            }
            finally { if (vm.IsConnected) await vm.ToggleConnectionCommand.ExecuteAsync(null); File.Delete(path); }
            return true;
        }, default);
    }

    private static async Task Until(Func<bool> condition)
    {
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < limit) await Task.Delay(20);
        Assert.True(condition());
    }
    private sealed class FakePort : ModbusService
    {
        public override Task<bool> ConnectAsync(string port, int baudRate, System.IO.Ports.Parity parity, int dataBits, System.IO.Ports.StopBits stopBits)
        { IsConnected = true; return Task.FromResult(true); }
        public override Task ConfigureLineAsync(int baudRate, System.IO.Ports.Parity parity, int dataBits, System.IO.Ports.StopBits stopBits) => Task.CompletedTask;
    }
    private sealed class FakeDevices : IModbusService
    {
        public volatile ushort Raw = 245;
        public int Reads;
        public Task<ushort[]> ReadRegistersAsync(byte slave, ushort address, ushort count, CancellationToken ct = default)
        { Interlocked.Increment(ref Reads); return Task.FromResult(Enumerable.Repeat(address == 0 ? (ushort)77 : Raw, count).ToArray()); }
        public Task<bool> WriteRegisterAsync(byte slave, ushort address, ushort value, bool verify = true, CancellationToken ct = default) => throw new InvalidOperationException("Unexpected write");
        public Task<bool> CheckPortAsync(CancellationToken ct = default) => throw new InvalidOperationException("Unexpected probe");
        public void SubscribeRegister(byte slave, ushort address, Action<ushort[]> callback) { }
    }
    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
    private static void Capture(Window window, string name)
    {
        var path = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS"); if (string.IsNullOrEmpty(path)) return;
        Directory.CreateDirectory(path); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(path, name));
    }
}
