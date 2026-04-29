using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class CheckRegisterRangeNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private byte slaveId;
        [ObservableProperty] private ushort address;
        [ObservableProperty] private int min;
        [ObservableProperty] private int max;
        [ObservableProperty] private bool useCurrentSlaveId;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public ObservableCollection<RegisterItem> AvailableRegisters { get; } = new();

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
                if (value != null) Address = (ushort)value.Address;
            }
        }

        public CheckRegisterRangeNodeViewModel()
        {
            Title = "Проверка диапазона";
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
            if (_selectedSlave == null) return;
            foreach (var reg in _selectedSlave.RegisterItems)
                AvailableRegisters.Add(reg);
            _selectedRegister = AvailableRegisters.FirstOrDefault(r => r.Address == Address);
            OnPropertyChanged(nameof(SelectedRegister));
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

        public ITestStep CreateStep(ILogger logger)
        {
            return new CheckRegisterRangeStep(SlaveId, Address, Min, Max, logger, UseCurrentSlaveId);
        }
    }
}