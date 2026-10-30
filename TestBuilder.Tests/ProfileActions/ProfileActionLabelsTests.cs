using System.Text.Json.Nodes;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;

namespace TestBuilder.Tests.ProfileActions;

public class ProfileActionLabelsTests
{
    [Fact]
    public async Task ReprintStyleButton_UsesCustomRunningAndResultLabels_AndEmptyLabelsHaveNoFallback()
    {
        var labels = new ProfileActionLabels { Ready = "", Running = "Печать…", Success = "Отправлено принтеру", Error = "Печать не выполнена" };
        var gate = new TaskCompletionSource<ProfileActionResult>();
        var vm = new ProfileActionViewModel(new() { Labels = labels }, (_, _, _, _) => gate.Task, () => true, () => { });
        Assert.Equal("", vm.StateText);
        var task = vm.RunCommand.ExecuteAsync(null);
        Assert.Equal("Печать…", vm.StateText);
        gate.SetResult(new(true, "OK")); await task;
        Assert.Equal("Отправлено принтеру", vm.StateText);
        gate = new(); task = vm.RunCommand.ExecuteAsync(null);
        gate.SetResult(new(false, "Принтер недоступен")); await task;
        Assert.Equal("Печать не выполнена", vm.StateText); Assert.Equal("Принтер недоступен", vm.DetailText);

        var empty = new ProfileActionViewModel(new() { Labels = new() { Success = "", Error = "", Ready = "До запуска" } },
            (_, _, _, _) => Task.FromResult(new ProfileActionResult(true, "OK")), () => true, () => { });
        await empty.RunCommand.ExecuteAsync(null);
        Assert.Equal("", empty.StateText); // Must not fall back to Ready after a blank result.
    }

    [Fact]
    public async Task Toggle_LabelsApplyWithAndWithoutFeedback_AndDoNotChangeAvailability()
    {
        var labels = new ProfileActionLabels { Unknown = "Нет показаний", On = "Подано", Off = "Снято" };
        var vm = new ProfileActionViewModel(new() { Display = ProfileActionDisplay.Toggle, Labels = labels },
            (_, _, _, _) => Task.FromResult(new ProfileActionResult(true, "OK")), () => true, () => { });
        Assert.Equal("Нет показаний", vm.StateText);
        await vm.RunCommand.ExecuteAsync(null); Assert.Equal("Подано", vm.StateText);
        await vm.RunCommand.ExecuteAsync(null); Assert.Equal("Снято", vm.StateText);
        vm.InvalidateState(); Assert.Equal("Нет показаний", vm.StateText);

        var bound = new ProfileActionViewModel(new() { Display = ProfileActionDisplay.Toggle,
            Labels = new() { Unknown = "", On = "Подано", Off = "", Error = "" }, StateBinding = ProfileActionStateTests.Binding() },
            (_, _, _, _) => Task.FromResult(new ProfileActionResult(false, "Диагностика")), () => true, () => { });
        Assert.Equal("", bound.StateText); Assert.False(bound.CanRun);
        var state = new RegisterState(); state.Update(42, 123, 1);
        bound.RefreshFeedback(state, ProfileActionStateTests.Hardware(), true, true);
        Assert.Equal("Подано", bound.StateText); Assert.True(bound.CanRun);
        await bound.RunCommand.ExecuteAsync(null);
        Assert.Equal("", bound.StateText); Assert.Equal("Диагностика", bound.DetailText); Assert.False(bound.CanRun);
    }

    [Fact]
    public async Task HiddenStatus_StillProvidesDiagnosticTooltip_AndCancellationIsCustomizable()
    {
        var vm = new ProfileActionViewModel(new() { Labels = new() { ShowStatus = false, Cancelled = "Прервано" } },
            async (_, _, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return new(true, "OK"); }, () => true, () => { });
        Assert.False(vm.ShowStatusText);
        var task = vm.RunCommand.ExecuteAsync(null); vm.Cancel(); await task;
        Assert.Equal("", vm.StateText); Assert.Equal("Прервано", vm.DetailText);
    }

    [Fact]
    public async Task RunningTemplate_ReplacesTokensOnceWithoutInterpretingStepNames()
    {
        var gate = new TaskCompletionSource<ProfileActionResult>();
        var vm = new ProfileActionViewModel(new() { Name = "Имя {done}", Labels = new() { Running = "{step}: {done}/{total}" } },
            (_, _, _, _) => gate.Task, () => true, () => { });
        var task = vm.RunCommand.ExecuteAsync(null);
        Assert.Equal("Имя {done}: 0/2", vm.StateText);
        gate.SetResult(new(true, "OK")); await task;
    }

    [Fact]
    public void Json_CopiesLabels_AndRejectsBadTextWithoutReplacingProfile()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        ProfileActionTests.LoadEmpty(vm);
        vm.RootGraph.QuickActions.Add(new() { Labels = new() { Ready = "", Unknown = "Нет показаний", Success = "Отправлено" } });
        var json = GraphSerializer.Serialize(vm, "labels"); Assert.Contains("Start (action labels v1)", json);
        using var copy = new TestViewModel(bus, new SlaveManager(bus)); GraphSerializer.Deserialize(json, copy);
        var labels = copy.RootGraph.QuickActions[0].Labels!;
        Assert.Equal("", labels.Ready); Assert.Equal("Отправлено", labels.Success);
        labels.Success = "Другое"; Assert.Equal("Отправлено", vm.RootGraph.QuickActions[0].Labels!.Success);
        foreach (var invalid in new string?[] { null, "строка\nстрока", new string('x', 121) })
        {
            var dto = JsonNode.Parse(json)!; dto["quickActions"]![0]!["labels"]!["ready"] = invalid;
            var previous = copy.RootGraph.QuickActions[0];
            Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(dto.ToJsonString(), copy));
            Assert.Same(previous, copy.RootGraph.QuickActions[0]);
        }
    }

    [Fact]
    public void ExistingModbusLabels_ArePreservedWhenOpeningTextSettings()
    {
        var definition = new ProfileActionDefinition { StateBinding = ProfileActionStateTests.Binding() };
        definition.StateBinding.OnText = "Работает"; definition.StateBinding.OffText = "Отключено";
        Assert.Equal("Работает", definition.GetLabels().On);
        Assert.Equal("Отключено", definition.GetLabels().Off);
    }
}
