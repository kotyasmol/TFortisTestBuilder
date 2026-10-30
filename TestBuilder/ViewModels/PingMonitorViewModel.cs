using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Models;
using TestBuilder.Services.Network;

namespace TestBuilder.ViewModels;

public partial class PingMonitorViewModel : ObservableObject, IDisposable
{
    private readonly IPingProbe _probe;
    private readonly Func<Action, Task> _publish;
    private CancellationTokenSource? _cancellation;
    private Task _runner = Task.CompletedTask;
    private (string Host, int Interval, int Timeout)? _runningSettings;
    private long _version;
    private bool _disposed;
    [ObservableProperty] private string hostText = "Адрес не задан";
    [ObservableProperty] private string statusText = "Не настроен";
    [ObservableProperty] private string detail = "";
    [ObservableProperty] private string lastReplyText = "";
    [ObservableProperty] private string intervalText = "";
    [ObservableProperty] private bool hasReply;
    [ObservableProperty] private bool noReply;
    [ObservableProperty] private bool hasError;

    public PingMonitorViewModel() : this(new IcmpPingProbe()) { }
    internal PingMonitorViewModel(IPingProbe probe, Func<Action, Task>? publish = null)
    {
        _probe = probe;
        _publish = publish ?? (async action => await Dispatcher.UIThread.InvokeAsync(action));
    }

    internal Task Completion => _runner;

    internal void Configure(PingMonitorSettings? settings, bool active)
    {
        if (_disposed) return;
        settings?.Validate();
        HostText = string.IsNullOrEmpty(settings?.Host) ? "Адрес не задан" : settings.Host;
        IntervalText = settings == null ? "" : $"Период {settings.IntervalMs} мс · таймаут {settings.TimeoutMs} мс";
        (string Host, int Interval, int Timeout)? next = settings is { Enabled: true } && active
            ? (settings.Host, settings.IntervalMs, settings.TimeoutMs) : null;
        if (next != null && _runningSettings == next) return;
        _runningSettings = next;
        var version = Interlocked.Increment(ref _version);
        _cancellation?.Cancel();
        _cancellation = null;
        HasReply = NoReply = HasError = false;
        LastReplyText = Detail = "";
        StatusText = settings == null ? "Не настроен" : !settings.Enabled ? "Опрос выключен" : !active ? "Опрос остановлен" : "Проверяем…";
        if (next is not { } config) return;
        var cancellation = _cancellation = new CancellationTokenSource();
        // Wait for the previous probe to finish before starting another, even after reconfiguration.
        _runner = RunAsync(_runner, config, cancellation, version);
    }

    private async Task RunAsync(Task previous, (string Host, int Interval, int Timeout) config, CancellationTokenSource cancellation, long version)
    {
        var token = cancellation.Token;
        try
        {
            await previous.ConfigureAwait(false);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var started = Stopwatch.GetTimestamp();
                PingSample sample;
                try { sample = await _probe.ProbeAsync(config.Host, config.Timeout, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { sample = new(PingOutcome.Error, Detail: ex.GetBaseException().Message); }
                await _publish(() =>
                {
                    if (_disposed || version != _version || token.IsCancellationRequested) return;
                    HasReply = sample.Outcome == PingOutcome.Reply;
                    NoReply = sample.Outcome == PingOutcome.NoReply;
                    HasError = sample.Outcome == PingOutcome.Error;
                    StatusText = HasReply ? $"Есть ответ · {sample.RoundtripMs} мс" : NoReply ? "Нет ответа" : "Ошибка пинга";
                    Detail = sample.Detail;
                    LastReplyText = "Проверено " + DateTime.Now.ToString("HH:mm:ss");
                }).ConfigureAwait(false);
                var remaining = TimeSpan.FromMilliseconds(config.Interval) - Stopwatch.GetElapsedTime(started);
                // A slow response never creates overlapping probes or a zero-delay retry loop.
                await Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { cancellation.Dispose(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _version);
        _cancellation?.Cancel();
        _cancellation = null;
    }
}
