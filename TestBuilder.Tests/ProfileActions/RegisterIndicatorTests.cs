using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.ProfileActions;

public class RegisterIndicatorTests
{
    internal static HardwareConfiguration Hardware() => new() { PollIntervalMs = 100, Devices = new() { new() {
        SlaveId = 42, Name = "Блок питания", IdentificationValue = 77,
        Registers = new() { new() { Address = 123, Name = "Напряжение", Unit = "В" },
            new() { Address = 123, Area = ModbusArea.Input, Name = "Ток", Unit = "А" } } } } };
    internal static RegisterIndicatorDefinition Indicator() => new() {
        Name = "Напряжение БП", SlaveId = 42, Address = 123, Divisor = 10, DecimalPlaces = 1,
        Unit = "В", Minimum = 24, Maximum = 25, Color = "system" };

    [Fact]
    public void CacheDisplay_ScalesAndFormats_AndComparesUnroundedValues_InCorrectAreaAndSlave()
    {
        var state = new RegisterState(); var definition = Indicator();
        var vm = new RegisterIndicatorViewModel(definition); var hardware = Hardware();
        state.Update(41, 123, 99); state.Update(42, 123, 999, ModbusArea.Input);
        vm.Refresh(state, hardware, true, true); Assert.False(vm.HasValue);
        foreach (var (raw, status) in new[] { (240, "В допуске"), (250, "В допуске"), (239, "Ниже допуска"), (251, "Выше допуска") })
        {
            state.Update(42, 123, raw); vm.Refresh(state, hardware, true, true);
            Assert.True(vm.HasValue);
            Assert.Equal((raw / 10m).ToString("F1", CultureInfo.CurrentCulture) + " В", vm.ValueText);
            Assert.Equal(status, vm.StatusText); Assert.Equal(raw < 240 || raw > 250, vm.IsOutOfRange);
        }
        definition.DecimalPlaces = 0;
        vm.Refresh(state, hardware, true, true);
        Assert.Equal("25 В", vm.ValueText); Assert.True(vm.IsOutOfRange);
        definition.Minimum = null; definition.Maximum = null;
        state.Update(42, 123, 0); vm.Refresh(state, hardware, true, true);
        Assert.True(vm.HasValue); Assert.Equal("0 В", vm.ValueText); Assert.Equal("", vm.StatusText);
    }

    [Fact]
    public void DisconnectStoppedPollingInvalidatedAndStaleSamples_NeverShowLiveNumbers()
    {
        var state = new RegisterState(); var vm = new RegisterIndicatorViewModel(Indicator()); var hardware = Hardware();
        var now = Stopwatch.GetTimestamp(); state.Update(42, 123, 245, startedAt: now);
        vm.Refresh(state, hardware, true, true, now); Assert.True(vm.HasValue);
        vm.Refresh(state, hardware, false, true, now); Assert.Equal("Нет связи", vm.StatusText); Assert.Equal("—", vm.ValueText);
        vm.Refresh(state, hardware, true, false, now); Assert.Equal("Опрос остановлен", vm.StatusText);
        vm.Refresh(state, hardware, true, true, now + 11 * Stopwatch.Frequency); Assert.Equal("Данные устарели", vm.StatusText);
        hardware.PollIntervalMs = 60000;
        vm.Refresh(state, hardware, true, true, now + 120 * Stopwatch.Frequency); Assert.True(vm.HasValue);
        state.Invalidate(42); vm.Refresh(state, hardware, true, true, now); Assert.Equal("Нет данных", vm.StatusText);
        hardware.Devices[0].Registers.Clear(); vm.Refresh(state, hardware, true, true, now);
        Assert.Contains("отсутствует", vm.StatusText); Assert.False(vm.HasValue); Assert.False(vm.IsOutOfRange);
    }

    [Theory]
    [InlineData("divisor", "0")]
    [InlineData("decimalPlaces", "7")]
    [InlineData("slaveId", "0")]
    [InlineData("address", "65536")]
    [InlineData("area", "99")]
    [InlineData("unit", "null")]
    [InlineData("color", "\"not-a-color\"")]
    [InlineData("minimum", "100")]
    public void InvalidIndicator_IsRejectedAtomically(string field, string jsonValue)
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        ProfileActionTests.LoadEmpty(vm); var definition = Indicator(); vm.RootGraph.RegisterIndicators.Add(definition);
        var node = vm.RootGraph.Nodes[0];
        var dto = JsonNode.Parse(GraphSerializer.Serialize(vm, "test"))!;
        dto["registerIndicators"]![0]![field] = JsonNode.Parse(jsonValue);
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(dto.ToJsonString(), vm));
        Assert.Same(node, vm.RootGraph.Nodes[0]); Assert.Same(definition, Assert.Single(vm.RootGraph.RegisterIndicators));
    }

    [Fact]
    public void ProfileRoundtripAndCopy_KeepSettingsOnly_AndDoNotInvalidatePreparation()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        ProfileActionTests.LoadEmpty(vm); vm.RootGraph.Hardware = Hardware();
        vm.RootGraph.QuickActions.Add(new() { Name = "Питание" });
        var before = GraphSerializer.Serialize(vm, "test");
        var definition = Indicator(); vm.RootGraph.RegisterIndicators.Add(definition);
        var json = GraphSerializer.Serialize(vm, "test");
        Assert.Contains("Start (indicators v1)", json);
        Assert.Equal(FirstRunProfileKey.Create(null, before), FirstRunProfileKey.Create(null, json));
        Assert.DoesNotContain("valueText", json, StringComparison.OrdinalIgnoreCase);
        GraphSerializer.Deserialize(json, vm);
        var copy = Assert.Single(vm.RootGraph.RegisterIndicators);
        Assert.NotSame(definition, copy); Assert.Equal(definition.Id, copy.Id);
        Assert.Equal(10, copy.Divisor); Assert.Equal(1, copy.DecimalPlaces); Assert.Equal(24, copy.Minimum);
        Assert.Equal("system", copy.Color); Assert.Equal("В", copy.Unit); Assert.Equal(42, copy.SlaveId);
        copy.Name = "Другое имя"; copy.Divisor = 100; copy.Minimum = 2;
        Assert.Equal("Напряжение БП", definition.Name);
        Assert.Equal(FirstRunProfileKey.Create(null, before), FirstRunProfileKey.Create(null, GraphSerializer.Serialize(vm, "test")));
        vm.RootGraph.Hardware!.PollIntervalMs = 200;
        Assert.NotEqual(FirstRunProfileKey.Create(null, before), FirstRunProfileKey.Create(null, GraphSerializer.Serialize(vm, "test")));
        GraphSerializer.Deserialize(before, vm); Assert.Empty(vm.RootGraph.RegisterIndicators); Assert.Empty(vm.RegisterIndicators);
    }

    [Fact]
    public void DuplicateNullAndNestedIndicators_AreRejected()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        ProfileActionTests.LoadEmpty(vm); vm.RootGraph.RegisterIndicators.Add(Indicator());
        var dto = JsonNode.Parse(GraphSerializer.Serialize(vm, "test"))!;
        var cards = dto["registerIndicators"]!.AsArray(); cards.Add(cards[0]!.DeepClone());
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(dto.ToJsonString(), vm));
        cards.RemoveAt(1); cards[0] = null;
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(dto.ToJsonString(), vm));
        var subtest = new SubtestNodeViewModel(); subtest.BodyGraph.RegisterIndicators.Add(Indicator());
        vm.RootGraph.Nodes.Add(subtest);
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Serialize(vm, "test"));
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.ExportActionGraph(subtest.BodyGraph));
        var actionDto = GraphSerializer.ExportActionGraph(ProfileActionDefinition.EmptyGraph("Кнопка"));
        actionDto.RegisterIndicators = new() { Indicator() };
        Assert.Throws<InvalidOperationException>(() => GraphSerializer.ImportActionGraph(actionDto));
    }

    [Fact]
    public void OfflineEditor_UsesProfileRegisters_ChangesScaleWithoutMutatingSource_AndValidatesSelection()
    {
        var original = Indicator(); var hardware = Hardware();
        var editor = new RegisterIndicatorEditorViewModel(original, hardware);
        Assert.Equal(42, editor.SelectedDevice!.SlaveId); Assert.Equal(ModbusArea.Holding, editor.SelectedRegister!.Register.Area);
        editor.Divisor = 100; Assert.Equal(2, editor.DecimalPlaces);
        editor.SelectedRegister = null; // typing a new search or changing the slave clears selection first
        editor.SelectedRegister = editor.Registers[1];
        Assert.Equal("Напряжение БП", editor.Name); Assert.Equal("А", editor.Unit);
        editor.Minimum = 1; editor.Maximum = 2;
        editor.SelectedColor = editor.Colors[1]; var copy = editor.BuildResult();
        Assert.Equal(ModbusArea.Input, copy.Area); Assert.Equal(100, copy.Divisor); Assert.Equal("#2563EB", copy.Color);
        Assert.Equal(10, original.Divisor); Assert.Equal("system", original.Color);
        editor.Color = "system"; Assert.Equal("Системный", editor.SelectedColor!.Name);
        editor.Minimum = 3; Assert.Throws<InvalidOperationException>(() => editor.BuildResult());
        editor.Minimum = null; editor.Maximum = null; Assert.Null(editor.BuildResult().Minimum);
        var missing = new RegisterIndicatorEditorViewModel(original, new() { Devices = new() { new() { SlaveId = 5 } } });
        Assert.Null(missing.SelectedDevice); Assert.Throws<InvalidOperationException>(() => missing.BuildResult());
        var empty = new RegisterIndicatorEditorViewModel(original, null); Assert.False(empty.HasEquipment);
        Assert.Throws<InvalidOperationException>(() => empty.BuildResult());
    }
}
