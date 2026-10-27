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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ushort>? IdentificationValues { get; set; }
    [JsonIgnore]
    public IReadOnlyList<ushort> ExpectedIdentificationValues => IdentificationValues ??
        (IdentificationValue is { } value ? new List<ushort> { value } : new List<ushort>());
    [JsonIgnore]
    public string IdentificationValuesText
    {
        get => string.Join(", ", ExpectedIdentificationValues);
        set
        {
            var values = new List<ushort>();
            if (!string.IsNullOrWhiteSpace(value))
                foreach (var part in value.Split(','))
                {
                    if (!ushort.TryParse(part.Trim(), out var number))
                        throw new FormatException("Укажите значения 0..65535 через запятую, например 9, 10.");
                    if (!values.Contains(number)) values.Add(number);
                }
            IdentificationValue = values.Count == 1 ? values[0] : null;
            IdentificationValues = values.Count > 1 ? values : null;
        }
    }
    public ushort IdentificationAddress { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ushort? StandSerialNumberAddress { get; set; }
    public List<RegisterDefinition> Registers { get; set; } = new();
    public override string ToString() => $"{SlaveId} — {Name}";
}
public sealed class HardwareConfiguration
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StandName { get; set; }
    // Retained for JSON compatibility. Stand discovery uses device identities, not this port name.
    public string? PortName { get; set; }
    public List<ConsoleDeviceDefinition> ConsoleDevices { get; set; } = new();
    public int BaudRate { get; set; } = 9600;
    public Parity Parity { get; set; } = Parity.None;
    public int DataBits { get; set; } = 8;
    public StopBits StopBits { get; set; } = StopBits.One;
    public int PollIntervalMs { get; set; } = 1000;
    public List<DeviceDefinition> Devices { get; set; } = new();
    public void Validate()
    {
        if (ConsoleDevices == null || ConsoleDevices.Count > 64 || ConsoleDevices.Any(d => d == null) ||
            ConsoleDevices.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != ConsoleDevices.Count)
            throw new InvalidOperationException("Нужны уникальные коды консольных устройств (не более 64).");
        foreach (var console in ConsoleDevices) console.Validate();
        var ports = ConsoleDevices.Select(d => d.PortName?.Trim()).Append(PortName?.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (ports.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ports.Length)
            throw new InvalidOperationException("Modbus и консольные устройства должны использовать разные COM-порты.");
        if (BaudRate is < 300 or > 1000000 || DataBits is < 5 or > 8 ||
            !Enum.IsDefined(Parity) || !Enum.IsDefined(StopBits) || StopBits == StopBits.None ||
            PollIntervalMs is < 100 or > 60000)
            throw new InvalidOperationException("Некорректные параметры Modbus RTU (скорость, чётность, биты, период 100..60000 мс).");
        if (Devices == null || Devices.Count is < 1 or > 247 || Devices.Any(d => d == null) ||
            Devices.Select(d => d.SlaveId).Distinct().Count() != Devices.Count)
            throw new InvalidOperationException("Нужны устройства с уникальными адресами slave 1..247.");
        foreach (var d in Devices)
        {
            if (d.IdentificationValues != null && (d.IdentificationValue.HasValue || d.IdentificationValues.Count == 0))
                throw new InvalidOperationException($"Устройство {d.Name}: задайте одно значение типа либо непустой список допустимых значений.");
            if (d.SlaveId is < 1 or > 247 || string.IsNullOrWhiteSpace(d.Name) ||
                d.Registers == null || d.Registers.Count is < 1 or > 4096 || d.Registers.Any(x => x == null))
                throw new InvalidOperationException($"Устройство {d.Name}: нужны имя, slave 1..247 и 1..4096 регистров.");
            if (d.Registers.Select(x => (x.Area, x.Address)).Distinct().Count() != d.Registers.Count)
                throw new InvalidOperationException($"Устройство {d.Name}: повтор адреса в одной области Modbus.");
            if (d.StandSerialNumberAddress is { } serial && !d.Registers.Any(r => r.Address == serial && r.Area == ModbusArea.Holding))
                throw new InvalidOperationException($"Устройство {d.Name}: добавьте регистр серийного номера стенда {serial} в область Holding.");
            foreach (var reg in d.Registers)
                if (reg.Address is < 0 or > 65535 || !Enum.IsDefined(reg.Area) || string.IsNullOrWhiteSpace(reg.Name) ||
                    (reg.Writable && reg.Area is ModbusArea.Input or ModbusArea.Discrete))
                    throw new InvalidOperationException($"Устройство {d.Name}: некорректный регистр {reg.Address}.");
        }
        if (Devices.Count(d => d.StandSerialNumberAddress.HasValue) > 1)
            throw new InvalidOperationException("Укажите источник серийного номера стенда только у одного устройства.");
    }
}
