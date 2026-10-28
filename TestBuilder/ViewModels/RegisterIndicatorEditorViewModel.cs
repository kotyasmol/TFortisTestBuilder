using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;

namespace TestBuilder.ViewModels;

public sealed record IndicatorRegisterChoice(RegisterDefinition Register)
{
    public override string ToString() => $"{Register.Area} {Register.Address} — {Register.Name}";
}
public sealed record IndicatorColorChoice(string Name, string Value)
{
    public override string ToString() => Name;
}

public partial class RegisterIndicatorEditorViewModel : ObservableObject
{
    private readonly string _id;
    private bool _loading = true;
    private IndicatorRegisterChoice? _lastSelectedRegister;
    public IReadOnlyList<DeviceDefinition> Devices { get; }
    public ObservableCollection<IndicatorRegisterChoice> Registers { get; } = new();
    public bool HasEquipment => Devices.Count > 0;
    public IReadOnlyList<IndicatorColorChoice> Colors { get; } = new[] {
        new IndicatorColorChoice("Системный", ProfileActionDefinition.SystemColor),
        new IndicatorColorChoice("Синий", "#2563EB"), new IndicatorColorChoice("Бирюзовый", "#0F766E"),
        new IndicatorColorChoice("Зелёный", "#15803D"), new IndicatorColorChoice("Жёлтый", "#EAB308"),
        new IndicatorColorChoice("Оранжевый", "#EA580C"), new IndicatorColorChoice("Красный", "#B91C1C"),
        new IndicatorColorChoice("Фиолетовый", "#7C3AED") };
    [ObservableProperty] private DeviceDefinition? selectedDevice;
    [ObservableProperty] private IndicatorRegisterChoice? selectedRegister;
    [ObservableProperty] private string name = "Регистр";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Preview))] private string unit = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Preview))] private decimal? divisor = 1;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Preview))] private decimal? decimalPlaces = 0;
    [ObservableProperty] private decimal? minimum;
    [ObservableProperty] private decimal? maximum;
    [ObservableProperty] private string color = "Системный";
    [ObservableProperty] private IndicatorColorChoice? selectedColor;
    [ObservableProperty] private string error = "";

    public string Preview => Divisor is >= 0.000001m and <= 1000000000m && DecimalPlaces is >= 0 and <= 6
        ? "1234 → " + RegisterIndicatorViewModel.FormatValue(1234, Divisor.Value, (int)DecimalPlaces.Value, Unit) : "";
    private string ColorValue => string.Equals(Color?.Trim(), "Системный", StringComparison.OrdinalIgnoreCase)
        ? ProfileActionDefinition.SystemColor : Color?.Trim() ?? "";

    public RegisterIndicatorEditorViewModel(RegisterIndicatorDefinition source, HardwareConfiguration? hardware)
    {
        _id = source.Id;
        Devices = hardware?.Devices.OrderBy(d => d.SlaveId).ToArray() ?? Array.Empty<DeviceDefinition>();
        SelectedDevice = Devices.FirstOrDefault(d => d.SlaveId == source.SlaveId);
        SelectedRegister = Registers.FirstOrDefault(r => r.Register.Address == source.Address && r.Register.Area == source.Area);
        Name = source.Name; Unit = source.Unit; Divisor = source.Divisor; DecimalPlaces = source.DecimalPlaces;
        Minimum = source.Minimum; Maximum = source.Maximum;
        Color = ProfileActionDefinition.IsSystemColor(source.Color) ? "Системный" : source.Color;
        OnColorChanged(Color);
        _loading = false;
        if (SelectedRegister != null && Name == "Регистр") OnSelectedRegisterChanged(null, SelectedRegister);
    }

    partial void OnSelectedDeviceChanged(DeviceDefinition? value)
    {
        SelectedRegister = null;
        Registers.Clear();
        if (value != null)
            foreach (var register in value.Registers.OrderBy(r => r.Area).ThenBy(r => r.Address))
                Registers.Add(new(register));
    }
    partial void OnSelectedRegisterChanged(IndicatorRegisterChoice? oldValue, IndicatorRegisterChoice? newValue)
    {
        if (newValue == null) return;
        var previous = oldValue ?? _lastSelectedRegister;
        _lastSelectedRegister = newValue;
        if (_loading) return;
        if (string.IsNullOrWhiteSpace(Name) || Name == "Регистр" || Name == previous?.Register.Name) Name = newValue.Register.Name;
        if (string.IsNullOrWhiteSpace(Unit) || Unit == previous?.Register.Unit) Unit = newValue.Register.Unit;
    }
    partial void OnDivisorChanged(decimal? value)
    {
        if (_loading || value == null) return;
        decimal factor = 1;
        for (var places = 0; places <= 6; places++, factor *= 10)
            if (value == factor) { DecimalPlaces = places; break; }
    }
    partial void OnSelectedColorChanged(IndicatorColorChoice? value)
    { if (value != null) Color = ProfileActionDefinition.IsSystemColor(value.Value) ? "Системный" : value.Value; }
    partial void OnColorChanged(string value)
    {
        var normalized = ColorValue;
        SelectedColor = Colors.FirstOrDefault(c => string.Equals(c.Value, normalized, StringComparison.OrdinalIgnoreCase) ||
            (Avalonia.Media.Color.TryParse(c.Value, out var a) && Avalonia.Media.Color.TryParse(normalized, out var b) && a == b));
    }

    internal RegisterIndicatorDefinition BuildResult()
    {
        if (SelectedDevice == null || SelectedRegister == null || !Registers.Contains(SelectedRegister))
            throw new InvalidOperationException("Выберите устройство и регистр из оборудования профиля.");
        if (Divisor == null || DecimalPlaces == null || DecimalPlaces != decimal.Truncate(DecimalPlaces.Value))
            throw new InvalidOperationException("Укажите делитель и целое число знаков после запятой.");
        var result = new RegisterIndicatorDefinition {
            Id = _id, Name = Name?.Trim() ?? "", SlaveId = SelectedDevice.SlaveId,
            Address = SelectedRegister.Register.Address, Area = SelectedRegister.Register.Area,
            Divisor = Divisor.Value, DecimalPlaces = (int)DecimalPlaces.Value, Unit = Unit?.Trim() ?? "",
            Minimum = Minimum, Maximum = Maximum, Color = ColorValue
        };
        result.Validate();
        return result;
    }
}
