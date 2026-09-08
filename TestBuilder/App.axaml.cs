using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TestBuilder.Views;

namespace TestBuilder;

public partial class App : Application
{
    public static string StartupSessionId { get; private set; } = string.Empty;

    internal static void ConfigureStartupArguments(string[] args)
    {
        StartupSessionId = args is { Length: > 0 }
            ? args[0]?.Trim() ?? string.Empty
            : string.Empty;
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
