using System;
using System.IO;
using System.Linq;
using Avalonia.Platform;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services;

internal sealed class TutorialSession : IDisposable
{
    public const int Version = 1;
    public const string IntroName = "Учебная проверка";
    private readonly ModbusService _bus = new();
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "TFortis-training", Guid.NewGuid().ToString("N"));
    public TestViewModel Model { get; }
    public string OriginalPath => Path.Combine(DirectoryPath, "intro.json");

    public TutorialSession()
    {
        Directory.CreateDirectory(DirectoryPath);
        using var source = AssetLoader.Open(new Uri("avares://TestBuilder/Assets/Tutorial/intro.json"));
        using (var output = File.Create(OriginalPath)) source.CopyTo(output);
        Model = new TestViewModel(_bus, new SlaveManager(_bus), true, DirectoryPath) { IsOperatorView = true };
        foreach (var group in Model.StationProfileGroups) group.IsExpanded = false;
        Model.AvailableNodeCategories.Clear();
        Model.AvailableNodeCategories.Add(new NodePaletteCategoryViewModel("Структура",
            Model.AvailableNodes.OfType<StartNodeViewModel>().Single(),
            Model.AvailableNodes.OfType<OperatorActionNodeViewModel>().Single(),
            Model.AvailableNodes.OfType<DelayNodeViewModel>().Single(),
            Model.AvailableNodes.OfType<EndNodeViewModel>().Single()));
    }

    public static bool ShouldOffer(AppSettings settings) => settings.TutorialOfferedVersion < Version;
    public static void RecordOffer(AppSettings settings) { settings.TutorialOfferedVersion = Version; settings.Save(); }
    public static void RecordCompletion(AppSettings settings) { settings.TutorialCompletedVersion = Version; settings.Save(); }

    public void Dispose()
    {
        Model.Dispose(); _bus.Dispose();
        try { Directory.Delete(DirectoryPath, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
