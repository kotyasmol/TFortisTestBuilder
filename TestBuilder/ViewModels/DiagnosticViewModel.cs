using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Modbus;

public class DiagnosticViewModel : INotifyPropertyChanged
{
    private readonly IModbusService _modbus;
    private readonly SlaveManager _slaveManager;

    public ObservableCollection<SlaveModelBase> Slaves => _slaveManager.Slaves;

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected != value)
            {
                _isConnected = value;
                OnPropertyChanged(nameof(IsConnected));
            }
        }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public DiagnosticViewModel(IModbusService modbus, SlaveManager slaveManager)
    {
        _modbus = modbus;
        _slaveManager = slaveManager;
    }

    public async Task ScanSlavesAsync()
    {
        await _slaveManager.ScanAsync();

        foreach (var slave in _slaveManager.Slaves)
        {
            foreach (var reg in slave.RegisterItems)
            {
                _modbus.SubscribeRegister(slave.SlaveId, reg.Address, values =>
                {
                    reg.Value = values[0];
                    OnPropertyChanged(nameof(Slaves)); // уведомляем UI
                });
            }
        }

        IsConnected = _slaveManager.Slaves.Count > 0;
    }
}
