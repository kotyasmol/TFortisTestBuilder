using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TestBuilder.Services;
using TestBuilder.ViewModels;
using TestBuilder.Views;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class WorkspaceNavigationTests
{
    private static void Layout(Control control)
    {
        Dispatcher.UIThread.RunJobs(); control.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Dispatcher.UIThread.RunJobs();
    }
    private static void Click(Control root, string caption)
    {
        root.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(root);
    }
    private static void Back(Window window)
    {
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WorkspacePageBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(window);
    }

    [Fact]
    public async Task NetworkChildEditorReturnsToParentDraftWithoutCreatingWindowsOrSaving()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var source = NetworkTestPlans.TenPorts(); var before = JsonSerializer.Serialize(source);
            BenchNetworkPlan? saved = null;
            var workspace = new TextBox { Text = "Исходный экран" };
            var owner = new Window { Content = workspace, Width = 1100, Height = 760 };
            owner.Show(); Layout(owner);
            try
            {
                var plan = new NetworkPlanView(source, result => saved = result);
                var pending = plan.ShowAsync<bool>(owner); Layout(owner);
                var mask = plan.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "NetworkPlanMask");
                mask.Text = "255.255.0.0";
                Click(plan, "Добавить порты…");
                Assert.IsType<NetworkPortBatchView>(owner.OpenPages().Last());
                Assert.Equal(2, owner.OpenPages().Count); Assert.Empty(owner.OwnedWindows);
                Assert.False(plan.IsVisible); Assert.False(workspace.IsVisible); Assert.Null(saved);
                Back(owner); await Task.Yield(); Layout(owner);
                Assert.Same(plan, owner.OpenPages().Single()); Assert.True(plan.IsVisible);
                Assert.Equal("255.255.0.0", mask.Text); Assert.Null(saved);
                Back(owner); Assert.False(await pending);
                Assert.Empty(owner.OpenPages()); Assert.Empty(owner.OwnedWindows);
                Assert.True(workspace.IsVisible); Assert.True(workspace.IsEnabled);
                Assert.Equal(before, JsonSerializer.Serialize(source)); Assert.Null(saved);
            }
            finally { owner.Close(); }
            return true;
        }, default);
    }

    [Fact]
    public async Task BusyEditorRejectsBackAndWindowCloseAndCompletesExactlyOnce()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var owner = new Window { Content = new Border(), Width = 900, Height = 650 };
            owner.Show();
            var busy = true; var closed = 0;
            var page = new WorkspacePage { Title = "Выполняется операция", Content = new TextBox() };
            page.Closing += (_, args) => args.Cancel = busy;
            page.Closed += (_, _) => closed++;
            var pending = page.ShowAsync<bool>(owner); Layout(owner);
            Back(owner); owner.Close();
            Assert.True(owner.IsVisible); Assert.False(pending.IsCompleted); Assert.Equal(0, closed);
            busy = false; page.Close(true); Assert.True(await pending);
            page.Close(); Assert.Equal(1, closed); Assert.Empty(owner.OpenPages());
            owner.Close(); return true;
        }, default);
    }

    [Fact]
    public async Task OwnerCloseCancelsAllPendingPagesAndDisposesTheirDrafts()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var owner = new Window { Content = new Border() }; owner.Show();
            var first = new WorkspacePage { Title = "Первый" }; var second = new WorkspacePage { Title = "Второй" };
            var a = first.ShowAsync<string?>(owner); var b = second.ShowAsync<string?>(first);
            var closed = 0; first.Closed += (_, _) => closed++; second.Closed += (_, _) => closed++;
            owner.Close(); Assert.Null(await a); Assert.Null(await b); Assert.Equal(2, closed);
            Assert.Empty(owner.OwnedWindows); return true;
        }, default);
    }

    [Fact]
    public async Task ConfirmationInsideActionEditorKeepsTheDraftAndUsesTheSameWindow()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var owner = new Window { Content = new Border(), Width = 1280, Height = 800 }; owner.Show();
            var page = new ProfileActionEditorView(); var pending = page.ShowAsync(owner); Layout(owner);
            var editor = (TestViewModel)page.FindControl<TestBuilder.Views.TestViews.GraphEditorView>("OnEditor")!.DataContext!;
            var before = JsonSerializer.Serialize(GraphSerializer.ExportActionGraph(editor.RootGraph));
            var confirmation = editor.ConfirmClearGraphAsync(); Layout(owner);
            Assert.IsType<ClearGraphConfirmationView>(owner.OpenPages().Last()); Assert.Empty(owner.OwnedWindows);
            Back(owner); Assert.False(await confirmation); Layout(owner);
            Assert.Same(page, owner.OpenPages().Single());
            Assert.Equal(before, JsonSerializer.Serialize(GraphSerializer.ExportActionGraph(editor.RootGraph)));
            page.Close(); await pending; owner.Close(); return true;
        }, default);
    }

    [Fact]
    public async Task EnteringAnEditorClosesThePreviousScreensDropdown()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var combo = new ComboBox { ItemsSource = new[] { "Первый", "Второй" }, SelectedIndex = 0, Width = 250 };
            var owner = new Window { Content = combo, Width = 900, Height = 650 };
            WorkspacePageHost.For(owner); owner.Show(); Layout(owner);
            try
            {
                combo.IsDropDownOpen = true; Layout(owner); Assert.True(combo.IsDropDownOpen);
                var page = new WorkspacePage { Title = "Редактор" }; var pending = page.ShowAsync(owner); Layout(owner);
                Assert.False(combo.IsDropDownOpen); Assert.Equal("Первый", combo.SelectedItem);
                Back(owner); await pending;
                Assert.True(combo.IsEffectivelyVisible); Assert.Equal("Первый", combo.SelectedItem);
            }
            finally { owner.Close(); }
            return true;
        }, default);
    }

    [Theory]
    [InlineData(false, 1100, 720)] [InlineData(true, 1440, 900)]
    public async Task EditorsRenderInTheActualMainWindow(bool dark, int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var clean = new FreshUserJourneyTests.CleanWorkstation();
            var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var owner = new MainWindow { Width = width, Height = height, RequestedThemeVariant = theme };
            owner.Show(); Layout(owner);
            var directory = Environment.GetEnvironmentVariable("TFORTIS_UI_ARTIFACTS");
            try
            {
                foreach (var page in new WorkspacePage[] { new NetworkPlanView(NetworkTestPlans.TenPorts(), _ => { }),
                    new ProfileActionEditorView(), new UserGuideView() })
                {
                    var pending = page.ShowAsync(owner); Layout(owner);
                    Assert.Empty(owner.OwnedWindows); Assert.Same(page, owner.OpenPages().Single());
                    Assert.True(page.Bounds.Width > 500); Assert.True(page.Bounds.Height > 400);
                    if (directory != null)
                    {
                        Directory.CreateDirectory(directory);
                        using var bitmap = HeadlessWindowExtensions.CaptureRenderedFrame(owner);
                        bitmap!.Save(Path.Combine(directory, page.GetType().Name + (dark ? "-dark" : "-light") + ".png"));
                    }
                    Back(owner); await pending;
                }
            }
            finally { owner.Close(); }
            return true;
        }, default);
    }
}
