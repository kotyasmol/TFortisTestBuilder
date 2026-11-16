using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TestBuilder.Services;

/// <summary>Workstation-local choices; never part of a profile or a release.</summary>
internal sealed class ProfileCurrentStore(string path)
{
    internal static ProfileCurrentStore Local { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TFortisTestBuilder", "profile-current.json"));
    internal static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    internal static string Normalize(string profilePath) => Path.GetFullPath(profilePath);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private sealed record State(int SchemaVersion, string[] Profiles);

    internal HashSet<string> Read()
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var state = JsonSerializer.Deserialize<State>(stream, JsonOptions);
            if (state == null || state.SchemaVersion != 1 || state.Profiles == null ||
                state.Profiles.Any(p => string.IsNullOrWhiteSpace(p) || !Path.IsPathFullyQualified(p)))
                throw new JsonException("Некорректный формат локальных отметок профилей.");
            return new HashSet<string>(state.Profiles.Select(Normalize), PathComparer);
        }
        catch (FileNotFoundException) { return new(PathComparer); }
        catch (DirectoryNotFoundException) { return new(PathComparer); }
    }

    internal Task<HashSet<string>> SetAsync(string profilePath, bool current) => UpdateAsync(paths =>
        current ? paths.Add(Normalize(profilePath)) : paths.Remove(Normalize(profilePath)));

    internal Task<HashSet<string>> MoveAsync(string oldPath, string newPath) => UpdateAsync(paths =>
    {
        if (!paths.Remove(Normalize(oldPath))) return false;
        paths.Add(Normalize(newPath));
        return true;
    });

    private async Task<HashSet<string>> UpdateAsync(Func<HashSet<string>, bool> update)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var lease = await AcquireLockAsync();
        // Re-read under the process lock so another app instance's choices survive.
        var paths = Read();
        if (!update(paths)) return paths;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, new State(1, paths.OrderBy(p => p, PathComparer).ToArray()), JsonOptions);
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return paths;
    }

    private async Task<FileStream> AcquireLockAsync()
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (timer.ElapsedMilliseconds < 2000) { await Task.Delay(50); }
        }
    }
}
