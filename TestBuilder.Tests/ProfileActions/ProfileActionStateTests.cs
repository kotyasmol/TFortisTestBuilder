using System.Diagnostics;
using System.Text.Json.Nodes;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;

namespace TestBuilder.Tests.ProfileActions;

public class ProfileActionStateTests
{
    internal static HardwareConfiguration Hardware() => new() { Devices = new() {
        new() { SlaveId = 42, Registers = new() {
            new() { Address = 123, Name = "Обратная связь" },
            new() { Address = 123, Area = ModbusArea.Input, Name = "Вход" } } } } };
    internal static ProfileActionStateBinding Binding() => new() {
        On = new() { Conditions = new() { new() { SlaveId = 42, Address = 123, Value = 1 } } },
        Off = new() { Conditions = new() { new() { SlaveId = 42, Address = 123, Value = 0 } } }
    };

    [Theory]
    [InlineData(RegisterComparison.Equal, 10, true)]
    [InlineData(RegisterComparison.NotEqual, 10, false)]
    [InlineData(RegisterComparison.Greater, 10, false)]
    [InlineData(RegisterComparison.GreaterOrEqual, 10, true)]
    [InlineData(RegisterComparison.Less, 11, true)]
    [InlineData(RegisterComparison.LessOrEqual, 10, true)]
    public void Comparisons_UseMaskedRawValue(RegisterComparison op, int target, bool expected)
    {
        var condition = new RegisterStateCondition { Mask = 15, Comparison = op, Value = target };
        Assert.Equal(expected, condition.Matches(0x1a));
    }

    [Fact]
    public void Rules_RequireFreshMappedSources_AndExactlyOneMatchingState()
    {
        var state = new RegisterState(); var hardware = Hardware(); var binding = Binding();
        var now = Stopwatch.GetTimestamp();
        ProfileActionState Read(bool connected = true, bool monitoring = true) =>
            ProfileActionStateEvaluator.Evaluate(binding, state, hardware, connected, monitoring, now: now);
        Assert.Null(Read().IsOn);
        state.Update(42, 123, 1, ModbusArea.Input, now);
        Assert.Null(Read().IsOn); // Same address in another area cannot supply feedback.
        state.Update(42, 123, 1, startedAt: now);
        Assert.True(Read().IsOn);
        Assert.Null(Read(connected: false).IsOn); Assert.Null(Read(monitoring: false).IsOn);
        state.Update(42, 123, 0, startedAt: now);
        Assert.False(Read().IsOn);
        state.Update(42, 123, 2, startedAt: now);
        Assert.Contains("Ни одно", Read().Reason);
        binding.Off.Conditions[0].Value = 2; binding.On.Conditions[0].Value = 2;
        Assert.Contains("Оба", Read().Reason);
        state.Update(42, 123, 2, startedAt: now - 11 * Stopwatch.Frequency);
        Assert.Null(Read().IsOn);
        state.Update(42, 123, 2, startedAt: now); state.Invalidate(42);
        Assert.Null(Read().IsOn);
        state.Update(42, 123, 2, startedAt: now); hardware.Devices.Clear();
        Assert.Contains("отсутствует", Read().Reason);
    }

    [Fact]
    public void MultipleSources_AllAny_AndMissingAnyOperand()
    {
        var state = new RegisterState(); var binding = Binding();
        binding.On.Conditions.Add(new() { SlaveId = 42, Area = ModbusArea.Input, Address = 123, Value = 8, Mask = 8 });
        state.Update(42, 123, 1); state.Update(42, 123, 0, ModbusArea.Input);
        ProfileActionState Read() => ProfileActionStateEvaluator.Evaluate(binding, state, Hardware(), true, true);
        Assert.Null(Read().IsOn);
        binding.On.Match = StateRuleMatch.Any; Assert.True(Read().IsOn);
        state.Update(42, 123, 24, ModbusArea.Input);
        binding.On.Match = StateRuleMatch.All; Assert.True(Read().IsOn);
        binding.On.Match = StateRuleMatch.Any;
        state.Clear(); state.Update(42, 123, 1);
        Assert.Null(Read().IsOn);
    }

    [Fact]
    public async Task Toggle_UsesFeedbackForDirection_AndWaitsForPostCommandRead()
    {
        var state = new RegisterState(); var hardware = Hardware(); var directions = new List<bool>();
        ProfileActionViewModel? vm = null;
        void Refresh() => vm!.RefreshFeedback(state, hardware, true, true);
        vm = new(new() { Display = ProfileActionDisplay.Toggle, StateBinding = Binding() },
            (_, off, _, _) => { directions.Add(off); return Task.FromResult(new ProfileActionResult(true, "OK")); }, () => true, () => { }, Refresh);
        Assert.False(vm.CanRun);
        state.Update(42, 123, 1); Refresh();
        Assert.True(vm.IsOn); Assert.True(vm.CanRun);
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(new[] { true }, directions); Assert.False(vm.IsStateKnown); Assert.False(vm.CanRun);
        Refresh(); Assert.False(vm.IsStateKnown); // Pre-command cache does not confirm completion.
        state.Update(42, 123, 0); Refresh();
        Assert.False(vm.IsOn); Assert.True(vm.IsStateKnown);
        // An external change between UI refresh and click still chooses the correct graph.
        state.Update(42, 123, 1);
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(new[] { true, true }, directions);
        state.Update(42, 123, 0); Refresh();
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(new[] { true, true, false }, directions);
        state.Update(42, 123, 1); Refresh();
        state.Clear(); await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(3, directions.Count); Assert.False(vm.CanRun);
    }

    [Fact]
    public async Task Failure_PreservesErrorButFreshFeedbackDeterminesState_AndNextDirection()
    {
        var state = new RegisterState(); var directions = new List<bool>();
        var vm = new ProfileActionViewModel(new() { Display = ProfileActionDisplay.Toggle, StateBinding = Binding() },
            (_, off, _, _) => { directions.Add(off); return Task.FromResult(new ProfileActionResult(false, "Сбой графа")); }, () => true, () => { });
        state.Update(42, 123, 0); vm.RefreshFeedback(state, Hardware(), true, true);
        await vm.RunCommand.ExecuteAsync(null);
        Assert.False(vm.IsStateKnown);
        state.Update(42, 123, 1); vm.RefreshFeedback(state, Hardware(), true, true);
        Assert.True(vm.IsOn); Assert.Equal("Сбой графа", vm.ErrorMessage);
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(new[] { false, true }, directions);
    }

    [Fact]
    public void Serialization_CopiesRules_ProtectsFromOlderRuntimes_AndRejectsInvalidRulesAtomically()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        ProfileActionTests.LoadEmpty(vm); vm.RootGraph.Hardware = Hardware();
        vm.RootGraph.QuickActions.Add(new() { Display = ProfileActionDisplay.Toggle, StateBinding = Binding() });
        // Later feature markers must not overwrite the state-binding compatibility marker.
        vm.RootGraph.RegisterIndicators.Add(new() { SlaveId = 42, Address = 123 });
        var json = GraphSerializer.Serialize(vm, "test");
        Assert.Contains("Start (action state v1)", json);
        using var copy = new TestViewModel(bus, new SlaveManager(bus)); GraphSerializer.Deserialize(json, copy);
        var rule = copy.RootGraph.QuickActions[0].StateBinding!;
        Assert.Equal(1, rule.On.Conditions[0].Value);
        rule.On.Conditions[0].Value = 8;
        Assert.Equal(1, vm.RootGraph.QuickActions[0].StateBinding!.On.Conditions[0].Value);
        foreach (var invalid in new[] { "empty", "null", "mask", "comparison", "address" })
        {
            var dto = JsonNode.Parse(json)!;
            var on = dto["quickActions"]![0]!["stateBinding"]!["on"]!;
            if (invalid == "empty") on["conditions"] = new JsonArray();
            else if (invalid == "null") on["conditions"] = null;
            else if (invalid == "mask") on["conditions"]![0]!["mask"] = 0;
            else if (invalid == "address") on["conditions"]![0]!["address"] = -1;
            else on["conditions"]![0]!["comparison"] = 100;
            var previous = copy.RootGraph.QuickActions[0];
            Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(dto.ToJsonString(), copy));
            Assert.Same(previous, copy.RootGraph.QuickActions[0]);
        }
    }
}
