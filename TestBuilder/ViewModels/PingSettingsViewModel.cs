using System;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Models;

namespace TestBuilder.ViewModels;

public partial class PingSettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string host = "";
    [ObservableProperty] private decimal? intervalMs;
    [ObservableProperty] private decimal? timeoutMs;
    [ObservableProperty] private bool showInEngineer;
    [ObservableProperty] private bool showInOperator;
    [ObservableProperty] private string error = "";

    public PingSettingsViewModel(PingMonitorSettings source)
    {
        Enabled = source.Enabled; Host = source.Host;
        IntervalMs = source.IntervalMs; TimeoutMs = source.TimeoutMs;
        ShowInEngineer = source.ShowInEngineer; ShowInOperator = source.ShowInOperator;
    }

    partial void OnHostChanged(string value) => Error = "";
    partial void OnEnabledChanged(bool value) => Error = "";
    partial void OnIntervalMsChanged(decimal? value) => Error = "";
    partial void OnTimeoutMsChanged(decimal? value) => Error = "";

    internal PingMonitorSettings BuildResult()
    {
        if (IntervalMs is not >= 250 or > 60000 || TimeoutMs is not >= 100 or > 30000 ||
            IntervalMs != decimal.Truncate(IntervalMs.Value) || TimeoutMs != decimal.Truncate(TimeoutMs.Value))
            throw new InvalidOperationException("Укажите целый период 250..60000 мс и таймаут 100..30000 мс.");
        var result = new PingMonitorSettings {
            Enabled = Enabled, Host = Host?.Trim() ?? "", IntervalMs = (int)IntervalMs.Value, TimeoutMs = (int)TimeoutMs.Value,
            ShowInEngineer = ShowInEngineer, ShowInOperator = ShowInOperator
        };
        result.Validate();
        return result;
    }
}
