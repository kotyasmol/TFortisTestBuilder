using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using TestBuilder.Services;
using TestBuilder.ViewModels;

namespace TestBuilder.Views;

internal static class TutorialLauncher
{
    public static async Task OfferOnFirstLaunchAsync(Window owner)
    {
        if (!TutorialSession.ShouldOffer(AppSettings.Instance)) return;
        TutorialSession.RecordOffer(AppSettings.Instance);
        var welcome = CreateWelcome();
        if (await welcome.ShowDialog<bool>(owner) && owner.IsVisible) await StartAsync(owner);
    }

    internal static Window CreateWelcome()
    {
        var window = new Window { Title = "Знакомство с TestBuilder", Width = 600, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.Bind(Window.BackgroundProperty, new DynamicResourceExtension("SurfaceBrush"));
        var content = new StackPanel { Margin = new Thickness(30), Spacing = 18 };
        content.Children.Add(new TextBlock { Text = "Хотите попробовать программу?", FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "Вы выберете и запустите учебную проверку, измените профиль, создадите новое устройство и соедините успешный исход и отмену в редакторе. Подсказки покажут, куда нажимать.", FontSize = 16, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "Обучение займёт около десяти минут. Стенд не нужен; рабочие профили не изменятся. Обучение можно закрыть в любой момент и повторить из настроек.", FontSize = 14, TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var skip = new Button { Content = "Пропустить", Name = "SkipTutorial", Margin = new Thickness(0, 0, 10, 0) };
        var start = new Button { Content = "Пройти обучение", Name = "StartTutorial" }; start.Classes.Add("primary");
        skip.Click += (_, _) => window.Close(false); start.Click += (_, _) => window.Close(true);
        buttons.Children.Add(skip); buttons.Children.Add(start); content.Children.Add(buttons); window.Content = content;
        return window;
    }

    public static async Task StartAsync(Window owner)
    {
        if (owner.DataContext is MainWindowViewModel vm && (vm.TestVM.IsTestRunning || vm.TestVM.IsConnecting)) return;
        if (owner.OwnedWindows.OfType<TutorialWindow>().FirstOrDefault() is { } existing) { existing.Activate(); return; }
        await new TutorialWindow().ShowDialog(owner);
    }
}
