using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TestBuilder.Domain.Execution;
using TestBuilder.Services.Logging;

namespace TestBuilder.Services;

internal sealed class ReportVariableHistory
{
    private static readonly object StorageLock = new();
    private readonly string _directory;
    private readonly Dictionary<string, HashSet<string>> _session = new(StringComparer.Ordinal);
    private readonly HashSet<string> _draft = new(StringComparer.Ordinal);

    internal ReportVariableHistory(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TFortisTestBuilder", "report-variable-catalog");
    }

    internal IReadOnlyList<string> Read(string? profilePath)
    {
        lock (StorageLock)
        {
            if (profilePath == null) return Ordered(_draft);
            var file = HistoryFile(profilePath);
            var keys = SessionKeys(file);
            keys.UnionWith(ReadPersisted(file));
            return Ordered(keys);
        }
    }

    internal void Remember(string? profilePath, IEnumerable<string> names)
    {
        var selected = names.Where(name => ReportTemplate.CanSelectVariable(name)).ToArray();
        if (selected.Length == 0) return;
        lock (StorageLock)
        {
            if (profilePath == null)
            {
                _draft.UnionWith(selected);
                return;
            }

            var file = HistoryFile(profilePath);
            var keys = SessionKeys(file);
            keys.UnionWith(selected);
            var persisted = ReadPersisted(file);
            if (keys.IsSubsetOf(persisted)) return;
            keys.UnionWith(persisted);
            Save(file, Ordered(keys));
        }
    }

    internal void ClearDraft()
    {
        lock (StorageLock) _draft.Clear();
    }

    private HashSet<string> SessionKeys(string file)
    {
        if (!_session.TryGetValue(file, out var keys))
            _session[file] = keys = new HashSet<string>(StringComparer.Ordinal);
        return keys;
    }

    private string HistoryFile(string profilePath)
    {
        var path = Path.GetFullPath(profilePath);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        return Path.Combine(_directory, id + ".json");
    }

    private static string[] ReadPersisted(string file)
    {
        try
        {
            if (!File.Exists(file)) return Array.Empty<string>();
            var names = JsonSerializer.Deserialize<string[]>(File.ReadAllText(file)) ?? Array.Empty<string>();
            return names.Where(name => ReportTemplate.CanSelectVariable(name)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DiagnosticLog.WriteException("Чтение каталога переменных отчёта", ex);
            return Array.Empty<string>();
        }
    }

    private void Save(string file, IReadOnlyList<string> names)
    {
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temporary, JsonSerializer.Serialize(names));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticLog.WriteException("Сохранение каталога переменных отчёта", ex);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DiagnosticLog.WriteException("Удаление временного каталога переменных отчёта", ex);
            }
        }
    }

    private static string[] Ordered(IEnumerable<string> names) => names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
}
