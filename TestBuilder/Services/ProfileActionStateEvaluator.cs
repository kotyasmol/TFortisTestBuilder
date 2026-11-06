using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Models;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Services;

public sealed record ProfileActionState(bool? IsOn, string Reason, string Readings = "");

public static class ProfileActionStateEvaluator
{
    public static ProfileActionState Evaluate(ProfileActionStateBinding binding, RegisterState state,
        HardwareConfiguration? hardware, bool connected, bool monitoring, long notBefore = 0, long? now = null)
    {
        if (!connected) return new(null, "Нет связи");
        if (!monitoring) return new(null, "Опрос остановлен");
        var age = Math.Max(10000L, (hardware?.PollIntervalMs ?? 1000) * 3L);
        var cutoff = Math.Max(notBefore, (now ?? Stopwatch.GetTimestamp()) - (long)(age / 1000d * Stopwatch.Frequency));
        // Capture each referenced register once so on/off see the same sample. All sources must be fresh,
        // including the unused side of an Any condition: missing feedback never implies a known state.
        var values = new Dictionary<RegisterKey, int>();
        foreach (var source in Sources(binding))
        {
            if (!source.IsMapped(hardware)) return MissingRegister(source);
            if (!state.TryGet(source.SlaveId, source.Address, out var raw, source.Area, cutoff))
                return new(null, state.TryGet(source.SlaveId, source.Address, out _, source.Area) ? "Ожидание свежих данных" : "Нет данных");
            values.Add(Key(source), raw);
        }
        return MatchRules(binding, values);
    }

    internal static async Task<ProfileActionState> ReadAsync(ProfileActionStateBinding binding,
        IModbusService modbus, HardwareConfiguration? hardware, CancellationToken token)
    {
        var values = new Dictionary<RegisterKey, int>();
        foreach (var source in Sources(binding))
        {
            token.ThrowIfCancellationRequested();
            if (!source.IsMapped(hardware)) return MissingRegister(source);
            var raw = await modbus.ReadAreaAsync(source.SlaveId, (ushort)source.Address, 1, source.Area, token);
            if (raw.Length != 1) return new(null, $"Нет данных для регистра {source.SlaveId}/{source.Area}/{source.Address}");
            values.Add(Key(source), raw[0]);
        }
        return MatchRules(binding, values);
    }

    private static RegisterKey Key(RegisterStateCondition condition) => new(condition.SlaveId, condition.Address, condition.Area);
    private static IEnumerable<RegisterStateCondition> Sources(ProfileActionStateBinding binding) =>
        binding.On.Conditions.Concat(binding.Off.Conditions).DistinctBy(Key);
    private static ProfileActionState MissingRegister(RegisterStateCondition source) =>
        new(null, $"Регистр {source.SlaveId}/{source.Area}/{source.Address} отсутствует в оборудовании профиля");

    private static ProfileActionState MatchRules(ProfileActionStateBinding binding, Dictionary<RegisterKey, int> values)
    {
        bool Match(RegisterStateRule rule)
        {
            bool Test(RegisterStateCondition c) => c.Matches(values[Key(c)]);
            return rule.Match == StateRuleMatch.All ? rule.Conditions.All(Test) : rule.Conditions.Any(Test);
        }
        var on = Match(binding.On); var off = Match(binding.Off);
        var readings = string.Join(Environment.NewLine, values.Select(pair =>
            $"{pair.Key.SlaveId}/{pair.Key.Area}/{pair.Key.Address} = {pair.Value}"));
        return on == off
            ? new(null, on ? "Оба правила состояния выполнены" : "Ни одно правило состояния не выполнено", readings)
            : new(on, "", readings);
    }
}
