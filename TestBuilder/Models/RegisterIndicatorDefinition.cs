using System;
using System.Linq;
using System.Text.Json.Serialization;
using TestBuilder.Domain.Modbus;

namespace TestBuilder.Models;

public sealed class RegisterIndicatorDefinition
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("name")] public string Name { get; set; } = "Регистр";
    [JsonPropertyName("slaveId")] public byte SlaveId { get; set; } = 1;
    [JsonPropertyName("address")] public int Address { get; set; }
    [JsonPropertyName("area")] public ModbusArea Area { get; set; }
    [JsonPropertyName("divisor")] public decimal Divisor { get; set; } = 1;
    [JsonPropertyName("decimalPlaces")] public int DecimalPlaces { get; set; }
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
    [JsonPropertyName("minimum")] public decimal? Minimum { get; set; }
    [JsonPropertyName("maximum")] public decimal? Maximum { get; set; }
    [JsonPropertyName("color")] public string Color { get; set; } = ProfileActionDefinition.SystemColor;

    public RegisterIndicatorDefinition Copy() => (RegisterIndicatorDefinition)MemberwiseClone();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100)
            throw new InvalidOperationException("Некорректный ID индикатора.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100 || Name.Any(char.IsControl))
            throw new InvalidOperationException("Введите название индикатора до 100 символов.");
        if (SlaveId is < 1 or > 247 || Address is < 0 or > 65535 || !Enum.IsDefined(Area))
            throw new InvalidOperationException("Выберите slave 1..247 и регистр 0..65535.");
        if (Divisor is < 0.000001m or > 1000000000m || DecimalPlaces is < 0 or > 6)
            throw new InvalidOperationException("Делитель: 0,000001..1000000000. Знаков после запятой: 0..6.");
        if (Unit == null || Unit.Length > 24 || Unit.Any(char.IsControl))
            throw new InvalidOperationException("Единица измерения: до 24 символов.");
        if (Minimum.HasValue && Maximum.HasValue && Minimum > Maximum)
            throw new InvalidOperationException("Минимум допуска не может быть больше максимума.");
        if (!ProfileActionDefinition.IsSystemColor(Color) && !Avalonia.Media.Color.TryParse(Color, out _))
            throw new InvalidOperationException("Выберите цвет индикатора.");
    }
}
