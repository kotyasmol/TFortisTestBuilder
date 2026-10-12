using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus;

/// <summary>Offline documentation only; never discovers devices or supplies runtime values.</summary>
public static class RegisterReference
{
    private sealed record Entry(string Model, int Address, string Name, bool IsReadOnly);
    private static readonly Lazy<IReadOnlyList<Entry>> Entries = new(CreateEntries);

    public static string Describe(ushort address, SlaveModelBase? connectedSlave = null)
    {
        if (connectedSlave != null)
        {
            var register = connectedSlave.RegisterItems.FirstOrDefault(r => r.Address == address);
            return register == null
                ? $"{connectedSlave.DeviceType}: адрес {address} отсутствует в карте регистров."
                : Format(connectedSlave.DeviceType, register.Name, register.IsReadOnly);
        }

        var matches = Entries.Value.Where(r => r.Address == address).ToArray();
        return matches.Length == 0
            ? $"Адрес {address}: описание отсутствует в справочнике."
            : "Справочник по адресу (модель платы не определена):\n" +
              string.Join("\n", matches.Select(r => Format(r.Model, r.Name, r.IsReadOnly)));
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
            register.Address, register.Name, register.IsReadOnly))).ToArray();
    }
}
