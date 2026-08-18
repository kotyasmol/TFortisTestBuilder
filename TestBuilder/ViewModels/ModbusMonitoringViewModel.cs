using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Logging;
using TestBuilder.Services.Modbus;

namespace TestBuilder.ViewModels
{
    public class ModbusMonitoringViewModel : ViewModelBase, IDisposable
    {
        private readonly SlaveManager _slaveManager;
        private readonly ModbusService _modbusService;
        private CancellationTokenSource? _cts;
        private Task? _monitorTask;

        public bool IsConnected => _modbusService.IsConnected;

        public ObservableCollection<SlaveModelBase> Slaves => _slaveManager.Slaves;
        public ObservableCollection<RegisterItem> FilteredRegisters { get; } = new();
        public ObservableCollection<string> Categories { get; } = new();

        private const string AllCategories = "Все категории";

        private SlaveModelBase? _selectedSlave;
        public SlaveModelBase? SelectedSlave
        {
            get => _selectedSlave;
            set
            {
                if (!SetProperty(ref _selectedSlave, value))
                    return;

                RefreshCategories();
                RefreshRegisters();
                OnPropertyChanged(nameof(SelectedSlaveTitle));
                OnPropertyChanged(nameof(SelectedSlaveDetails));
            }
        }

        public string SelectedSlaveTitle => SelectedSlave?.DeviceType ?? "Выберите устройство";

        public string SelectedSlaveDetails => SelectedSlave is null
            ? "После сканирования выберите устройство слева."
            : $"Slave ID: {SelectedSlave.SlaveId} · {SelectedSlave.RegisterItems.Count} регистров";

        private string _registerSearch = string.Empty;
        public string RegisterSearch
        {
            get => _registerSearch;
            set
            {
                if (SetProperty(ref _registerSearch, value))
                    RefreshRegisters();
            }
        }

        private bool _showWritableOnly;
        public bool ShowWritableOnly
        {
            get => _showWritableOnly;
            set
            {
                if (SetProperty(ref _showWritableOnly, value))
                    RefreshRegisters();
            }
        }

        private string _selectedCategory = AllCategories;
        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                    RefreshRegisters();
            }
        }

        public int VisibleRegistersCount => FilteredRegisters.Count;

        private bool _isMonitoring;
        public bool IsMonitoring
        {
            get => _isMonitoring;
            private set => SetProperty(ref _isMonitoring, value);
        }

        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            private set => SetProperty(ref _isScanning, value);
        }

        public AsyncRelayCommand ScanCommand { get; }
        public ILogger TestingLogger { get; }

        public ModbusMonitoringViewModel(SlaveManager slaveManager, ModbusService modbusService, ILogger testingLogger)
        {
            _slaveManager = slaveManager ?? throw new ArgumentNullException(nameof(slaveManager));
            _modbusService = modbusService ?? throw new ArgumentNullException(nameof(modbusService));
            TestingLogger = testingLogger ?? throw new ArgumentNullException(nameof(testingLogger));
            ScanCommand = new AsyncRelayCommand(ScanAndStartAsync);

            // Подписываемся на изменение состояния подключения —
            // уведомляем UI что IsConnected изменилось
            _modbusService.IsConnectedChanged += OnModbusConnectionChanged;
        }

        public async Task ScanAndStartAsync()
        {
            if (!_modbusService.IsConnected)
                return;
            Stop();

            IsScanning = true;
            try
            {
                await _slaveManager.ScanAsync();
                if (SelectedSlave is null || !Slaves.Contains(SelectedSlave))
                    SelectedSlave = Slaves.FirstOrDefault();
                else
                    RefreshRegisters();
            }
            finally
            {
                IsScanning = false;
            }

            await StartAsync();
        }

        public Task StartAsync()
        {
            if (_monitorTask is { IsCompleted: false })
                return Task.CompletedTask;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            IsMonitoring = true;
            _monitorTask = Task.Run(() => MonitorLoop(token), token);
            return Task.CompletedTask;
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            IsMonitoring = false;
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var slave in _slaveManager.GetSlavesSnapshot())
                {
                    try
                    {
                        await slave.PollAsync();
                    }
                    catch { }
                }

                await Task.Delay(1000, token);
            }
        }

        private void OnModbusConnectionChanged(object? sender, EventArgs e)
            => OnPropertyChanged(nameof(IsConnected));

        private void RefreshCategories()
        {
            var currentCategory = SelectedCategory;
            Categories.Clear();
            Categories.Add(AllCategories);

            if (SelectedSlave is not null)
            {
                foreach (var category in SelectedSlave.RegisterItems
                             .Select(register => register.Category)
                             .Where(category => !string.IsNullOrWhiteSpace(category))
                             .Distinct()
                             .OrderBy(category => category))
                {
                    Categories.Add(category);
                }
            }

            _selectedCategory = Categories.Contains(currentCategory)
                ? currentCategory
                : AllCategories;
            OnPropertyChanged(nameof(SelectedCategory));
        }

        private void RefreshRegisters()
        {
            FilteredRegisters.Clear();

            if (SelectedSlave is null)
            {
                OnPropertyChanged(nameof(VisibleRegistersCount));
                return;
            }

            var search = RegisterSearch.Trim();
            foreach (var register in SelectedSlave.RegisterItems)
            {
                var matchesSearch = string.IsNullOrEmpty(search)
                    || register.Address.ToString().Contains(search, StringComparison.OrdinalIgnoreCase)
                    || register.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
                var matchesCategory = SelectedCategory == AllCategories
                    || register.Category == SelectedCategory;

                if (matchesSearch && matchesCategory && (!ShowWritableOnly || !register.IsReadOnly))
                    FilteredRegisters.Add(register);
            }

            OnPropertyChanged(nameof(VisibleRegistersCount));
        }

        public void Dispose()
        {
            Stop();
            _modbusService.IsConnectedChanged -= OnModbusConnectionChanged;
        }
    }
}
