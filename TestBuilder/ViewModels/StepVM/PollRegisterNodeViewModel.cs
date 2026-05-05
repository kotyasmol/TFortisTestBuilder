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
    public partial class PollRegisterNodeViewModel : NodeViewModel
    {
        [ObservableProperty] private byte slaveId;
        [ObservableProperty] private ushort address;
        [ObservableProperty] private int min;
        [ObservableProperty] private int max;
        [ObservableProperty] private int durationMs = 5000;
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

        public PollRegisterNodeViewModel()
        {
            Title = "Опрос регистра";
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

        protected override void OnSlavesLoaded()
        {
            RestoreSelections();
        }

        public void RestoreSelections()
        {
            _selectedSlave = AvailableSlaves.FirstOrDefault(s => s.SlaveId == SlaveId);
            OnPropertyChanged(nameof(SelectedSlave));
            RefreshRegisters();
        }

        public ITestStep CreateStep(ILogger logger)
        {
            return new PollRegisterStep(SlaveId, Address, Min, Max, DurationMs, logger, UseCurrentSlaveId);
        }
    }
}
