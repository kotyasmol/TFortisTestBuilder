using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text.Json.Serialization;

namespace TestBuilder.Domain.Modbus;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ModbusArea { Holding, Input, Coil, Discrete }

public sealed class RegisterDefinition
{
    public int Address { get; set; }
    public string Name { get; set; } = "Регистр";
    public ModbusArea Area { get; set; }
    public bool Writable { get; set; }
    public string Unit { get; set; } = "";
    public string Category { get; set; } = "";
}
public sealed class DeviceDefinition
{
    public string Name { get; set; } = "Новое устройство";
    public byte SlaveId { get; set; } = 1;
    public ushort? IdentificationValue { get; set; }
    public ushort IdentificationAddress { get; set; }
    public List<RegisterDefinition> Registers { get; set; } = new();
    public override string ToString() => $"{SlaveId} — {Name}";
}
public sealed class HardwareConfiguration
{
    public int BaudRate { get; set; } = 9600;
    public Parity Parity { get; set; } = Parity.None;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public int PollIntervalMs { get; set; } = 1000;
    public List<DeviceDefinition> Devices { get; set; } = new();
    public void Validate()
    {
        if (BaudRate is < 300 or > 1000000 || DataBits is < 5 or > 8 ||
            !Enum.IsDefined(Parity) || !Enum.IsDefined(StopBits) || StopBits == StopBits.None ||
            PollIntervalMs is < 100 or > 60000)
            throw new InvalidOperationException("Некорректные параметры Modbus RTU (скорость, чётность, биты, период 100..60000 мс).");
        if (Devices == null || Devices.Count is < 1 or > 247 || Devices.Any(d => d == null) ||
            Devices.Select(d => d.SlaveId).Distinct().Count() != Devices.Count)
            throw new InvalidOperationException("Нужны устройства с уникальными адресами slave 1..247.");
        foreach (var d in Devices)
        {
            if (d.SlaveId is < 1 or > 247 || string.IsNullOrWhiteSpace(d.Name) ||
                d.Registers == null || d.Registers.Count is < 1 or > 4096 || d.Registers.Any(x => x == null))
                throw new InvalidOperationException($"Устройство {d.Name}: нужны имя, slave 1..247 и 1..4096 регистров.");
            if (d.Registers.Select(x => (x.Area, x.Address)).Distinct().Count() != d.Registers.Count)
                throw new InvalidOperationException($"Устройство {d.Name}: повтор адреса в одной области Modbus.");
            foreach (var reg in d.Registers)
                if (reg.Address is < 0 or > 65535 || !Enum.IsDefined(reg.Area) || string.IsNullOrWhiteSpace(reg.Name) ||
                    (reg.Writable && reg.Area is ModbusArea.Input or ModbusArea.Discrete))
                    throw new InvalidOperationException($"Устройство {d.Name}: некорректный регистр {reg.Address}.");
        }
    }
}
