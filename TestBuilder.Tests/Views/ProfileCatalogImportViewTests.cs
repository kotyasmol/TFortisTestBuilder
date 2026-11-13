using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.Views;
using TestBuilder.Views.TestViews;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class ProfileCatalogImportViewTests
{
    [Fact]
    public async Task ExportAndDependencyCommandsAreAccessibleFromTheProfileMenus()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var files = new ProfileCopyTests.ProfileFolder();
            files.Write("profile.json", ProfileCatalogTransferTests.Json());
            using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false };
            var profile = vm.Profiles.Single(); vm.SelectedProfile = profile;
            var list = new ProfileListView { DataContext = vm, Width = 320 };
            using var editor = new GraphEditorView { DataContext = vm };
            var grid = new Grid { ColumnDefinitions = new("320,*"), Children = { list, editor } }; Grid.SetColumn(editor, 1);
            var window = new Window { Width = 1440, Height = 900, Content = grid };
            void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
            try
            {
                window.Show(); foreach (var group in vm.EngineerProfileGroups) group.IsExpanded = true; Layout();
                var item = list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => ReferenceEquals(i.DataContext, profile));
                var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right); Layout();
                var export = list.ProfileMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Экспорт профиля"));
                Assert.Same(vm.ExportProfileCommand, export.Command); Assert.Same(profile, export.CommandParameter);
                Assert.True(export.IsEnabled); list.ProfileMenu.Close();
                var more = editor.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Ещё"));
                more.Flyout!.ShowAt(more); Layout();
                var flyout = Assert.IsType<MenuFlyout>(more.Flyout);
                Assert.Same(vm.ShowProfileDependenciesCommand, flyout.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Зависимости профиля")).Command);
                flyout.Hide();
                vm.IsTestRunning = true; Layout();
                Assert.False(vm.ExportProfileCommand.CanExecute(profile)); Assert.False(vm.ShowProfileDependenciesCommand.CanExecute(null));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, CatalogImportChoice.Local)]
    [InlineData(true, CatalogImportChoice.Profile)]
    [InlineData(false, CatalogImportChoice.Cancel)]
    public async Task ConflictShowsBothIdentitiesAndAnExplicitChoiceInBothThemes(bool dark, CatalogImportChoice choice)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(async () =>
        {
            Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var owner = new Window { Width = 1000, Height = 850 }; owner.Show();
            var incoming = ProfileCatalogTransferTests.Card;
            var local = incoming with { Name = "Местное изделие", SerialSeries = incoming.SerialSeries! with { ServerDeviceType = "Местный тип" } };
            var dialog = new ProfileCatalogImportDialog(new(local, incoming));
            try
            {
                var result = dialog.ShowDialog<CatalogImportChoice>(owner);
                dialog.UpdateLayout(); Dispatcher.UIThread.RunJobs(); dialog.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var texts = dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Assert.Contains(texts, t => t?.Contains("Местный тип") == true);
                Assert.Contains(texts, t => t?.Contains("Portable-test-server-type") == true);
                Assert.Contains(texts, t => t?.Contains("всех профилей") == true);
                var buttons = dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Content is string).ToArray();
                foreach (var button in buttons)
                {
                    var pos = button.TranslatePoint(default, dialog)!.Value;
                    Assert.InRange(pos.X + button.Bounds.Width, 0, dialog.ClientSize.Width);
                    Assert.InRange(pos.Y + button.Bounds.Height, 0, dialog.ClientSize.Height);
                }
                var folder = Environment.GetEnvironmentVariable("TESTBUILDER_LAYOUT_CAPTURES");
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                    using var frame = dialog.CaptureRenderedFrame(); frame!.Save(Path.Combine(folder, $"catalog-conflict-{(dark ? "dark" : "light")}.png"));
                }
                var caption = choice switch { CatalogImportChoice.Local => "Карточка этого компьютера", CatalogImportChoice.Profile => "Карточка из профиля", _ => "Отмена" };
                buttons.Single(b => Equals(b.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(choice, await result);
            }
            finally { dialog.Close(); owner.Close(); }
        }, CancellationToken.None);
    }
}
