using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private RegisterItem? _standSerialRegister;

    private static RegisterItem? SerialRegister(SlaveModelBase model)
    {
        if (model is not ConfiguredSlaveModel configured) return null;
        // Compatibility with earlier profiles whose register map already identifies this field.
        var matches = model.RegisterItems.Where(r => r.Area == ModbusArea.Holding &&
            (configured.StandSerialNumberAddress is { } address ? r.Address == address :
                string.Equals(r.Name.Trim(), "Серийный номер стенда", StringComparison.OrdinalIgnoreCase))).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public string StandSectionTitle
    {
        get
        {
            var hardware = RootGraph.Hardware;
            var name = hardware?.StandName?.Trim();
            if (string.IsNullOrWhiteSpace(name) && IsConnected)
            {
                var controllers = _slaveManager.GetSlavesSnapshot().Where(s => SerialRegister(s) != null).ToArray();
                if (controllers.Length == 1) name = controllers[0].DeviceType;
            }
            if (string.IsNullOrWhiteSpace(name) && hardware?.Devices.Count == 1) name = hardware.Devices[0].Name;
            if (string.IsNullOrWhiteSpace(name)) name = hardware == null && !IsBoardProfile ? "АПК-03" : "";
            return string.IsNullOrWhiteSpace(name) ? "Стенд" :
                name.StartsWith("Стенд ", StringComparison.OrdinalIgnoreCase) ? name : "Стенд " + name;
        }
    }

    public string StandSerialCaption => IsConnected && _standSerialRegister != null
        ? "Серийный номер стенда: " + _standSerialRegister.Value : "";
    public bool HasStandSerial => !string.IsNullOrEmpty(StandSerialCaption);

    private void OnStandDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshStandIdentity();
    private void OnStandSerialChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RegisterItem.Value)) return;
        OnPropertyChanged(nameof(StandSerialCaption));
        OnPropertyChanged(nameof(HasStandSerial));
    }
    private void RefreshStandIdentity()
    {
        if (_standSerialRegister != null) _standSerialRegister.PropertyChanged -= OnStandSerialChanged;
        var registers = _slaveManager.GetSlavesSnapshot().Select(SerialRegister).OfType<RegisterItem>().ToArray();
        _standSerialRegister = IsConnected && registers.Length == 1 ? registers[0] : null;
        if (_standSerialRegister != null) _standSerialRegister.PropertyChanged += OnStandSerialChanged;
        OnPropertyChanged(nameof(StandSectionTitle));
        OnPropertyChanged(nameof(StandSerialCaption));
        OnPropertyChanged(nameof(HasStandSerial));
    }
}
