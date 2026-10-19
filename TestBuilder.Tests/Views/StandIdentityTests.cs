using System.Text.Json;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;

namespace TestBuilder.Tests.Views;

[Collection("Graph editor UI")]
public class StandIdentityTests
{
    [Theory]
    [InlineData("RTL-SW", 2300)]
    [InlineData("RTL-POE", 2400)]
    public void ConnectedControllerShowsLiveSerial_AndDisconnectClearsIt(string name, int address)
    {
        using var bus = new ModbusService();
        var manager = new SlaveManager(bus);
        using var vm = new TestViewModel(bus, manager);
        var device = new DeviceDefinition { Name = name, StandSerialNumberAddress = (ushort)address,
            Registers = new() { new() { Address = address, Name = "Номер" } } };
        vm.ApplyHardware(new HardwareConfiguration { StandName = name, Devices = new() { device } });
        Assert.Equal("Стенд " + name, vm.StandSectionTitle);
        Assert.False(vm.HasStandSerial);
        var model = new ConfiguredSlaveModel(device, bus);
        model.RegisterItems[0].Value = 3;
        manager.Slaves.Add(model);
        Assert.False(vm.HasStandSerial);
        vm.IsConnected = true;
        Assert.Equal("Серийный номер стенда: 3", vm.StandSerialCaption);
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        model.RegisterItems[0].Value = 7;
        Assert.Equal("Серийный номер стенда: 7", vm.StandSerialCaption);
        Assert.Contains(nameof(vm.StandSerialCaption), notifications);
        vm.IsConnected = false;
        Assert.False(vm.HasStandSerial);
        Assert.Empty(vm.StandSerialCaption);
        notifications.Clear(); model.RegisterItems[0].Value = 9;
        Assert.DoesNotContain(nameof(vm.StandSerialCaption), notifications);
        manager.Slaves.Clear(); vm.IsConnected = true;
        Assert.False(vm.HasStandSerial);
    }

    [Fact]
    public void DiscoveryWithoutProfileAndOldRegisterMap_UseRealController_NotProductOrReportId()
    {
        using var bus = new ModbusService(); var manager = new SlaveManager(bus);
        using var vm = new TestViewModel(bus, manager);
        Assert.Equal("Стенд АПК-03", vm.StandSectionTitle);
        vm.DeviceName = "Проверяемое изделие";
        var device = new DeviceDefinition { Name = "RTL-SW", Registers = new() {
            new() { Address = 2300, Name = "Серийный номер стенда" } } };
        var model = new ConfiguredSlaveModel(device, bus);
        model.RegisterItems[0].Value = 3;
        manager.Slaves.Add(model); vm.IsConnected = true;
        Assert.Equal("Стенд RTL-SW", vm.StandSectionTitle);
        Assert.Equal("Серийный номер стенда: 3", vm.StandSerialCaption);
        manager.Slaves.Clear();
        Assert.Empty(vm.StandSerialCaption);
        Assert.Equal("Стенд АПК-03", vm.StandSectionTitle);
    }

    [Fact]
    public void IdentityMetadataRoundTrips_AndSourceMustExistInPolledRegisters()
    {
        var hardware = new HardwareConfiguration { StandName = "Линия 4", Devices = new() {
            new() { Name = "Контроллер", StandSerialNumberAddress = 42,
                Registers = new() { new() { Address = 42, Name = "Номер" } } } } };
        hardware.Validate();
        var copy = JsonSerializer.Deserialize<HardwareConfiguration>(JsonSerializer.Serialize(hardware))!;
        Assert.Equal("Линия 4", copy.StandName);
        Assert.Equal((ushort)42, copy.Devices[0].StandSerialNumberAddress);
        copy.Devices[0].StandSerialNumberAddress = 43;
        Assert.Throws<InvalidOperationException>(copy.Validate);
    }
}
