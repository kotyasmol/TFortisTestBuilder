using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileSaveViewTests
{
    [Fact]
    public async Task MouseSelectionOfVersionAtLeast_IsSavedAndUsedWithoutRestart()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "profile-version-edit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "firmware.json");
            File.WriteAllText(path, """
                {"name":"Firmware test","nodes":[
                  {"id":"0","type":"Start"},
                  {"id":"1","type":"Get Serial Number","useFixedSerialNumber":true,"fixedSerialNumber":1120},
                  {"id":"2","type":"Subtest","name":"Firmware","bodyGraph":{
                    "name":"Firmware","nodes":[
                      {"id":"0","type":"Start","x":0,"y":0},
                      {"id":"1","type":"Check Variable Equality","variableName":"SerialNumber","expectedValue":"1119","comparisonType":"Version","x":200,"y":0},
                      {"id":"2","type":"End","x":650,"y":0}],
                    "connections":[
                      {"sourceNodeId":"0","sourceConnector":"Выход","targetNodeId":"1","targetConnector":"In"},
                      {"sourceNodeId":"1","sourceConnector":"True","targetNodeId":"2","targetConnector":"Вход"}]}},
                  {"id":"3","type":"End"}],
                  "connections":[
                    {"sourceNodeId":"0","sourceConnector":"Выход","targetNodeId":"1","targetConnector":"In"},
                    {"sourceNodeId":"1","sourceConnector":"True","targetNodeId":"2","targetConnector":"In"},
                    {"sourceNodeId":"2","sourceConnector":"Success","targetNodeId":"3","targetConnector":"Вход"}]}
                """);
            var previousFolder = AppSettings.Instance.GraphsFolder;
            AppSettings.Instance.GraphsFolder = folder;
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1500, Height = 900, Content = view };
            try
            {
                await vm.SelectProfileAsync(GraphSerializer.ReadProfile(path));
                Assert.DoesNotContain("Ошибка", vm.StatusMessage);
                var body = vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single();
                var check = body.BodyGraph.Nodes.OfType<CheckVariableEqualityNodeViewModel>().Single();
                await vm.RunGraphCommand.ExecuteAsync(null);
                Assert.True(check.HasExecutionError);
                vm.OpenCompositeNodeBodyCommand.Execute(body);
                window.Show(); Layout(window);
                var selector = view.GetVisualDescendants().OfType<ComboBox>()
                    .Single(c => ReferenceEquals(c.DataContext, check) && c.Items.Contains(VariableComparisonType.VersionAtLeast));
                Click(selector);
                Assert.True(selector.IsDropDownOpen);
                var popup = selector.GetVisualDescendants().OfType<Popup>().Single();
                var option = popup.Child!.GetVisualDescendants().OfType<ComboBoxItem>()
                    .Single(c => Equals(c.DataContext, VariableComparisonType.VersionAtLeast));
                Click(option);
                Assert.Equal(VariableComparisonType.VersionAtLeast, check.ComparisonType);

                Click(view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveGraphCommand)));
                await vm.SaveGraphCommand.ExecutionTask!;
                Assert.Contains("\"comparisonType\": \"VersionAtLeast\"", File.ReadAllText(path));
                Assert.Same(body, vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single());
                await vm.RunGraphCommand.ExecuteAsync(null);
                Assert.True(check.HasExecutionSucceeded);
                Assert.True(vm.Station.IsSuccessful);

                await vm.SelectProfileAsync(vm.SelectedProfile, reload: true);
                await vm.RunGraphCommand.ExecuteAsync(null);
                Assert.True(vm.Station.IsSuccessful);
            }
            finally { window.Close(); AppSettings.Instance.GraphsFolder = previousFolder; Directory.Delete(folder, true); }
            return true;

            void Click(Control control)
            {
                var top = TopLevel.GetTopLevel(control)!;
                var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
                top.MouseDown(point, MouseButton.Left); top.MouseUp(point, MouseButton.Left); Layout(window);
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DateIsBelowNameOnRight_AndSaveClickCommitsNameAndCreatesCopy(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "profile-date-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "original.json");
            const string original = """{"name":"Проверка питания платы","deviceModel":"RTL-SW","updatedAt":"2025-01-02T03:04:05Z","nodes":[{"id":"0","type":"Delay","milliseconds":10}],"connections":[]}""";
            File.WriteAllText(path, original);
            var previousFolder = AppSettings.Instance.GraphsFolder;
            var previousTheme = Application.Current!.RequestedThemeVariant;
            AppSettings.Instance.GraphsFolder = folder;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var profiles = new ProfileListView { DataContext = vm };
            using var graph = new GraphEditorView { DataContext = vm };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("300,*"), Children = { profiles, graph } };
            Grid.SetColumn(graph, 1);
            var window = new Window { Width = 1250, Height = 800, Content = grid };
            try
            {
                vm.RefreshProfiles(); window.Show(); Layout(window);
                foreach (var group in vm.EngineerProfileGroups) group.IsExpanded = true;
                Layout(window);
                var profile = Assert.Single(vm.Profiles);
                var labels = profiles.GetVisualDescendants().OfType<TextBlock>().ToArray();
                var name = Assert.Single(labels.Where(x => x.Text == profile.DisplayName));
                var date = Assert.Single(labels.Where(x => x.Text == profile.UpdatedAtText));
                Assert.True(date.FontSize < name.FontSize);
                var namePoint = name.TranslatePoint(default, window)!.Value;
                var datePoint = date.TranslatePoint(default, window)!.Value;
                Assert.True(datePoint.Y >= namePoint.Y + name.Bounds.Height);
                Assert.True(Math.Abs(datePoint.X + date.Bounds.Width - (namePoint.X + name.Bounds.Width)) < 1);
                Click(date);
                Assert.Equal(profile.Name, vm.CurrentProfileName);

                var field = graph.GetVisualDescendants().OfType<AutoCompleteBox>().Single(x => AutomationProperties.GetName(x) == "Название профиля").GetVisualDescendants().OfType<TextBox>().Single();
                Assert.Equal(profile.Name, field.Text);
                field.Focus(); field.Text = "Проверка питания для завода 2";
                var save = graph.GetVisualDescendants().OfType<Button>().Single(x => ReferenceEquals(x.Command, vm.SaveGraphCommand));
                Click(save);
                if (vm.SaveGraphCommand.ExecutionTask != null) await vm.SaveGraphCommand.ExecutionTask;
                Layout(window);
                Assert.Equal("RTL-SW · Проверка питания для завода 2", vm.CurrentProfileName);
                Assert.Equal(2, vm.Profiles.Count);
                Assert.Equal(original, File.ReadAllText(path));
                Assert.Equal(vm.CurrentProfileName, vm.SelectedProfile!.Name);
                Assert.Contains(profiles.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == vm.SelectedProfile.UpdatedAtText);
                var artifacts = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
                if (!string.IsNullOrEmpty(artifacts))
                {
                    Directory.CreateDirectory(artifacts);
                    using var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(artifacts, dark ? "ProfileDates-dark.png" : "ProfileDates-light.png"));
                }
            }
            finally
            {
                window.Close(); AppSettings.Instance.GraphsFolder = previousFolder;
                Application.Current.RequestedThemeVariant = previousTheme; Directory.Delete(folder, true);
            }
            void Click(Control control)
            {
                var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
            }
            return true;
        }, CancellationToken.None);
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs();
    }
}
