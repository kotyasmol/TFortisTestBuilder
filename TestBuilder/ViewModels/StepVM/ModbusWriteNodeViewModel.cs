using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class ModbusWriteNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private ModbusArea registerArea;
        public IReadOnlyList<ModbusArea> RegisterAreas { get; } = new[] { ModbusArea.Holding, ModbusArea.Coil };
        public bool HasReadOnlyArea => RegisterArea is not (ModbusArea.Holding or ModbusArea.Coil);
        public ModbusArea? SelectedWritableArea
        {
            get => HasReadOnlyArea ? null : RegisterArea;
            set
            {
                if (value is ModbusArea.Holding or ModbusArea.Coil) RegisterArea = value.Value;
            }
        }

        partial void OnRegisterAreaChanged(ModbusArea value)
        {
            OnPropertyChanged(nameof(HasReadOnlyArea));
            OnPropertyChanged(nameof(SelectedWritableArea));
            RestoreSelections();
        }
        [ObservableProperty] private byte slaveId;
        [ObservableProperty] private ushort address;
        [ObservableProperty] private ushort value;
        [ObservableProperty] private bool useCurrentSlaveId;
        [ObservableProperty] private bool verifyWrite;
        [ObservableProperty] private string slaveIdText = "0";
        [ObservableProperty] private string addressText = "0";
        [ObservableProperty] private string valueText = "0";
        [ObservableProperty] private bool hasIntegerInputError;

        private bool _slaveIdInputInvalid;
        private bool _addressInputInvalid;
        private bool _valueInputInvalid;

        public string IntegerInputError => "Вводите только целочисленные значения.";

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public ObservableCollection<RegisterItem> AvailableRegisters { get; } = new();
        public IReadOnlyList<ModbusDeviceChoice> DeviceChoices { get; private set; } = System.Array.Empty<ModbusDeviceChoice>();
        public bool ShowDeviceSelector => DeviceChoices.Count > 0;
        public ModbusDeviceChoice? SelectedDevice
        {
            get => DeviceChoices.FirstOrDefault(device => device.SlaveId == SlaveId);
            set
            {
                if (value != null && DeviceChoices.Contains(value)) SlaveId = value.SlaveId;
            }
        }
        public bool HasSelectedRegister => SelectedRegister != null;
        public bool ShowRegisterSelector => AvailableRegisters.Count > 0;
        public bool ShowAddressTextBox => true;

        public string RegisterDescription => RegisterReference.Describe(Address,
            IsConnected && !UseCurrentSlaveId ? SelectedSlave : null, RegisterArea,
            RegisterReferenceHardware, UseCurrentSlaveId ? null : SlaveId);

        partial void OnUseCurrentSlaveIdChanged(bool value) => RestoreSelections();

        private SlaveModelBase? _selectedSlave;
        public SlaveModelBase? SelectedSlave
        {
            get => _selectedSlave;
            set
            {
                _selectedSlave = value;
                OnPropertyChanged();
                RefreshRegisters();
                if (value != null) SlaveId = value.SlaveId;
            }
        }

        private RegisterItem? _selectedRegister;
        public RegisterItem? SelectedRegister
        {
            get => _selectedRegister;
            set
            {
                _selectedRegister = value;
                OnPropertyChanged();
                OnRegisterSelectionChanged();
                if (value != null) { RegisterArea = value.Area; Address = (ushort)value.Address; }
            }
        }

        public ModbusWriteNodeViewModel()
        {
            Title = "Запись регистра";
            In = new ConnectorViewModel { Title = "Вход" };
            TrueOut = new ConnectorViewModel { Title = "True" };
            FalseOut = new ConnectorViewModel { Title = "False" };
            AddInput(In);
            AddOutput(TrueOut);
            AddOutput(FalseOut);
            RestoreSelections();
        }

        private void RefreshRegisters()
        {
            AvailableRegisters.Clear();
            foreach (var reg in RegisterReference.Choices(RegisterArea, writableOnly: true,
                IsConnected && !UseCurrentSlaveId ? _selectedSlave : null,
                RegisterReferenceHardware, UseCurrentSlaveId ? null : SlaveId))
                AvailableRegisters.Add(reg);
            _selectedRegister = AvailableRegisters.FirstOrDefault(r => r.Address == Address && r.Area == RegisterArea);
            OnPropertyChanged(nameof(SelectedRegister));
            OnRegisterSelectionChanged();
        }

        partial void OnAddressChanged(ushort value)
        {
            _addressInputInvalid = false;
            UpdateIntegerInputError();
            AddressText = value.ToString(CultureInfo.InvariantCulture);
            _selectedRegister = AvailableRegisters.FirstOrDefault(r => r.Address == value && r.Area == RegisterArea);
            OnPropertyChanged(nameof(SelectedRegister));
            OnRegisterSelectionChanged();
        }

        partial void OnSlaveIdChanged(byte value)
        {
            _slaveIdInputInvalid = false;
            UpdateIntegerInputError();
            SlaveIdText = value.ToString(CultureInfo.InvariantCulture);
            OnPropertyChanged(nameof(SelectedDevice));
            RestoreSelections();
        }

        partial void OnValueChanged(ushort value)
        {
            _valueInputInvalid = false;
            UpdateIntegerInputError();
            ValueText = value.ToString(CultureInfo.InvariantCulture);
        }

        partial void OnSlaveIdTextChanged(string value)
        {
            if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                _slaveIdInputInvalid = false;
                SlaveId = parsed;
            }
            else
            {
                _slaveIdInputInvalid = true;
            }

            UpdateIntegerInputError();
        }

        partial void OnAddressTextChanged(string value)
        {
            if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                _addressInputInvalid = false;
                Address = parsed;
            }
            else
            {
                _addressInputInvalid = true;
            }

            UpdateIntegerInputError();
        }

        partial void OnValueTextChanged(string value)
        {
            if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                _valueInputInvalid = false;
                Value = parsed;
            }
            else
            {
                _valueInputInvalid = true;
            }

            UpdateIntegerInputError();
        }

        private void UpdateIntegerInputError()
        {
            HasIntegerInputError = _slaveIdInputInvalid || _addressInputInvalid || _valueInputInvalid;
        }

        private void OnRegisterSelectionChanged()
        {
            OnPropertyChanged(nameof(RegisterDescription));
            OnPropertyChanged(nameof(HasSelectedRegister));
            OnPropertyChanged(nameof(ShowRegisterSelector));
            OnPropertyChanged(nameof(ShowAddressTextBox));
        }

        /// <summary>Вызывается при подключении — восстанавливает выбранные слейв и регистр</summary>
        protected override void OnSlavesLoaded()
        {
            if (RegisterReferenceHardware?.Devices is { Count: > 0 } devices)
                DeviceChoices = devices.OrderBy(d => d.SlaveId).Select(d => new ModbusDeviceChoice(d.SlaveId, d.Name)).ToArray();
            else
                DeviceChoices = IsConnected
                    ? AvailableSlaves.OrderBy(s => s.SlaveId).Select(s => new ModbusDeviceChoice(s.SlaveId, s.DeviceType)).ToArray()
                    : System.Array.Empty<ModbusDeviceChoice>();
            OnPropertyChanged(nameof(DeviceChoices));
            OnPropertyChanged(nameof(ShowDeviceSelector));
            OnPropertyChanged(nameof(SelectedDevice));
            RestoreSelections();
        }

        /// <summary>Восстанавливает SelectedSlave и SelectedRegister по сохранённым SlaveId/Address</summary>
        public void RestoreSelections()
        {
            _selectedSlave = AvailableSlaves.FirstOrDefault(s => s.SlaveId == SlaveId);
            OnPropertyChanged(nameof(SelectedSlave));
            RefreshRegisters();
        }

        public ITestStep CreateStep(IModbusService modbusService, ILogger logger, IReadOnlySet<byte>? readOnlySlaves = null)
        {
            return new ModbusWriteStep(modbusService, logger, SlaveId, Address, Value, UseCurrentSlaveId, VerifyWrite, RegisterArea, readOnlySlaves);
        }

        public override NodeViewModel Clone() => new ModbusWriteNodeViewModel
        {
            SlaveId = SlaveId,
            Address = Address,
            RegisterArea = RegisterArea,
            Value = Value,
            UseCurrentSlaveId = UseCurrentSlaveId,
            VerifyWrite = VerifyWrite
        };
    }

    public sealed record ModbusDeviceChoice(byte SlaveId, string Name)
    {
        public override string ToString() => $"{SlaveId} — {Name}";
    }
}
