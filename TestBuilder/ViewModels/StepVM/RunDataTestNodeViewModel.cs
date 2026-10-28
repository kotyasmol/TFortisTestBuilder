using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Logging;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class RunDataTestNodeViewModel : NodeViewModel
    {
        public const string DefaultPortsText =
            "port0-1,192.168.0.2,192.168.0.3,100\r\n" +
            "port2-3,192.168.0.4,192.168.0.5,100\r\n" +
            "port4-5,192.168.0.6,192.168.0.7,100\r\n" +
            "port6-7,192.168.0.8,192.168.0.9,100\r\n" +
            "port8-9,192.168.0.10,192.168.0.11,100";

        [ObservableProperty] private string mode = "SoftwarePcap";
        [ObservableProperty] private int expectedPackets = 10000;
        [ObservableProperty] private int packetSizeBytes = 1514;
        [ObservableProperty] private int udpPort = 43962;
        [ObservableProperty] private int maxPortTestTimeMs = 15000;
        [ObservableProperty] private int targetBandwidthMbps = 100;
        [ObservableProperty] private bool allowGigabit;
        [ObservableProperty] private int durationMs = 5000;
        [ObservableProperty] private int warmupMs = 500;
        [ObservableProperty] private int interPairDelayMs = 5000;
        [ObservableProperty] private double allowedLossPercent = 1.0;
        [ObservableProperty] private double allowedTxDeficitPercent = 2.0;
        [ObservableProperty] private bool bidirectional = true;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PairsSummary))]
        private string portsText = DefaultPortsText;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PairsSummary))]
        private string disabledPortsText = "";
        [ObservableProperty] private string outputVariableName = "DataTest";
        [ObservableProperty] private bool failOnError = true;

        [ObservableProperty] private string networkPlanStatus = "";
        [RelayCommand]
        private void ImportNetworkPlan()
        {
            try
            {
                var plan = AppSettings.Instance.NetworkPlan;
                var pairs = plan.BuildDataTestPairs();
                AllowGigabit = plan.Addresses.Any(p => p.IncludeInDataTest && p.TestMbps > 100);
                PortsText = pairs;
                DisabledPortsText = "";
                NetworkPlanStatus = "Пары скопированы. Сохраните профиль.";
            }
            catch (Exception ex) { NetworkPlanStatus = ex.Message; }
        }

        public string PairsSummary
        {
            get
            {
                try
                {
                    var rows = new DataTestPairEditorViewModel(this).Rows;
                    return string.Join(Environment.NewLine, rows.Select(r =>
                        $"{(r.Enabled ? "✓" : "—")} {r.Name}: {r.FirstIp} ↔ {r.SecondIp} · {r.Mbps} Мбит/с"));
                }
                catch { return "Не удалось прочитать пары. Проверьте профиль."; }
            }
        }

        [RelayCommand]
        private async System.Threading.Tasks.Task EditPairs()
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
            try
            {
                if (await new TestBuilder.Views.DataTestPairsWindow(this).ShowDialog<bool>(desktop.MainWindow))
                    NetworkPlanStatus = "Пары изменены. Сохраните профиль.";
            }
            catch (Exception ex) { NetworkPlanStatus = ex.Message; }
        }

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public RunDataTestNodeViewModel()
        {
            Title = "Run Data Test";
            In = new ConnectorViewModel { Title = "In", Parent = this };
            TrueOut = new ConnectorViewModel { Title = "True", Parent = this };
            FalseOut = new ConnectorViewModel { Title = "False", Parent = this };
            Input.Add(In);
            Output.Add(TrueOut);
            Output.Add(FalseOut);
        }

        partial void OnTargetBandwidthMbpsChanged(int value)
        {
            var normalized = RunDataTestStep.NormalizeBandwidth(value, AllowGigabit);
            if (normalized != value)
            {
                TargetBandwidthMbps = normalized;
            }
        }

        partial void OnAllowGigabitChanged(bool value) =>
            TargetBandwidthMbps = RunDataTestStep.NormalizeBandwidth(TargetBandwidthMbps, value);

        public ITestStep CreateStep(ILogger logger) =>
            new RunDataTestStep(
                logger,
                Mode,
                ExpectedPackets,
                PacketSizeBytes,
                UdpPort,
                MaxPortTestTimeMs,
                TargetBandwidthMbps,
                DurationMs,
                WarmupMs,
                InterPairDelayMs,
                AllowedLossPercent,
                AllowedTxDeficitPercent,
                Bidirectional,
                ParsePorts(),
                OutputVariableName,
                FailOnError,
                AllowGigabit);

        public override NodeViewModel Clone() => new RunDataTestNodeViewModel
        {
            Mode = Mode,
            ExpectedPackets = ExpectedPackets,
            PacketSizeBytes = PacketSizeBytes,
            UdpPort = UdpPort,
            MaxPortTestTimeMs = MaxPortTestTimeMs,
            AllowGigabit = AllowGigabit,
            TargetBandwidthMbps = TargetBandwidthMbps,
            DurationMs = DurationMs,
            WarmupMs = WarmupMs,
            InterPairDelayMs = InterPairDelayMs,
            AllowedLossPercent = AllowedLossPercent,
            AllowedTxDeficitPercent = AllowedTxDeficitPercent,
            Bidirectional = Bidirectional,
            PortsText = PortsText,
            DisabledPortsText = DisabledPortsText,
            OutputVariableName = OutputVariableName,
            FailOnError = FailOnError
        };

        private IEnumerable<DataTestPortConfig> ParsePorts()
        {
            return PortsText
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(','))
                .Where(parts => parts.Length >= 3)
                .Select(parts => new DataTestPortConfig(
                    parts[0].Trim(),
                    parts[1].Trim(),
                    parts[2].Trim(),
                    parts.Length >= 4 && int.TryParse(parts[3].Trim(), out var mbps)
                        ? RunDataTestStep.NormalizeBandwidth(mbps, AllowGigabit)
                        : null));
        }
    }
}
