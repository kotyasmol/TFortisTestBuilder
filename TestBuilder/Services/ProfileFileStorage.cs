using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace TestBuilder.Services;

internal static class ProfileFileStorage
{
    private static readonly object PublishLock = new();
    public static (string Json, string Revision) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes));
        return (reader.ReadToEnd(), Convert.ToHexString(SHA256.HashData(bytes)));
    }

    public static void CheckRevision(string path, string expectedRevision)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (Convert.ToHexString(SHA256.HashData(stream)) == expectedRevision) return;
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
        throw new IOException("Файл изменился на диске. Обновите профиль или сохраните правки в копию.");
    }

    public static string WithName(string json, GraphProfile source, string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Any(char.IsControl))
            throw new ArgumentException("Введите название профиля без управляющих символов.");
        var root = JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonObject;
        if (root == null || root["nodes"] is not JsonArray || root["connections"] is not (null or JsonArray))
            throw new InvalidOperationException("Файл не содержит граф профиля.");
        // Keep every graph, coordinate, button and unknown field intact.
        description = description.Trim();
        root["name"] = GraphProfile.ComposeName(source.DeviceModel, description);
        root["configurationName"] = description;
        if (root["deviceModel"] == null && source.DeviceModel != null) root["deviceModel"] = source.DeviceModel;
        if (root["deviceModelId"] == null && source.DeviceModelId != null) root["deviceModelId"] = source.DeviceModelId;
        root["updatedAt"] = DateTimeOffset.UtcNow;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

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

    public static async Task<string> WriteAsync(string path, string json, bool createNew, string? expectedRevision = null)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await PublishAsync(path, createNew, temporaryPath => File.WriteAllBytesAsync(temporaryPath, bytes), expectedRevision);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static Task ImportAsync(Stream source, string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
            !string.Equals(Path.GetExtension(name), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Выберите файл профиля .json.");
        return PublishAsync(Path.Combine(folder, name), createNew: true, async temporaryPath =>
        {
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                await source.CopyToAsync(destination);
            GraphSerializer.ValidateProfile(await File.ReadAllTextAsync(temporaryPath));
        });
    }

    private static async Task PublishAsync(string path, bool createNew, Func<string, Task> prepare, string? expectedRevision = null)
    {
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".profile-{Guid.NewGuid():N}.tmp");
        try
        {
            await prepare(temporaryPath);
            // Publish only a complete file. A collision must never replace another profile.
            lock (PublishLock)
            {
                if (expectedRevision != null) CheckRevision(fullPath, expectedRevision);
                File.Move(temporaryPath, fullPath, overwrite: !createNew);
            }
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
