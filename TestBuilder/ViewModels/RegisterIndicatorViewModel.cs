using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Models;

namespace TestBuilder.ViewModels;

public partial class RegisterIndicatorViewModel : ObservableObject
{
    public RegisterIndicatorDefinition Definition { get; }
    public string Name => Definition.Name;
    public string SourceText => $"Slave {Definition.SlaveId} · {Definition.Area} {Definition.Address}";
    public bool UsesSystemColor => ProfileActionDefinition.IsSystemColor(Definition.Color);
    public IBrush? Background => UsesSystemColor ? null : Brush.Parse(Definition.Color);
    public bool HasLimits => Definition.Minimum.HasValue || Definition.Maximum.HasValue;
    public string LimitsText => !HasLimits ? "" : "Допуск: " +
        (Definition.Minimum.HasValue ? "от " + Definition.Minimum.Value.ToString(CultureInfo.CurrentCulture) : "") +
        (Definition.Minimum.HasValue && Definition.Maximum.HasValue ? " " : "") +
        (Definition.Maximum.HasValue ? "до " + Definition.Maximum.Value.ToString(CultureInfo.CurrentCulture) : "") +
        (Definition.Unit.Length > 0 ? " " + Definition.Unit : "");
    [ObservableProperty] private string valueText = "—";
    [ObservableProperty] private string statusText = "Нет данных";
    [ObservableProperty] private bool isOutOfRange;
    [ObservableProperty] private bool hasValue;

    public RegisterIndicatorViewModel(RegisterIndicatorDefinition definition)
    {
        definition.Validate();
        Definition = definition;
    }

    internal void Refresh(RegisterState state, HardwareConfiguration? hardware, bool connected, bool monitoring, long? now = null)
    {
        if (!connected) { Unavailable("Нет связи"); return; }
        if (!IsMapped(hardware)) { Unavailable("Регистр отсутствует в оборудовании профиля"); return; }
        if (!monitoring) { Unavailable("Опрос остановлен"); return; }
        var maxAgeMs = Math.Max(10000, (hardware?.PollIntervalMs ?? 1000) * 3);
        var notBefore = (now ?? Stopwatch.GetTimestamp()) - (long)(maxAgeMs / 1000d * Stopwatch.Frequency);
        if (!state.TryGet(Definition.SlaveId, Definition.Address, out var raw, Definition.Area, notBefore))
        {
            Unavailable(state.TryGet(Definition.SlaveId, Definition.Address, out _, Definition.Area) ? "Данные устарели" : "Нет данных");
            return;
        }
        var value = raw / Definition.Divisor;
        ValueText = FormatValue(raw, Definition.Divisor, Definition.DecimalPlaces, Definition.Unit);
        HasValue = true;
        var below = Definition.Minimum.HasValue && value < Definition.Minimum;
        var above = Definition.Maximum.HasValue && value > Definition.Maximum;
        IsOutOfRange = below || above;
        StatusText = below ? "Ниже допуска" : above ? "Выше допуска" : HasLimits ? "В допуске" : "";
    }

    internal bool IsMapped(HardwareConfiguration? hardware) => hardware?.Devices.Any(d => d.SlaveId == Definition.SlaveId &&
        d.Registers.Any(r => r.Address == Definition.Address && r.Area == Definition.Area)) == true;

    internal static string FormatValue(int raw, decimal divisor, int places, string unit) =>
        (raw / divisor).ToString("F" + places, CultureInfo.CurrentCulture) + (string.IsNullOrWhiteSpace(unit) ? "" : " " + unit);

    private void Unavailable(string reason)
    {
        ValueText = "—"; StatusText = reason; HasValue = false; IsOutOfRange = false;
    }
}
