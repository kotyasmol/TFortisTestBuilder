using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Monitoring;

namespace TestBuilder.Domain.Modbus;

/// <summary>
/// Immutable register names from the devices discovered for this run.
/// A register address alone is not enough: PS-2 and PS-3 share addresses.
/// </summary>
public sealed class ModbusRegisterCatalog
{
    public static ModbusRegisterCatalog Empty { get; } = new(Array.Empty<SlaveModelBase>());

    private readonly FrozenDictionary<RegisterKey, (string Name, bool IsReadOnly)> _registers;

    public ModbusRegisterCatalog(IEnumerable<SlaveModelBase> slaves)
    {
        var registers = new Dictionary<RegisterKey, (string Name, bool IsReadOnly)>();
        foreach (var slave in slaves)
        foreach (var register in slave.RegisterItems)
            registers[new RegisterKey(slave.SlaveId, register.Address, register.Area)] = (register.Name, register.IsReadOnly);
        _registers = registers.ToFrozenDictionary();
    }

    public IEnumerable<byte> SlaveIds => _registers.Keys.Select(k => k.SlaveId).Distinct();

    public bool IsReadOnly(byte slaveId, int address, ModbusArea area) =>
        _registers.TryGetValue(new RegisterKey(slaveId, address, area), out var register) && register.IsReadOnly;

    public string FormatAddress(byte? slaveId, int address, ModbusArea area = ModbusArea.Holding)
    {
        var name = slaveId.HasValue && _registers.TryGetValue(new RegisterKey(slaveId.Value, address, area), out var found)
            && !string.IsNullOrWhiteSpace(found.Name) ? found.Name : "описание неизвестно";
        return $"{address} ({name})";
    }
}
