using Avalonia.Headless;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.Tests.Views;

namespace TestBuilder.Tests.MonitoringTests;

[Collection("Graph editor UI")]
public class ConfiguredHardwareTests
{
    [Fact]
    public void Apk03PresetsPreserveAllLegacyRegisterAddressesAndAccessModes()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../TestBuilder/Assets/hardware-presets.json"));
        var presets = System.Text.Json.JsonSerializer.Deserialize<List<DeviceDefinition>>(File.ReadAllText(path))!;
        var bus = new RecordingBus();
        SlaveModelBase[] legacy = {new El60Model(1,bus),new PS1Model(1,bus),new PS2Model(1,bus),
            new El60v5Model(1,bus),new IO2Model(1,bus),new StandRpsModel(1,bus),new StandPwr180Model(1,bus),
            new Ps3Model(1,bus),new Simbat24Model(1,bus),new Simbat48Model(1,bus)};
        for (var index=0;index<legacy.Length;index++)
        {
            var preset = Assert.Single(presets.Where(p=>p.IdentificationValue==index+1));
            Assert.Equal(legacy[index].RegisterItems.Select(r=>(r.Address,!r.IsReadOnly)).OrderBy(x=>x.Address),
                preset.Registers.Select(r=>(r.Address,r.Writable)).OrderBy(x=>x.Address));
            Assert.All(preset.Registers, r=>Assert.Equal(ModbusArea.Holding,r.Area));
        }
    }

    [Fact]
    public async Task PollingSeparatesAreasAndGaps_AndRejectsIncompleteReplies()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new RecordingBus();
            var definition = new DeviceDefinition { Registers = new()
            {
                new() { Address = 10 }, new() { Address = 11 }, new() { Address = 13 },
                new() { Address = 10, Area = ModbusArea.Input },
                new() { Address = 10, Area = ModbusArea.Coil },
                new() { Address = 10, Area = ModbusArea.Discrete }
            }};
            var model = new ConfiguredSlaveModel(definition,bus);
            await model.PollAsync();
            Assert.Equal(new[]{(ModbusArea.Holding,10,2),(ModbusArea.Holding,13,1),
                (ModbusArea.Input,10,1),(ModbusArea.Coil,10,1),(ModbusArea.Discrete,10,1)},bus.Reads);
            Assert.Equal(11,model.RegisterItems[1].Value);
            bus.Truncate = true;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>model.PollAsync());
            return true;
        },CancellationToken.None);
    }

    [Fact]
    public async Task UnknownDeviceAtEvenSlaveAddressNeedsNoBuiltInModelOrIdentity()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new RecordingBus();
            var manager = new SlaveManager(bus);
            var config = new HardwareConfiguration { Devices = new() { new()
            {
                Name = "Новый датчик", SlaveId = 42,
                Registers = new() { new() { Address = 700, Area = ModbusArea.Input } }
            } } };
            Assert.Equal(1, await manager.ConfigureAsync(config));
            Assert.Equal(42, Assert.Single(manager.GetSlavesSnapshot()).SlaveId);
            Assert.Equal(new[] { (ModbusArea.Input, 700, 1) }, bus.Reads);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InvalidIdentityDoesNotReplaceExistingDevices()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new RecordingBus();var manager = new SlaveManager(bus);
            var original = new ConfiguredSlaveModel(new() { Registers = new() { new() { Address=1 } } },bus);
            manager.Slaves.Add(original);
            var config = new HardwareConfiguration { Devices = new() { new()
            { IdentificationValue=104,Registers=new(){new(){Address=2300}} } } };
            await Assert.ThrowsAsync<InvalidOperationException>(()=>manager.ConfigureAsync(config));
            Assert.Same(original,Assert.Single(manager.Slaves));
            return true;
        },CancellationToken.None);
    }

    [Fact]
    public async Task StopDrainsPendingPollWithoutPublishingCancelledData()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new RecordingBus { Block=true };var manager = new SlaveManager(bus);
            manager.Slaves.Add(new ConfiguredSlaveModel(new() { Registers=new(){new(){Address=10}} },bus));
            var state = new RegisterState();using var monitor = new RegisterMonitor(manager,state,NullLogger.Instance);
            monitor.Start();await bus.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await monitor.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(state.TryGet(1,10,out _));Assert.True(bus.Cancelled);
            return true;
        },CancellationToken.None);
    }

    private sealed class RecordingBus : IModbusService
    {
        public List<(ModbusArea,int,int)> Reads {get;}=new();
        public bool Truncate,Block,Cancelled;
        public TaskCompletionSource<bool> Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ushort[]> ReadAreaAsync(byte slave,ushort address,ushort count,ModbusArea area,CancellationToken ct=default)
        {
            Reads.Add((area,address,count));Started.TrySetResult(true);
            if(Block)try{await Task.Delay(Timeout.Infinite,ct);}catch(OperationCanceledException){Cancelled=true;throw;}
            return Enumerable.Range(address,Truncate?count-1:count).Select(v=>(ushort)v).ToArray();
        }
        public Task<ushort[]> ReadRegistersAsync(byte slave,ushort address,ushort count,CancellationToken ct=default)=>ReadAreaAsync(slave,address,count,ModbusArea.Holding,ct);
        public Task<bool> WriteRegisterAsync(byte slave,ushort address,ushort value,bool verify=true,CancellationToken ct=default)=>Task.FromResult(true);
        public Task<bool> CheckPortAsync(CancellationToken ct=default)=>Task.FromResult(true);
        public void SubscribeRegister(byte slave,ushort address,Action<ushort[]> callback){}
    }
}
