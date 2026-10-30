using TestBuilder.Services;

namespace TestBuilder.Domain.Execution;

/// <summary>Validated print input, captured before contacting the printer.</summary>
public sealed record PreparedLabel(LabelTemplateDefinition Template, string Serial, string Mac,
    string PrinterName, int Copies)
{
    public PreparedLabel Copy() => this with { Template = Template.Copy() };
}

/// <summary>One run's latest prepared label; shared by parallel branches, never by separate runs.</summary>
public sealed class LabelPrintCapture
{
    private readonly object _gate = new();
    private PreparedLabel? _last;
    public PreparedLabel? Last { get { lock (_gate) return _last?.Copy(); } }
    public void Set(PreparedLabel label) { lock (_gate) _last = label.Copy(); }
}
