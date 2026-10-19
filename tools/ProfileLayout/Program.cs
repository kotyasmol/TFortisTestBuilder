using Avalonia;
using Avalonia.Headless;
using ProfileLayout;

if (args.Length < 2 || args[0] is not ("--check" or "--write"))
{
    Console.Error.WriteLine("Usage: ProfileLayout --check|--write profile.json [profile2.json ...]");
    return 2;
}
try
{
    using var session = HeadlessUnitTestSession.StartNew(typeof(LayoutApp));
    return await session.Dispatch(() =>
    {
        var failures = 0;
        foreach (var file in args.Skip(1))
        {
            var report = ProfileLayoutRunner.Process(file, args[0] == "--write");
            Console.WriteLine($"{Path.GetFileName(file)}: {report.Graphs} graphs, {report.Nodes} nodes, {report.Conflicts.Count} spacing conflicts, {report.Moved} moved");
            foreach (var conflict in report.Conflicts.Take(12)) Console.Error.WriteLine("  " + conflict);
            if (report.Conflicts.Count != 0) failures++;
        }
        return failures == 0 ? 0 : 1;
    }, CancellationToken.None);
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

public sealed class LayoutApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestBuilder.App>()
        .UseSkia().WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
