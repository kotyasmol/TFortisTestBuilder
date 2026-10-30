using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.Tests.Views;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;

namespace TestBuilder.Tests.ProfileActions;

[Collection("Graph editor UI")]
public class ReprintLabelActionTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Reprint_UsesSavedIdentityAndTemplate_AndConfigurablePrinterAndCopies(bool epl)
    {
        var template = LabelTemplateCatalog.Defaults()[epl ? 2 : 0];
        template.Name = "Изделие последнего теста";
        var saved = new PreparedLabel(template, epl ? "00123456" : "3200428", epl ? "" : "02:00:00:00:00:01", "Saved printer", 4);
        var context = new TestContext(new RegisterState()) { DeviceModel = "Другой профиль", LastTestLabel = saved };
        context.SetVariable("SerialNumber", "99999");
        context.SetVariable("Dut.default_mac", "FF:FF:FF:FF:FF:FF");
        var printer = new Printer();
        var node = new PrintLabelNodeViewModel { UseLastTestLabel = true, PrinterName = "", Copies = 2,
            UseManualSerialNumber = true, ManualSerialNumber = "99999" };
        Assert.Equal(StepResult.True, await node.CreateStep(NullLogger.Instance, printer).ExecuteAsync(context, default));
        var command = LabelPrintContent.Render(template, saved.Serial, epl ? 0 : 428, saved.Mac);
        Assert.Equal(command + command, Assert.Single(printer.Jobs));
        Assert.Equal("Saved printer", printer.PrinterName);
        Assert.Equal("LastTest", context.GetVariable<string>("PrintLabel.SerialSource"));
        Assert.Equal(saved.Template.Name, context.LabelPrinting.Last!.Template.Name);
        node.PrinterName = "Replacement printer"; node.Copies = 1;
        printer.Fail = true;
        Assert.Equal(StepResult.False, await node.CreateStep(NullLogger.Instance, printer).ExecuteAsync(context, default));
        printer.Fail = false;
        Assert.Equal(StepResult.True, await node.CreateStep(NullLogger.Instance, printer).ExecuteAsync(context, default));
        Assert.Equal(command, printer.Jobs.Last()); Assert.Equal("Replacement printer", printer.PrinterName);
        Assert.Equal(4, saved.Copies); Assert.Equal("Saved printer", saved.PrinterName);
    }

    [Fact]
    public async Task NoPreviousLabel_AndCancellation_NeverSendDataToPrinter()
    {
        var printer = new Printer();
        var node = new PrintLabelNodeViewModel { UseLastTestLabel = true };
        var context = new TestContext(new RegisterState());
        Assert.Equal(StepResult.False, await node.CreateStep(NullLogger.Instance, printer).ExecuteAsync(context, default));
        Assert.Contains("нет подготовленной этикетки", context.GetVariable<string>("PrintLabel.Error"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.CreateStep(NullLogger.Instance, printer)
            .ExecuteAsync(context, new CancellationToken(true)));
        Assert.Empty(printer.Jobs);
    }

    [Fact]
    public async Task OperatorButton_UsesFullTestResult_WithoutConnection_AndDoesNotReplaceIt()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            var print = new PrintLabelNodeViewModel { UseManualSerialNumber = true, ManualSerialNumber = "00123456", PrinterName = "" };
            vm.RootGraph.Nodes.Add(print); vm.RootGraph.Connections.Clear();
            vm.RootGraph.Connections.Add(new(vm.RootGraph.Nodes[0].Output[0], print.In));
            var results = new List<LastTestLabelSnapshot>(); vm.LastTestCompleted += results.Add;
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Equal("00123456", Assert.Single(results).Label!.Serial);
            Assert.Equal(ExecutionStatus.Failed, results[0].Result);
            var printer = new Printer(); vm.ActionLabelPrinter = printer;
            var action = ReprintAction(); vm.RootGraph.QuickActions.Add(action);
            vm.IsOperatorView = true; vm.IsTestRunning = true;
            var button = Assert.Single(vm.QuickActions);
            Assert.True(button.RunCommand.CanExecute(null));
            await button.RunCommand.ExecuteAsync(null);
            Assert.Equal("Выполнено", button.Status); Assert.Single(printer.Jobs);
            Assert.False(vm.IsConnected); Assert.Single(results);
            Assert.Contains("00123456", printer.Jobs[0]);
            // Restoration after restarting uses the same snapshot, even under another profile name.
            vm.RestoreLastTest(results[0]); vm.RootGraph.DeviceModel = "Другой профиль";
            await button.RunCommand.ExecuteAsync(null); Assert.Equal(printer.Jobs[0], printer.Jobs[1]);
            vm.IsTestRunning = false; vm.IsOperatorView = false;
            print.ManualSerialNumber = "";
            await vm.RunGraphCommand.ExecuteAsync(null);
            Assert.Equal(2, results.Count); Assert.Null(results[1].Label);
            await button.RunCommand.ExecuteAsync(null);
            Assert.Equal(2, printer.Jobs.Count); Assert.Contains("нет подготовленной этикетки", button.Status);
            return true;
        }, default);
    }

    [Fact]
    public async Task ActionEditor_ChangesDataSource_AndSavesIndependentCopy()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var original = ReprintAction();
            var editor = new ProfileActionEditorWindow(original, null);
            try
            {
                editor.Show(); Dispatcher.UIThread.RunJobs(); editor.UpdateLayout();
                var picker = editor.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "LabelDataSource");
                var node = Assert.IsType<PrintLabelNodeViewModel>(picker.DataContext);
                Assert.Equal(2, picker.SelectedIndex);
                picker.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
                Assert.True(node.IsManualSerialMode); Assert.False(node.UseLastTestLabel);
                picker.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
                Assert.True(node.IsVariableSerialMode); Assert.False(node.UseLastTestLabel);
                picker.SelectedIndex = 2; Dispatcher.UIThread.RunJobs();
                Assert.True(node.UseLastTestLabel); Assert.False(node.IsNewLabelMode);
                var copy = editor.BuildResult();
                var saved = copy.Graph.Nodes.OfType<PrintLabelNodeViewModel>().Single();
                Assert.True(saved.UseLastTestLabel);
                Assert.True(Assert.IsType<PrintLabelNodeViewModel>(saved.Clone()).UseLastTestLabel);
                Assert.NotSame(original.Graph.Nodes[2], saved);
                using var bus = new ModbusService();
                using var vm = new TestViewModel(bus, new SlaveManager(bus));
                ProfileActionTests.LoadEmpty(vm); vm.RootGraph.QuickActions.Add(copy);
                var json = GraphSerializer.Serialize(vm, "Копия профиля");
                Assert.Contains("Print Label (last test v1)", json);
                GraphSerializer.Deserialize(json, vm);
                Assert.True(vm.RootGraph.QuickActions.Single().Graph.Nodes.OfType<PrintLabelNodeViewModel>().Single().UseLastTestLabel);
            }
            finally { editor.Close(); }
            return true;
        }, default);
    }

    private static ProfileActionDefinition ReprintAction()
    {
        var action = new ProfileActionDefinition { Name = "Повторить этикетку", Color = "system" };
        var graph = action.Graph; var start = graph.Nodes[0]; var end = graph.Nodes[1];
        var print = new PrintLabelNodeViewModel { UseLastTestLabel = true, Copies = 1, PrinterName = "Replacement printer", Location = new Point(400, 80) };
        end.Location = new Point(1000, 80); graph.Nodes.Add(print); graph.Connections.Clear();
        graph.Connections.Add(new(start.Output[0], print.In)); graph.Connections.Add(new(print.TrueOut, end.Input[0]));
        return action;
    }

    private sealed class Printer : IRawLabelPrinter
    {
        public bool Fail; public string PrinterName = ""; public List<string> Jobs { get; } = new();
        public RawLabelPrintResult Print(string printerName, byte[] bytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); PrinterName = printerName; Jobs.Add(Encoding.GetEncoding(1251).GetString(bytes));
            return Fail ? RawLabelPrintResult.Fail(5, "Printer offline") : RawLabelPrintResult.Ok();
        }
    }
}
