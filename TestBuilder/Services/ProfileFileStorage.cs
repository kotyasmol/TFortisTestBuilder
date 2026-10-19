using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.Services;

internal static class ProfileFileStorage
{
    public static string NewCopyPath(string folder, string name)
    {
        // Profiles travel between Windows workstations and macOS; use a name valid on both.
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        var fileName = new string(name.Trim().Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).Take(80).ToArray()).TrimEnd(' ', '.');
        if (fileName.Length > 0 && char.IsHighSurrogate(fileName[^1])) fileName = fileName[..^1];
        if (fileName.Length == 0) fileName = "Профиль";
        var stem = fileName.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')
            fileName = "_" + fileName;
        var names = Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = fileName + ".json";
        for (var index = 2; names.Contains(candidate); index++) candidate = $"{fileName} ({index}).json";
        return Path.Combine(folder, candidate);
    }

    public static async Task WriteAsync(string path, string json, bool createNew)
    {
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".profile-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false));
            // Publish only a complete file. A collision must never replace another profile.
            File.Move(temporaryPath, fullPath, overwrite: !createNew);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
