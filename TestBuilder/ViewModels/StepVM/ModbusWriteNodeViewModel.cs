using CommunityToolkit.Mvvm.ComponentModel;
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
        partial void OnRegisterAreaChanged(ModbusArea value) => RestoreSelections();
        public ModbusArea[] RegisterAreas { get; } = System.Enum.GetValues<ModbusArea>();
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
        public bool HasSelectedRegister => SelectedRegister != null;
        public bool ShowRegisterSelector => IsConnected && HasSelectedRegister;
        public bool ShowAddressTextBox => !IsConnected || !HasSelectedRegister;

        public string RegisterDescription => RegisterReference.Describe(Address,
            IsConnected && !UseCurrentSlaveId ? SelectedSlave : null);

        partial void OnUseCurrentSlaveIdChanged(bool value) =>
            OnPropertyChanged(nameof(RegisterDescription));

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
        }

        private void RefreshRegisters()
        {
            AvailableRegisters.Clear();
            foreach (var reg in (_selectedSlave?.RegisterItems ?? Enumerable.Empty<RegisterItem>()).Where(r => !r.IsReadOnly))
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
            RestoreSelections();
        }

        /// <summary>Восстанавливает SelectedSlave и SelectedRegister по сохранённым SlaveId/Address</summary>
        public void RestoreSelections()
        {
            _selectedSlave = AvailableSlaves.FirstOrDefault(s => s.SlaveId == SlaveId);
            OnPropertyChanged(nameof(SelectedSlave));
            RefreshRegisters();
        }

        public ITestStep CreateStep(IModbusService modbusService, ILogger logger)
        {
            return new ModbusWriteStep(modbusService, logger, SlaveId, Address, Value, UseCurrentSlaveId, VerifyWrite, RegisterArea);
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
}
