using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Models;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Tests.Views;

namespace TestBuilder.Tests.ProfileActions;

[Collection("Graph editor UI")]
public class ProfileActionStateViewTests
{
    [Theory]
    [InlineData(940)]
    [InlineData(1200)]
    public async Task Editor_RoundTripsRules_AndKeepsDraftDetached(int width)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var original = new ProfileActionDefinition { Display = ProfileActionDisplay.Toggle, StateBinding = ProfileActionStateTests.Binding() };
            var window = new ProfileActionEditorWindow(original, ProfileActionStateTests.Hardware()) { Width = width };
            try
            {
                window.Show(); window.FindControl<TabControl>("GraphTabs")!.SelectedIndex = 2; Layout(window);
                var panel = window.FindControl<ProfileActionStateEditorView>("StateEditor")!;
                var vm = Assert.IsType<ProfileActionStateEditorViewModel>(panel.DataContext);
                Assert.True(vm.UseModbus);
                vm.OnText = "Готово"; vm.OffText = "Ожидание";
                var row = vm.Rules[0].Conditions[0];
                var inputs = panel.GetVisualDescendants().OfType<NumericUpDown>().Where(c => ReferenceEquals(c.DataContext, row)).ToArray();
                Assert.Equal(2, inputs.Length); Assert.Null(inputs[1].Value);
                inputs[1].Value = 8; inputs[0].Value = 8;
                vm.Rules[1].Conditions[0].Mask = 8;
                vm.Rules[0].AddConditionCommand.Execute(null);
                var extra = vm.Rules[0].Conditions[1];
                extra.SelectedDevice = extra.Devices.Single(); extra.SelectedRegister = extra.Registers.Last();
                extra.Value = 20; extra.SelectedComparison = extra.Comparisons.Single(c => c.Value == RegisterComparison.Greater);
                vm.Rules[0].MatchIndex = 1;
                Layout(window);
                var saved = window.BuildResult();
                Assert.Equal("Готово", saved.StateBinding!.OnText);
                Assert.Equal(8, saved.StateBinding.On.Conditions[0].Mask);
                Assert.Equal(2, saved.StateBinding.On.Conditions.Count);
                Assert.Equal(StateRuleMatch.Any, saved.StateBinding.On.Match);
                Assert.Null(original.StateBinding!.On.Conditions[0].Mask);
                Assert.Single(original.StateBinding.On.Conditions);
                foreach (var control in panel.GetVisualDescendants().OfType<NumericUpDown>())
                {
                    Assert.True(control.Bounds.Width >= 90);
                    var position = control.TranslatePoint(default, panel)!.Value;
                    Assert.True(position.X >= 0 && position.X + control.Bounds.Width <= panel.Bounds.Width);
                }
                var path = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
                if (path != null) { Directory.CreateDirectory(path); using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(path, $"action-state-{width}.png")); }
                extra.RemoveCommand.Execute(null); Assert.Single(vm.Rules[0].Conditions);
                vm.UseModbus = false; Assert.Null(window.BuildResult().StateBinding);
                vm.UseModbus = true; Assert.NotNull(window.BuildResult().StateBinding);
            }
            finally { window.Close(); }
        }, default);
    }
    [Fact]
    public async Task TextTab_EditsAndHidesLabelsWithoutChangingOriginal_AndReopensSavedValues()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            var original = new ProfileActionDefinition { Name = "Повторить этикетку" };
            ProfileActionDefinition saved;
            var editor = new ProfileActionEditorWindow(original, null) { Width = 940 };
            try
            {
                editor.Show(); editor.FindControl<TabControl>("GraphTabs")!.SelectedIndex = 3; Layout(editor);
                var panel = editor.FindControl<StackPanel>("LabelsEditor")!;
                var labels = Assert.IsType<ProfileActionLabels>(panel.DataContext);
                var fields = panel.GetVisualDescendants().OfType<TextBox>().ToArray();
                Assert.Equal(8, fields.Length);
                fields[0].Text = ""; fields[4].Text = "Печать…"; fields[5].Text = "Отправлено принтеру";
                panel.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = false;
                Layout(editor); saved = editor.BuildResult();
                Assert.Null(original.Labels);
                Assert.False(saved.Labels!.ShowStatus); Assert.Equal("", saved.Labels.Ready);
                Assert.Equal("Печать…", saved.Labels.Running); Assert.Equal("Отправлено принтеру", saved.Labels.Success);
                var path = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
                if (path != null) { Directory.CreateDirectory(path); using var frame = editor.CaptureRenderedFrame(); frame?.Save(Path.Combine(path, "action-labels.png")); }
            }
            finally { editor.Close(); }
            var reopened = new ProfileActionEditorWindow(saved, null);
            try
            {
                reopened.Show(); reopened.FindControl<TabControl>("GraphTabs")!.SelectedIndex = 3; Layout(reopened);
                var labels = Assert.IsType<ProfileActionLabels>(reopened.FindControl<StackPanel>("LabelsEditor")!.DataContext);
                Assert.Equal("Отправлено принтеру", labels.Success); Assert.False(labels.ShowStatus);
            }
            finally { reopened.Close(); }
        }, default);
    }

    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
}
