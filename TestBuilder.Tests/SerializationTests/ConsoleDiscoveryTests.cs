using System.Text.Json;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;

namespace TestBuilder.Tests.SerializationTests;

public class ConsoleDiscoveryTests
{
    private static ConsoleDeviceDefinition Automatic() => new()
    { Id = "dut", Name = "Процессор DUT", PortDescriptionContains = "CH340", UsbVendorId = "1A86", UsbProductId = "7523" };

    [Fact]
    public void MatchesWindowsDescriptionOrUsbIds_ExcludesStand_AndNeverChoosesArbitrarily()
    {
        var device = Automatic();
        var ports = new[] { new SerialPortInfo("COM1", "USB Serial CH340 (COM1)"),
            new SerialPortInfo("COM2", "USB2.0-Serial", "1a86", "7523"), new SerialPortInfo("COM3", "Other device") };
        Assert.Equal("COM2", ConsolePortDiscovery.Resolve(device, ports, new[] { "COM1" }));
        Assert.Throws<InvalidOperationException>(() => ConsolePortDiscovery.Resolve(device, ports, Array.Empty<string>()));
        Assert.Throws<TimeoutException>(() => ConsolePortDiscovery.Resolve(device, ports, new[] { "COM1", "COM2" }));
        Assert.Throws<TimeoutException>(() => ConsolePortDiscovery.Resolve(device, new[] { ports[2] }, Array.Empty<string>()));
        device.PortName = "COM3";
        Assert.Equal("COM3", ConsolePortDiscovery.Resolve(device, ports, Array.Empty<string>()));
        Assert.Throws<InvalidOperationException>(() => ConsolePortDiscovery.Resolve(device, ports, new[] { "COM3" }));
    }

    [Fact]
    public async Task LateAdapterIsFoundOnRetry_AndSelectedPortStaysStableForTheRun()
    {
        var catalog = new Catalog();
        using var session = new SerialConsoleSession(catalog, _ => new[] { "COM-STAND" });
        var device = Automatic();
        await Assert.ThrowsAsync<TimeoutException>(() => session.ResolveDeviceAsync(device, default));
        catalog.Ports = new[] { new SerialPortInfo("COM-STAND", "CH340"), new SerialPortInfo("COM-DUT", "CH340") };
        var resolved = await session.ResolveDeviceAsync(device, default);
        Assert.Equal("COM-DUT", resolved.PortName);
        Assert.Equal("", device.PortName); // Discovery must not modify the portable profile.
        catalog.Ports = new[] { new SerialPortInfo("COM-ANOTHER", "CH340") };
        Assert.Equal("COM-DUT", (await session.ResolveDeviceAsync(device, default)).PortName);
        Assert.Equal(2, catalog.Calls);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ResolveDeviceAsync(device, cancellation.Token));
        Assert.Equal(2, catalog.Calls);
    }

    [Fact]
    public async Task ExplicitPortDoesNotEnumerateUsbDevices()
    {
        var catalog = new Catalog();
        using var session = new SerialConsoleSession(catalog, _ => Array.Empty<string>());
        Assert.Equal("COM7", (await session.ResolveDeviceAsync(Automatic() with { PortName = "COM7" }, default)).PortName);
        Assert.Equal(0, catalog.Calls);
    }

    [Theory]
    [InlineData("1A86", "")][InlineData("", "7523")][InlineData("invalid", "7523")][InlineData("1A86\n", "7523")]
    public void InvalidUsbRulesAreRejected(string vendor, string product)
    {
        var device = Automatic() with { UsbVendorId = vendor, UsbProductId = product };
        Assert.Throws<InvalidOperationException>(() => device.Validate());
    }

    [Fact]
    public void WindowsDescriptorsParseUsbIdsIncludingCompositeInterface()
    {
        var port = SystemSerialPortCatalog.FromWindowsDescriptor("COM4", "USB-SERIAL CH340 (COM4)", @"USB\VID_1A86&PID_7523&MI_00");
        Assert.Equal("1A86", port.VendorId); Assert.Equal("7523", port.ProductId);
        Assert.True(ConsolePortDiscovery.Matches(Automatic(), port));
    }

    [Fact]
    public void MacCalloutPortsInheritUsbIdentity_WithoutChoosingDialInDuplicate()
    {
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><array><dict>
            <key>idVendor</key><integer>6790</integer><key>idProduct</key><integer>29987</integer>
            <key>USB Product Name</key><string>USB2.0-Serial</string>
            <key>IORegistryEntryChildren</key><array><dict><key>IORegistryEntryChildren</key><array><dict>
            <key>IODialinDevice</key><string>/dev/tty.usbserial-1</string>
            <key>IOCalloutDevice</key><string>/dev/cu.usbserial-1</string>
            </dict></array></dict></array></dict></array></plist>
            """;
        var ports = SystemSerialPortCatalog.ParseMacPorts(xml);
        Assert.Equal("/dev/cu.usbserial-1", ConsolePortDiscovery.Resolve(Automatic(), ports, Array.Empty<string>()));
        Assert.Single(ports);
    }

    [Fact]
    public void ShippedSwProfileContainsBothConnections_AndRunsPreflightWithoutEnteringDutPort()
    {
        using var bus = new ModbusService(); using var vm = new TestViewModel(bus, new SlaveManager(bus));
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles/RTL_SW_board.json"));
        GraphSerializer.Deserialize(File.ReadAllText(file), vm);
        GraphCompiler.ValidateRunConnections(vm.RootGraph, vm.RootGraph.Hardware, "COM-STAND");
        var hardware = vm.RootGraph.Hardware!;
        Assert.Equal(9600, hardware.BaudRate);
        Assert.Equal((ushort)104, Assert.Single(hardware.Devices).IdentificationValue);
        var dut = Assert.Single(hardware.ConsoleDevices);
        Assert.Equal("", dut.PortName); Assert.Equal("CH340", dut.PortDescriptionContains);
        Assert.Equal("1A86", dut.UsbVendorId); Assert.Equal("7523", dut.UsbProductId);
        Assert.Equal(115200, dut.BaudRate); Assert.True(dut.HasAutoDetection);
        var roundtrip = JsonSerializer.Deserialize<HardwareConfiguration>(JsonSerializer.Serialize(hardware))!;
        Assert.Equal(dut, Assert.Single(roundtrip.ConsoleDevices));
    }

    private sealed class Catalog : ISerialPortCatalog
    {
        public IReadOnlyList<SerialPortInfo> Ports = Array.Empty<SerialPortInfo>();
        public int Calls;
        public Task<IReadOnlyList<SerialPortInfo>> GetPortsAsync(CancellationToken token) { Calls++; return Task.FromResult(Ports); }
    }
}
