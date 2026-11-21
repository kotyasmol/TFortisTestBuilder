using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Views;

namespace TestBuilder.Tests.MonitoringTests;

[Collection("Graph editor UI")]
public class Apk03RegisterMapTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
    private static JsonDocument Source() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,
        "docs/references/modbus/stand-apk-03-registers-2026-11-19.json")));

    [Fact]
    public void ArchivedPrimaryDocumentMatchesTheExtractedSourceSnapshot()
    {
        using var document = File.OpenRead(Path.Combine(Root, "docs/references/modbus/Карта регистров СТЕНД-АПК-03.docx"));
        using var source = Source();
        Assert.Equal(source.RootElement.GetProperty("SourceSha256").GetString(), Convert.ToHexString(SHA256.HashData(document)).ToLowerInvariant());
    }

    [Theory]
    [InlineData("EL-60v5", 23)] [InlineData("PS-3", 7)]
    [InlineData("SIMBAT-24", 13)] [InlineData("IO-2", 30)]
    public void SuppliedAndEmbeddedMapsMatchEveryDocumentedAddressAndAccessRight(string name, int count)
    {
        using var source = Source();
        var expected = source.RootElement.GetProperty("Devices").EnumerateArray().Single(d => d.GetProperty("Name").GetString() == name)
            .GetProperty("Registers").EnumerateArray().Select(r => (Address: r.GetProperty("Address").GetInt32(), Writable: r.GetProperty("Writable").GetBoolean()))
            .OrderBy(r => r.Address).ToArray();
        using var embedded = typeof(HardwarePresets).Assembly.GetManifestResourceStream("TestBuilder.hardware-presets.json")!;
        var fallback = JsonSerializer.Deserialize<List<DeviceDefinition>>(embedded)!;
        foreach (var map in new[] { HardwarePresets.Load().Single(d => d.Name == name), fallback.Single(d => d.Name == name) })
        {
            new HardwareConfiguration { Devices = new() { map } }.Validate();
            Assert.Equal(count, map.Registers.Count);
            Assert.Equal(expected, map.Registers.Select(r => (r.Address, r.Writable)).OrderBy(r => r.Address));
            Assert.All(map.Registers, r => Assert.Equal(ModbusArea.Holding, r.Area));
            Assert.Equal(source.RootElement.GetProperty("SourceSha256").GetString(),
                map.ExtensionData!["RegisterMapSource"].GetProperty("Sha256").GetString());
        }
    }

    [Fact]
    public void BaseConfigurationUsesTheDocumentedSlotsAndTwoSimbat24Boards()
    {
        var config = JsonSerializer.Deserialize<HardwareConfiguration>(File.ReadAllText(Path.Combine(Root, "reference/modbus/stand-apk-03.json")))!;
        config.Validate();
        Assert.Equal(Enumerable.Range(0, 12).Select(i => (byte)(i * 2 + 1)), config.Devices.Select(d => d.SlaveId));
        Assert.All(config.Devices.Take(8), d => { Assert.Equal("EL-60v5", d.Name); Assert.Equal(23, d.Registers.Count); });
        Assert.All(config.Devices.Skip(8).Take(2), d => { Assert.Equal("SIMBAT-24", d.Name); Assert.Equal((ushort)9, d.IdentificationValue); Assert.Equal(13, d.Registers.Count); });
        Assert.Equal("IO-2", config.Devices[10].Name); Assert.Equal(30, config.Devices[10].Registers.Count);
        Assert.Equal("PS-3", config.Devices[11].Name); Assert.Equal(7, config.Devices[11].Registers.Count);
        var restored = JsonSerializer.Deserialize<HardwareConfiguration>(JsonSerializer.Serialize(config))!;
        Assert.Equal(JsonSerializer.Serialize(config), JsonSerializer.Serialize(restored));
    }

    [Theory]
    [InlineData("EL-60v5")] [InlineData("PS-3")] [InlineData("SIMBAT-24")] [InlineData("IO-2")]
    public async Task PollingDoesNotReadRemovedAddressesOrShiftValuesAcrossGaps(string name)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            using var source = Source();
            var allowed = source.RootElement.GetProperty("Devices").EnumerateArray().Single(d => d.GetProperty("Name").GetString() == name)
                .GetProperty("Registers").EnumerateArray().Select(r => r.GetProperty("Address").GetInt32()).ToHashSet();
            var bus = new MapBus(allowed);
            var model = new ConfiguredSlaveModel(HardwarePresets.Load().Single(d => d.Name == name), bus);
            await model.PollAsync();
            Assert.Equal(allowed.OrderBy(a => a), bus.Reads.SelectMany(r => Enumerable.Range(r.Start, r.Count)).OrderBy(a => a));
            Assert.All(model.RegisterItems, r => Assert.Equal(r.Address, r.Value));
            Assert.Equal(name == "EL-60v5" ? 2 : name == "IO-2" ? 1 : 3, bus.Reads.Count);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public void OfflineReferenceUsesCurrentMapsWhileExplicitOldProfileMapsStayIndependent()
    {
        var choices = RegisterReference.Choices(ModbusArea.Holding, false).ToArray();
        Assert.DoesNotContain(choices, r => r.Name.StartsWith("EL-60v5:") && r.Address is >= 1404 and <= 1411);
        Assert.DoesNotContain(choices, r => r.Name.StartsWith("PS-3:") && r.Address == 1204);
        Assert.DoesNotContain(choices, r => r.Name.StartsWith("SIMBAT-24:") && r.Address == 1703);
        Assert.Contains(choices, r => r.Name.StartsWith("PS-3:") && r.Address == 1216 && r.IsReadOnly);
        Assert.DoesNotContain(RegisterReference.Choices(ModbusArea.Holding, true), r => r.Name.StartsWith("PS-3:") && r.Address == 1216);
        Assert.Contains("турбовентилятора", RegisterReference.Describe(1418));
        Assert.Contains("Автономный режим", RegisterReference.Describe(1420));

        var oldProfile = new HardwareConfiguration { Devices = new() { new() { SlaveId = 23, Name = "Моя прежняя PS-3", Registers = new()
            { new() { Address = 1204, Name = "Своё назначение", Writable = true, Unit = "Ом" } } } } };
        var snapshot = JsonSerializer.Serialize(oldProfile);
        var restored = JsonSerializer.Deserialize<HardwareConfiguration>(snapshot)!;
        var selected = Assert.Single(RegisterReference.Choices(ModbusArea.Holding, true, hardware: restored, slaveId: 23));
        Assert.Equal(1204, selected.Address); Assert.Contains("Своё назначение, Ом", selected.Name);
        Assert.Equal(snapshot, JsonSerializer.Serialize(restored));
    }

    private sealed class MapBus(HashSet<int> allowed) : IModbusService
    {
        public List<(int Start, int Count)> Reads { get; } = new();
        public Task<ushort[]> ReadAreaAsync(byte slave, ushort address, ushort count, ModbusArea area, CancellationToken ct = default)
        {
            Assert.Equal(ModbusArea.Holding, area);
            Assert.All(Enumerable.Range(address, count), a => Assert.Contains(a, allowed));
            Reads.Add((address, count));
            return Task.FromResult(Enumerable.Range(address, count).Select(a => (ushort)a).ToArray());
        }
        public Task<ushort[]> ReadRegistersAsync(byte slave, ushort address, ushort count, CancellationToken ct = default) => ReadAreaAsync(slave, address, count, ModbusArea.Holding, ct);
        public Task<bool> WriteRegisterAsync(byte slave, ushort address, ushort value, bool verify = true, CancellationToken ct = default) => throw new InvalidOperationException("This test must never write to hardware.");
        public Task<bool> CheckPortAsync(CancellationToken ct = default) => Task.FromResult(true);
        public void SubscribeRegister(byte slave, ushort address, Action<ushort[]> callback) { }
    }
}
