using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using TestBuilder.Domain.Modbus;
namespace TestBuilder.Domain.Monitoring;

public class RegisterState
{
    private readonly ConcurrentDictionary<RegisterKey, Sample> _values = new();
    private sealed record Sample(int Value, long StartedAt);
    public void Update(byte slaveId, int address, int value, ModbusArea area = ModbusArea.Holding, long? startedAt = null)
        => _values[new RegisterKey(slaveId, address, area)] = new Sample(value, startedAt ?? Stopwatch.GetTimestamp());
    public bool TryGet(byte slaveId, int address, out int value, ModbusArea area = ModbusArea.Holding, long notBefore = 0)
    {
        value = 0;
        if (!_values.TryGetValue(new RegisterKey(slaveId, address, area), out var sample) || sample.StartedAt < notBefore) return false;
        value = sample.Value;
        return true;
    }
    public void Invalidate(byte slaveId)
    {
        foreach (var key in _values.Keys) if (key.SlaveId == slaveId) _values.TryRemove(key, out _);
    }
    public void Clear() => _values.Clear();
}
