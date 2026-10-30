using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;

namespace TestBuilder.ViewModels;

public sealed record RegisterComparisonChoice(RegisterComparison Value, string Label)
{
    public override string ToString() => Label;
}

public partial class RegisterStateConditionEditorViewModel : ObservableObject
{
    public IReadOnlyList<DeviceDefinition> Devices { get; }
    public ObservableCollection<IndicatorRegisterChoice> Registers { get; } = new();
    public IReadOnlyList<RegisterComparisonChoice> Comparisons { get; } = new[] {
        new RegisterComparisonChoice(RegisterComparison.Equal, "Равно"), new(RegisterComparison.NotEqual, "Не равно"),
        new(RegisterComparison.Greater, "Больше"), new(RegisterComparison.GreaterOrEqual, "Больше или равно"),
        new(RegisterComparison.Less, "Меньше"), new(RegisterComparison.LessOrEqual, "Меньше или равно") };
    [ObservableProperty] private DeviceDefinition? selectedDevice;
    [ObservableProperty] private IndicatorRegisterChoice? selectedRegister;
    [ObservableProperty] private RegisterComparisonChoice? selectedComparison;
    [ObservableProperty] private decimal? value;
    [ObservableProperty] private decimal? mask;
    public IRelayCommand RemoveCommand { get; }
    public RegisterStateConditionEditorViewModel(RegisterStateCondition source, HardwareConfiguration? hardware, Action remove)
    {
        Devices = hardware?.Devices.OrderBy(d => d.SlaveId).ToArray() ?? Array.Empty<DeviceDefinition>();
        SelectedDevice = Devices.FirstOrDefault(d => d.SlaveId == source.SlaveId);
        SelectedRegister = Registers.FirstOrDefault(r => r.Register.Address == source.Address && r.Register.Area == source.Area);
        SelectedComparison = Comparisons.FirstOrDefault(c => c.Value == source.Comparison);
        Value = source.Value; Mask = source.Mask;
        RemoveCommand = new RelayCommand(remove);
    }
    partial void OnSelectedDeviceChanged(DeviceDefinition? value)
    {
        SelectedRegister = null; Registers.Clear();
        if (value != null)
            foreach (var r in value.Registers.OrderBy(r => r.Area).ThenBy(r => r.Address)) Registers.Add(new(r));
    }
    public RegisterStateCondition BuildResult()
    {
        if (SelectedDevice == null || SelectedRegister == null || !Registers.Contains(SelectedRegister) || SelectedComparison == null)
            throw new InvalidOperationException("В каждом условии выберите устройство, регистр и сравнение.");
        if (Value == null || Value < 0 || Value > 65535 || Value != decimal.Truncate(Value.Value) ||
            (Mask.HasValue && (Mask < 1 || Mask > 65535 || Mask != decimal.Truncate(Mask.Value))))
            throw new InvalidOperationException("Значение: целое 0..65535. Маска: целое 1..65535 или пустое поле.");
        return new() { SlaveId = SelectedDevice.SlaveId, Area = SelectedRegister.Register.Area,
            Address = SelectedRegister.Register.Address, Comparison = SelectedComparison.Value,
            Value = (int)Value.Value, Mask = Mask.HasValue ? (int)Mask.Value : null };
    }
}

public partial class RegisterStateRuleEditorViewModel : ObservableObject
{
    private readonly HardwareConfiguration? _hardware;
    public string Title { get; }
    public ObservableCollection<RegisterStateConditionEditorViewModel> Conditions { get; } = new();
    public IReadOnlyList<string> MatchChoices { get; } = new[] { "Все условия", "Любое условие" };
    [ObservableProperty] private int matchIndex;
    public RegisterStateRuleEditorViewModel(string title, RegisterStateRule source, HardwareConfiguration? hardware)
    {
        Title = title; _hardware = hardware; MatchIndex = (int)source.Match;
        foreach (var c in source.Conditions) Add(c);
    }
    private void Add(RegisterStateCondition source)
    {
        RegisterStateConditionEditorViewModel? row = null;
        row = new(source, _hardware, () => Conditions.Remove(row!));
        Conditions.Add(row);
    }
    [RelayCommand] private void AddCondition() => Add(new());
    public RegisterStateRule BuildResult() => new() { Match = (StateRuleMatch)MatchIndex, Conditions = Conditions.Select(c => c.BuildResult()).ToList() };
}

public partial class ProfileActionStateEditorViewModel : ObservableObject
{
    [ObservableProperty] private bool useModbus;
    private readonly ProfileActionLabels _labels;
    public string OnText { get => _labels.On; set => _labels.On = value; }
    public string OffText { get => _labels.Off; set => _labels.Off = value; }
    public IReadOnlyList<RegisterStateRuleEditorViewModel> Rules { get; }
    public bool HasEquipment { get; }
    public ProfileActionStateEditorViewModel(ProfileActionStateBinding? source, HardwareConfiguration? hardware, ProfileActionLabels? labels = null)
    {
        _labels = labels ?? new() { On = source?.OnText ?? "Включено", Off = source?.OffText ?? "Выключено" };
        UseModbus = source != null; HasEquipment = hardware?.Devices.Count > 0;
        Rules = new[] {
            new RegisterStateRuleEditorViewModel("Включено", source?.On ?? new() { Conditions = new() { new() { Value = 1 } } }, hardware),
            new RegisterStateRuleEditorViewModel("Выключено", source?.Off ?? new() { Conditions = new() { new() { Value = 0 } } }, hardware)
        };
    }
    public ProfileActionStateBinding? BuildResult()
    {
        if (!UseModbus) return null;
        var result = new ProfileActionStateBinding { On = Rules[0].BuildResult(), Off = Rules[1].BuildResult(), OnText = OnText.Trim(), OffText = OffText.Trim() };
        result.Validate(); return result;
    }
}
