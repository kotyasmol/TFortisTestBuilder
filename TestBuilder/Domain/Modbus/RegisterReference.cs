using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Modbus.Models;

namespace TestBuilder.Domain.Modbus;

/// <summary>Offline documentation only; never discovers devices or supplies runtime values.</summary>
public static class RegisterReference
{
    private sealed record Entry(string Model, int Address, string Name, bool IsReadOnly, ModbusArea Area);
    private static readonly Lazy<IReadOnlyList<Entry>> Entries = new(CreateEntries);

    public static string Describe(ushort address, SlaveModelBase? connectedSlave = null,
        ModbusArea area = ModbusArea.Holding, HardwareConfiguration? hardware = null, byte? slaveId = null)
    {
        var matches = Resolve(connectedSlave, hardware, slaveId).Where(r => r.Address == address && r.Area == area).ToArray();
        return matches.Length == 0
            ? $"Адрес {address} ({area}): описание отсутствует в справочнике выбранного оборудования."
            : (connectedSlave == null && hardware == null ? "Справочник по адресу (модель платы не определена):\n" : "") +
              string.Join("\n", matches.Select(r => Format(r.Model, r.Name, r.IsReadOnly)));
    }

    public static IEnumerable<RegisterItem> Choices(ModbusArea area, bool writableOnly,
        SlaveModelBase? connectedSlave = null, HardwareConfiguration? hardware = null, byte? slaveId = null)
    {
        // Preserve connected selection identity; offline entries contain metadata only.
        if (hardware == null && connectedSlave != null)
            return connectedSlave.RegisterItems.Where(r => r.Area == area && (!writableOnly || !r.IsReadOnly));
        return Resolve(connectedSlave, hardware, slaveId)
            .Where(r => r.Area == area && (!writableOnly || !r.IsReadOnly))
            .OrderBy(r => r.Address).ThenBy(r => r.Model)
            .Select(r => new RegisterItem { Address = r.Address, Area = r.Area,
                Name = $"{r.Model}: {r.Name}", IsReadOnly = r.IsReadOnly });
    }

    private static IEnumerable<Entry> Resolve(SlaveModelBase? connectedSlave, HardwareConfiguration? hardware, byte? slaveId)
    {
        if (hardware != null)
            return hardware.Devices.Where(d => !slaveId.HasValue || d.SlaveId == slaveId.Value)
                .SelectMany(d => d.Registers.Select(r => new Entry(d.Name, r.Address, NameWithUnit(r), !r.Writable, r.Area)));
        if (connectedSlave != null)
            return connectedSlave.RegisterItems.Select(r => new Entry(connectedSlave.DeviceType, r.Address, r.Name, r.IsReadOnly, r.Area));
        return Entries.Value;
    }

    private static string Format(string model, string name, bool readOnly) =>
        $"{model}: {name} ({(readOnly ? "RO — чтение" : "RW — чтение и запись")})";

    private static IReadOnlyList<Entry> CreateEntries()
    {
        // The editor, discovery and offline help use the same supplied maps.
        // Historical C# models retain their old maps for compatibility only.
        return HardwarePresets.Load().SelectMany(device => device.Registers.Select(register => new Entry(
            device.Name, register.Address, NameWithUnit(register), !register.Writable, register.Area))).ToArray();
    }

    private static string NameWithUnit(RegisterDefinition register) => register.Name +
        (string.IsNullOrWhiteSpace(register.Unit) ? "" : ", " + register.Unit);
}
