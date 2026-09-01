using Avalonia;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using TestBuilder.Serialization;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Domain.Steps;

namespace TestBuilder.Services
{
    public static class GraphSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Маппинг ViewModel -> английский тип для JSON
        private static string GetNodeType(NodeViewModel node) => node switch
        {
            StartNodeViewModel => "Start",
            EndNodeViewModel => "End",
            BodyStartNodeViewModel => "Body Start",
            BodyEndNodeViewModel => "Body End",
            DelayNodeViewModel => "Delay",
            LabelNodeViewModel => "Label",
            ModbusWriteNodeViewModel => "Write Register",
            CheckRegisterRangeNodeViewModel => "Check Register Range",
            CheckRegisterEqualityNodeViewModel => "Check Register Equality",
            WaitUntilNodeViewModel => "Wait Until",
            PollRegisterNodeViewModel => "Poll Register",
            OperatorActionNodeViewModel => "Operator Action",
            SelfTestCheckNodeViewModel => "Selftest Check",
            CheckVariableEqualityNodeViewModel => "Check Variable Equality",
            CheckVariableRangeNodeViewModel => "Check Variable Range",
            ClearArpCacheNodeViewModel => "Clear ARP Cache",
            GetSerialNumberFromServerNodeViewModel => "Get Serial Number",
            SendUdpSetMacPacketNodeViewModel => "Send UDP Set MAC",
            RunDataTestNodeViewModel => "Run Data Test",
            GetUpsStatusNodeViewModel => "Get UPS Status",
            GetUpsVoltageNodeViewModel => "Get UPS Voltage",
            GetIrpStatusNodeViewModel => "Get IRP Status",
            ReadHttpVariableNodeViewModel => "Read HTTP Variable",
            BuildMacFromSerialNodeViewModel => "Build MAC From Serial",
            CompareVariablesNodeViewModel => "Compare Variables",
            WaitVariableUntilNodeViewModel => "Wait Variable Until",
            BuildTestReportNodeViewModel => "Build Test Report",
            PrintLabelNodeViewModel => "Print Label",
            SendTestReportNodeViewModel => "Send Test Report",
            SubtestNodeViewModel => "Subtest",
            ForEachSlaveNodeViewModel => "For Slaves",
            _ => node.Title
        };

        public static string Serialize(TestViewModel vm, string profileName)
        {
            var dto = SerializeGraph(vm.RootGraph, profileName);
            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        private static GraphDto SerializeGraph(GraphWorkspaceViewModel graph, string name)
        {
            var dto = new GraphDto { Name = name };
            var nodeIds = new Dictionary<NodeViewModel, string>();

            for (var i = 0; i < graph.Nodes.Count; i++)
                nodeIds[graph.Nodes[i]] = i.ToString();

            foreach (var node in graph.Nodes)
            {
                var n = new NodeDto
                {
                    Id = nodeIds[node],
                    Type = GetNodeType(node),  // всегда английский
                    X = node.Location.X,
                    Y = node.Location.Y,
                    Color = node.NodeColor
                };

                switch (node)
                {
                    case DelayNodeViewModel d:
                        n.Milliseconds = d.Milliseconds;
                        break;

                    case LabelNodeViewModel l:
                        n.Text = l.Text;
                        n.LabelWidth = l.LabelWidth;
                        n.LabelHeight = l.LabelHeight;
                        break;

                    case ModbusWriteNodeViewModel w:
                        n.SlaveId = w.SlaveId;
                        n.Address = w.Address;
                        n.Value = w.Value;
                        n.UseCurrentSlaveId = w.UseCurrentSlaveId;
                        n.VerifyWrite = w.VerifyWrite;
                        break;

                    case CheckRegisterRangeNodeViewModel c:
                        n.SlaveId = c.SlaveId;
                        n.Address = c.Address;
                        n.Min = c.Min;
                        n.Max = c.Max;
                        n.UseCurrentSlaveId = c.UseCurrentSlaveId;
                        n.LiveRead = c.LiveRead;
                        break;

                    case CheckRegisterEqualityNodeViewModel eq:
                        n.SlaveId = eq.SlaveId;
                        n.Address = eq.Address;
                        n.ExpectedValue = eq.ExpectedValue;
                        n.UseCurrentSlaveId = eq.UseCurrentSlaveId;
                        n.LiveRead = eq.LiveRead;
                        break;

                    case WaitUntilNodeViewModel w:
                        n.SlaveId = w.SlaveId;
                        n.Address = w.Address;
                        n.ExpectedValue = w.ExpectedValue;
                        n.DurationMs = w.TimeoutMs;
                        n.UseCurrentSlaveId = w.UseCurrentSlaveId;
                        n.LiveRead = w.LiveRead;
                        break;

                    case PollRegisterNodeViewModel p:
                        n.SlaveId = p.SlaveId;
                        n.Address = p.Address;
                        n.Min = p.Min;
                        n.Max = p.Max;
                        n.SampleCount = p.SampleCount;
                        n.UseCurrentSlaveId = p.UseCurrentSlaveId;
                        n.LiveRead = p.LiveRead;
                        break;

                    case OperatorActionNodeViewModel op:
                        n.Text = op.Message;
                        break;

                    case SelfTestCheckNodeViewModel s:
                        n.Url = s.Url;
                        n.TimeoutMs = s.TimeoutMs;
                        n.PollIntervalMs = s.PollIntervalMs;
                        n.OutputPrefix = s.OutputPrefix;
                        n.ValidationRules = s.ValidationRules;
                        n.FailOnError = s.FailOnError;
                        break;

                    case CheckVariableEqualityNodeViewModel v:
                        n.VariableName = v.VariableName;
                        n.ExpectedValue = v.ExpectedValue;
                        n.ComparisonType = v.ComparisonType.ToString();
                        n.FailMessage = v.FailMessage;
                        break;

                    case CheckVariableRangeNodeViewModel r:
                        n.VariableName = r.VariableName;
                        n.Min = r.Min;
                        n.Max = r.Max;
                        n.Inclusive = r.Inclusive;
                        n.FailMessage = r.FailMessage;
                        break;

                    case ClearArpCacheNodeViewModel a:
                        n.RunArpdBat = a.RunArpdBat;
                        n.ArpdBatPath = a.ArpdBatPath;
                        n.Command = a.Command;
                        n.Arguments = a.Arguments;
                        n.TimeoutMs = a.TimeoutMs;
                        n.FailOnError = a.FailOnError;
                        break;

                    case GetSerialNumberFromServerNodeViewModel s:
                        n.ServerBaseUrl = s.ServerBaseUrl;
                        n.DeviceType = s.DeviceType;
                        n.CpuIdVariableName = s.CpuIdVariableName;
                        n.TimeoutMs = s.TimeoutMs;
                        n.RetryCount = s.RetryCount;
                        n.RetryDelayMs = s.RetryDelayMs;
                        n.OutputVariableName = s.OutputVariableName;
                        n.FailOnError = s.FailOnError;
                        break;

                    case SendUdpSetMacPacketNodeViewModel u:
                        n.TargetIp = u.TargetIp;
                        n.TargetPort = u.TargetPort;
                        n.MacVariableName = u.MacVariableName;
                        n.TimeoutMs = u.TimeoutMs;
                        n.RepeatCount = u.RepeatCount;
                        n.DelayBetweenRepeatsMs = u.DelayBetweenRepeatsMs;
                        n.FailOnSendError = u.FailOnSendError;
                        break;

                    case RunDataTestNodeViewModel d:
                        n.Mode = d.Mode;
                        n.ExpectedPackets = d.ExpectedPackets;
                        n.PacketSizeBytes = d.PacketSizeBytes;
                        n.UdpPort = d.UdpPort;
                        n.MaxPortTestTimeMs = d.MaxPortTestTimeMs;
                        n.TargetBandwidthMbps = RunDataTestStep.NormalizeBandwidth(d.TargetBandwidthMbps);
                        n.DurationMs = d.DurationMs;
                        n.WarmupMs = d.WarmupMs;
                        n.InterPairDelayMs = d.InterPairDelayMs;
                        n.AllowedLossPercent = d.AllowedLossPercent;
                        n.AllowedTxDeficitPercent = d.AllowedTxDeficitPercent;
                        n.Bidirectional = d.Bidirectional;
                        n.PortsText = NormalizeDataTestPortsText(d.PortsText);
                        n.OutputVariableName = d.OutputVariableName;
                        n.FailOnError = d.FailOnError;
                        break;

                    case GetUpsStatusNodeViewModel us:
                        n.BaseUrl = us.BaseUrl;
                        n.TimeoutMs = us.TimeoutMs;
                        n.OutputVariableName = us.OutputVariableName;
                        n.FailOnError = us.FailOnError;
                        break;

                    case GetUpsVoltageNodeViewModel uv:
                        n.BaseUrl = uv.BaseUrl;
                        n.TimeoutMs = uv.TimeoutMs;
                        n.OutputVariableName = uv.OutputVariableName;
                        n.FailOnError = uv.FailOnError;
                        break;

                    case GetIrpStatusNodeViewModel irp:
                        n.BaseUrl = irp.BaseUrl;
                        n.TimeoutMs = irp.TimeoutMs;
                        n.OutputVariableName = irp.OutputVariableName;
                        n.FailOnError = irp.FailOnError;
                        break;

                    case ReadHttpVariableNodeViewModel httpRead:
                        n.BaseUrl = httpRead.BaseUrl;
                        n.Endpoint = httpRead.Endpoint;
                        n.ResponseType = httpRead.ResponseType.ToString();
                        n.TimeoutMs = httpRead.TimeoutMs;
                        n.OutputVariableName = httpRead.OutputVariableName;
                        n.FailOnError = httpRead.FailOnError;
                        break;

                    case BuildMacFromSerialNodeViewModel bm:
                        n.SerialVariableName = bm.SerialVariableName;
                        n.SerialOffset = bm.SerialOffset;
                        n.MacPrefix = bm.MacPrefix;
                        n.SerialShortVariableName = bm.SerialShortVariableName;
                        n.MacVariableName = bm.MacVariableName;
                        n.FailOnError = bm.FailOnError;
                        break;

                    case CompareVariablesNodeViewModel cv:
                        n.LeftVariableName = cv.LeftVariableName;
                        n.RightVariableName = cv.RightVariableName;
                        n.ComparisonType = cv.ComparisonType.ToString();
                        n.FailMessage = cv.FailMessage;
                        break;

                    case WaitVariableUntilNodeViewModel wait:
                        n.VariableName = wait.VariableName;
                        n.ExpectedValue = wait.ExpectedValue;
                        n.ComparisonType = wait.ComparisonType.ToString();
                        n.PollAction = wait.PollAction;
                        n.BaseUrl = wait.BaseUrl;
                        n.Endpoint = wait.Endpoint;
                        n.ResponseType = wait.ResponseType.ToString();
                        n.RequestTimeoutMs = wait.RequestTimeoutMs;
                        n.TimeoutMs = wait.TimeoutMs;
                        n.IntervalMs = wait.IntervalMs;
                        n.FailOnTimeout = wait.FailOnTimeout;
                        break;

                    case BuildTestReportNodeViewModel br:
                        n.ReportVariableName = br.ReportVariableName;
                        n.DeviceName = br.DeviceName;
                        n.DeviceType = br.DeviceType;
                        n.SerialVariableName = br.SerialVariableName;
                        n.MacVariableName = br.MacVariableName;
                        n.IncludeAllVariables = br.IncludeAllVariables;
                        break;

                    case PrintLabelNodeViewModel pl:
                        n.PrinterName = pl.PrinterName;
                        n.DeviceName = pl.DeviceName;
                        n.DeviceType = pl.DeviceType;
                        n.SerialVariableName = pl.SerialVariableName;
                        n.MacVariableName = pl.MacVariableName;
                        n.Copies = pl.Copies;
                        n.IncludeMac = pl.IncludeMac;
                        n.EquipmentFieldUse = pl.EquipmentFieldUse;
                        n.EquipmentType = pl.EquipmentType;
                        n.EquipmentText = pl.EquipmentText;
                        n.FailOnPrinterError = pl.FailOnPrinterError;
                        break;

                    case SendTestReportNodeViewModel sr:
                        n.ServerBaseUrl = sr.ServerBaseUrl;
                        n.ReportVariableName = sr.ReportVariableName;
                        n.Endpoint = sr.Endpoint;
                        n.TimeoutMs = sr.TimeoutMs;
                        n.RetryCount = sr.RetryCount;
                        n.RetryDelayMs = sr.RetryDelayMs;
                        n.SaveLocalCopy = sr.SaveLocalCopy;
                        n.LocalReportsDirectory = sr.LocalReportsDirectory;
                        n.FailOnError = sr.FailOnError;
                        break;

                    case SubtestNodeViewModel s:
                        n.Name = s.Name;
                        n.Description = s.Description;
                        n.IsEnabled = s.IsEnabled;
                        n.StopOnError = s.StopOnError;
                        n.RunOnFailure = s.RunOnFailure;
                        n.BodyGraph = SerializeGraph(s.BodyGraph, s.BodyGraph.Title);
                        break;

                    case ForEachSlaveNodeViewModel f:
                        n.FromSlaveId = f.FromSlaveId;
                        n.ToSlaveId = f.ToSlaveId;
                        n.Step = f.Step;
                        n.StopOnError = f.StopOnError;
                        n.Body = SerializeGraph(f.BodyGraph, f.BodyGraph.Title);
                        break;
                }

                dto.Nodes.Add(n);
            }

            foreach (var conn in graph.Connections)
            {
                var src = conn.Source.Parent;
                var tgt = conn.Target.Parent;

                if (src == null || tgt == null)
                    continue;

                if (!nodeIds.ContainsKey(src) || !nodeIds.ContainsKey(tgt))
                    continue;

                dto.Connections.Add(new ConnectionDto
                {
                    SourceNodeId = nodeIds[src],
                    SourceConnector = conn.Source.Title,
                    TargetNodeId = nodeIds[tgt],
                    TargetConnector = conn.Target.Title
                });
            }

            return dto;
        }

        public static string Deserialize(string json, TestViewModel vm)
        {
            var dto = JsonSerializer.Deserialize<GraphDto>(json, JsonOptions)
                      ?? throw new InvalidOperationException("Не удалось прочитать JSON");

            vm.ResetToRootGraph();
            vm.RootGraph.Clear();

            DeserializeGraph(dto, vm.RootGraph, isBodyGraph: false);

            vm.ResetToRootGraph();

            return dto.Name;
        }

        private static void DeserializeGraph(GraphDto dto, GraphWorkspaceViewModel graph, bool isBodyGraph)
        {
            graph.Clear();
            graph.Title = dto.Name;
            graph.IsBodyGraph = isBodyGraph;

            var nodeMap = new Dictionary<string, NodeViewModel>();

            foreach (var n in dto.Nodes)
            {
                var location = new Point(n.X, n.Y);
                var type = string.IsNullOrWhiteSpace(n.Type)
                    ? n.NodeType ?? string.Empty
                    : n.Type;

                NodeViewModel node = type switch
                {
                    "Start" or "Старт" => new StartNodeViewModel { Location = location },

                    "End" or "Конец" => new EndNodeViewModel { Location = location },

                    "Body Start" or "Тело: начало" => new BodyStartNodeViewModel { Location = location },

                    "Body End" or "Тело: конец" => new BodyEndNodeViewModel { Location = location },

                    "Delay" or "Задержка" => new DelayNodeViewModel
                    {
                        Location = location,
                        Milliseconds = n.Milliseconds ?? 1000
                    },

                    "Label" or "Метка" => new LabelNodeViewModel
                    {
                        Location = location,
                        Text = n.Text ?? "Этап",
                        LabelWidth = n.LabelWidth ?? 300,
                        LabelHeight = n.LabelHeight ?? 120
                    },

                    "Write Register" or "WriteRegister" or "Запись регистра" => CreateModbusWriteNode(n, location),

                    "Check Register Range" or "Проверка диапазона" => CreateCheckRangeNode(n, location),

                    "Selftest Check" or "SELFTEST_CHECK" or "Проверка selftest" => new SelfTestCheckNodeViewModel
                    {
                        Location = location,
                        Url = n.Url ?? SelfTestCheckStep.DefaultUrl,
                        TimeoutMs = n.TimeoutMs ?? SelfTestCheckStep.DefaultTimeoutMs,
                        PollIntervalMs = n.PollIntervalMs ?? SelfTestCheckStep.DefaultPollIntervalMs,
                        OutputPrefix = n.OutputPrefix ?? SelfTestCheckStep.DefaultOutputPrefix,
                        ValidationRules = n.ValidationRules ?? SelfTestCheckStep.DefaultValidationRules,
                        FailOnError = n.FailOnError ?? true
                    },

                    "Check Variable Equality" or "CheckVariableEquality" or "CHECK_VARIABLE_EQUALITY" or "Проверка переменной" => new CheckVariableEqualityNodeViewModel
                    {
                        Location = location,
                        VariableName = n.VariableName ?? "Dut.init_ok",
                        ExpectedValue = GetExpectedValueAsString(n.ExpectedValue, "1"),
                        ComparisonType = ParseComparisonType(n.ComparisonType),
                        FailMessage = n.FailMessage ?? string.Empty
                    },

                    "Check Variable Range" or "CHECK_VARIABLE_RANGE" or "Проверка диапазона переменной" => new CheckVariableRangeNodeViewModel
                    {
                        Location = location,
                        VariableName = n.VariableName ?? "Dut.akb_voltage",
                        Min = n.Min ?? 0,
                        Max = n.Max ?? 0,
                        Inclusive = n.Inclusive ?? true,
                        FailMessage = n.FailMessage ?? string.Empty
                    },

                    "Clear ARP Cache" or "CLEAR_ARP_CACHE" or "Очистка ARP" => new ClearArpCacheNodeViewModel
                    {
                        Location = location,
                        RunArpdBat = n.RunArpdBat ?? true,
                        ArpdBatPath = n.ArpdBatPath ?? "arpd.bat",
                        Command = n.Command ?? "arp",
                        Arguments = n.Arguments ?? "-d *",
                        TimeoutMs = n.TimeoutMs ?? 5000,
                        FailOnError = n.FailOnError ?? false
                    },

                    "Get Serial Number" or "GET_SERIAL_NUMBER_FROM_SERVER" or "Получить серийный номер" => new GetSerialNumberFromServerNodeViewModel
                    {
                        Location = location,
                        ServerBaseUrl = n.ServerBaseUrl ?? string.Empty,
                        DeviceType = GetObjectAsString(n.DeviceType, "PSW+UPS-Box 8x2Pro"),
                        CpuIdVariableName = n.CpuIdVariableName ?? "Dut.cpu_id",
                        TimeoutMs = n.TimeoutMs ?? 30000,
                        RetryCount = n.RetryCount ?? 1,
                        RetryDelayMs = n.RetryDelayMs ?? 1000,
                        OutputVariableName = n.OutputVariableName ?? "SerialNumber",
                        FailOnError = n.FailOnError ?? true
                    },

                    "Send UDP Set MAC" or "SEND_UDP_SET_MAC_PACKET" or "UDP установка MAC" => new SendUdpSetMacPacketNodeViewModel
                    {
                        Location = location,
                        TargetIp = n.TargetIp ?? "192.168.0.1",
                        TargetPort = n.TargetPort ?? 43962,
                        MacVariableName = n.MacVariableName ?? "Dut.NewMac",
                        TimeoutMs = n.TimeoutMs ?? 1000,
                        RepeatCount = n.RepeatCount ?? 1,
                        DelayBetweenRepeatsMs = n.DelayBetweenRepeatsMs ?? 200,
                        FailOnSendError = n.FailOnSendError ?? true
                    },

                    "Run Data Test" or "RUN_DATA_TEST" or "Тест передачи данных" => new RunDataTestNodeViewModel
                    {
                        Location = location,
                        Mode = n.Mode ?? "SoftwarePcap",
                        ExpectedPackets = n.ExpectedPackets ?? 10000,
                        PacketSizeBytes = n.PacketSizeBytes ?? 1514,
                        UdpPort = n.UdpPort ?? 43962,
                        MaxPortTestTimeMs = n.MaxPortTestTimeMs ?? 15000,
                        TargetBandwidthMbps = RunDataTestStep.NormalizeBandwidth(n.TargetBandwidthMbps ?? 100),
                        DurationMs = n.DurationMs ?? 5000,
                        WarmupMs = n.WarmupMs ?? 500,
                        InterPairDelayMs = n.InterPairDelayMs ?? 5000,
                        AllowedLossPercent = n.AllowedLossPercent ?? 1.0,
                        AllowedTxDeficitPercent = n.AllowedTxDeficitPercent ?? 2.0,
                        Bidirectional = n.Bidirectional ?? true,
                        PortsText = NormalizeDataTestPortsText(n.PortsText ?? PortsToText(n.Ports)),
                        OutputVariableName = n.OutputVariableName ?? "DataTest",
                        FailOnError = n.FailOnError ?? true
                    },

                    "Get UPS Status" or "GET_UPS_STATUS" or "Получить UPS статус" => new GetUpsStatusNodeViewModel
                    {
                        Location = location,
                        BaseUrl = n.BaseUrl ?? "http://192.168.0.1",
                        TimeoutMs = n.TimeoutMs ?? 5000,
                        OutputVariableName = n.OutputVariableName ?? "Dut.ups_rez",
                        FailOnError = n.FailOnError ?? true
                    },

                    "Get UPS Voltage" or "GET_UPS_VOLTAGE" or "Получить UPS напряжение" => new GetUpsVoltageNodeViewModel
                    {
                        Location = location,
                        BaseUrl = n.BaseUrl ?? "http://192.168.0.1",
                        TimeoutMs = n.TimeoutMs ?? 5000,
                        OutputVariableName = n.OutputVariableName ?? "Dut.akb_voltage",
                        FailOnError = n.FailOnError ?? true
                    },

                    "Get IRP Status" or "GET_IRP_STATUS" or "Получить IRP статус" => new GetIrpStatusNodeViewModel
                    {
                        Location = location,
                        BaseUrl = n.BaseUrl ?? "http://192.168.0.1",
                        TimeoutMs = n.TimeoutMs ?? 5000,
                        OutputVariableName = n.OutputVariableName ?? "Dut.ups_det",
                        FailOnError = n.FailOnError ?? true
                    },

                    "Read HTTP Variable" or "READ_HTTP_VARIABLE" or "Прочитать HTTP переменную" => new ReadHttpVariableNodeViewModel
                    {
                        Location = location,
                        BaseUrl = n.BaseUrl ?? "http://192.168.0.1",
                        Endpoint = n.Endpoint ?? "/api/getUpsStatus",
                        ResponseType = ParseHttpResponseValueType(n.ResponseType),
                        TimeoutMs = n.TimeoutMs ?? 5000,
                        OutputVariableName = n.OutputVariableName ?? "Dut.value",
                        FailOnError = n.FailOnError ?? true
                    },

                    "Build MAC From Serial" or "BUILD_MAC_FROM_SERIAL" or "Расчет MAC" => new BuildMacFromSerialNodeViewModel
                    {
                        Location = location,
                        SerialVariableName = n.SerialVariableName ?? "SerialNumber",
                        SerialOffset = n.SerialOffset ?? 3200000,
                        MacPrefix = n.MacPrefix ?? "C0:11:A6:20",
                        SerialShortVariableName = n.SerialShortVariableName ?? "SerialShort",
                        MacVariableName = n.MacVariableName ?? "Dut.NewMac",
                        FailOnError = n.FailOnError ?? true
                    },

                    "Compare Variables" or "COMPARE_VARIABLES" or "Сравнить переменные" => new CompareVariablesNodeViewModel
                    {
                        Location = location,
                        LeftVariableName = n.LeftVariableName ?? "Dut.default_mac",
                        RightVariableName = n.RightVariableName ?? "Dut.NewMac",
                        ComparisonType = ParseComparisonType(n.ComparisonType),
                        FailMessage = n.FailMessage ?? string.Empty
                    },

                    "Wait Variable Until" or "WAIT_VARIABLE_UNTIL" or "Ожидание переменной" => new WaitVariableUntilNodeViewModel
                    {
                        Location = location,
                        VariableName = n.VariableName ?? "Dut.ups_rez",
                        ExpectedValue = GetExpectedValueAsString(n.ExpectedValue, "1"),
                        ComparisonType = ParseComparisonType(n.ComparisonType),
                        PollAction = n.PollAction ?? "GetUpsStatus",
                        BaseUrl = n.BaseUrl ?? "http://192.168.0.1",
                        Endpoint = n.Endpoint ?? GetLegacyPollEndpoint(n.PollAction),
                        ResponseType = ParseHttpResponseValueType(
                            n.ResponseType,
                            GetLegacyPollResponseType(n.PollAction)),
                        RequestTimeoutMs = n.RequestTimeoutMs ?? 5000,
                        TimeoutMs = n.TimeoutMs ?? 160000,
                        IntervalMs = n.IntervalMs ?? 5000,
                        FailOnTimeout = n.FailOnTimeout ?? true
                    },

                    "Build Test Report" or "BUILD_TEST_REPORT" or "Собрать отчёт" => new BuildTestReportNodeViewModel
                    {
                        Location = location,
                        ReportVariableName = n.ReportVariableName ?? "TestReportJson",
                        DeviceName = n.DeviceName ?? "PSW+UPS-Box 8x2Pro",
                        DeviceType = GetObjectAsInt(n.DeviceType, 32),
                        SerialVariableName = n.SerialVariableName ?? "SerialShort",
                        MacVariableName = n.MacVariableName ?? "Dut.NewMac",
                        IncludeAllVariables = n.IncludeAllVariables ?? true
                    },

                    "Print Label" or "PRINT_LABEL" or "Печать этикетки" => new PrintLabelNodeViewModel
                    {
                        Location = location,
                        PrinterName = n.PrinterName ?? "Zebra",
                        DeviceName = n.DeviceName ?? "PSW+UPS-Box 8x2Pro",
                        DeviceType = GetObjectAsInt(n.DeviceType, 32),
                        SerialVariableName = n.SerialVariableName ?? "SerialShort",
                        MacVariableName = n.MacVariableName ?? "Dut.NewMac",
                        Copies = n.Copies ?? 4,
                        IncludeMac = n.IncludeMac ?? true,
                        EquipmentFieldUse = n.EquipmentFieldUse ?? false,
                        EquipmentType = n.EquipmentType ?? 0,
                        EquipmentText = n.EquipmentText ?? string.Empty,
                        FailOnPrinterError = n.FailOnPrinterError ?? true
                    },

                    "Send Test Report" or "SEND_TEST_REPORT" or "Отправить отчёт" => new SendTestReportNodeViewModel
                    {
                        Location = location,
                        ServerBaseUrl = n.ServerBaseUrl ?? "http://server-address",
                        ReportVariableName = n.ReportVariableName ?? "TestReportJson",
                        Endpoint = n.Endpoint ?? "/api/Api.svc/result.json",
                        TimeoutMs = n.TimeoutMs ?? 10000,
                        RetryCount = n.RetryCount ?? 1,
                        RetryDelayMs = n.RetryDelayMs ?? 1000,
                        SaveLocalCopy = n.SaveLocalCopy ?? true,
                        LocalReportsDirectory = n.LocalReportsDirectory ?? "reports",
                        FailOnError = n.FailOnError ?? false
                    },

                    "Subtest" or "SUBTEST" or "Подтест" => CreateSubtestNode(n, location),

                    "For Slaves" or "Цикл For" => CreateForEachSlaveNode(n, location),

                    "Check Register Equality" or "Проверка равенства" => CreateCheckEqualityNode(n, location),

                    "Wait Until" or "Ожидание значения" => CreateWaitUntilNode(n, location),

                    "Poll Register" or "Опрос регистра" => CreatePollRegisterNode(n, location),

                    "Operator Action" or "Действие оператора" => new OperatorActionNodeViewModel
                    {
                        Location = location,
                        Message = n.Text ?? string.Empty
                    },

                    _ => throw new InvalidOperationException($"Неизвестный тип ноды: {type}")
                };

                node.NodeColor = n.Color ?? "blue";

                nodeMap[n.Id] = node;
                graph.Nodes.Add(node);
            }

            foreach (var c in dto.Connections)
            {
                if (!nodeMap.TryGetValue(c.SourceNodeId, out var srcNode))
                    continue;

                if (!nodeMap.TryGetValue(c.TargetNodeId, out var tgtNode))
                    continue;

                var srcConn = FindConnector(
                    srcNode,
                    srcNode.Output.Concat(srcNode.Input),
                    c.SourceConnector);

                var tgtConn = FindConnector(
                    tgtNode,
                    tgtNode.Input.Concat(tgtNode.Output),
                    c.TargetConnector);

                if (srcConn == null || tgtConn == null)
                    continue;

                graph.Connections.Add(new ConnectionViewModel(srcConn, tgtConn));
            }
        }

        private static ModbusWriteNodeViewModel CreateModbusWriteNode(NodeDto n, Point location)
        {
            var node = new ModbusWriteNodeViewModel
            {
                Location = location,
                SlaveId = n.SlaveId ?? 0,
                Address = n.Address ?? 0,
                Value = n.Value ?? 0,
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false,
                VerifyWrite = n.VerifyWrite ?? false
            };

            // Восстанавливаем SelectedSlave и SelectedRegister из SlaveRegistry
            node.RestoreSelections();

            return node;
        }

        private static CheckRegisterRangeNodeViewModel CreateCheckRangeNode(NodeDto n, Point location)
        {
            var node = new CheckRegisterRangeNodeViewModel
            {
                Location = location,
                SlaveId = n.SlaveId ?? 0,
                Address = n.Address ?? 0,
                Min = ToInt(n.Min),
                Max = ToInt(n.Max),
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false,
                LiveRead = n.LiveRead ?? false
            };

            node.RestoreSelections();

            return node;
        }

        private static CheckRegisterEqualityNodeViewModel CreateCheckEqualityNode(NodeDto n, Point location)
        {
            var node = new CheckRegisterEqualityNodeViewModel
            {
                Location = location,
                SlaveId = n.SlaveId ?? 0,
                Address = n.Address ?? 0,
                ExpectedValue = GetExpectedValueAsInt(n.ExpectedValue),
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false,
                LiveRead = n.LiveRead ?? false
            };

            node.RestoreSelections();

            return node;
        }

        private static WaitUntilNodeViewModel CreateWaitUntilNode(NodeDto n, Point location)
        {
            var node = new WaitUntilNodeViewModel
            {
                Location = location,
                SlaveId = n.SlaveId ?? 0,
                Address = n.Address ?? 0,
                ExpectedValue = GetExpectedValueAsInt(n.ExpectedValue),
                TimeoutMs = n.DurationMs ?? 5000,
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false,
                LiveRead = n.LiveRead ?? false
            };

            node.RestoreSelections();

            return node;
        }

        private static PollRegisterNodeViewModel CreatePollRegisterNode(NodeDto n, Point location)
        {
            var node = new PollRegisterNodeViewModel
            {
                Location = location,
                SlaveId = n.SlaveId ?? 0,
                Address = n.Address ?? 0,
                Min = ToInt(n.Min),
                Max = ToInt(n.Max),
                SampleCount = n.SampleCount ?? 10,
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false,
                LiveRead = n.LiveRead ?? false
            };

            node.RestoreSelections();

            return node;
        }

        private static ForEachSlaveNodeViewModel CreateForEachSlaveNode(NodeDto n, Point location)
        {
            var node = new ForEachSlaveNodeViewModel
            {
                Location = location,
                FromSlaveId = n.FromSlaveId ?? 1,
                ToSlaveId = n.ToSlaveId ?? 20,
                Step = n.Step ?? 1,
                StopOnError = n.StopOnError ?? true
            };

            if (n.Body != null)
                DeserializeGraph(n.Body, node.BodyGraph, isBodyGraph: true);
            else
                node.EnsureDefaultBodyNodes();

            return node;
        }

        private static SubtestNodeViewModel CreateSubtestNode(NodeDto n, Point location)
        {
            var node = new SubtestNodeViewModel
            {
                Location = location,
                Name = n.Name ?? "Подтест",
                Description = n.Description ?? string.Empty,
                IsEnabled = n.IsEnabled ?? true,
                StopOnError = n.StopOnError ?? true,
                RunOnFailure = n.RunOnFailure ?? false
            };

            var body = n.BodyGraph ?? n.Body;

            if (body != null)
                DeserializeGraph(body, node.BodyGraph, isBodyGraph: true);
            else
                node.EnsureDefaultBodyNodes();

            node.BodyGraph.UsesBodyBoundaryNodes = false;

            return node;
        }

        private static ConnectorViewModel? FindConnector(
            NodeViewModel node,
            IEnumerable<ConnectorViewModel> connectors,
            string title)
        {
            var connector = connectors.FirstOrDefault(x => x.Title == title);

            if (connector != null)
                return connector;

            if (node is ForEachSlaveNodeViewModel)
            {
                return title switch
                {
                    "Success" => connectors.FirstOrDefault(x => x.Title == "True"),
                    "Error" => connectors.FirstOrDefault(x => x.Title == "False"),
                    _ => null
                };
            }

            return null;
        }

        private static int ToInt(double? value)
        {
            return value.HasValue
                ? (int)value.Value
                : 0;
        }

        private static int GetExpectedValueAsInt(object? value)
        {
            return value switch
            {
                null => 0,
                int i => i,
                long l => (int)l,
                double d => (int)d,
                decimal d => (int)d,
                string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                JsonElement json => JsonElementToInt(json),
                _ => 0
            };
        }

        private static int JsonElementToInt(JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.Number when json.TryGetInt32(out var value) => value,
                JsonValueKind.String when int.TryParse(json.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
                _ => 0
            };
        }

        private static string GetExpectedValueAsString(object? value, string fallback)
        {
            return value switch
            {
                null => fallback,
                string s => s,
                bool b => b.ToString(),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                JsonElement json => JsonElementToString(json, fallback),
                _ => value.ToString() ?? fallback
            };
        }

        private static string JsonElementToString(JsonElement json, string fallback)
        {
            return json.ValueKind switch
            {
                JsonValueKind.String => json.GetString() ?? fallback,
                JsonValueKind.Number => json.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => fallback
            };
        }

        private static VariableComparisonType ParseComparisonType(string? value)
        {
            return Enum.TryParse<VariableComparisonType>(value, ignoreCase: true, out var parsed)
                ? parsed
                : VariableComparisonType.Number;
        }

        private static HttpResponseValueType ParseHttpResponseValueType(
            string? value,
            HttpResponseValueType fallback = HttpResponseValueType.Integer)
        {
            if (string.Equals(value, "Double", StringComparison.OrdinalIgnoreCase))
            {
                return HttpResponseValueType.Number;
            }

            return Enum.TryParse<HttpResponseValueType>(value, ignoreCase: true, out var parsed)
                ? parsed
                : fallback;
        }

        private static string GetLegacyPollEndpoint(string? pollAction)
        {
            return pollAction?.ToLowerInvariant() switch
            {
                "getupsvoltage" => "/api/getUpsVoltage",
                "getirpstatus" => "/api/isUPS",
                _ => "/api/getUpsStatus"
            };
        }

        private static HttpResponseValueType GetLegacyPollResponseType(string? pollAction)
        {
            return pollAction?.Equals("GetUpsVoltage", StringComparison.OrdinalIgnoreCase) == true
                ? HttpResponseValueType.Number
                : HttpResponseValueType.Integer;
        }

        private static string GetObjectAsString(object? value, string fallback)
        {
            return value switch
            {
                null => fallback,
                string s => s,
                JsonElement json => JsonElementToString(json, fallback),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? fallback
            };
        }

        private static int GetObjectAsInt(object? value, int fallback)
        {
            return value switch
            {
                null => fallback,
                int i => i,
                long l => (int)l,
                double d => (int)d,
                string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                JsonElement json => JsonElementToInt(json),
                _ => fallback
            };
        }

        private static string PortsToText(List<DataTestPortDto>? ports)
        {
            if (ports == null || ports.Count == 0)
            {
                return RunDataTestNodeViewModel.DefaultPortsText;
            }

            return string.Join(
                Environment.NewLine,
                ports.Select(port => port.BandwidthMbps.HasValue
                    ? $"{port.Name},{port.InIp},{port.OutIp},{RunDataTestStep.NormalizeBandwidth(port.BandwidthMbps.Value)}"
                    : $"{port.Name},{port.InIp},{port.OutIp}"));
        }

        private static string NormalizeDataTestPortsText(string portsText)
        {
            var lines = portsText.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return string.Join(
                Environment.NewLine,
                lines.Select(line =>
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 4 &&
                        int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bandwidthMbps))
                    {
                        parts[3] = RunDataTestStep.NormalizeBandwidth(bandwidthMbps).ToString(CultureInfo.InvariantCulture);
                    }

                    return string.Join(",", parts.Select(part => part.Trim()));
                }));
        }

        public static string? ReadProfileName(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var dto = JsonSerializer.Deserialize<GraphDto>(json, JsonOptions);
                return dto?.Name;
            }
            catch
            {
                return null;
            }
        }
    }
}
