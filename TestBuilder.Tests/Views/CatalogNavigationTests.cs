using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class CatalogNavigationTests
{
    [Fact]
    public async Task PersistentCatalogPageUsesTheCurrentProfilesFolder()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var originalFolder = new ProfileCopyTests.ProfileFolder();
            var oldMode = AppSettings.Instance.EngineerMode;
            var window = new MainWindow();
            var shell = (MainWindowViewModel)window.DataContext!;
            try
            {
                shell.SettingsVM.IsEngineerMode = true;
                window.Show(); Layout(window);
                shell.SelectPageCommand.Execute("Catalog"); Layout(window);
                var page = window.FindControl<EquipmentCatalogView>("EquipmentCatalogViewControl")!;
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<TabControl>(), tab => tab.Name == "EquipmentCatalogTabs");
                using var currentFolder = new ProfileCopyTests.ProfileFolder();
                EquipmentCatalog.SaveModel(currentFolder.Path, new EquipmentModel("folder-fixture", "Изделие нового каталога"));
                shell.SelectPageCommand.Execute("Editor");
                shell.SelectPageCommand.Execute("Catalog"); Layout(window);
                var paths = page.GetVisualDescendants().OfType<SelectableTextBlock>().Single(block => block.Name == "CatalogDataPaths");
                Assert.Contains(EquipmentCatalog.LocalPath(currentFolder.Path), paths.Text);
                var models = page.GetVisualDescendants().OfType<DataGrid>().Single(grid => grid.Name == "CatalogDataModels");
                Assert.Contains(models.ItemsSource.Cast<EquipmentCatalogDataView.CatalogRow>(), row => row.Model.Id == "folder-fixture");
            }
            finally { window.Close(); AppSettings.Instance.EngineerMode = oldMode; }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, 1100)] [InlineData(true, 1100)]
    [InlineData(false, 1440)] [InlineData(true, 1440)]
    public async Task CatalogIsAWorkspacePageAndItsEditorUsesTheMainWindow(bool dark, int width)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch(() =>
        {
            using var folder = new ProfileCopyTests.ProfileFolder();
            var oldMode = AppSettings.Instance.EngineerMode;
            var oldTheme = Application.Current!.RequestedThemeVariant;
            var window = new MainWindow { Width = width, Height = 800 };
            var shell = (MainWindowViewModel)window.DataContext!;
            try
            {
                shell.SettingsVM.IsEngineerMode = true;
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Show(); Layout(window);
                var before = GraphSerializer.Serialize(shell.TestVM, shell.TestVM.CurrentProfileName);
                var navigation = window.FindControl<Button>("CatalogNavigationButton")!;
                Click(window, navigation);
                Assert.True(shell.IsCatalogPage);
                var page = window.FindControl<EquipmentCatalogView>("EquipmentCatalogViewControl")!;
                Assert.True(page.IsEffectivelyVisible);
                Assert.Empty(window.OwnedWindows);
                Assert.False(window.FindControl<TestView>("TestViewControl")!.IsEffectivelyVisible);

                var hardware = window.FindControl<Button>("EquipmentLibraryNavigationButton")!;
                var hardwareLeft = hardware.TranslatePoint(default, window)!.Value.X;
                foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("workspaceNav") && button.IsEffectivelyVisible))
                    Assert.True(button.TranslatePoint(default, window)!.Value.X + button.Bounds.Width <= hardwareLeft);

                Assert.DoesNotContain(page.GetVisualDescendants().OfType<TabControl>(), tab => tab.Name == "EquipmentCatalogTabs");
                var models = page.GetVisualDescendants().OfType<DataGrid>().Single(grid => grid.Name == "CatalogDataModels");
                Assert.Equal(shell.TestVM.EquipmentModels.Count, models.ItemsSource.Cast<object>().Count());
                var edit = page.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "EditCatalogDataModel");
                edit.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Layout(window);
                var card = Assert.Single(window.OpenPages().OfType<CatalogItemEditorView>());
                card.Close(); Layout(window);
                Assert.Equal(before, GraphSerializer.Serialize(shell.TestVM, shell.TestVM.CurrentProfileName));
                WorkspacePageTesting.CaptureRenderedFrame(window)!.Save(Path.Combine(Path.GetTempPath(), $"tfortis-catalog-navigation-{dark}-{width}.png"));

                GraphSerializer.Deserialize("""{"name":"Плата","productKind":"Board","nodes":[],"connections":[]}""", shell.TestVM);
                Layout(window);
                Assert.False(shell.ShowSwitchTools);
                Assert.True(navigation.IsEffectivelyVisible);
                Assert.True(shell.IsCatalogPage);
                var board = GraphSerializer.Serialize(shell.TestVM, shell.TestVM.CurrentProfileName);
                shell.SelectPageCommand.Execute("Editor"); Layout(window);
                Assert.False(page.IsEffectivelyVisible);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && Equals(button.Content, "Все изделия…"));
                Click(window, navigation);
                Assert.Equal(board, GraphSerializer.Serialize(shell.TestVM, shell.TestVM.CurrentProfileName));
            }
            finally
            {
                window.Close();
                AppSettings.Instance.EngineerMode = oldMode;
                Application.Current.RequestedThemeVariant = oldTheme;
            }
        }, CancellationToken.None);
    }

    private static void Layout(Control window) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
    private static void Click(Control window, Button button)
    {
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        WorkspacePageTesting.MouseDown(window, point, MouseButton.Left); WorkspacePageTesting.MouseUp(window, point, MouseButton.Left); Layout(window);
    }
}
