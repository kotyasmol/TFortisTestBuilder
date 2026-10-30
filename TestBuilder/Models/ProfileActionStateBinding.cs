using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using TestBuilder.Domain.Modbus;

namespace TestBuilder.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StateRuleMatch { All, Any }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RegisterComparison { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual }

public sealed class RegisterStateCondition
{
    [JsonPropertyName("slaveId")] public byte SlaveId { get; set; } = 1;
    [JsonPropertyName("area")] public ModbusArea Area { get; set; }
    [JsonPropertyName("address")] public int Address { get; set; }
    [JsonPropertyName("mask")] public int? Mask { get; set; }
    [JsonPropertyName("comparison")] public RegisterComparison Comparison { get; set; }
    [JsonPropertyName("value")] public int Value { get; set; }
    public RegisterStateCondition Copy() => (RegisterStateCondition)MemberwiseClone();
    public bool IsMapped(HardwareConfiguration? hardware) => hardware?.Devices.Any(d => d.SlaveId == SlaveId &&
        d.Registers.Any(r => r.Area == Area && r.Address == Address)) == true;
    public void Validate()
    {
        if (SlaveId is < 1 or > 247 || Address is < 0 or > 65535 || !Enum.IsDefined(Area) ||
            !Enum.IsDefined(Comparison) || Value is < 0 or > 65535 || Mask is < 1 or > 65535)
            throw new InvalidOperationException("Условие состояния: slave 1..247, адрес и значение 0..65535, маска 1..65535.");
    }
    public bool Matches(int raw)
    {
        var value = Mask.HasValue ? raw & Mask.Value : raw;
        return Comparison switch {
            RegisterComparison.Equal => value == Value, RegisterComparison.NotEqual => value != Value,
            RegisterComparison.Greater => value > Value, RegisterComparison.GreaterOrEqual => value >= Value,
            RegisterComparison.Less => value < Value, RegisterComparison.LessOrEqual => value <= Value,
            _ => false
        };
    }
}

public sealed class RegisterStateRule
{
    [JsonPropertyName("match")] public StateRuleMatch Match { get; set; }
    [JsonPropertyName("conditions")] public List<RegisterStateCondition> Conditions { get; set; } = new();
    public RegisterStateRule Copy() => new() { Match = Match, Conditions = Conditions.Select(c => c.Copy()).ToList() };
    public void Validate()
    {
        if (!Enum.IsDefined(Match) || Conditions == null || Conditions.Count is < 1 or > 64 || Conditions.Any(c => c == null))
            throw new InvalidOperationException("Для каждого состояния задайте от 1 до 64 условий и способ объединения.");
        foreach (var condition in Conditions) condition.Validate();
    }
}

public sealed class ProfileActionStateBinding
{
    [JsonPropertyName("onText")] public string OnText { get; set; } = "Включено";
    [JsonPropertyName("offText")] public string OffText { get; set; } = "Выключено";
    [JsonPropertyName("on")] public RegisterStateRule On { get; set; } = new();
    [JsonPropertyName("off")] public RegisterStateRule Off { get; set; } = new();
    public ProfileActionStateBinding Copy() => new() { On = On.Copy(), Off = Off.Copy(), OnText = OnText, OffText = OffText };
    public void Validate()
    {
        if (new[] { OnText, OffText }.Any(t => t == null || t.Length > 120 || t.Any(char.IsControl)))
            throw new InvalidOperationException("Подписи состояний: до 120 символов без переносов строк.");
        if (On == null || Off == null) throw new InvalidOperationException("Задайте правила включённого и выключенного состояния.");
        On.Validate(); Off.Validate();
    }
}
