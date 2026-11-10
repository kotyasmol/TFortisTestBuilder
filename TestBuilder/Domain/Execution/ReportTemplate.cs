using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TestBuilder.Domain.Execution;

public sealed record ReportTemplate
{
    internal const string FailureLabel = "Причина остановки";

    private static readonly HashSet<string> ServiceLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        "test_result", "stand_id", "serial_num", "session", ReportField.TestTypeLabel, FailureLabel
    };

    private readonly IReadOnlyList<ReportField>? _fields = Array.Empty<ReportField>();

    [JsonRequired, JsonPropertyName("fields")]
    public IReadOnlyList<ReportField> Fields
    {
        get => _fields!;
        init => _fields = value == null ? null : Array.AsReadOnly(value.ToArray());
    }

    [JsonPropertyName("includeAutomaticEntries")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IncludeAutomaticEntries { get; init; }

    public string? Validate()
    {
        if (_fields == null) return "Список строк отчёта не задан.";

        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasTestType = false;
        for (var index = 0; index < _fields.Count; index++)
        {
            var field = _fields[index];
            var row = $"Строка отчёта {index + 1}";
            if (field == null || !Enum.IsDefined(field.Source) || !Enum.IsDefined(field.Format))
                return $"{row}: неизвестный источник или формат.";

            if (field.Source == ReportSourceKind.TestType)
            {
                if (hasTestType) return $"{row}: тип проверки можно указать один раз.";
                hasTestType = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(field.Key)) return $"{row}: укажите ключ источника.";
            var key = field.Key.Trim();
            var statusKey = field.StatusKey?.Trim();
            if (ServiceLabels.Contains(key) ||
                field.Source == ReportSourceKind.Variable && !CanSelectVariable(key) ||
                !string.IsNullOrEmpty(statusKey) && !CanSelectVariable(statusKey))
                return $"{row}: ключ источника зарезервирован.";
            if (field.Source != ReportSourceKind.Variable && !string.IsNullOrWhiteSpace(field.StatusKey))
                return $"{row}: отдельный ключ результата допустим только для переменной.";
            if (field.Source == ReportSourceKind.Variable && field.Format == ReportValueFormat.PoeMillivolts &&
                string.IsNullOrWhiteSpace(field.StatusKey))
                return $"{row}: для напряжения PoE укажите ключ результата проверки.";

            if (field.Source == ReportSourceKind.AllPassed)
            {
                var keys = SplitKeys(key);
                if (keys.Any(string.IsNullOrEmpty) || string.IsNullOrWhiteSpace(field.Label) ||
                    field.Format is ReportValueFormat.FixedSix or ReportValueFormat.PoeMillivolts)
                    return $"{row}: задайте подпись, логический формат и ключи результатов через «;».";
                if (keys.Any(variable => !CanSelectVariable(variable)))
                    return $"{row}: ключ источника зарезервирован.";
            }

            var label = field.DisplayLabel;
            if (label.Contains('=') || label.Contains('\r') || label.Contains('\n') || ServiceLabels.Contains(label))
                return $"{row}: подпись «{label}» недопустима или зарезервирована.";
            if (field.Enabled && !labels.Add(label))
                return $"{row}: подпись «{label}» уже используется.";
        }

        return null;
    }

    internal IReadOnlyList<TestReportEntry> Project(
        TestContext context, IReadOnlyList<TestReportEntry> entries, string testType, string reportVariableName)
    {
        var projected = new List<TestReportEntry>();
        var typeEntry = new TestReportEntry(ReportField.TestTypeLabel, true, testType);
        if (!Fields.Any(field => field.Source == ReportSourceKind.TestType))
            projected.Add(typeEntry);

        foreach (var field in Fields)
        {
            if (field.Source == ReportSourceKind.TestType)
            {
                projected.Add(typeEntry);
                continue;
            }

            if (!field.Enabled) continue;

            var key = field.Key.Trim();
            var (value, status) = field.Source switch
            {
                ReportSourceKind.Entry => ReadEntry(entries, key),
                ReportSourceKind.Variable => ReadVariable(context, key, field.StatusKey?.Trim(), reportVariableName),
                ReportSourceKind.AllPassed => ReadCombinedResult(context, key, reportVariableName),
                _ => (null, null)
            };

            if (value != null && field.FormatEntry(value, status) is { } entry)
                projected.Add(entry);
        }

        if (IncludeAutomaticEntries)
            InsertAutomaticEntries(projected, entries, typeEntry);

        return projected;
    }

    private void InsertAutomaticEntries(List<TestReportEntry> projected, IReadOnlyList<TestReportEntry> entries,
        TestReportEntry typeEntry)
    {
        var explicitEntryKeys = Fields.Where(field => field.Source == ReportSourceKind.Entry)
            .Select(field => field.Key.Trim()).ToHashSet(StringComparer.Ordinal);
        var excludedLabels = new HashSet<string>(ServiceLabels, StringComparer.OrdinalIgnoreCase);
        excludedLabels.UnionWith(Fields.Where(field => field.Enabled).Select(field => field.DisplayLabel));
        excludedLabels.UnionWith(projected.Select(entry => ReportField.SanitizeWireText(entry.Name)));

        // Keep the automatic section after a leading type field and before the rest of the selected rows.
        var index = projected.Count > 0 && ReferenceEquals(projected[0], typeEntry) ? 1 : 0;
        projected.InsertRange(index, entries.Where(entry =>
            !string.IsNullOrWhiteSpace(entry.Name) && !explicitEntryKeys.Contains(entry.Name) &&
            !excludedLabels.Contains(ReportField.SanitizeWireText(entry.Name))));
    }

    private static (string? Value, bool? Status) ReadEntry(IReadOnlyList<TestReportEntry> entries, string key)
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            var entry = entries[index];
            if (string.Equals(entry.Name, key, StringComparison.Ordinal))
                return (entry.Value, entry.IsSuccess);
        }
        return (null, null);
    }

    private static (string? Value, bool? Status) ReadVariable(
        TestContext context, string key, string? statusKey, string reportVariableName)
    {
        var value = ReadVariableText(context, key, reportVariableName);
        if (string.IsNullOrEmpty(statusKey)) return (value, null);
        return ReportField.TryReadBoolean(ReadVariableText(context, statusKey, reportVariableName), out var status)
            ? (value, status)
            : (null, null);
    }

    private static (string? Value, bool? Status) ReadCombinedResult(TestContext context, string keys,
        string reportVariableName)
    {
        var hasMissing = false;
        foreach (var key in SplitKeys(keys))
        {
            if (!ReportField.TryReadBoolean(ReadVariableText(context, key, reportVariableName), out var result))
                hasMissing = true;
            else if (!result)
                return ("false", false);
        }
        return hasMissing ? (null, null) : ("true", true);
    }

    private static string? ReadVariableText(TestContext context, string key, string reportVariableName) =>
        CanSelectVariable(key, reportVariableName) &&
        !string.Equals(key, context.GetVariable<string>("BuildReport.VariableName"), StringComparison.Ordinal) &&
        context.Variables.TryGetValue(key, out var value)
            ? ReportField.FormatText(value)
            : null;

    internal static bool CanSelectVariable(string? name, string? reportVariableName = null) =>
        !string.IsNullOrWhiteSpace(name) && !ServiceLabels.Contains(name) &&
        !name.StartsWith("ReportDelivery.", StringComparison.Ordinal) &&
        IsLegacyReportVariable(name, reportVariableName);

    internal static bool IsLegacyReportVariable(string name, string? reportVariableName) =>
        !string.Equals(name, reportVariableName, StringComparison.Ordinal) &&
        !name.StartsWith("BuildReport.", StringComparison.Ordinal) &&
        !name.StartsWith("SendReport.", StringComparison.Ordinal);

    private static string[] SplitKeys(string keys) => keys.Split(';', StringSplitOptions.TrimEntries);
}
