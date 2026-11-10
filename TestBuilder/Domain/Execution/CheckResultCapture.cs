using System;
using System.Globalization;

namespace TestBuilder.Domain.Execution;

/// <summary>Stores the outcome under a configured name, independent of the shared LastCheck diagnostics.</summary>
internal static class CheckResultCapture
{
    internal static string ResolveName(string? name, byte? slaveId)
    {
        var result = name?.Trim() ?? string.Empty;
        if (!result.Contains("{slaveId}", StringComparison.Ordinal)) return result;
        return slaveId.HasValue
            ? result.Replace("{slaveId}", slaveId.Value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            : string.Empty;
    }

    internal static void Clear(TestContext context, string name)
    {
        if (name.Length == 0) return;
        context.Variables.Remove(name + ".Passed");
        context.Variables.Remove(name + ".Value");
    }

    internal static void Save(TestContext context, string name, bool passed, object? value)
    {
        if (name.Length == 0) return;
        context.SetVariable(name + ".Passed", passed);
        if (value != null) context.SetVariable(name + ".Value", value);
    }
}
