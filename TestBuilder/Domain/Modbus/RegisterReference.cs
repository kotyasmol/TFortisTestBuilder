using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Modbus;

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
                .SelectMany(d => d.Registers.Select(r => new Entry(d.Name, r.Address, r.Name, !r.Writable, r.Area)));
        if (connectedSlave != null)
            return connectedSlave.RegisterItems.Select(r => new Entry(connectedSlave.DeviceType, r.Address, r.Name, r.IsReadOnly, r.Area));
        return Entries.Value;
    }

    private static string Format(string model, string name, bool readOnly) =>
        $"{model}: {name} ({(readOnly ? "RO — чтение" : "RW — чтение и запись")})";

    private static IReadOnlyList<Entry> CreateEntries()
    {
        // Model constructors only build their register maps. No port is opened.
        using var modbus = new ModbusService();
        SlaveModelBase[] models =
        {
            new El60Model(0, modbus), new PS1Model(0, modbus),
            new PS2Model(0, modbus), new El60v5Model(0, modbus),
            new IO2Model(0, modbus), new StandRpsModel(0, modbus),
            new StandPwr180Model(0, modbus), new Ps3Model(0, modbus),
            new Simbat24Model(0, modbus), new Simbat48Model(0, modbus)
        };
        return models.SelectMany(model => model.RegisterItems.Select(register => new Entry(
            model is Simbat24Model ? "SIMBAT 24" : model is Simbat48Model ? "SIMBAT 48" : model.DeviceType,
            register.Address, register.Name, register.IsReadOnly, register.Area))).ToArray();
    }
}
