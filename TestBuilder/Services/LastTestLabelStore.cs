using System;
using System.IO;
using System.Text.Json;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Services;

public sealed record LastTestLabelSnapshot(DateTimeOffset FinishedAt, string ProfileName, string DeviceModel,
    ExecutionStatus Result, string Error, PreparedLabel? Label)
{
    public LastTestLabelSnapshot Copy() => this with { Label = Label?.Copy() };
}

/// <summary>Only the latest production run's label data, without credentials or hardware state.</summary>
public sealed class LastTestLabelStore
{
    private readonly string _path;
    public LastTestLabelStore(string? path = null) =>
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "last-test-label.json");

    public LastTestLabelSnapshot? Load()
    {
        if (!File.Exists(_path)) return null;
        var snapshot = JsonSerializer.Deserialize<LastTestLabelSnapshot>(File.ReadAllText(_path))
            ?? throw new InvalidDataException("Файл последнего теста пуст.");
        snapshot.Label?.Template.Validate();
        return snapshot;
    }

    public void Save(LastTestLabelSnapshot snapshot)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
