using Avalonia.Headless;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Views;
using TestBuilder.ViewModels;

namespace TestBuilder.Tests.MonitoringTests;

[Collection("Graph editor UI")]
public class StandDiscoveryTests
{
    [Theory]
    [InlineData(104, "RTL-SW", 2300, 86)]
    [InlineData(105, "RTL-POE", 2400, 112)]
    public async Task RtlStandIsDiscoveredWithoutAProfile(int type, string name, int start, int count)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new DiscoveryBus((ushort)type, (ushort)start, (ushort)count);
            var manager = new SlaveManager(bus);
            Assert.Equal(1, await manager.ScanAsync());
            var model = Assert.IsType<ConfiguredSlaveModel>(Assert.Single(manager.Slaves));
            Assert.Equal(name, model.DeviceType);
            Assert.Equal(1, model.SlaveId);
            Assert.Equal(count, model.RegisterItems.Count);
            Assert.Equal(42, model.RegisterItems.Single(r => r.Address == start).Value);
            Assert.Contains(((byte)1, (ushort)start, (ushort)count), bus.Reads);
            return true;
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(104, true)]
    public async Task UnknownTypeOrIncompleteRtlMapIsNotAccepted(int type, bool incomplete)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var manager = new SlaveManager(new DiscoveryBus((ushort)type, 2300, 86) { Incomplete = incomplete });
            Assert.Equal(0, await manager.ScanAsync());
            Assert.Empty(manager.Slaves);
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Apk03DiscoveryKeepsItsExistingModel()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(async () =>
        {
            var bus = new DiscoveryBus(1, 0, 0);
            var manager = new SlaveManager(bus);
            Assert.Equal(1, await manager.ScanAsync());
            Assert.IsType<El60Model>(Assert.Single(manager.Slaves));
            Assert.All(bus.Reads, r => Assert.Equal((ushort)0, r.Address));
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RtlProfilesAreVisibleAndLoadableBeforeConnecting()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(GraphEditorViewTests));
        await session.Dispatch<bool>(() =>
        {
            var oldFolder = AppSettings.Instance.GraphsFolder;
            try
            {
                AppSettings.Instance.GraphsFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../profiles"));
                using var bus = new ModbusService();
                using var vm = new TestViewModel(bus, new SlaveManager(bus)) { IsOperatorView = false, ProductFilter = "Платы" };
                Assert.False(vm.IsConnected);
                Assert.True(vm.CanChooseProfile);
                Assert.Contains(vm.EngineerProfileGroups, g => g.Profiles.Any(p => Path.GetFileName(p.FilePath) == "RTL_SW_board.json"));
                foreach (var file in new[] { "RTL_SW_board.json", "RTL_POE_board.json" })
                {
                    vm.SelectedProfile = Assert.Single(vm.Profiles.Where(p => Path.GetFileName(p.FilePath) == file));
                    Assert.NotNull(vm.RootGraph.Hardware);
                    Assert.NotEmpty(vm.RootGraph.Nodes);
                    Assert.False(vm.IsConnected);
                }
                return true;
            }
            finally { AppSettings.Instance.GraphsFolder = oldFolder; }
        }, CancellationToken.None);
    }

    private sealed class DiscoveryBus(ushort type, ushort blockStart, ushort blockCount) : IModbusService
    {
        public List<(byte Slave, ushort Address, ushort Count)> Reads { get; } = new();
        public bool Incomplete { get; init; }
        public Task<ushort[]> ReadRegistersAsync(byte slaveId, ushort address, ushort count, CancellationToken cancellationToken = default)
        {
            Reads.Add((slaveId, address, count));
            if (slaveId != 1) throw new TimeoutException("Нет устройства на этом адресе.");
            if (address == 0 && count == 1) return Task.FromResult(new[] { type });
            if (address != blockStart || count != blockCount) throw new InvalidOperationException("Запрос не соответствует контракту RTL_v2.");
            var result = new ushort[Incomplete ? count - 1 : count];
            result[0] = 42;
            return Task.FromResult(result);
        }
        public Task<bool> WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Поиск стенда не должен писать в регистры.");
        public Task<bool> CheckPortAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public void SubscribeRegister(byte slaveId, ushort address, Action<ushort[]> callback) { }
    }
}
