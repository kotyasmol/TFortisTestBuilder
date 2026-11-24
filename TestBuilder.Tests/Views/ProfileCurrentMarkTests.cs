using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
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
public class ProfileCurrentMarkTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothMenusAllowSeveralLocalMarksWithoutChangingTheOpenGraph(bool dark)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            using var files = new ProfileCopyTests.ProfileFolder();
            var paths = Enumerable.Range(1, 3).Select(i => files.Write($"p{i}.json",
                ProfileCopyTests.SimpleJson.Replace("Проверка", "Вариант " + i).Replace("\"nodes\":",
                    $"\"updatedAt\":\"2026-11-{10 + i}T12:00:00Z\",\"nodes\":"))).ToArray();
            var snapshots = paths.Select(File.ReadAllBytes).ToArray();
            var times = paths.Select(File.GetLastWriteTimeUtc).ToArray();
            var store = new ProfileCurrentStore(Path.Combine(files.Path, "local", "current.json"));
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus), false, null, profileCurrentStore: store);
            var profiles = paths.Select(p => vm.Profiles.Single(i => i.FilePath == p)).ToArray();
            vm.SelectedProfile = profiles[2];
            var originalNode = Assert.IsType<DelayNodeViewModel>(Assert.Single(vm.Nodes));
            originalNode.Milliseconds = 42;
            var graphBefore = GraphSerializer.Serialize(vm, vm.CurrentProfileName);
            var engineer = new ProfileListView { DataContext = vm };
            var station = new StationDashboardView { DataContext = vm };
            var window = new Window { Width = 360, Height = 700, Content = engineer };
            try
            {
                window.Show(); vm.EngineerProfileGroups.Single().IsExpanded = true; Layout(window);
                var row = engineer.GetVisualDescendants().OfType<ListBoxItem>().Single(r => ReferenceEquals(r.DataContext, profiles[0]));
                RightClick(window, row);
                var item = Assert.Single(engineer.ProfileMenu!.Items.OfType<MenuItem>().Where(i => Equals(i.Header, "Актуальный")));
                Assert.False(item.IsChecked);
                Assert.Same(profiles[0], item.CommandParameter);
                Assert.True(item.Command!.CanExecute(item.CommandParameter));
                engineer.ProfileMenu.Close();
                await ((IAsyncRelayCommand)item.Command).ExecuteAsync(item.CommandParameter);
                await vm.ToggleProfileCurrentCommand.ExecuteAsync(profiles[1]);
                await vm.ToggleProfileCurrentCommand.ExecuteAsync(profiles[2]);
                vm.EngineerProfileGroups.Single().IsExpanded = false; Layout(window);
                Assert.Equal(3, vm.Profiles.Count(p => p.IsMarkedCurrent));
                Assert.Empty(vm.EngineerProfileGroups.Single().VisibleProfiles);
                Assert.Empty(Badges(engineer, "Актуальный"));
                Assert.Empty(Badges(engineer, "Новый"));
                Capture(window, $"profile-collapsed-engineer-{(dark ? "dark" : "light")}.png");
                vm.EngineerProfileGroups.Single().IsExpanded = true; Layout(window);
                Assert.Equal(3, vm.EngineerProfileGroups.Single().VisibleProfiles.Count());
                Assert.Equal(3, Badges(engineer, "Актуальный").Count());
                Assert.Single(Badges(engineer, "Новый"));
                CheckMetadataLayout(engineer, window);
                Capture(window, $"profile-current-engineer-{(dark ? "dark" : "light")}.png");

                vm.IsOperatorView = true; window.Width = 1250; window.Height = 850; window.Content = station; Layout(window);
                Assert.Empty(vm.StationProfileGroups.Single().VisibleProfiles);
                Assert.Empty(Badges(station, "Актуальный"));
                Capture(window, $"profile-collapsed-station-{(dark ? "dark" : "light")}.png");
                vm.StationProfileGroups.Single().IsExpanded = true; Layout(window);
                var button = station.GetVisualDescendants().OfType<Button>().First(b =>
                    b.DataContext is StationProfileOptionViewModel option && ReferenceEquals(option.Profile, profiles[0]));
                RightClick(window, button);
                var stationItem = Assert.Single(station.ProfileMenu!.Items.OfType<MenuItem>());
                Assert.True(stationItem.IsChecked);
                Assert.Same(profiles[0], stationItem.CommandParameter);
                station.ProfileMenu.Close();
                await ((IAsyncRelayCommand)stationItem.Command!).ExecuteAsync(stationItem.CommandParameter);
                Layout(window);
                Assert.False(profiles[0].IsMarkedCurrent);
                Assert.Equal(3, vm.StationProfileGroups.Single().VisibleProfiles.Count());
                Assert.Equal(2, Badges(station, "Актуальный").Count());
                Assert.Single(Badges(station, "Новый"));
                CheckMetadataLayout(station, window);
                Capture(window, $"profile-current-station-{(dark ? "dark" : "light")}.png");

                vm.RefreshProfiles();
                Assert.Equal(2, vm.Profiles.Count(p => p.IsMarkedCurrent));
                using var restartedBus = new ModbusService();
                using var restarted = new TestViewModel(restartedBus, new SlaveManager(restartedBus), false, null,
                    profileCurrentStore: new ProfileCurrentStore(Path.Combine(files.Path, "local", "current.json")));
                Assert.Equal(2, restarted.Profiles.Count(p => p.IsMarkedCurrent));
                using var otherBus = new ModbusService();
                using var otherPc = new TestViewModel(otherBus, new SlaveManager(otherBus), false, null,
                    profileCurrentStore: new ProfileCurrentStore(Path.Combine(files.Path, "another-pc", "current.json")));
                Assert.DoesNotContain(otherPc.Profiles, p => p.IsMarkedCurrent);
                Assert.Equal(graphBefore, GraphSerializer.Serialize(vm, vm.CurrentProfileName));
                Assert.Same(originalNode, Assert.Single(vm.Nodes));
                Assert.Same(profiles[2], vm.SelectedProfile);
                for (var i = 0; i < paths.Length; i++)
                {
                    Assert.Equal(snapshots[i], File.ReadAllBytes(paths[i]));
                    Assert.Equal(times[i], File.GetLastWriteTimeUtc(paths[i]));
                }
            }
            finally { engineer.ProfileMenu?.Close(); station.ProfileMenu?.Close(); window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenameAndAlignedSaveKeepTheMarkButCopyAndDeletionDoNot()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new ProfileCopyTests.ProfileFolder();
            var originalPath = files.Write("original.json", ProfileCopyTests.SimpleJson);
            var store = new ProfileCurrentStore(Path.Combine(files.Path, "local", "current.json"));
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus), true, null, profileCurrentStore: store);
            var original = Assert.Single(vm.Profiles); vm.SelectedProfile = original;
            await vm.ToggleProfileCurrentCommand.ExecuteAsync(original);
            vm.PromptProfileNameAsync = (_, copy, _, _) => Task.FromResult<string?>(copy ? "Копия" : "Переименованный");
            await vm.CopyProfileCommand.ExecuteAsync(original);
            var copy = vm.Profiles.Single(p => p.FilePath != originalPath);
            Assert.False(copy.IsMarkedCurrent);
            await vm.RenameProfileCommand.ExecuteAsync(original);
            var renamed = vm.Profiles.Single(p => p.IsMarkedCurrent);
            Assert.NotEqual(originalPath, renamed.FilePath);
            Assert.DoesNotContain(ProfileCurrentStore.Normalize(originalPath), store.Read());
            vm.SelectedProfile = renamed; vm.ProfileDescription = "Сохранённый вариант";
            await vm.SaveGraphToPathAsync(renamed.FilePath, alignFileName: true);
            var saved = vm.SelectedProfile!;
            Assert.True(saved.IsMarkedCurrent); Assert.NotEqual(renamed.FilePath, saved.FilePath);
            vm.ShowDeleteProfileDialogAsync = _ => Task.FromResult(true);
            await vm.DeleteProfileCommand.ExecuteAsync(saved);
            Assert.Empty(store.Read());
            Assert.False(Assert.Single(vm.Profiles).IsMarkedCurrent);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RenamePreservesAChoiceMadeByAnotherInstanceSinceTheLastRefresh()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new ProfileCopyTests.ProfileFolder();
            var path = files.Write("source.json", ProfileCopyTests.SimpleJson);
            var store = new ProfileCurrentStore(Path.Combine(files.Path, "local", "current.json"));
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus), true, null, profileCurrentStore: store);
            var source = Assert.Single(vm.Profiles);
            await new ProfileCurrentStore(Path.Combine(files.Path, "local", "current.json")).SetAsync(path, true);
            Assert.False(source.IsMarkedCurrent);
            vm.PromptProfileNameAsync = (_, _, _, _) => Task.FromResult<string?>("Новое имя");
            await vm.RenameProfileCommand.ExecuteAsync(source);
            Assert.True(Assert.Single(vm.Profiles).IsMarkedCurrent);
            Assert.DoesNotContain(ProfileCurrentStore.Normalize(path), store.Read());
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StorageFailureDoesNotShowASuccessfulMarkOrChangeTheProfile()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var files = new ProfileCopyTests.ProfileFolder();
            var source = files.Write("source.json", ProfileCopyTests.SimpleJson);
            var blocker = Path.Combine(files.Path, "blocked"); File.WriteAllText(blocker, "keep");
            var store = new ProfileCurrentStore(Path.Combine(blocker, "current.json"));
            using var bus = new ModbusService();
            using var vm = new TestViewModel(bus, new SlaveManager(bus), true, null, profileCurrentStore: store);
            var profile = Assert.Single(vm.Profiles);
            await vm.ToggleProfileCurrentCommand.ExecuteAsync(profile);
            Assert.False(profile.IsMarkedCurrent);
            Assert.Contains("Не удалось сохранить", vm.StatusMessage);
            Assert.Equal(ProfileCopyTests.SimpleJson, File.ReadAllText(source));
            Assert.Equal("keep", File.ReadAllText(blocker));
            return true;
        }, CancellationToken.None);
    }

    private static IEnumerable<TextBlock> Badges(Control view, string caption) => view.GetVisualDescendants().OfType<TextBlock>()
        .Where(t => t.Text == caption && t.IsEffectivelyVisible && t.GetVisualAncestors().OfType<Border>()
            .Any(b => b.Classes.Contains("profileLatestBadge")));

    private static void RightClick(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(20, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Layout(window);
    }

    private static void CheckMetadataLayout(Control view, Window window)
    {
        foreach (var grid in view.GetVisualDescendants().OfType<Grid>().Where(g => g.IsEffectivelyVisible &&
                     g.Children.OfType<WrapPanel>().Any(w => w.Children.OfType<Border>().Any(b => b.Classes.Contains("profileLatestBadge")))))
        {
            var date = grid.Children.OfType<TextBlock>().Single(t => Grid.GetColumn(t) == 1);
            var dateBounds = new Rect(date.TranslatePoint(default, window)!.Value, date.Bounds.Size);
            foreach (var badge in grid.GetVisualDescendants().OfType<Border>().Where(b => b.IsEffectivelyVisible && b.Classes.Contains("profileLatestBadge")))
                Assert.False(dateBounds.Intersects(new Rect(badge.TranslatePoint(default, window)!.Value, badge.Bounds.Size)));
        }
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        var folder = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder); using var image = window.CaptureRenderedFrame(); image?.Save(Path.Combine(folder, name));
    }
}
