using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Models;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    public PingMonitorViewModel PingMonitor { get; } = new();
    private bool _pingMonitoringActive;
    public bool ShowEngineerPing => !IsActionGraphEditor && !IsTraining && (RootGraph.PingMonitor?.ShowInEngineer ?? !IsBoardProfile);
    public bool ShowOperatorPing => !IsTraining && !IsActionGraphEditor && RootGraph.PingMonitor?.ShowInOperator == true;

    internal void SetPingMonitoringActive(bool active)
    {
        _pingMonitoringActive = active;
        RefreshPingMonitor();
    }

    internal void ResetPingMonitor() => PingMonitor.Configure(null, false);

    private void RefreshPingMonitor()
    {
        PingMonitor.Configure(RootGraph.PingMonitor, _pingMonitoringActive && !IsTraining && !IsActionGraphEditor && _profileLoadError.Length == 0);
        OnPropertyChanged(nameof(ShowEngineerPing));
        OnPropertyChanged(nameof(ShowOperatorPing));
    }

    [RelayCommand(CanExecute = nameof(CanEditQuickActions))]
    private async Task ConfigurePing(Window? owner)
    {
        if (!CanEditQuickActions || owner == null) return;
        var graph = RootGraph;
        var draft = graph.PingMonitor?.Copy() ?? new PingMonitorSettings { ShowInEngineer = !IsBoardProfile };
        var result = await new Views.PingSettingsWindow(draft).ShowDialog<PingMonitorSettings?>(owner);
        if (result != null && CanEditQuickActions && ReferenceEquals(graph, RootGraph)) ApplyPingSettings(result);
    }

    internal void ApplyPingSettings(PingMonitorSettings settings)
    {
        if (!CanEditQuickActions) return;
        settings.Validate();
        var graph = RootGraph;
        var previous = graph.PingMonitor;
        var next = settings.Copy();
        void Set(PingMonitorSettings? value) { graph.PingMonitor = value; RefreshPingMonitor(); }
        _history.Execute(graph, new ProfileMetadataCommand(() => Set(next), () => Set(previous)));
    }
}
