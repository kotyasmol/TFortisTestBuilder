using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TestBuilder.Views;
using TestBuilder.Services;

namespace TestBuilder;

public partial class App : Application
{
    public static LauncherOptions StartupOptions { get; private set; } = LauncherOptions.Parse(System.Array.Empty<string>());
    public static string StartupSessionId => StartupOptions.SessionId;
    public static string StartupUserName => StartupOptions.UserName;

    internal static void ConfigureStartupArguments(string[] args)
    {
        StartupOptions = LauncherOptions.Parse(args);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
