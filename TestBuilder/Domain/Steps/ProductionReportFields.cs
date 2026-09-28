using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Domain.Steps;

// Device information selected by Qt TestThread::make_report. Runtime diagnostics
// remain in TestContext/logs; they are not inferred to be manufacturing checks.
internal static class ProductionReportFields
{
    internal static IEnumerable<TestReportEntry> GetEntries(TestContext context)
    {
        var prefix = context.GetVariable<string>("SelfTest.OutputPrefix");
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = new[] { "DutFinal", "DutAfterMac", "Dut" }.FirstOrDefault(candidate =>
                context.Variables.Keys.Any(key => key.StartsWith(candidate + ".", StringComparison.Ordinal)));
        if (prefix == null) yield break;
        // Do not silently substitute an old snapshot when the latest read failed.
        if (context.Variables.TryGetValue("SelfTest.Parsed", out var parsed) && parsed is false)
        {
            yield return new TestReportEntry("Загрузка тестовой страницы", false, "Нет свежих данных устройства");
            yield break;
        }
        var fields = context.GetVariable<Dictionary<string, string>>("SelfTest.ReportFields")
            ?? context.Variables.Where(item => item.Key.StartsWith(prefix + ".", StringComparison.Ordinal))
            .ToDictionary(item => item.Key[(prefix.Length + 1)..], item => Text(item.Value), StringComparer.OrdinalIgnoreCase);
        if (context.GetVariable<int>("SelfTest.CheckedRuleCount") > 0)
            yield return new TestReportEntry("Проверки тестовой страницы",
                context.GetVariable<int>("SelfTest.FailedRuleCount") == 0,
                context.GetVariable<string>("SelfTest.ValidationSummary") ?? string.Empty);
        var names = new Dictionary<string, string>
        {
            ["cpu_id"] = "cpu_id", ["firmvare_vers"] = "Версия прошивки",
            ["boot_vers"] = "Версия бутлоадера", ["default_mac"] = "MAC адрес",
            ["board_version"] = "версия платы", ["poe_controller"] = "ID микросхемы PoE контроллера",
            ["marvell_id"] = "ID микросхемы Switch контроллера"
        };
        foreach (var field in names)
            if (fields.TryGetValue(field.Key, out var value))
                yield return new TestReportEntry(field.Value, true, value);
        if (fields.TryGetValue("init_ok", out var init))
            yield return new TestReportEntry("самотестирование", init == "1", init);
        if (fields.TryGetValue("ups_det", out var ups) && ups == "1" &&
            fields.TryGetValue("akb_voltage", out var voltage))
            yield return new TestReportEntry("напряжение АКБ", true, voltage);

        // Canonical aliases use zero-based port indexes, while the XML names
        // link_1/poe_a_1_state are one-based. Emit each measurement once.
        foreach (var link in Indexed(fields, "link", @"^link_(\d+)$", subtractOne: true))
            yield return new TestReportEntry($"link_{link.Key}", true, link.Value);
        foreach (var state in Indexed(fields, "poe_a_st", @"^poe_a_(\d+)_(?:state|st)$", subtractOne: true))
        {
            if (!double.TryParse(state.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var on) || on == 0)
                continue;
            yield return new TestReportEntry($"poe_a_st_{state.Key}", true, state.Value);
            foreach (var suffix in new[] { "v", "c" })
            {
                var values = Indexed(fields, "poe_a_" + suffix, $@"^poe_a_(\d+)_{suffix}$", subtractOne: true);
                if (values.TryGetValue(state.Key, out var value))
                    yield return new TestReportEntry($"poe_a_{suffix}_{state.Key}", true, value);
            }
        }
        // The current selftest parser preserves the XML's physical SFP number.
        foreach (var sfp in Indexed(fields, "sfp_pres", @"^sfp_(\d+)_pres$", subtractOne: false))
            if (sfp.Value == "1")
                yield return new TestReportEntry($"присутствие SFP{sfp.Key}", true, sfp.Value);
    }

    private static SortedDictionary<int, string> Indexed(
        Dictionary<string, string> fields, string canonical, string xmlPattern, bool subtractOne)
    {
        var result = new SortedDictionary<int, string>();
        foreach (var field in fields)
        {
            var match = Regex.Match(field.Key, "^" + canonical + @"\[(\d+)\]$", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var index))
                result[index] = field.Value;
        }
        foreach (var field in fields)
        {
            var match = Regex.Match(field.Key, xmlPattern, RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var index))
                result[index - (subtractOne ? 1 : 0)] = field.Value;
        }
        return result;
    }

    private static string Text(object value) => value is IFormattable formatted
        ? formatted.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty
        : value?.ToString() ?? string.Empty;
}
