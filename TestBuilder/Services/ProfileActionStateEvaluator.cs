using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Models;

namespace TestBuilder.Services;

public sealed record ProfileActionState(bool? IsOn, string Reason);

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
        foreach (var c in binding.On.Conditions.Concat(binding.Off.Conditions))
        {
            if (!c.IsMapped(hardware)) return new(null, $"Регистр {c.SlaveId}/{c.Area}/{c.Address} отсутствует в оборудовании профиля");
            var key = new RegisterKey(c.SlaveId, c.Address, c.Area);
            if (values.ContainsKey(key)) continue;
            if (!state.TryGet(c.SlaveId, c.Address, out var raw, c.Area, cutoff))
                return new(null, state.TryGet(c.SlaveId, c.Address, out _, c.Area) ? "Ожидание свежих данных" : "Нет данных");
            values.Add(key, raw);
        }
        bool Match(RegisterStateRule rule)
        {
            bool Test(RegisterStateCondition c) => c.Matches(values[new RegisterKey(c.SlaveId, c.Address, c.Area)]);
            return rule.Match == StateRuleMatch.All ? rule.Conditions.All(Test) : rule.Conditions.Any(Test);
        }
        var on = Match(binding.On); var off = Match(binding.Off);
        return on == off ? new(null, on ? "Оба правила состояния выполнены" : "Ни одно правило состояния не выполнено") : new(on, "");
    }
}
