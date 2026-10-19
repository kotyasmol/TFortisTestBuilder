using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class RussianInterfaceTests
{
    [Fact]
    public async Task PaletteRendersRussianNames_AndAddingFromPaletteKeepsLegacyIdentifiers()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var palette = new NodePaletteView { DataContext = vm };
            var window = new Window { Width = 330, Height = 850, Content = palette };
            try
            {
                window.Show(); Layout(window);
                var labels = palette.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
                foreach (var node in vm.AvailableNodes)
                {
                    Assert.Matches("[А-Яа-яЁё]", node.DisplayTitle);
                    Assert.Contains(node.DisplayTitle, labels);
                    if (node.Title != node.DisplayTitle) Assert.DoesNotContain(node.Title, labels);
                }
                var parse = vm.AvailableNodes.OfType<ParseResponseNodeViewModel>().Single();
                Assert.Equal("Parse Response", parse.Title);
                Assert.Equal("Parse Response", parse.Clone().Title);
                SaveFrame(window, "RussianPalette.png");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RussianSelectorsEditUnderlyingValues_AndRoundTripKeepsTypesAndConnections()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var parse = new ParseResponseNodeViewModel();
            var equality = new CheckVariableEqualityNodeViewModel();
            var led = new CheckLedColorNodeViewModel();
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1250, Height = 850, Content = view };
            try
            {
                window.Show();
                foreach (var node in new NodeViewModel[] { parse, equality, led })
                {
                    vm.Nodes.Clear(); vm.Nodes.Add(node); Layout(window);
                    var selectors = view.GetVisualDescendants().OfType<ComboBox>().Where(x => ReferenceEquals(x.DataContext, node)).ToArray();
                    void Select(object raw, string expectedLabel)
                    {
                        var selector = Assert.Single(selectors.Where(x => x.Items.Contains(raw)));
                        selector.SelectedItem = raw; Layout(window);
                        Assert.Contains(expectedLabel, selector.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text));
                    }
                    switch (node)
                    {
                        case ParseResponseNodeViewModel:
                            Select("Regex", "Регулярное выражение"); Select("Number", "Число");
                            Assert.Equal("Regex", parse.ParseMode); Assert.Equal("Number", parse.ValueType);
                            SaveFrame(window, "RussianGraphEditor.png");
                            break;
                        case CheckVariableEqualityNodeViewModel:
                            Select(VariableComparisonType.VersionAtLeast, "Версия: не ниже заданной");
                            Assert.Equal(VariableComparisonType.VersionAtLeast, equality.ComparisonType);
                            break;
                        case CheckLedColorNodeViewModel:
                            Select("green", "Зелёный"); Assert.Equal("green", led.ExpectedColor);
                            break;
                    }
                }
                vm.Nodes.Clear(); vm.Nodes.Add(parse); vm.Nodes.Add(equality); vm.Nodes.Add(led);
                vm.Connections.Add(new ConnectionViewModel(parse.TrueOut, equality.In));
                vm.Connections.Add(new ConnectionViewModel(equality.TrueOut, led.In));
                Assert.Equal("Да", parse.TrueOut.DisplayTitle);
                var saved = GraphSerializer.Serialize(vm, "Проверка русского интерфейса");
                Assert.Contains("Parse Response", saved);
                GraphSerializer.Deserialize(saved, vm);
                Assert.Equal("Regex", vm.Nodes.OfType<ParseResponseNodeViewModel>().Single().ParseMode);
                Assert.Equal("Number", vm.Nodes.OfType<ParseResponseNodeViewModel>().Single().ValueType);
                Assert.Equal(VariableComparisonType.VersionAtLeast, vm.Nodes.OfType<CheckVariableEqualityNodeViewModel>().Single().ComparisonType);
                Assert.Equal("green", vm.Nodes.OfType<CheckLedColorNodeViewModel>().Single().ExpectedColor);
                Assert.Equal(2, vm.Connections.Count);
                Assert.All(vm.Connections, c => { Assert.Equal("True", c.Source.Title); Assert.Equal("In", c.Target.Title); });
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static void Layout(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs();
    }

    private static void SaveFrame(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, name));
    }
}
