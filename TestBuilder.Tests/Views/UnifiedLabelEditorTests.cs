using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class UnifiedLabelEditorTests
{
    [Theory]
    [InlineData(false, 1280, 800)]
    [InlineData(true, 1024, 768)]
    public async Task NodeOnlyChoosesSavedTemplatesAndOpeningItsPickerDoesNotEditTheProfile(bool light, int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            Application.Current!.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
            var saved = LabelTypeStore.UniversalTemplate(LabelTypeStore.DeviceTypeId); saved.Id = "custom:node-choice";
            LabelTypeStore.Save(LabelTypeStore.LocalPath(clean.Profiles), new LabelTypeFile { Types = new() { new(saved.Id, "Шаблон стенда", null, saved) } });
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            vm.Nodes.Clear(); var node = new PrintLabelNodeViewModel { LabelTemplate = LabelTemplateCatalog.Defaults()[0] }; vm.Nodes.Add(node);
            var before = GraphSerializer.Serialize(vm, "Test");
            var view = new GraphEditorView { DataContext = vm }; var window = new Window { Content = view, Width = width, Height = height };
            try
            {
                window.Show(); Layout(window);
                Assert.Equal(before, GraphSerializer.Serialize(vm, "Test"));
                Assert.Empty(window.GetVisualDescendants().OfType<LabelTemplateEditorView>());
                Assert.Empty(window.GetVisualDescendants().OfType<Button>().Where(b => b.Name == "EditProfileLabel"));
                var picker = Find<ComboBox>(window, "NodeLabelType"); picker.IsDropDownOpen = true; Layout(window); picker.IsDropDownOpen = false;
                Assert.Equal(before, GraphSerializer.Serialize(vm, "Test"));
                picker.SelectedItem = node.LabelTypeChoices.Single(t => t.Id == saved.Id); Layout(window);
                Assert.Equal(saved.Id, node.LabelTypeId); Assert.Equal(saved.Id, node.LabelTemplate!.Id);
                Assert.Contains("Print Label (type v1)", GraphSerializer.Serialize(vm, "Test"));
                vm.UndoCommand.Execute(null); Assert.Equal(before, GraphSerializer.Serialize(vm, "Test"));
                vm.RedoCommand.Execute(null); Assert.Equal(saved.Id, node.LabelTypeId);
                Assert.Empty(window.OwnedWindows); Evidence(window, light, "node-saved-template");
            }
            finally { window.Close(); view.Dispose(); Application.Current!.RequestedThemeVariant = ThemeVariant.Default; }
        }, default);
    }

    [Fact]
    public async Task InvalidDimensionsCannotBeAppliedAndTemplateSelectionRetainsEachDraft()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var source = LabelTemplateCatalog.Defaults()[0]; var original = JsonSerializer.Serialize(source);
            var view = new ProfileLabelEditorView(source, () => true); LabelTemplateDefinition? applied = null;
            view.Finished += value => applied = value;
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            try
            {
                window.Show(); Layout(window); var first = Find<LabelTemplateEditorView>(view, "UnifiedLabelEditor");
                first.Editor.TopMargin = .25m;
                var picker = Find<ComboBox>(view, "ProfileLabelTemplates"); picker.SelectedIndex = 2; Layout(window);
                var next = Find<LabelTemplateEditorView>(view, "UnifiedLabelEditor");
                Assert.False(ReferenceEquals(first.Editor, next.Editor), $"SelectedIndex={picker.SelectedIndex}; status={Find<TextBlock>(view, "ProfileLabelStatus").Text}");
                picker.SelectedIndex = 0; Layout(window);
                var active = Find<LabelTemplateEditorView>(view, "UnifiedLabelEditor"); Assert.Same(first.Editor, active.Editor); Assert.Equal(.254m, active.Editor.TopMargin);
                Find<NumericUpDown>(active, "LayoutX").Text = "ошибка"; Layout(window);
                Click(Find<Button>(view, "ApplyProfileLabel")); Assert.Null(applied); Assert.Contains("Исправьте", Find<TextBlock>(view, "ProfileLabelStatus").Text);
                Assert.Equal(original, JsonSerializer.Serialize(source)); Assert.False(File.Exists(LabelTypeStore.LocalPath(clean.Profiles)));
            }
            finally { window.Close(); }
        }, default);
    }

    [Fact]
    public async Task ReadyFieldsNeedNoTokensAndOnlyCustomContentShowsTheFormulaInput()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var source = LabelTemplateCatalog.Defaults()[0]; var before = JsonSerializer.Serialize(source);
            var editor = new LabelLayoutEditorViewModel(source); var view = new LabelTemplateEditorView(editor);
            var window = new Window { Content = view, Width = 1100, Height = 760 };
            try
            {
                window.Show(); Layout(window); var picker = Find<ComboBox>(view, "LabelContentSource"); var text = Find<TextBox>(view, "LabelElementValue");
                Assert.False(text.IsEffectivelyVisible);
                picker.SelectedItem = LabelLayoutEditorViewModel.ContentFields.Single(f => f.Key == "Mac"); Layout(window);
                Assert.Equal("MAC: {{Mac}}", editor.Value); Assert.False(text.IsEffectivelyVisible);
                picker.SelectedItem = LabelLayoutEditorViewModel.ContentFields.Single(f => f.Key == "Custom"); Layout(window);
                Assert.True(text.IsEffectivelyVisible); text.Text = "Адрес: {{Mac}}"; Layout(window);
                Assert.Contains(editor.Preview!.Elements, e => e.Text.StartsWith("Адрес:"));
                picker.SelectedItem = LabelLayoutEditorViewModel.ContentFields.Single(f => f.Key == "Name"); Layout(window);
                Assert.Equal("{{Name}}", editor.Value); Assert.False(text.IsEffectivelyVisible);
                editor.Add("Box"); Layout(window); Assert.False(picker.IsEffectivelyVisible); Assert.False(text.IsEffectivelyVisible);
                Assert.Equal(before, JsonSerializer.Serialize(source));
            }
            finally { window.Close(); }
        }, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileEditorCanOpenTheSharedTypesPageWithoutApplyingItsDraftOrOpeningAWindow(bool assigned)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
            var model = EquipmentCatalog.LoadBundled().Models.Single(m => m.LabelCode == 32);
            var expectedId = LabelTypeStore.DeviceTypeId;
            if (assigned)
            {
                expectedId = "custom:assigned-ui";
                var template = LabelTypeStore.UniversalTemplate(LabelTypeStore.DeviceTypeId); template.Id = expectedId;
                LabelTypeStore.Save(LabelTypeStore.LocalPath(clean.Profiles), new LabelTypeFile {
                    Types = new() { new(expectedId, "Шаблон стенда", null, template, LabelTypeStore.DeviceTypeId) },
                    Assignments = new() { new(model.Id, expectedId) } });
            }
            var window = new MainWindow { Width = 1440, Height = 960, RequestedThemeVariant = ThemeVariant.Dark }; var shell = (MainWindowViewModel)window.DataContext!;
            shell.SettingsVM.IsEngineerMode = true; shell.SelectedPage = "Editor"; shell.TestVM.IsOperatorView = false;
            shell.TestVM.SelectedEquipmentModel = shell.TestVM.EquipmentModels.Single(m => m.Id == model.Id);
            shell.LabelPrintingVM.Types.EnsureLoaded(); shell.LabelPrintingVM.Types.SelectedSection = 1; shell.LabelPrintingVM.Types.Search = "Не найдено";
            shell.TestVM.Nodes.Clear(); var node = new PrintLabelNodeViewModel { LabelTemplate = LabelTemplateCatalog.Defaults()[0] }; shell.TestVM.Nodes.Add(node);
            var before = JsonSerializer.Serialize(node.LabelTemplate);
            try
            {
                window.Show(); Layout(window);
                var graph = window.GetVisualDescendants().OfType<GraphEditorView>().Single(v => v.DataContext == shell.TestVM);
                Click(Find<Button>(graph, "OpenSharedLabelTypes")); Layout(window);
                Assert.Equal("Labels", shell.SelectedPage); Assert.Equal(1, shell.LabelPrintingVM.SelectedSection);
                Assert.Equal(0, shell.LabelPrintingVM.Types.SelectedSection);
                Assert.Equal(expectedId, shell.LabelPrintingVM.Types.Selected!.Definition.Id);
                Assert.Equal(before, JsonSerializer.Serialize(node.LabelTemplate));
                var shared = window.GetVisualDescendants().OfType<LabelTypesView>().Single();
                Assert.True(shared.IsEffectivelyVisible); Assert.True(Find<LabelTemplateEditorView>(shared, "UnifiedLabelEditor").IsEffectivelyVisible);
                Assert.Empty(window.OwnedWindows); Evidence(window, false, assigned ? "shared-assigned-type" : "shared-main-window");
            }
            finally { window.Close(); Application.Current!.RequestedThemeVariant = ThemeVariant.Default; }
        }, default);
    }

    [Fact]
    public async Task AnExistingGraphDialogGetsNoAdditionalLabelWindow()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            vm.Nodes.Clear(); var node = new PrintLabelNodeViewModel { LabelTemplate = LabelTemplateCatalog.Defaults()[0] }; vm.Nodes.Add(node);
            var view = new GraphEditorView { DataContext = vm }; var owner = new Window(); var dialog = new Window { Content = view, Width = 1200, Height = 800 };
            try
            {
                owner.Show(); var task = dialog.ShowDialog(owner); Layout(dialog);
                var picker = Find<ComboBox>(dialog, "NodeLabelType"); picker.IsDropDownOpen = true; Layout(dialog); picker.IsDropDownOpen = false;
                Assert.Same(dialog, Assert.Single(owner.OwnedWindows)); Assert.Empty(dialog.OwnedWindows);
                Assert.Empty(dialog.GetVisualDescendants().OfType<LabelTemplateEditorView>());
                Assert.True(dialog.IsVisible); Assert.False(task.IsCompleted); Assert.Empty(dialog.OwnedWindows);
            }
            finally { dialog.Close(); owner.Close(); view.Dispose(); }
        }, default);
    }

    [Fact]
    public async Task UnsupportedCommandsStayEditableOnTheSamePageAndTheSourceIsPreserved()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var source = LabelTemplateCatalog.Defaults()[0]; source.Command = "^XA^FO20,20^GFA,1,1,1,FF^FS^XZ";
            var original = JsonSerializer.Serialize(source); LabelTemplateDefinition? result = null;
            var view = new ProfileLabelEditorView(source, () => true); view.Finished += value => result = value;
            var window = new Window { Content = view, Width = 1100, Height = 760 };
            try
            {
                window.Show(); Layout(window);
                var command = Find<TextBox>(window, "ProfileLabelRawCommand"); Assert.Equal(source.Command, command.Text);
                command.Text = "^XA^FO25,20^GFA,1,1,1,FF^FS^XZ"; Layout(window);
                Click(Find<Button>(window, "ApplyProfileLabel")); Assert.NotNull(result); Assert.Equal(command.Text, result.Command);
                Assert.Null(result.Layout); Assert.Equal(original, JsonSerializer.Serialize(source)); Assert.Empty(window.OwnedWindows);
                Assert.False(File.Exists(LabelTypeStore.LocalPath(clean.Profiles)));
            }
            finally { window.Close(); }
        }, default);
    }

    [Fact]
    public async Task LegacyTemplateDialogEmbedsTheSameEditorInsteadOfOpeningAChildWindow()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation(); var window = new LabelTemplatesWindow();
            try
            {
                window.Show(); Layout(window); Click(Find<Button>(window, "EditLabelLayout")); Layout(window);
                Assert.Empty(window.OwnedWindows); Assert.Single(window.GetVisualDescendants().OfType<LabelTemplateEditorView>());
                Click(Find<Button>(window, "BackToLabelTemplates")); Layout(window);
                Assert.Empty(window.GetVisualDescendants().OfType<LabelTemplateEditorView>()); Assert.Empty(window.OwnedWindows);
            }
            finally { window.Close(); }
        }, default);
    }

    private static T Find<T>(Control root, string? name = null) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => name == null || c.Name == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Layout(Window window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs(); }
    private static void Evidence(Window window, bool light, string name)
    {
        if (Environment.GetEnvironmentVariable("TFORTIS_UNIFIED_LABEL_EVIDENCE") is not { } folder) return;
        Directory.CreateDirectory(folder); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, name + (light ? "-light.png" : "-dark.png")));
    }
}
