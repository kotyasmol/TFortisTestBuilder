using Avalonia.Headless;
using Avalonia.Threading;
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
    [Theory]
    [InlineData(9, true)]
    [InlineData(10, true)]
    [InlineData(8, false)]
    public async Task SimbatAcceptsEitherConfiguredTypeAndRejectsOthers(ushort type, bool accepted)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new RecordingBus { Identity = type };
            var manager = new SlaveManager(bus);
            var config = new HardwareConfiguration { Devices = new() { new()
            {
                SlaveId = 17, Name = "SIMBAT-24/48", IdentificationValues = new() { 9, 10 },
                Registers = new() { new() { Address = 1700 } }
            } } };
            config = System.Text.Json.JsonSerializer.Deserialize<HardwareConfiguration>(
                System.Text.Json.JsonSerializer.Serialize(config))!;
            if (accepted) Assert.Equal(1, await manager.ConfigureAsync(config));
            else
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ConfigureAsync(config));
                Assert.Contains("ожидалось 9 или 10, получено 8", error.Message);
                Assert.Empty(manager.GetSlavesSnapshot());
            }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public void IdentificationEditorRejectsInvalidListWithoutChangingPreviousValues()
    {
        var device = new DeviceDefinition { IdentificationValue = 9 };
        device.IdentificationValuesText = "9, 10";
        Assert.Null(device.IdentificationValue);
        Assert.Equal(new ushort[] { 9, 10 }, device.ExpectedIdentificationValues);
        Assert.Throws<FormatException>(() => device.IdentificationValuesText = "9, wrong");
        Assert.Equal(new ushort[] { 9, 10 }, device.ExpectedIdentificationValues);
        device.IdentificationValuesText = "9";
        Assert.Equal((ushort)9, device.IdentificationValue);
        Assert.Null(device.IdentificationValues);
    }

    [Fact]
    public async Task AllExpectedSlavesMustPassBeforePublishingConnection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new RecordingBus();
            var manager = new SlaveManager(bus);
            var config = new HardwareConfiguration { Devices = new()
            {
                new() { Name = "Первое", SlaveId = 1, IdentificationValue = 0,
                    Registers = new() { new() { Address = 100 } } },
                new() { Name = "Второе", SlaveId = 23, IdentificationValue = 8,
                    Registers = new() { new() { Address = 1200 } } }
            } };
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ConfigureAsync(config));
            Assert.Contains("Slave 23", error.Message);
            Assert.Contains("ожидалось 8, получено 0", error.Message);
            Assert.Empty(manager.GetSlavesSnapshot());
            config.Devices[1].IdentificationValue = 0;
            Assert.Equal(2, await manager.ConfigureAsync(config));
            Assert.Equal(new byte[] { 1, 23 }, manager.GetSlavesSnapshot().Select(d => d.SlaveId));
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CancelAfterReadBeforeUiUpdateDoesNotRaiseUnhandledExceptionOrPublishValues()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new RecordingBus();
            var model = new ConfiguredSlaveModel(new() { Registers = new()
                { new() { Address = 10 }, new() { Address = 20 } } }, bus);
            using var cancellation = new CancellationTokenSource();
            using var queued = new ManualResetEventSlim();
            var unhandled = new List<Exception>();
            var uiCancellations = new List<Exception>();
            void OnFirstChance(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
            {
                if (e.Exception is OperationCanceledException && Dispatcher.UIThread.CheckAccess())
                    uiCancellations.Add(e.Exception);
            }
            void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
            {
                unhandled.Add(e.Exception);
                e.Handled = true;
            }
            Dispatcher.UIThread.UnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            try
            {
                // Keep the UI thread occupied until the background poll queues publication.
                var poll = Task.Run(() =>
                {
                    var pending = model.PollAsync(cancellation.Token);
                    queued.Set();
                    return pending;
                });
                Assert.True(queued.Wait(TimeSpan.FromSeconds(2)));
                Assert.False(poll.IsCompleted);
                Assert.Single(bus.Reads);
                cancellation.Cancel();
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(uiCancellations);
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.Empty(unhandled);
                Assert.All(model.RegisterItems, register => Assert.Equal(0, register.Value));
                Assert.Single(bus.Reads);

                // Cancellation of the old poll must not prevent the next session from updating.
                await model.PollAsync();
                Assert.Equal(new[] { 10, 20 }, model.RegisterItems.Select(register => (int)register.Value));
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
                Dispatcher.UIThread.UnhandledException -= OnUnhandled;
            }
            return true;
        }, CancellationToken.None);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscoveryUsesCustomCatalogueEvenAddressAndIdentificationRegister(bool ambiguous)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new RecordingBus();
            var manager = new SlaveManager(bus);
            var definition = new DeviceDefinition { Name = "Новый датчик", SlaveId = 1,
                IdentificationAddress = 700, IdentificationValue = 700,
                Registers = new() { new() { Address = 900, Area = ModbusArea.Input } } };
            var catalogue = new List<TestBuilder.Services.EquipmentTemplate> { new("custom", definition, false) };
            var settings = new DiscoverySettings { SlaveAddresses = "42", DelayMs = 0, TemplateIds = new() { "custom" } };
            if (ambiguous)
            {
                catalogue.Add(new("other", definition, false)); settings.TemplateIds.Add("other");
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ScanAsync(settings, catalogue));
                Assert.Empty(manager.Slaves);
            }
            else
            {
                Assert.Equal(1, await manager.ScanAsync(settings, catalogue));
                var model = Assert.Single(manager.Slaves);
                Assert.Equal(42, model.SlaveId);
                Assert.Equal("Новый датчик", model.DeviceType);
                Assert.Equal(1, definition.SlaveId);
                Assert.Contains((ModbusArea.Input, 900, 1), bus.Reads);
            }
            Assert.DoesNotContain(bus.Reads, r => r.Item2 == 0);
            return true;
        }, CancellationToken.None);
    }

    private sealed class RecordingBus : IModbusService
    {
        public List<(ModbusArea,int,int)> Reads {get;}=new();
        public bool Truncate,Block,Cancelled;
        public ushort? Identity;
        public TaskCompletionSource<bool> Started {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ushort[]> ReadAreaAsync(byte slave,ushort address,ushort count,ModbusArea area,CancellationToken ct=default)
        {
            Reads.Add((area,address,count));Started.TrySetResult(true);
            if(Block)try{await Task.Delay(Timeout.Infinite,ct);}catch(OperationCanceledException){Cancelled=true;throw;}
            if (address == 0 && count == 1 && Identity.HasValue && !Truncate) return new[] { Identity.Value };
            return Enumerable.Range(address,Truncate?count-1:count).Select(v=>(ushort)v).ToArray();
        }
        public Task<ushort[]> ReadRegistersAsync(byte slave,ushort address,ushort count,CancellationToken ct=default)=>ReadAreaAsync(slave,address,count,ModbusArea.Holding,ct);
        public Task<bool> WriteRegisterAsync(byte slave,ushort address,ushort value,bool verify=true,CancellationToken ct=default)=>Task.FromResult(true);
        public Task<bool> CheckPortAsync(CancellationToken ct=default)=>Task.FromResult(true);
        public void SubscribeRegister(byte slave,ushort address,Action<ushort[]> callback){}
    }
}
