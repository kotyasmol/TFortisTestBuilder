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

    internal static string DeletedPath(string path) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "deleted", Path.GetFileName(path));

    internal static void MoveToDeleted(string path, string expectedRevision)
    {
        lock (PublishLock)
        {
            CheckRevision(path, expectedRevision);
            var destination = DeletedPath(path);
            var folder = Path.GetDirectoryName(destination)!;
            Directory.CreateDirectory(folder);
            if (File.Exists(destination))
                destination = NewCopyPath(folder, Path.GetFileNameWithoutExtension(path));
            File.Move(path, destination, overwrite: false);
        }
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

    public static string DisplayPath(string folder, string name)
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
        return Path.Combine(folder, fileName + ".json");
    }

    public static string NewCopyPath(string folder, string name)
    {
        var fileName = Path.GetFileNameWithoutExtension(DisplayPath(folder, name));
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

    public static async Task<string> RenameAsync(string path, string destination, string json, string expectedRevision)
    {
        if (string.Equals(path, destination, StringComparison.Ordinal))
            return await WriteAsync(path, json, createNew: false, expectedRevision);

        var bytes = Encoding.UTF8.GetBytes(json);
        var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".profile-{Guid.NewGuid():N}.tmp");
        var caseOnly = string.Equals(path, destination, StringComparison.OrdinalIgnoreCase);
        var original = caseOnly ? temporary + ".original" : destination;
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes);
            lock (PublishLock)
            {
                CheckRevision(path, expectedRevision);
                EnsureNameAvailable(destination, caseOnly ? path : null);
                // Moving the original first protects its contents if replacing it fails.
                File.Move(path, original, overwrite: false);
                try { File.Move(temporary, destination, overwrite: !caseOnly); }
                catch
                {
                    if (!File.Exists(path)) File.Move(original, path, overwrite: false);
                    throw;
                }
                if (caseOnly) DeleteTemporary(original);
            }
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally { DeleteTemporary(temporary); }
    }

    public static async Task<bool> ImportAsync(Stream source, string folder, string name,
        Func<CatalogImportConflict, Task<CatalogImportChoice>>? resolveCatalog = null, Func<bool>? canPublish = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
            !string.Equals(Path.GetExtension(name), ".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Выберите файл профиля .json.");
        var temporary = Path.Combine(folder, $".profile-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                await source.CopyToAsync(destination);
            var json = await File.ReadAllTextAsync(temporary);
            GraphSerializer.ValidateProfile(json);
            var plan = await ProfileCatalogImport.PrepareAsync(json, folder, resolveCatalog);
            if (plan == null) return false;
            await File.WriteAllTextAsync(temporary, plan.Json);
            GraphSerializer.ValidateProfile(plan.Json);
            var profile = GraphSerializer.ReadProfile(temporary);
            var destinationPath = DisplayPath(folder, profile.DisplayName);
            lock (PublishLock)
            {
                if (canPublish != null && !canPublish()) throw new IOException("Импорт отменён: состояние программы изменилось. Остановите проверку и повторите импорт.");
                EnsureNameAvailable(destinationPath);
                plan.Publish(folder, () => File.Move(temporary, destinationPath, overwrite: false));
            }
            return true;
        }
        finally { DeleteTemporary(temporary); }
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
                if (createNew) EnsureNameAvailable(fullPath);
                File.Move(temporaryPath, fullPath, overwrite: !createNew);
            }
        }
        finally { DeleteTemporary(temporaryPath); }
    }

    private static void DeleteTemporary(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void EnsureNameAvailable(string path, string? except = null)
    {
        var name = Path.GetFileName(path);
        var exceptName = Path.GetFileName(except);
        if (Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(Path.GetFullPath(path))!)
            .Select(Path.GetFileName)
            .Any(existing => existing != exceptName && string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)))
            throw new IOException($"Файл «{name}» уже существует.");
    }
}
