using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileSaveViewTests
{
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
