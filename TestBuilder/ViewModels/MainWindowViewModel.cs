using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodify;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly ModbusService _modbusService = new();

        public ObservableCollection<NodeViewModel> Nodes { get; } = new();
        public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

        public ObservableCollection<string> AvailablePorts { get; } = new();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        private string? selectedPort;

        // Скорость и формат кадра

        [ObservableProperty]
        private int baudRate = 9600;

        [ObservableProperty]
        private Parity parity = Parity.None;

        [ObservableProperty]
        private int dataBits = 8;

        [ObservableProperty]
        private StopBits stopBits = StopBits.One;

        // Индексы для ComboBox'ов в UI
        [ObservableProperty]
        private int parityIndex;

        [ObservableProperty]
        private int stopBitsIndex;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private bool isConnecting;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
        [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
        private bool isConnected;

        [ObservableProperty]
        private string? statusMessage;

        public PendingConnectionViewModel PendingConnection { get; }

        public IAsyncRelayCommand RefreshPortsCommand { get; }
        public IAsyncRelayCommand ConnectCommand { get; }
        public IAsyncRelayCommand DisconnectCommand { get; }


        public MainWindowViewModel()
        {
            PendingConnection = new PendingConnectionViewModel(this);

            RefreshPortsCommand = new AsyncRelayCommand(RefreshPortsAsync);
            ConnectCommand = new AsyncRelayCommand(ConnectAsync, CanConnect);
            DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);

            // Инициализируем список доступных COM-портов
            _ = RefreshPortsAsync();

            var node1 = new NodeViewModel
            {
                Title = "Welcome",
                Input = { new ConnectorViewModel { Title = "In" } },
                Output = { new ConnectorViewModel { Title = "Out" } }
            };

            var node2 = new NodeViewModel
            {
                Title = "To Nodify",
                Input = { new ConnectorViewModel { Title = "In" } }
            };

            Nodes.Add(node1);
            Nodes.Add(node2);
        }

        public void Connect(ConnectorViewModel source, ConnectorViewModel target)
        {
            Connections.Add(new ConnectionViewModel(source, target));
        }

        private async Task RefreshPortsAsync()
        {
            AvailablePorts.Clear();

            foreach (var port in SerialPort.GetPortNames().OrderBy(p => p))
            {
                AvailablePorts.Add(port);
            }

            if (SelectedPort is null)
            {
                SelectedPort = AvailablePorts.FirstOrDefault();
            }

            StatusMessage = AvailablePorts.Count == 0
                ? "COM‑порты не найдены"
                : "Выберите порт и параметры, затем подключитесь";
        }

        private bool CanConnect()
        {
            return !IsConnected
                   && !IsConnecting
                   && !string.IsNullOrWhiteSpace(SelectedPort);
        }

        private async Task ConnectAsync()
        {
            if (!CanConnect())
                return;

            IsConnecting = true;
            StatusMessage = $"Подключение к {SelectedPort}...";

            var ok = await _modbusService.ConnectAsync(SelectedPort!, BaudRate, Parity, DataBits, StopBits);

            IsConnecting = false;
            IsConnected = ok;

            StatusMessage = ok
                ? $"Подключено к {SelectedPort}"
                : $"Ошибка подключения: {_modbusService.LastError}";
        }

        private async Task DisconnectAsync()
        {
            if (!IsConnected && !IsConnecting)
                return;

            StatusMessage = "Отключение...";
            await _modbusService.DisconnectAsync();

            IsConnected = false;
            IsConnecting = false;
            StatusMessage = "Отключено";
        }

        partial void OnParityIndexChanged(int value)
        {
            Parity = value switch
            {
                0 => System.IO.Ports.Parity.None,
                1 => System.IO.Ports.Parity.Odd,
                2 => System.IO.Ports.Parity.Even,
                _ => System.IO.Ports.Parity.None
            };
        }

        partial void OnStopBitsIndexChanged(int value)
        {
            StopBits = value switch
            {
                0 => System.IO.Ports.StopBits.One,
                1 => System.IO.Ports.StopBits.Two,
                _ => System.IO.Ports.StopBits.One
            };
        }
    }
}
