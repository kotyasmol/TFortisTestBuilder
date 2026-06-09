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
            HttpRequestNodeViewModel => "HTTP Request",
            RequestTestPageNodeViewModel => "Request Test Page",
            ParseTestPageNodeViewModel => "Parse Test Page",
            CheckVariableEqualityNodeViewModel => "Check Variable Equality",
            CheckVariableRangeNodeViewModel => "Check Variable Range",
            ClearArpCacheNodeViewModel => "Clear ARP Cache",
            GetSerialNumberFromServerNodeViewModel => "Get Serial Number",
            SendUdpSetMacPacketNodeViewModel => "Send UDP Set MAC",
            RunDataTestNodeViewModel => "Run Data Test",
            GetUpsStatusNodeViewModel => "Get UPS Status",
            GetUpsVoltageNodeViewModel => "Get UPS Voltage",
            PrintLabelNodeViewModel => "Print Label",
            SendTestReportNodeViewModel => "Send Test Report",
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
                    Y = node.Location.Y
                };

                switch (node)
                {
                    case DelayNodeViewModel d:
                        n.Milliseconds = d.Milliseconds;
                        break;

                    case LabelNodeViewModel l:
                        n.Text = l.Text;
                        break;

                    case ModbusWriteNodeViewModel w:
                        n.SlaveId = w.SlaveId;
                        n.Address = w.Address;
                        n.Value = w.Value;
                        n.UseCurrentSlaveId = w.UseCurrentSlaveId;
                        break;

                    case CheckRegisterRangeNodeViewModel c:
                        n.SlaveId = c.SlaveId;
                        n.Address = c.Address;
                        n.Min = c.Min;
                        n.Max = c.Max;
                        n.UseCurrentSlaveId = c.UseCurrentSlaveId;
                        break;

                    case CheckRegisterEqualityNodeViewModel eq:
                        n.SlaveId = eq.SlaveId;
                        n.Address = eq.Address;
                        n.ExpectedValue = eq.ExpectedValue;
                        n.UseCurrentSlaveId = eq.UseCurrentSlaveId;
                        break;

                    case WaitUntilNodeViewModel w:
                        n.SlaveId = w.SlaveId;
                        n.Address = w.Address;
                        n.ExpectedValue = w.ExpectedValue;
                        n.DurationMs = w.TimeoutMs;
                        n.UseCurrentSlaveId = w.UseCurrentSlaveId;
                        break;

                    case PollRegisterNodeViewModel p:
                        n.SlaveId = p.SlaveId;
                        n.Address = p.Address;
                        n.Min = p.Min;
                        n.Max = p.Max;
                        n.SampleCount = p.SampleCount;
                        n.UseCurrentSlaveId = p.UseCurrentSlaveId;
                        break;

                    case OperatorActionNodeViewModel op:
                        n.Text = op.Message;
                        break;

                    case HttpRequestNodeViewModel h:
                        n.Url = h.Url;
                        n.TimeoutMs = h.TimeoutMs;
                        n.OutputVariableName = h.OutputVariableName;
                        n.RequireSuccessStatusCode = h.RequireSuccessStatusCode;
                        break;

                    case RequestTestPageNodeViewModel r:
                        n.BaseUrl = r.BaseUrl;
                        n.Path = r.Path;
                        n.TimeoutMs = r.TimeoutMs;
                        n.RetryCount = r.RetryCount;
                        n.RetryDelayMs = r.RetryDelayMs;
                        n.OutputVariableName = r.OutputVariableName;
                        n.FailOnError = r.FailOnError;
                        n.RequireSuccessStatusCode = r.RequireSuccessStatusCode;
                        n.ExpectedContentContains = r.ExpectedContentContains;
                        n.SaveStatusCodeTo = r.SaveStatusCodeTo;
                        n.SaveErrorTo = r.SaveErrorTo;
                        n.SaveElapsedMsTo = r.SaveElapsedMsTo;
                        break;

                    case ParseTestPageNodeViewModel p:
                        n.InputVariableName = p.InputVariableName;
                        n.OutputPrefix = p.OutputPrefix;
                        n.FailOnInvalidXml = p.FailOnInvalidXml;
                        n.ApplyPsw2gAdc25Fix = p.ApplyPsw2gAdc25Fix;
                        n.FieldNames = p.FieldNames;
                        n.RequiredFieldNames = p.RequiredFieldNames;
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
                        n.PortsText = d.PortsText;
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
                        Text = n.Text ?? "Этап"
                    },

                    "Write Register" or "Запись регистра" => CreateModbusWriteNode(n, location),

                    "Check Register Range" or "Проверка диапазона" => CreateCheckRangeNode(n, location),

                    "HTTP Request" => new HttpRequestNodeViewModel
                    {
                        Location = location,
                        Url = n.Url ?? HttpRequestStep.DefaultUrl,
                        TimeoutMs = n.TimeoutMs ?? HttpRequestStep.DefaultTimeoutMs,
                        OutputVariableName = n.OutputVariableName ?? HttpRequestStep.DefaultOutputVariableName,
                        RequireSuccessStatusCode = n.RequireSuccessStatusCode ?? true
                    },

                    "Request Test Page" or "REQUEST_TEST_PAGE" or "Запрос тестовой страницы" => new RequestTestPageNodeViewModel
                    {
                        Location = location,
                        BaseUrl = n.BaseUrl ?? RequestTestPageStep.DefaultBaseUrl,
                        Path = n.Path ?? RequestTestPageStep.DefaultPath,
                        TimeoutMs = n.TimeoutMs ?? RequestTestPageStep.DefaultTimeoutMs,
                        RetryCount = n.RetryCount ?? RequestTestPageStep.DefaultRetryCount,
                        RetryDelayMs = n.RetryDelayMs ?? RequestTestPageStep.DefaultRetryDelayMs,
                        OutputVariableName = n.OutputVariableName ?? RequestTestPageStep.DefaultOutputVariableName,
                        FailOnError = n.FailOnError ?? true,
                        RequireSuccessStatusCode = n.RequireSuccessStatusCode ?? true,
                        ExpectedContentContains = n.ExpectedContentContains ?? RequestTestPageStep.DefaultExpectedContentContains,
                        SaveStatusCodeTo = n.SaveStatusCodeTo ?? RequestTestPageStep.DefaultStatusCodeVariableName,
                        SaveErrorTo = n.SaveErrorTo ?? RequestTestPageStep.DefaultErrorVariableName,
                        SaveElapsedMsTo = n.SaveElapsedMsTo ?? RequestTestPageStep.DefaultElapsedMsVariableName
                    },

                    "Parse Test Page" or "PARSE_TEST_PAGE" or "Парсинг тестовой страницы" => new ParseTestPageNodeViewModel
                    {
                        Location = location,
                        InputVariableName = n.InputVariableName ?? ParseTestPageStep.DefaultInputVariableName,
                        OutputPrefix = n.OutputPrefix ?? ParseTestPageStep.DefaultOutputPrefix,
                        FailOnInvalidXml = n.FailOnInvalidXml ?? true,
                        ApplyPsw2gAdc25Fix = n.ApplyPsw2gAdc25Fix ?? true,
                        FieldNames = n.FieldNames ?? ParseTestPageStep.DefaultFieldNames,
                        RequiredFieldNames = n.RequiredFieldNames ?? ParseTestPageStep.DefaultRequiredFieldNames
                    },

                    "Check Variable Equality" or "CHECK_VARIABLE_EQUALITY" or "Проверка переменной" => new CheckVariableEqualityNodeViewModel
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
                        Arguments = n.Arguments ?? "-d",
                        TimeoutMs = n.TimeoutMs ?? 5000,
                        FailOnError = n.FailOnError ?? false
                    },

                    "Get Serial Number" or "GET_SERIAL_NUMBER_FROM_SERVER" or "Получить серийный номер" => new GetSerialNumberFromServerNodeViewModel
                    {
                        Location = location,
                        ServerBaseUrl = n.ServerBaseUrl ?? "http://server-address",
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
                        PortsText = n.PortsText ?? PortsToText(n.Ports),
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

                nodeMap[n.Id] = node;
                graph.Nodes.Add(node);
            }

            foreach (var c in dto.Connections)
            {
                if (!nodeMap.TryGetValue(c.SourceNodeId, out var srcNode))
                    continue;

                if (!nodeMap.TryGetValue(c.TargetNodeId, out var tgtNode))
                    continue;

                var srcConn = srcNode.Output.Concat(srcNode.Input)
                    .FirstOrDefault(x => x.Title == c.SourceConnector);

                var tgtConn = tgtNode.Input.Concat(tgtNode.Output)
                    .FirstOrDefault(x => x.Title == c.TargetConnector);

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
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false
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
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false
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
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false
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
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false
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
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false
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
                return "Port 0,192.168.10.1,192.168.10.2";
            }

            return string.Join(
                Environment.NewLine,
                ports.Select(port => $"{port.Name},{port.InIp},{port.OutIp}"));
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
