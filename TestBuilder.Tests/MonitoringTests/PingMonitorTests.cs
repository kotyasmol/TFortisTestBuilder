using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
using TestBuilder.Domain.Modbus;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Services.Network;
using TestBuilder.Tests.ProfileActions;
using TestBuilder.ViewModels;

namespace TestBuilder.Tests.MonitoringTests;

public class PingMonitorTests
{
    internal static PingMonitorSettings Settings(string host = "switch.local") => new() {
        Enabled = true, Host = host, IntervalMs = 250, TimeoutMs = 100, ShowInOperator = true };
    private static Task Inline(Action action) { action(); return Task.CompletedTask; }

    [Fact]
    public async Task ProfileChange_WaitsForCancelledProbe_RejectsLateResult_AndDisposesWithoutMoreRequests()
    {
        var probe = new ControlledProbe(); using var vm = new PingMonitorViewModel(probe, Inline);
        vm.Configure(Settings("old.local"), true); await Until(() => probe.Calls.Count == 1);
        var old = probe.Calls.ElementAt(0);
        vm.Configure(Settings("new.local"), true);
        Assert.True(old.Token.IsCancellationRequested); Assert.Single(probe.Calls);
        Assert.Equal("new.local", vm.HostText); Assert.False(vm.HasReply);
        old.Result.SetResult(new(PingOutcome.Reply, 3));
        await Until(() => probe.Calls.Count == 2);
        Assert.False(vm.HasReply); Assert.Equal("Проверяем…", vm.StatusText);
        var current = probe.Calls.ElementAt(1); Assert.Equal("new.local", current.Host);
        current.Result.SetResult(new(PingOutcome.NoReply, Detail: "TimedOut"));
        await Until(() => vm.NoReply && vm.LastReplyText.Length > 0);
        Assert.Equal("Нет ответа", vm.StatusText); Assert.NotEmpty(vm.LastReplyText);
        vm.Dispose(); await vm.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, probe.Calls.Count); Assert.Equal(1, probe.MaxActive);
    }

    [Fact]
    public async Task Polling_RespectsInterval_RecoversFromFailure_AndPresentationChangesDoNotRestartIt()
    {
        var starts = new ConcurrentQueue<long>(); var outcome = PingOutcome.Reply;
        var probe = new DelegateProbe((_, _, _) => { starts.Enqueue(Stopwatch.GetTimestamp()); return Task.FromResult(new PingSample(outcome, 7, "details")); });
        using var vm = new PingMonitorViewModel(probe, Inline);
        var settings = Settings(); vm.Configure(settings, true); await Until(() => vm.HasReply);
        Assert.Equal("Есть ответ · 7 мс", vm.StatusText);
        settings.ShowInOperator = false; vm.Configure(settings, true);
        Assert.True(vm.HasReply); Assert.Single(starts);
        outcome = PingOutcome.NoReply; await Until(() => vm.NoReply);
        Assert.True(Stopwatch.GetElapsedTime(starts.ElementAt(0), starts.ElementAt(1)).TotalMilliseconds >= 200);
        outcome = PingOutcome.Reply; await Until(() => vm.HasReply);
        settings.Enabled = false; vm.Configure(settings, true);
        await vm.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("Опрос выключен", vm.StatusText); Assert.False(vm.HasReply); Assert.Empty(vm.LastReplyText);
    }

    [Fact]
    public async Task DisabledInactiveAndMissingConfiguration_DoNotPing_AndErrorsAreDifferentFromNoReply()
    {
        var reads = 0;
        var probe = new DelegateProbe((_, _, _) => { reads++; throw new InvalidOperationException("Нет разрешения ICMP"); });
        using var vm = new PingMonitorViewModel(probe, Inline);
        vm.Configure(null, true); Assert.Equal("Не настроен", vm.StatusText);
        vm.Configure(new(), true); Assert.Equal("Опрос выключен", vm.StatusText);
        vm.Configure(Settings(), false); Assert.Equal("Опрос остановлен", vm.StatusText); Assert.Equal(0, reads);
        vm.Configure(Settings(), true); await Until(() => vm.HasError);
        Assert.False(vm.NoReply); Assert.Equal("Ошибка пинга", vm.StatusText); Assert.Equal("Нет разрешения ICMP", vm.Detail);
        vm.Configure(null, false); await vm.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(vm.HasError); Assert.Empty(vm.Detail);
    }

    [Fact]
    public async Task IcmpProbe_ReachesLoopback_AndHonorsCancellation()
    {
        var probe = new IcmpPingProbe();
        var sample = await probe.ProbeAsync("127.0.0.1", 2000, default);
        Assert.True(sample.Outcome == PingOutcome.Reply, sample.Detail); Assert.NotNull(sample.RoundtripMs);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe.ProbeAsync("127.0.0.1", 2000, cancelled.Token));
    }

    [Theory]
    [InlineData("host", "\"http://switch.local/\"")]
    [InlineData("host", "\"switch.local:80\"")]
    [InlineData("host", "\"\"")]
    [InlineData("host", "null")]
    [InlineData("intervalMs", "0")]
    [InlineData("timeoutMs", "30001")]
    public void InvalidSettings_LeaveTheOpenProfileUntouched(string field, string value)
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        ProfileActionTests.LoadEmpty(vm); vm.ApplyPingSettings(Settings());
        var old = vm.RootGraph.PingMonitor; var node = vm.RootGraph.Nodes[0];
        var json = JsonNode.Parse(GraphSerializer.Serialize(vm, "profile"))!;
        json["pingMonitor"]![field] = JsonNode.Parse(value);
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(json.ToJsonString(), vm));
        Assert.Same(old, vm.RootGraph.PingMonitor); Assert.Same(node, vm.RootGraph.Nodes[0]);
    }

    [Fact]
    public void JsonRoundtripCopyAndUndo_KeepSettingsWithoutRuntimeState_AndPreservePreparationKey()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        ProfileActionTests.LoadEmpty(vm);
        var before = GraphSerializer.Serialize(vm, "profile");
        var settings = Settings(); vm.ApplyPingSettings(settings);
        var json = GraphSerializer.Serialize(vm, "profile");
        Assert.Contains("Start (ping v1)", json); Assert.DoesNotContain("lastReply", json);
        Assert.Equal(FirstRunProfileKey.Create(null, before), FirstRunProfileKey.Create(null, json));
        vm.UndoCommand.Execute(null); Assert.Null(vm.RootGraph.PingMonitor);
        vm.RedoCommand.Execute(null); Assert.Equal(settings.Host, vm.RootGraph.PingMonitor!.Host);
        var saved = vm.RootGraph.PingMonitor;
        GraphSerializer.Deserialize(json, vm); Assert.NotSame(saved, vm.RootGraph.PingMonitor);
        Assert.True(vm.RootGraph.PingMonitor!.ShowInOperator); Assert.Equal(250, vm.RootGraph.PingMonitor.IntervalMs);
        vm.RootGraph.PingMonitor.Host = "different.local"; Assert.Equal("switch.local", saved.Host);
        GraphSerializer.Deserialize(before, vm); Assert.Null(vm.RootGraph.PingMonitor); Assert.False(vm.ShowOperatorPing);
        var action = GraphSerializer.ExportActionGraph(ProfileActionDefinition.EmptyGraph("action"));
        action.PingMonitor = Settings(); Assert.Throws<InvalidOperationException>(() => GraphSerializer.ImportActionGraph(action));
        var nested = new TestBuilder.ViewModels.StepVM.SubtestNodeViewModel(); nested.BodyGraph.PingMonitor = Settings();
        vm.RootGraph.Nodes.Add(nested);
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Serialize(vm, "nested"));
    }

    [Fact]
    public void Editor_ValidatesDraftAndDoesNotMutateOriginal()
    {
        var source = Settings(); var editor = new PingSettingsViewModel(source);
        editor.Host = "  192.0.2.25  "; editor.IntervalMs = 2000; editor.ShowInEngineer = false;
        var result = editor.BuildResult(); Assert.Equal("192.0.2.25", result.Host); Assert.Equal(2000, result.IntervalMs);
        Assert.False(result.ShowInEngineer); Assert.True(source.ShowInEngineer); Assert.Equal("switch.local", source.Host);
        editor.IntervalMs = 250.5m; Assert.Throws<InvalidOperationException>(() => editor.BuildResult());
        editor.IntervalMs = null; Assert.Throws<InvalidOperationException>(() => editor.BuildResult());
    }

    internal static async Task Until(Func<bool> ready)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < until) await Task.Delay(10);
        Assert.True(ready());
    }
    private sealed class DelegateProbe(Func<string, int, CancellationToken, Task<PingSample>> action) : IPingProbe
    { public Task<PingSample> ProbeAsync(string host, int timeoutMs, CancellationToken token) => action(host, timeoutMs, token); }
    private sealed class ControlledProbe : IPingProbe
    {
        public ConcurrentQueue<(string Host, CancellationToken Token, TaskCompletionSource<PingSample> Result)> Calls { get; } = new();
        public int MaxActive;
        private int _active;
        public async Task<PingSample> ProbeAsync(string host, int timeoutMs, CancellationToken token)
        {
            var active = Interlocked.Increment(ref _active); MaxActive = Math.Max(MaxActive, active);
            var result = new TaskCompletionSource<PingSample>(TaskCreationOptions.RunContinuationsAsynchronously);
            Calls.Enqueue((host, token, result));
            try { return await result.Task; } finally { Interlocked.Decrement(ref _active); }
        }
    }
}
