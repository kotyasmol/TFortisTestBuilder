using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class PrinterPickerTests
{
    [Fact]
    public async Task Refresh_HandlesEmptyFailureAndRecovery_WithoutDroppingPreviousList()
    {
        var catalog = new Catalog(); var vm = new PrinterPickerViewModel(catalog);
        await vm.EnsureLoadedAsync(); Assert.Empty(vm.Printers); Assert.Contains("не найдены", vm.Message);
        catalog.Names = new[] { "Zebra", "", "TSC TE310", "tsc te310", "  ", @"\\server\Labels" };
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Printers.Count); Assert.True(vm.CanChoose); Assert.Empty(vm.Message);
        var good = vm.Printers;
        catalog.Error = new InvalidOperationException("Spooler stopped");
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Same(good, vm.Printers); Assert.Contains("Spooler stopped", vm.Message); Assert.True(vm.CanRefresh);
        catalog.Error = new PlatformNotSupportedException("Только Windows");
        await vm.RefreshCommand.ExecuteAsync(null); Assert.Equal("Только Windows", vm.Message);
        catalog.Error = new TimeoutException();
        await vm.RefreshCommand.ExecuteAsync(null); Assert.Contains("10 секунд", vm.Message);
        catalog.Error = null; catalog.Names = new[] { "New printer" };
        await vm.RefreshCommand.ExecuteAsync(null); Assert.Equal("New printer", Assert.Single(vm.Printers)); Assert.Empty(vm.Message);
    }

    [Fact]
    public async Task SlowRefresh_IsSingleFlight_AndCancelledResultsAreIgnored()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<string>>();
        var catalog = new Catalog { Pending = completion.Task };
        var vm = new PrinterPickerViewModel(catalog);
        var running = vm.EnsureLoadedAsync();
        Assert.True(vm.IsRefreshing); Assert.False(vm.CanRefresh); Assert.False(vm.CanChoose);
        await vm.EnsureLoadedAsync(); Assert.Equal(1, catalog.Calls);
        vm.CancelRefresh(); completion.SetResult(new[] { "Late printer" }); await running;
        Assert.Empty(vm.Printers); Assert.False(vm.IsRefreshing); Assert.Empty(vm.Message);
        catalog.Pending = null; catalog.Names = new[] { "Current printer" };
        await vm.EnsureLoadedAsync(); Assert.Equal("Current printer", Assert.Single(vm.Printers));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MouseSelectionAndManualInput_ReachPrintAndRepeat_RefreshPreservesName(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var catalog = new Catalog { Names = new[] { "TSC TE310", @"\\server\Label printer" } };
            var printer = new Printer();
            using var vm = new LabelPrintingViewModel(NullLogger.Instance, () => true, new Http(), printer)
                { PrinterName = "Previously selected", SerialNumber = "3200042", MacAddress = "C0:11:A6:20:01:AC" };
            var picker = new PrinterPicker(catalog);
            picker.Bind(PrinterPicker.PrinterNameProperty, new Binding(nameof(vm.PrinterName)) { Source = vm, Mode = BindingMode.TwoWay });
            var input = picker.FindControl<AutoCompleteBox>("PrinterNameInput")!;
            Popup? popup = null;
            input.TemplateApplied += (_, e) => popup = e.NameScope.Find<Popup>("PART_Popup");
            var window = new Window { Width = 800, Height = 480, Content = new StackPanel { Margin = new Thickness(24), Spacing = 12,
                Children = { new TextBlock { Text = "Принтер для этикеток" }, picker } } };
            void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); }
            void Click(Control target)
            {
                var top = TopLevel.GetTopLevel(target)!;
                var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), top)!.Value;
                top.MouseDown(point, MouseButton.Left); top.MouseUp(point, MouseButton.Left); Layout();
            }
            try
            {
                window.Show(); Layout(); await picker.State.EnsureLoadedAsync(); Layout();
                Assert.Equal("Previously selected", input.Text); Assert.Equal(1, catalog.Calls);
                Click(picker.FindControl<Button>("ChoosePrinterButton")!);
                Assert.True(input.IsDropDownOpen); Assert.NotNull(popup); Layout();
                var item = popup!.Child!.GetVisualDescendants().OfType<ListBoxItem>().Single(i => (string?)i.DataContext == "TSC TE310");
                Click(item);
                Assert.Equal("TSC TE310", vm.PrinterName); Assert.False(input.IsDropDownOpen);
                await vm.PrintCommand.ExecuteAsync(null); Assert.Equal("TSC TE310", Assert.Single(printer.Names));
                var manual = input.GetVisualDescendants().OfType<TextBox>().Single();
                manual.Focus(); manual.SelectAll(); window.KeyTextInput(@"\\custom\Queue"); Layout();
                input.IsDropDownOpen = false;
                Assert.Equal(@"\\custom\Queue", vm.PrinterName);
                catalog.Names = new[] { "Other queue" };
                Click(picker.FindControl<Button>("RefreshPrintersButton")!);
                await picker.State.RefreshCommand.ExecutionTask!; Layout();
                Assert.Equal(@"\\custom\Queue", input.Text); Assert.Equal(@"\\custom\Queue", vm.PrinterName);
                await vm.RepeatLastCommand.ExecuteAsync(null); Assert.Equal(@"\\custom\Queue", printer.Names.Last());
                // New names remain selectable with the mouse even though the old text has no match.
                Click(picker.FindControl<Button>("ChoosePrinterButton")!); Layout();
                Assert.True(input.IsDropDownOpen);
                window.CaptureRenderedFrame()!.Save($"/private/tmp/tfortis-printer-picker-{dark}.png");
                picker.IsEnabled = false; Layout();
                Assert.False(picker.FindControl<Button>("ChoosePrinterButton")!.IsEffectivelyEnabled);
                Assert.False(picker.FindControl<Button>("RefreshPrintersButton")!.IsEffectivelyEnabled);
                Assert.False(manual.IsEffectivelyEnabled);
            }
            finally { window.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Default; }
            return true;
        }, CancellationToken.None);
    }

    private sealed class Catalog : IPrinterCatalog
    {
        public string[] Names = Array.Empty<string>();
        public Exception? Error;
        public Task<IReadOnlyList<string>>? Pending;
        public int Calls;
        public Task<IReadOnlyList<string>> GetPrintersAsync(CancellationToken token)
        { Calls++; return Pending ?? (Error != null ? Task.FromException<IReadOnlyList<string>>(Error) : Task.FromResult<IReadOnlyList<string>>(Names)); }
    }
    private sealed class Printer : IRawLabelPrinter
    {
        public List<string> Names { get; } = new();
        public RawLabelPrintResult Print(string name, byte[] bytes, CancellationToken token)
        { Names.Add(name); return RawLabelPrintResult.Ok(); }
    }
    private sealed class Http : IHttpRequestService
    {
        public Task<HttpRequestResult> GetAsync(string url, TimeSpan timeout, CancellationToken token) => throw new NotSupportedException();
    }
}
