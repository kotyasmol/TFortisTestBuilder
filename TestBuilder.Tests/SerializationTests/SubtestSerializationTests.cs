using System.Linq;
using TestBuilder.Domain.Steps;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class SubtestSerializationTests
{
    [Fact]
    public void SerializeAndDeserialize_PreservesSafeSerialAndProMacSettings()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();
        vm.RootGraph.Nodes.Add(new GetSerialNumberFromServerNodeViewModel
        {
            UseFixedSerialNumber = true,
            FixedSerialNumber = 3200428
        });
        vm.RootGraph.Nodes.Add(new SetProMacNodeViewModel
        {
            BatchPath = @"C:\TFortisStandNew\set_mac_pro.bat",
            BoardVersion = "PSW+UPS-Box 8x2Pro",
            TimeoutMs = 45000,
            FailOnError = true
        });

        var json = GraphSerializer.Serialize(vm, "Profile");

        Assert.Contains("\"useFixedSerialNumber\": true", json);
        Assert.Contains("\"fixedSerialNumber\": 3200428", json);
        Assert.Contains("\"type\": \"Set Pro MAC\"", json);
        Assert.Contains("\"batchPath\": \"C:\\\\TFortisStandNew\\\\set_mac_pro.bat\"", json);
        Assert.Contains("\"boardVersion\": \"PSW+UPS-Box 8x2Pro\"", json);

        using var loadedModbus = new ModbusService();
        var loadedVm = new TestViewModel(loadedModbus, new SlaveManager(loadedModbus));
        GraphSerializer.Deserialize(json, loadedVm);

        var serialNode = loadedVm.RootGraph.Nodes.OfType<GetSerialNumberFromServerNodeViewModel>().Single();
        var setMacNode = loadedVm.RootGraph.Nodes.OfType<SetProMacNodeViewModel>().Single();
        Assert.True(serialNode.UseFixedSerialNumber);
        Assert.Equal(3200428, serialNode.FixedSerialNumber);
        Assert.Equal(@"C:\TFortisStandNew\set_mac_pro.bat", setMacNode.BatchPath);
        Assert.Equal("PSW+UPS-Box 8x2Pro", setMacNode.BoardVersion);
        Assert.Equal(45000, setMacNode.TimeoutMs);
        Assert.Equal(@"C:\TFortisStandNew\set_mac_pro.bat", ((SetProMacNodeViewModel)setMacNode.Clone()).BatchPath);
    }

    [Fact]
    public void Deserialize_LegacyUdpMacNode_MigratesToProMacNode()
    {
        const string json = """
            {
              "name": "Legacy",
              "nodes": [
                {
                  "id": "0",
                  "type": "Send UDP Set MAC",
                  "x": 10,
                  "y": 20,
                  "targetIp": "192.168.0.1",
                  "targetPort": 43962,
                  "localPort": 6123,
                  "macVariableName": "Dut.NewMac",
                  "timeoutMs": 3000
                }
              ],
              "connections": []
            }
            """;

        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));

        GraphSerializer.Deserialize(json, vm);

        var node = vm.RootGraph.Nodes.OfType<SetProMacNodeViewModel>().Single();
        Assert.Equal("set_mac_pro.bat", node.BatchPath);
        Assert.Equal("Dut.NewMac", node.MacVariableName);
        Assert.Equal("PSW+UPS-Box 8x2Pro", node.BoardVersion);
        Assert.Equal(60000, node.TimeoutMs);
    }

    [Fact]
    public void SerializeAndDeserialize_PreservesSubtestBodyGraph()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();

        var subtest = new SubtestNodeViewModel
        {
            Name = "Selftest",
            Description = "Проверка внутренних ошибок устройства",
            IsEnabled = true,
            StopOnError = false,
            RunOnFailure = true
        };

        vm.RootGraph.Nodes.Add(new StartNodeViewModel());
        vm.RootGraph.Nodes.Add(subtest);
        vm.RootGraph.Nodes.Add(new EndNodeViewModel());

        var json = GraphSerializer.Serialize(vm, "Profile");

        Assert.Contains("\"type\": \"Subtest\"", json);
        Assert.Contains("\"bodyGraph\"", json);
        Assert.Contains("\"name\": \"Selftest\"", json);
        Assert.Contains("\"runOnFailure\": true", json);

        using var loadedModbus = new ModbusService();
        var loadedVm = new TestViewModel(loadedModbus, new SlaveManager(loadedModbus));

        GraphSerializer.Deserialize(json, loadedVm);

        var loadedSubtest = loadedVm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Single();

        Assert.Equal("Selftest", loadedSubtest.Name);
        Assert.Equal("Проверка внутренних ошибок устройства", loadedSubtest.Description);
        Assert.False(loadedSubtest.StopOnError);
        Assert.True(loadedSubtest.RunOnFailure);
        Assert.Contains(loadedSubtest.BodyGraph.Nodes, n => n is StartNodeViewModel);
        Assert.Contains(loadedSubtest.BodyGraph.Nodes, n => n is EndNodeViewModel);
    }

    [Fact]
    public void SerializeAndDeserialize_PreservesLiveReadForModbusCheck()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();

        var check = new CheckRegisterRangeNodeViewModel
        {
            SlaveId = 1,
            Address = 100,
            Min = 10,
            Max = 20,
            UseCurrentSlaveId = true,
            LiveRead = true
        };

        vm.RootGraph.Nodes.Add(new StartNodeViewModel());
        vm.RootGraph.Nodes.Add(check);
        vm.RootGraph.Nodes.Add(new EndNodeViewModel());

        var json = GraphSerializer.Serialize(vm, "Profile");

        Assert.Contains("\"type\": \"Check Register Range\"", json);
        Assert.Contains("\"liveRead\": true", json);

        using var loadedModbus = new ModbusService();
        var loadedVm = new TestViewModel(loadedModbus, new SlaveManager(loadedModbus));

        GraphSerializer.Deserialize(json, loadedVm);

        var loadedCheck = loadedVm.RootGraph.Nodes.OfType<CheckRegisterRangeNodeViewModel>().Single();

        Assert.Equal(1, loadedCheck.SlaveId);
        Assert.Equal(100, loadedCheck.Address);
        Assert.Equal(10, loadedCheck.Min);
        Assert.Equal(20, loadedCheck.Max);
        Assert.True(loadedCheck.UseCurrentSlaveId);
        Assert.True(loadedCheck.LiveRead);
    }

    [Fact]
    public void SerializeAndDeserialize_PreservesSelftestPollInterval()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();

        var selftest = new SelfTestCheckNodeViewModel
        {
            TimeoutMs = 180000,
            PollIntervalMs = 7000
        };

        vm.RootGraph.Nodes.Add(new StartNodeViewModel());
        vm.RootGraph.Nodes.Add(selftest);
        vm.RootGraph.Nodes.Add(new EndNodeViewModel());

        var json = GraphSerializer.Serialize(vm, "Profile");

        Assert.Contains("\"type\": \"Selftest Check\"", json);
        Assert.Contains("\"pollIntervalMs\": 7000", json);

        using var loadedModbus = new ModbusService();
        var loadedVm = new TestViewModel(loadedModbus, new SlaveManager(loadedModbus));

        GraphSerializer.Deserialize(json, loadedVm);

        var loadedSelftest = loadedVm.RootGraph.Nodes.OfType<SelfTestCheckNodeViewModel>().Single();

        Assert.Equal(180000, loadedSelftest.TimeoutMs);
        Assert.Equal(7000, loadedSelftest.PollIntervalMs);
    }

    [Fact]
    public void SerializeAndDeserialize_PreservesUniversalHttpNodes()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();

        vm.RootGraph.Nodes.Add(new StartNodeViewModel());
        vm.RootGraph.Nodes.Add(new ReadHttpVariableNodeViewModel
        {
            BaseUrl = "http://dut",
            Endpoint = "/api/value",
            ResponseType = HttpResponseValueType.Number,
            TimeoutMs = 2345,
            OutputVariableName = "Dut.value",
            FailOnError = true
        });
        vm.RootGraph.Nodes.Add(new WaitVariableUntilNodeViewModel
        {
            VariableName = "Dut.state",
            ExpectedValue = "1",
            ComparisonType = VariableComparisonType.Number,
            PollAction = "HttpGet",
            BaseUrl = "http://dut",
            Endpoint = "/api/state",
            ResponseType = HttpResponseValueType.Integer,
            RequestTimeoutMs = 3456,
            TimeoutMs = 4567,
            IntervalMs = 123,
            FailOnTimeout = true
        });
        vm.RootGraph.Nodes.Add(new EndNodeViewModel());

        var json = GraphSerializer.Serialize(vm, "Profile");

        Assert.Contains("\"type\": \"Read HTTP Variable\"", json);
        Assert.Contains("\"endpoint\": \"/api/value\"", json);
        Assert.Contains("\"responseType\": \"Number\"", json);

        using var loadedModbus = new ModbusService();
        var loadedVm = new TestViewModel(loadedModbus, new SlaveManager(loadedModbus));
        GraphSerializer.Deserialize(json, loadedVm);

        var loadedRead = loadedVm.RootGraph.Nodes.OfType<ReadHttpVariableNodeViewModel>().Single();
        Assert.Equal("http://dut", loadedRead.BaseUrl);
        Assert.Equal("/api/value", loadedRead.Endpoint);
        Assert.Equal(HttpResponseValueType.Number, loadedRead.ResponseType);
        Assert.Equal(2345, loadedRead.TimeoutMs);
        Assert.Equal("Dut.value", loadedRead.OutputVariableName);

        var loadedWait = loadedVm.RootGraph.Nodes.OfType<WaitVariableUntilNodeViewModel>().Single();
        Assert.Equal("HttpGet", loadedWait.PollAction);
        Assert.Equal("/api/state", loadedWait.Endpoint);
        Assert.Equal(HttpResponseValueType.Integer, loadedWait.ResponseType);
        Assert.Equal(3456, loadedWait.RequestTimeoutMs);
        Assert.Equal(4567, loadedWait.TimeoutMs);
        Assert.Equal(123, loadedWait.IntervalMs);
        Assert.True(loadedWait.FailOnTimeout);
    }

    [Fact]
    public void Deserialize_LegacyWaitPollActionInfersEndpointAndResponseType()
    {
        const string json = """
            {
              "name": "Legacy wait",
              "nodes": [
                {
                  "id": "0",
                  "type": "Wait Variable Until",
                  "x": 0,
                  "y": 0,
                  "variableName": "Dut.akb_voltage",
                  "expectedValue": "24.5",
                  "comparisonType": "Number",
                  "pollAction": "GetUpsVoltage"
                }
              ],
              "connections": []
            }
            """;

        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));

        GraphSerializer.Deserialize(json, vm);

        var wait = vm.RootGraph.Nodes.OfType<WaitVariableUntilNodeViewModel>().Single();
        Assert.Equal("GetUpsVoltage", wait.PollAction);
        Assert.Equal("/api/getUpsVoltage", wait.Endpoint);
        Assert.Equal(HttpResponseValueType.Number, wait.ResponseType);
    }

    [Fact]
    public void Deserialize_LegacyReportMigratesShortSerialToFullServerSerial()
    {
        const string json = """
            {
              "name": "Legacy report",
              "nodes": [
                {
                  "id": "0",
                  "type": "Build Test Report",
                  "x": 0,
                  "y": 0,
                  "reportVariableName": "TestReportJson",
                  "serialVariableName": "SerialShort"
                }
              ],
              "connections": []
            }
            """;

        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));

        GraphSerializer.Deserialize(json, vm);

        var report = vm.RootGraph.Nodes.OfType<BuildTestReportNodeViewModel>().Single();
        Assert.Equal("SerialNumber", report.SerialVariableName);
        Assert.Equal("TestReportJson", report.ReportVariableName);
        Assert.Equal("production", report.TestType);
    }
}
