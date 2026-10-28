using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.ViewModels;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileListMenuTests
{
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
    private static void Layout(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); Dispatcher.UIThread.RunJobs(); }
}
