using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileListMenuTests
{
    [Fact]
    public async Task NewestProfilePerModel_IsVisibleInBothMenus_AndUpdatesOnlyItsGroup()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var files = new ProfileCopyTests.ProfileFolder();
            var modelAPath = files.Write("model-a.json", WithDate(ProfileCopyTests.SimpleJson, "2025-01-02"));
            var old = ProfileCopyTests.SimpleJson.Replace("Модель А", "Модель Б").Replace("Проверка", "Архив");
            var oldPath = files.Write("archive.json", WithDate(old, "2025-01-01"));
            var newestPath = files.Write("current.json", WithDate(old.Replace("Архив", "Текущая"), "2025-01-03"));
            var legacyPath = files.Write("legacy.json", old.Replace("Архив", "Резерв"));
            File.SetLastWriteTimeUtc(legacyPath, new DateTime(2025, 1, 2, 12, 0, 0, DateTimeKind.Utc));
            files.Write("unassigned.json", ProfileCopyTests.SimpleJson.Replace("\"deviceModel\":\"Модель А\",", ""));
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var latest = vm.Profiles.Single(p => p.FilePath == newestPath);
            var modelA = vm.Profiles.Single(p => p.FilePath == modelAPath);
            Assert.Equal(2, vm.Profiles.Count(p => p.IsLatest));
            var engineer = new ProfileListView { DataContext = vm };
            var station = new StationDashboardView { DataContext = vm };
            var window = new Window { Width = 1250, Height = 620, Content = engineer };
            try
            {
                window.Show(); Layout(window);
                Assert.Equal(modelA.ModelGroupKey, vm.EngineerProfileGroups.First().Key);
                var engineerGroup = vm.EngineerProfileGroups.Single(g => g.Key == latest.ModelGroupKey);
                Assert.False(engineerGroup.IsExpanded);
                Assert.Same(latest, Assert.Single(engineerGroup.VisibleProfiles));
                Assert.Equal(2, Badges(engineer).Count());
                var row = engineer.GetVisualDescendants().OfType<ListBoxItem>()
                    .Single(item => ReferenceEquals(item.DataContext, latest));
                Click(window, row);
                Assert.Same(latest, vm.SelectedProfile);
                engineerGroup.ToggleExpandedCommand.Execute(null);
                Layout(window);
                Assert.Equal(new[] { newestPath, oldPath, legacyPath }, engineer.GetVisualDescendants().OfType<ListBoxItem>()
                    .Select(item => (GraphProfile)item.DataContext!)
                    .Where(profile => profile.ModelGroupKey == latest.ModelGroupKey)
                    .Select(profile => profile.FilePath));

                vm.IsOperatorView = true;
                window.Content = station;
                Layout(window);
                Assert.Equal(modelA.ModelGroupKey, vm.StationProfileGroups.First().Key);
                var stationGroup = vm.StationProfileGroups.Single(g => g.Key == latest.ModelGroupKey);
                Assert.False(stationGroup.IsExpanded);
                Assert.Same(latest, Assert.Single(stationGroup.VisibleProfiles).Profile);
                Assert.Equal(2, Badges(station).Count());
                vm.StationProfileSearch = "Архив";
                Layout(window);
                Assert.Empty(Badges(station));
                Assert.Same(latest, vm.SelectedProfile);
                vm.StationProfileSearch = "";

                File.SetLastWriteTimeUtc(legacyPath, new DateTime(2025, 1, 4, 12, 0, 0, DateTimeKind.Utc));
                vm.RefreshProfiles();
                Layout(window);
                Assert.False(latest.IsLatest);
                Assert.True(modelA.IsLatest);
                Assert.Equal(legacyPath, engineerGroup.Profiles.First().FilePath);
                Assert.Equal(legacyPath, stationGroup.Profiles.First().Profile.FilePath);
                Assert.Equal(2, Badges(station).Count());
                var badge = Badges(station).Single(b => ((StationProfileOptionViewModel)b.DataContext!).Profile.FilePath == legacyPath);
                Assert.Same(latest, vm.SelectedProfile);
                var button = badge.GetVisualAncestors().OfType<Button>().First();
                Click(window, button);
                Assert.Equal(legacyPath, vm.SelectedProfile!.FilePath);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);

        static string WithDate(string json, string date) => json.Replace("\"nodes\":", $"\"updatedAt\":\"{date}T12:00:00Z\",\"nodes\":");
        static IEnumerable<TextBlock> Badges(Control view) => view.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Text == "Актуальный" && t.IsEffectivelyVisible);
    }

    [Fact]
    public async Task RightClickTargetsUnselectedProfile_CopyDialogValidatesAndCreatesEditableCopy()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new ProfileCopyTests.ProfileFolder();
            var sourcePath = files.Write("source.json", ProfileCopyTests.SimpleJson);
            var otherPath = files.Write("other.json", ProfileCopyTests.SimpleJson.Replace("Проверка", "Другая"));
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var source = vm.Profiles.Single(p => p.FilePath == sourcePath);
            vm.SelectedProfile = vm.Profiles.Single(p => p.FilePath == otherPath);
            var selectedNode = vm.Nodes.Single();
            var view = new ProfileListView { DataContext = vm };
            var window = new Window { Width = 340, Height = 620, Content = view };
            ProfileNameDialog? dialog = null;
            vm.PromptProfileNameAsync = (profile, copy, name, validate) =>
            {
                dialog = new ProfileNameDialog(profile, copy, name, validate);
                return dialog.ShowDialog<string?>(window);
            };
            try
            {
                window.Show(); foreach (var group in vm.EngineerProfileGroups) group.IsExpanded = true; Layout(window);
                var item = view.GetVisualDescendants().OfType<ListBoxItem>().Single(x => ReferenceEquals(x.DataContext, source));
                var point = item.TranslatePoint(new Point(30, item.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Layout(window);
                Assert.True(view.ProfileMenu!.IsOpen);
                Assert.Equal(otherPath, vm.SelectedProfile!.FilePath);
                Assert.Same(selectedNode, vm.Nodes.Single());
                var menuItem = view.ProfileMenu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "Копировать профиль"));
                Assert.Same(source, menuItem.CommandParameter);
                Assert.Same(vm.CopyProfileCommand, menuItem.Command);
                view.ProfileMenu.Close();
                var pending = ((IAsyncRelayCommand)menuItem.Command!).ExecuteAsync(menuItem.CommandParameter);
                var shownDialog = Assert.IsType<ProfileNameDialog>(dialog);
                Layout(shownDialog);
                var input = shownDialog.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.Equal("Проверка — копия", input.Text);
                var create = shownDialog.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Создать копию"));
                input.Text = " "; Click(shownDialog, create);
                Assert.True(shownDialog.IsVisible); Assert.Equal(2, Directory.GetFiles(files.Path, "*.json").Length);
                input.Text = "Проверка"; Click(shownDialog, create);
                Assert.True(shownDialog.IsVisible);
                input.Text = "Проверка — копия";
                Layout(shownDialog);
                var folder = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
                if (!string.IsNullOrWhiteSpace(folder)) { Directory.CreateDirectory(folder); using var frame = shownDialog.CaptureRenderedFrame(); frame?.Save(Path.Combine(folder, "copy-profile-shownDialog.png")); }
                Click(shownDialog, create); await pending; Layout(window);
                Assert.Equal("Модель А · Проверка — копия", vm.SelectedProfile!.DisplayName);
                Assert.Equal("Проверка — копия", vm.ProfileDescription);
                Assert.NotSame(selectedNode, vm.Nodes.Single());
                Assert.Equal(ProfileCopyTests.SimpleJson, File.ReadAllText(sourcePath));
                Assert.Contains(view.GetVisualDescendants().OfType<ListBoxItem>(), x => ReferenceEquals(x.DataContext, vm.SelectedProfile) && x.IsSelected);
                Assert.False(bus.IsConnected);
            }
            finally { view.ProfileMenu?.Close(); dialog?.Close(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout(window);
    }

    [Fact]
    public async Task RightClickDeleteCanBeCancelled_AndMovesOnlyTheConfirmedProfile()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new ProfileCopyTests.ProfileFolder();
            var sourcePath = files.Write("source.json", ProfileCopyTests.SimpleJson);
            var otherPath = files.Write("other.json", ProfileCopyTests.SimpleJson.Replace("Проверка", "Другая"));
            var originalOther = File.ReadAllBytes(otherPath);
            File.SetLastWriteTimeUtc(sourcePath, new DateTime(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(otherPath, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus));
            var source = vm.Profiles.Single(p => p.FilePath == sourcePath);
            var other = vm.Profiles.Single(p => p.FilePath == otherPath);
            vm.SelectedProfile = other;
            var currentNode = Assert.IsType<DelayNodeViewModel>(Assert.Single(vm.Nodes));
            currentNode.Milliseconds = 99;
            var view = new ProfileListView { DataContext = vm };
            var window = new Window { Width = 340, Height = 620, Content = view };
            ReloadProfileDialog? dialog = null;
            vm.ShowDeleteProfileDialogAsync = create => (dialog = create()).ShowDialog<bool>(window);
            try
            {
                window.Show();
                Assert.True(source.IsLatest);
                Assert.False(other.IsLatest);
                var pending = OpenDeleteMenu(source);
                ClickButton("Отмена");
                await pending;
                Assert.True(File.Exists(sourcePath));
                Assert.False(Directory.Exists(Path.Combine(files.Path, "deleted")));

                pending = OpenDeleteMenu(source);
                ClickButton("Удалить");
                await pending;
                Assert.False(File.Exists(sourcePath));
                Assert.Equal(ProfileCopyTests.SimpleJson, File.ReadAllText(ProfileFileStorage.DeletedPath(sourcePath)));
                Assert.Same(other, Assert.Single(vm.Profiles));
                Assert.True(other.IsLatest);
                Assert.Same(other, vm.SelectedProfile);
                Assert.Same(currentNode, Assert.Single(vm.Nodes));
                Assert.Equal(99, currentNode.Milliseconds);

                vm.IsConnected = true;
                Assert.False(vm.DeleteProfileCommand.CanExecute(other));
                vm.IsConnected = false;
                pending = OpenDeleteMenu(other);
                Assert.Contains(dialog!.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("Несохранённые изменения") == true);
                ClickButton("Удалить");
                await pending;
                Assert.Null(vm.SelectedProfile);
                Assert.Empty(vm.Nodes);
                Assert.Empty(vm.Profiles);
                Assert.Equal("Новый профиль", vm.CurrentProfileName);
                Assert.False(vm.CanReloadProfile);
                Assert.False(vm.UndoCommand.CanExecute(null));
                Assert.Equal(originalOther, File.ReadAllBytes(ProfileFileStorage.DeletedPath(otherPath)));
            }
            finally { view.ProfileMenu?.Close(); dialog?.Close(); window.Close(); }
            return true;

            Task OpenDeleteMenu(GraphProfile profile)
            {
                Layout(window);
                var item = view.GetVisualDescendants().OfType<ListBoxItem>().Single(x => ReferenceEquals(x.DataContext, profile));
                var point = item.TranslatePoint(new Point(30, item.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Layout(window);
                var menu = Assert.IsType<ContextMenu>(view.ProfileMenu);
                Assert.True(menu.IsOpen);
                Assert.Same(other, vm.SelectedProfile);
                var action = menu.Items.OfType<MenuItem>().Single(x => Equals(x.Header, "Удалить профиль"));
                Assert.Same(profile, action.CommandParameter);
                menu.Close();
                var task = ((IAsyncRelayCommand)action.Command!).ExecuteAsync(profile);
                Layout(dialog!);
                Assert.Contains(dialog!.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains(profile.DisplayName) == true);
                return task;
            }

            void ClickButton(string name)
            {
                var shown = Assert.IsType<ReloadProfileDialog>(dialog);
                Click(shown, shown.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, name)));
            }
        }, CancellationToken.None);
    }

    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
}
