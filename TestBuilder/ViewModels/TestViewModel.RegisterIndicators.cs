using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Models;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    public ObservableCollection<RegisterIndicatorViewModel> RegisterIndicators { get; } = new();
    public bool ShowRegisterIndicators => RegisterIndicators.Count > 0 || ShowQuickActionTools;
    private DispatcherTimer? _indicatorTimer;
    private bool _indicatorsDisposed;

    private void RefreshRegisterIndicators()
    {
        RegisterIndicators.Clear();
        foreach (var definition in RootGraph.RegisterIndicators)
            RegisterIndicators.Add(new RegisterIndicatorViewModel(definition));
        UpdateRegisterIndicators();
        OnPropertyChanged(nameof(ShowQuickActions));
        OnPropertyChanged(nameof(ShowRegisterIndicators));
    }

    private void UpdateRegisterIndicators()
    {
        if (_indicatorsDisposed) return;
        // UI snapshots use the existing polling cache. No additional Modbus requests.
        if (IsConnected && RegisterIndicators.Count > 0)
        {
            if (_indicatorTimer == null)
            {
                _indicatorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _indicatorTimer.Tick += (_, _) => RefreshRegisterIndicatorValues();
            }
            _indicatorTimer.Start();
        }
        else _indicatorTimer?.Stop();
        RefreshRegisterIndicatorValues();
    }

    internal void RefreshRegisterIndicatorValues()
    {
        if (_indicatorsDisposed) return;
        foreach (var indicator in RegisterIndicators)
            indicator.Refresh(_registerState, RootGraph.Hardware, IsConnected, IsMonitoringActive);
    }

    [RelayCommand(CanExecute = nameof(CanEditQuickActions))]
    private async Task AddRegisterIndicator()
    {
        if (!CanEditQuickActions || ActionOwner == null) return;
        var graph = RootGraph;
        var result = await new Views.RegisterIndicatorEditorWindow(new RegisterIndicatorDefinition(), graph.Hardware)
            .ShowDialog<RegisterIndicatorDefinition?>(ActionOwner);
        if (result == null || !CanEditQuickActions || !ReferenceEquals(graph, RootGraph)) return;
        _history.Execute(graph, new ProfileMetadataCommand(() => graph.RegisterIndicators.Add(result), () => graph.RegisterIndicators.Remove(result)));
    }

    [RelayCommand(CanExecute = nameof(CanEditQuickActions))]
    private async Task EditRegisterIndicator(RegisterIndicatorViewModel? indicator)
    {
        if (!CanEditQuickActions || indicator == null || ActionOwner == null) return;
        var graph = RootGraph;
        var previous = indicator.Definition;
        var result = await new Views.RegisterIndicatorEditorWindow(previous, graph.Hardware)
            .ShowDialog<RegisterIndicatorDefinition?>(ActionOwner);
        if (result == null || !CanEditQuickActions || !ReferenceEquals(graph, RootGraph)) return;
        var index = graph.RegisterIndicators.IndexOf(previous);
        if (index < 0) return;
        _history.Execute(graph, new ProfileMetadataCommand(() => graph.RegisterIndicators[index] = result, () => graph.RegisterIndicators[index] = previous));
    }

    internal Func<string, Task<bool>> ConfirmDeleteRegisterIndicatorAsync { get; set; } = async name =>
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is { } owner)
            return await new Views.DeleteProfileActionDialog(name, registerIndicator: true).ShowDialog<bool>(owner);
        return false;
    };

    [RelayCommand(CanExecute = nameof(CanEditQuickActions))]
    private async Task DeleteRegisterIndicator(RegisterIndicatorViewModel? indicator)
    {
        if (!CanEditQuickActions || indicator == null) return;
        var graph = RootGraph; var definition = indicator.Definition;
        if (!graph.RegisterIndicators.Contains(definition) || !await ConfirmDeleteRegisterIndicatorAsync(definition.Name) ||
            !CanEditQuickActions || !ReferenceEquals(graph, RootGraph)) return;
        var index = graph.RegisterIndicators.IndexOf(definition);
        if (index < 0) return;
        _history.Execute(graph, new ProfileMetadataCommand(() => graph.RegisterIndicators.Remove(definition), () => graph.RegisterIndicators.Insert(index, definition)));
    }
}
