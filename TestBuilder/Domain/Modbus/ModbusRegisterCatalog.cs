using System;
using System.Collections.Frozen;
using System.Collections.Generic;
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

    private readonly FrozenDictionary<RegisterKey, string> _names;

    public ModbusRegisterCatalog(IEnumerable<SlaveModelBase> slaves)
    {
        var names = new Dictionary<RegisterKey, string>();
        foreach (var slave in slaves)
        foreach (var register in slave.RegisterItems)
            if (!string.IsNullOrWhiteSpace(register.Name))
                names[new RegisterKey(slave.SlaveId, register.Address)] = register.Name;
        _names = names.ToFrozenDictionary();
    }

    public string FormatAddress(byte? slaveId, int address)
    {
        var name = slaveId.HasValue && _names.TryGetValue(new RegisterKey(slaveId.Value, address), out var found)
            ? found : "описание неизвестно";
        return $"{address} ({name})";
    }
}
