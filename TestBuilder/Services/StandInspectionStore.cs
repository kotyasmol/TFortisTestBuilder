using System;
using System.IO;

namespace TestBuilder.Services;

internal sealed class StandInspectionStore(string path)
{
    public static StandInspectionStore Local { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TFortisTestBuilder", "stand-inspection.required"));

    public bool IsRequired()
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    public void Require()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var marker = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        marker.Flush(flushToDisk: true);
    }

    public void Clear()
    {
        try { File.Delete(path); }
        catch (DirectoryNotFoundException) { }
    }
}
