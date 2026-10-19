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
public class DeviceProfileTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NewDeviceAndProfile_CanBeTypedSavedSuggestedAndReloaded(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "device-profile-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var oldFolder = AppSettings.Instance.GraphsFolder;
            var oldTheme = Application.Current!.RequestedThemeVariant;
            AppSettings.Instance.GraphsFolder = folder;
            Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            using var view = new GraphEditorView { DataContext = vm };
            var window = new Window { Width = 1000, Height = 750, Content = view };
            try
            {
                vm.NewProfileCommand.Execute(null); window.Show(); Layout();
                var model = Field("Устройство");
                var title = Field("Название профиля");
                var modelText = model.GetVisualDescendants().OfType<TextBox>().Single();
                var titleText = title.GetVisualDescendants().OfType<TextBox>().Single();
                modelText.Focus(); window.KeyTextInput("PSV2G4H+");
                titleText.Focus(); Layout();
                Assert.Equal("PSV2G4H+", vm.DeviceName);
                Assert.Equal("PSV2G4H+", vm.CurrentProfileName);
                titleText.SelectAll(); window.KeyTextInput("Проверка питания");
                SaveClick();
                await vm.SaveGraphCommand.ExecutionTask!; Layout();
                Assert.Equal("PSV2G4H+ · Проверка питания", vm.CurrentProfileName);
                Assert.Equal("Проверка питания", vm.RootGraph.ConfigurationName);
                Assert.Equal("PSV2G4H+", Assert.Single(vm.EngineerProfileGroups).Name);
                Assert.Single(vm.Profiles);
                var first = vm.SelectedProfile!;
                var bytes = File.ReadAllBytes(first.FilePath);
                Assert.Equal("PSV2G4H+", first.DeviceModel);
                Assert.Contains("PSV2G4H+", vm.DeviceNameSuggestions);
                Assert.Contains("Проверка питания", vm.ProfileNameSuggestions);
                titleText.Focus(); titleText.SelectAll(); window.KeyTextInput("Проверка"); Layout();
                Assert.True(title.IsDropDownOpen);
                // Suggestions are optional: free text must survive Save even while the popup is open.
                titleText.SelectAll(); window.KeyTextInput("Другой профиль");
                SaveClick(); await vm.SaveGraphCommand.ExecutionTask!; Layout();
                Assert.Equal(2, vm.Profiles.Count);
                Assert.Equal("PSV2G4H+ · Другой профиль", vm.CurrentProfileName);
                Assert.Equal("Другой профиль", vm.RootGraph.ConfigurationName);
                Assert.Equal(bytes, File.ReadAllBytes(first.FilePath));
                Assert.Equal(2, Assert.Single(vm.StationProfileGroups).Profiles.Count);
                vm.NewProfileCommand.Execute(null); Layout(); Assert.Empty(model.Text!);
                modelText.Focus(); window.KeyTextInput("psv"); Layout(); Assert.True(model.IsDropDownOpen);
                // Keyboard selection must commit the suggested device, not the typed prefix.
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
                titleText.Focus(); Layout(); Assert.Equal("PSV2G4H+", vm.DeviceName);
                using var reloaded = new TestViewModel(bus, new SlaveManager(bus));
                reloaded.SelectedProfile = reloaded.Profiles.First();
                Assert.Equal("PSV2G4H+", reloaded.DeviceName);
                Assert.Contains("PSV2G4H+", reloaded.DeviceNameSuggestions);
                var artifacts = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
                if (artifacts != null)
                {
                    Directory.CreateDirectory(artifacts);
                    using var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(artifacts, $"device-profile-{dark}.png"));
                }
            }
            finally
            {
                window.Close(); AppSettings.Instance.GraphsFolder = oldFolder;
                Application.Current.RequestedThemeVariant = oldTheme; Directory.Delete(folder, true);
            }
            return true;
            AutoCompleteBox Field(string name) => view.GetVisualDescendants().OfType<AutoCompleteBox>().Single(x => AutomationProperties.GetName(x) == name);
            void Layout() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
            void SaveClick()
            {
                var save = view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveGraphCommand));
                var point = save.TranslatePoint(new Point(save.Bounds.Width / 2, save.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public void ChangingKnownDeviceClearsOldId_AndUndoRestoresBothMetadataAndTitle()
    {
        var folder = Path.Combine(Path.GetTempPath(), "device-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "catalog"));
        File.WriteAllText(Path.Combine(folder, "catalog", "equipment-models.json"), """{"schemaVersion":1,"models":[{"id":"known","name":"PSW-2G6F+","aliases":["PSW 2G6F plus"]}]}""");
        var old = AppSettings.Instance.GraphsFolder;
        AppSettings.Instance.GraphsFolder = folder;
        try
        {
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            vm.NewProfileCommand.Execute(null);
            vm.DeviceName = "PSW 2G6F plus";
            Assert.Equal("known", vm.RootGraph.DeviceModelId);
            Assert.Equal("PSW-2G6F+", vm.DeviceName);
            Assert.Equal(vm.DeviceName, vm.CurrentProfileName);
            vm.DeviceName = "PSV2G4H+";
            Assert.Null(vm.RootGraph.DeviceModelId);
            vm.UndoCommand.Execute(null);
            Assert.Equal("known", vm.RootGraph.DeviceModelId);
            Assert.Equal("PSW-2G6F+", vm.DeviceName);
            vm.UndoCommand.Execute(null);
            Assert.Empty(vm.DeviceName); Assert.Equal("Новый профиль", vm.CurrentProfileName);
            vm.RedoCommand.Execute(null); Assert.Equal("known", vm.RootGraph.DeviceModelId);
        }
        finally { AppSettings.Instance.GraphsFolder = old; Directory.Delete(folder, true); }
    }
}
