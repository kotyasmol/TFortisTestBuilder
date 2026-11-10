using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.ProfileActions;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ReportTemplateEditorTests
{
    [Fact]
    public async Task Dialog_ProjectsDraftAndAppliesAsOneUndoableEdit()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            ProfileActionTests.LoadEmpty(vm);
            vm.IsOperatorView = false;
            var template = new ReportTemplate
            {
                Fields =
                [
                    new() { Source = ReportSourceKind.TestType },
                    new() { Source = ReportSourceKind.Variable, Key = "Measured", Label = "Замер" }
                ]
            };
            vm.RootGraph.ReportTemplate = template;
            using var graph = new GraphEditorView { DataContext = vm };
            var owner = new Window { Content = graph, Width = 1280, Height = 700 };
            try
            {
                owner.Show();
                Layout(owner);
                var toolbar = graph.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == "Состав отчёта");
                Assert.True(toolbar.IsEffectivelyVisible && toolbar.IsEffectivelyEnabled);

                var edit = vm.ConfigureReportTemplateAsync(owner);
                var dialog = Assert.IsType<ReportTemplateWindow>(owner.OwnedWindows.Single());
                Layout(dialog);
                var draft = Assert.IsType<ReportTemplateEditorViewModel>(dialog.DataContext);
                var fields = dialog.FindControl<ListBox>("FieldList")!;
                var rows = fields.GetVisualDescendants().OfType<CheckBox>().ToArray();
                Assert.Equal(2, rows.Length);
                Assert.False(rows.Single(row => Equals(row.DataContext, draft.Rows[0])).IsEffectivelyEnabled);
                var toggle = rows.Single(row => Equals(row.DataContext, draft.Rows[1]));
                toggle.IsChecked = false;
                Assert.Equal("Тип проверки", Assert.Single(draft.Preview).Name);
                Assert.Same(template, vm.RootGraph.ReportTemplate);
                Click(dialog, FindButton(dialog, "Отмена"));
                await edit;
                Assert.Same(template, vm.RootGraph.ReportTemplate);

                edit = vm.ConfigureReportTemplateAsync(owner);
                dialog = Assert.IsType<ReportTemplateWindow>(owner.OwnedWindows.Single());
                Layout(dialog);
                draft = Assert.IsType<ReportTemplateEditorViewModel>(dialog.DataContext);
                fields = dialog.FindControl<ListBox>("FieldList")!;
                fields.SelectedItem = draft.Rows[1];
                Layout(dialog);
                var name = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(input => AutomationProperties.GetName(input) == "Подпись в отчёте");
                name.Text = "Напряжение";
                var move = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == "Переместить выше");
                Click(dialog, move);
                Assert.Equal(new[] { "Напряжение", "Тип проверки" }, draft.Preview.Select(entry => entry.Name));
                Capture(dialog, "report-template-editor.png");
                Click(dialog, FindButton(dialog, "Применить к профилю"));
                await edit;
                Assert.Equal("Напряжение", vm.RootGraph.ReportTemplate!.Fields[0].Label);
                vm.UndoCommand.Execute(null);
                Assert.Same(template, vm.RootGraph.ReportTemplate);
            }
            finally { owner.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task VariablesTab_FiltersAndKeepsVariableChoicesAndTheirFormatting()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var keys = Enumerable.Range(0, 746).Select(index => $"Diagnostic.{index:D3}").Concat(
                ["DataTest.Port[1].Raw", "DataTest.Port[2].Raw", "Heater.Current"]).ToArray();
            var draft = new ReportTemplateEditorViewModel(null, source =>
                source == ReportSourceKind.Variable ? keys : [], initiallyIncludeKnownVariables: true);
            var owner = new Window { Width = 1280, Height = 700 };
            var dialog = new ReportTemplateWindow(draft);
            try
            {
                owner.Show();
                var edit = dialog.ShowDialog<ReportTemplateEditResult?>(owner);
                Layout(dialog);
                var useTemplate = dialog.GetVisualDescendants().OfType<CheckBox>()
                    .Single(box => Equals(box.Content, "Выбирать строки отчёта вручную"));
                Click(dialog, useTemplate);
                Assert.True(draft.UseTemplate);
                Assert.Equal("Переменные", Assert.IsType<TabItem>(dialog.FindControl<TabControl>("DetailsTabs")!.SelectedItem).Header);
                Assert.Equal(749, draft.VisibleVariables.Count);
                Assert.All(draft.VisibleVariables, variable => Assert.True(variable.IsIncluded));
                Click(dialog, FindButton(dialog, "Снять найденные"));
                Assert.All(draft.VisibleVariables, variable => Assert.False(variable.IsIncluded));

                var search = dialog.FindControl<TextBox>("VariableSearchBox")!;
                search.Text = "datatest.port[";
                Layout(dialog);
                Assert.Equal(new[] { "DataTest.Port[1].Raw", "DataTest.Port[2].Raw" },
                    draft.VisibleVariables.Select(variable => variable.Key));
                Click(dialog, FindButton(dialog, "Отметить найденные"));
                var retained = draft.Rows.Single(row => row.Key == "DataTest.Port[2].Raw");
                retained.Label = "Напряжение";
                retained.Format = ReportValueFormat.FixedSix;
                var position = retained.Position;

                Click(dialog, FindButton(dialog, "Снять найденные"));
                Assert.All(draft.VisibleVariables, choice => Assert.False(choice.IsIncluded));
                var checkbox = dialog.FindControl<ListBox>("VariableList")!.GetVisualDescendants().OfType<CheckBox>()
                    .Single(box => AutomationProperties.GetName(box) == "DataTest.Port[2].Raw");
                Click(dialog, checkbox);
                Assert.True(retained.Enabled);
                Assert.Equal(position, retained.Position);
                Assert.Equal("Напряжение", retained.Label);
                Assert.Equal(ReportValueFormat.FixedSix, retained.Format);

                search.Text = "heater";
                Layout(dialog);
                Click(dialog, FindButton(dialog, "Отметить найденные"));
                Assert.True(retained.Enabled);
                Assert.Equal("Отмечено 2 из 749", draft.VariableSummary);
                Capture(dialog, "report-variable-checklist.png");
                Click(dialog, FindButton(dialog, "Применить к профилю"));
                var result = Assert.IsType<ReportTemplateEditResult>(await edit).Template!;
                Assert.True(result.IncludeAutomaticEntries);
                Assert.Equal(new[] { "DataTest.Port[2].Raw", "Heater.Current" }, result.Fields
                    .Where(field => field.Source == ReportSourceKind.Variable && field.Enabled)
                    .Select(field => field.Key));
                Assert.Equal(ReportValueFormat.FixedSix, result.Fields.Single(field => field.Key == retained.Key).Format);
            }
            finally
            {
                dialog.Close();
                owner.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    private static Button FindButton(Window window, string title) => window.GetVisualDescendants()
        .OfType<Button>().Single(button => Equals(button.Content, title));

    private static void Click(Window window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Layout(window);
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(folder, name));
    }
}
