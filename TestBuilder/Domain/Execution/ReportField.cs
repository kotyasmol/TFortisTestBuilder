using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace TestBuilder.Domain.Execution;

public enum ReportSourceKind
{
    Entry,
    Variable,
    TestType,
    AllPassed
}

public enum ReportValueFormat
{
    Text,
    BooleanText,
    BooleanNumber,
    FixedSix,
    PoeMillivolts
}

public sealed record ReportField
{
    internal const string TestTypeLabel = "Тип проверки";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("source")]
    [JsonConverter(typeof(JsonStringEnumConverter<ReportSourceKind>))]
    public ReportSourceKind Source { get; init; }

    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("format")]
    [JsonConverter(typeof(JsonStringEnumConverter<ReportValueFormat>))]
    public ReportValueFormat Format { get; init; }

    [JsonPropertyName("statusKey")]
    public string StatusKey { get; init; } = "";

    internal string DisplayLabel => Source == ReportSourceKind.TestType
        ? TestTypeLabel
        : string.IsNullOrWhiteSpace(Label) ? Key?.Trim() ?? "" : Label.Trim();

    public TestReportEntry GetPreview()
    {
        if (Source == ReportSourceKind.TestType)
            return new TestReportEntry(TestTypeLabel, true, "production");

        var example = Format switch
        {
            ReportValueFormat.BooleanText or ReportValueFormat.BooleanNumber => "true",
            ReportValueFormat.FixedSix => "15.075",
            ReportValueFormat.PoeMillivolts => "55163",
            _ => "пример значения"
        };
        return FormatEntry(example, true) ?? new TestReportEntry(DisplayLabel, true, example);
    }

    internal TestReportEntry? FormatEntry(string value, bool? status)
    {
        var label = DisplayLabel;
        switch (Format)
        {
            case ReportValueFormat.BooleanText:
            case ReportValueFormat.BooleanNumber:
                if (!TryReadBoolean(value, out var boolean))
                {
                    if (!status.HasValue) return null;
                    boolean = status.Value;
                }
                var formatted = Format == ReportValueFormat.BooleanNumber
                    ? (boolean ? "1" : "0")
                    : (boolean ? "true" : "false");
                return new TestReportEntry(label, status ?? boolean, formatted);

            case ReportValueFormat.FixedSix:
                if (!TryReadNumber(value, out var number))
                    return status is false ? new TestReportEntry(label, false, value) : null;
                return new TestReportEntry(label, status ?? true, number.ToString("F6", CultureInfo.InvariantCulture));

            case ReportValueFormat.PoeMillivolts:
                if (!status.HasValue) return null;
                var verdict = status.Value ? "пройдено" : "не пройдено";
                if (!TryReadNumber(value, out var millivolts))
                    return status is false ? new TestReportEntry($"{label}: {verdict}", false, value) : null;
                var volts = (millivolts / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
                return new TestReportEntry($"{label}: {verdict}, {volts}V", status.Value,
                    millivolts.ToString("F6", CultureInfo.InvariantCulture));

            default:
                return new TestReportEntry(label, status ?? true, value);
        }
    }

    internal static bool TryReadBoolean(string? value, out bool boolean)
    {
        value = value?.Trim();
        if (value is "0" or "1")
        {
            boolean = value == "1";
            return true;
        }
        return bool.TryParse(value, out boolean);
    }

    internal static string FormatText(object? value) => value switch
    {
        null => "",
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => value.ToString() ?? ""
    };

    internal static string SanitizeWireText(string? value) =>
        (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('=', ':');

    private static bool TryReadNumber(string value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
}
