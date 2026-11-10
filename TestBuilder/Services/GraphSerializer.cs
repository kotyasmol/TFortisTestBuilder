using Avalonia;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TestBuilder.Serialization;
using TestBuilder.Models;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
using TestBuilder.Domain.Execution;
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
        private static readonly ConditionalWeakTable<object, Dictionary<string, JsonElement>> ExtensionData = new();
        private static readonly ConditionalWeakTable<NodeViewModel, OriginalNodeData> OriginalNodes = new();

        private sealed record OriginalNodeData(string Id, List<DataTestPortDto>? Ports, string? PortsText,
            Dictionary<string, JsonElement>? OptionExtensions);

        internal static void ClearProfileExtensions(GraphWorkspaceViewModel graph) => ExtensionData.Remove(graph);

        private static Dictionary<string, JsonElement>? GetExtensions(object item) =>
            ExtensionData.TryGetValue(item, out var fields) ? fields : null;

        private static void SetExtensions(object item, Dictionary<string, JsonElement>? fields)
        {
            ExtensionData.Remove(item);
            if (fields != null) ExtensionData.Add(item, fields);
        }

        private const string FirstRunPrefix = "First Run (v1): ";
        private const string ProfileOpenPrefix = "Profile Open (v2): ";
        private const string ReadSelftestFieldType = "Read Selftest Field (v1)";
        private const string CaptureRegisterRangeType = "Check Register Range (capture v1)";
        private const string CaptureVariableEqualityType = "Check Variable Equality (capture v1)";
        private const string CaptureCompareVariablesType = "Compare Variables (capture v1)";
        private const string CaptureWaitVariableType = "Wait Variable Until (capture v1)";
        private const string SerialWithPrefixType = "Get Serial Number (prefix v1)";
        private const string ReportWithSerialPrefixType = "Build Test Report (prefix v1)";
        private const string RtlReportType = "Send Test Report (RTL v1)";
        internal const string ReportStartType = "Start (report v1)";
        internal const string CatalogSerialsStartType = "Start (catalog serials v1)";
        private static string UnwrapNodeType(string? type) => type?.StartsWith(ProfileOpenPrefix, StringComparison.Ordinal) == true
            ? type[ProfileOpenPrefix.Length..] : type?.StartsWith(FirstRunPrefix, StringComparison.Ordinal) == true
                ? type[FirstRunPrefix.Length..] : type ?? "";

        // Маппинг ViewModel -> английский тип для JSON
        private static string GetNodeType(NodeViewModel node) => node switch
        {
            SerialConsoleNodeViewModel { ConnectOnly: true } => "Serial Console (connect v1)",
            SerialConsoleNodeViewModel => "Serial Console",
            RunProgramNodeViewModel => "Run Program",
            ParseResponseNodeViewModel => "Parse Response",
            CheckLedColorNodeViewModel => "Check LED Color",
            StartNodeViewModel => "Start",
            EndNodeViewModel => "End",
            BodyStartNodeViewModel => "Body Start",
            BodyEndNodeViewModel => "Body End",
            DelayNodeViewModel => "Delay",
            LabelNodeViewModel => "Label",
            ModbusWriteNodeViewModel => "Write Register",
            CheckRegisterRangeNodeViewModel check when !string.IsNullOrWhiteSpace(check.ResultVariableName) => CaptureRegisterRangeType,
            CheckRegisterRangeNodeViewModel => "Check Register Range",
            WaitUntilNodeViewModel => "Wait Until",
            OperatorActionNodeViewModel o => string.IsNullOrWhiteSpace(o.InputVariable) ? "Operator Action" : "Operator Input (v1)",
            SelfTestCheckNodeViewModel { IsReadingField: true } => ReadSelftestFieldType,
            SelfTestCheckNodeViewModel { RequiresTimingVersion: true } => "Selftest Check (timing v1)",
            SelfTestCheckNodeViewModel => "Selftest Check",
            CheckVariableEqualityNodeViewModel check when !string.IsNullOrWhiteSpace(check.ResultVariableName) => CaptureVariableEqualityType,
            CheckVariableEqualityNodeViewModel => "Check Variable Equality",
            CheckVariableRangeNodeViewModel => "Check Variable Range",
            ClearArpCacheNodeViewModel => "Clear ARP Cache",
            GetSerialNumberFromServerNodeViewModel serial when !string.IsNullOrWhiteSpace(serial.ExpectedSerialPrefix) => SerialWithPrefixType,
            GetSerialNumberFromServerNodeViewModel => "Get Serial Number",
            SetProMacNodeViewModel => "Set Pro MAC",
            SetPswMacNodeViewModel => "Set PSW MAC (UDP)",
            UpdatePswFirmwareNodeViewModel => "Update PSW Firmware",
            RunDataTestNodeViewModel => "Run Data Test",
            ReadHttpVariableNodeViewModel => "Read HTTP Variable",
            BuildMacFromSerialNodeViewModel => "Build MAC From Serial",
            CompareVariablesNodeViewModel compare when !string.IsNullOrWhiteSpace(compare.ResultVariableName) => CaptureCompareVariablesType,
            CompareVariablesNodeViewModel => "Compare Variables",
            WaitVariableUntilNodeViewModel wait when !string.IsNullOrWhiteSpace(wait.ResultVariableName) => CaptureWaitVariableType,
            WaitVariableUntilNodeViewModel wait when !string.IsNullOrWhiteSpace(wait.ReportName) => "Wait Variable Until (report v1)",
            WaitVariableUntilNodeViewModel => "Wait Variable Until",
            BuildTestReportNodeViewModel report when !string.IsNullOrWhiteSpace(report.ExpectedSerialPrefix) => ReportWithSerialPrefixType,
            BuildTestReportNodeViewModel => "Build Test Report",
            PrintLabelNodeViewModel p => p.UseLastTestLabel ? "Print Label (last test v1)" : "Print Label",
            SendTestReportNodeViewModel n => RtlTestReport.IsEndpoint(n.Endpoint) ? RtlReportType : "Send Test Report",
            SubtestNodeViewModel { FailureOrder: not null } => "Subtest (cleanup order v1)",
            SubtestNodeViewModel => "Subtest",
            ForEachSlaveNodeViewModel f => f.RepeatOnSameSlave ? "For Slaves (repeat v1)" : "For Slaves",
            _ => node.Title
        };

        public static string Serialize(TestViewModel vm, string profileName, DateTimeOffset? updatedAt = null, string? configurationName = null)
        {
            var dto = SerializeGraph(vm.RootGraph, profileName);
            dto.UpdatedAt = updatedAt ?? vm.RootGraph.UpdatedAt;
            dto.Hardware = vm.RootGraph.Hardware;
            dto.Hardware?.Validate();
            GraphCompiler.ValidateConnections(vm.RootGraph, dto.Hardware);
            dto.Board = vm.RootGraph.Board;
            dto.Board?.Validate();
            dto.ProductKind = vm.RootGraph.ProductKind;
            // Older runtimes must reject profiles whose equipment/layout they would ignore.
            if (dto.Hardware != null || dto.Board != null || dto.ProductKind == "Board" || RequiresBoardRuntime(vm.RootGraph))
                foreach (var n in dto.Nodes.Where(n => n.Type.StartsWith("Start", StringComparison.Ordinal))) n.Type = "Start (board v1)";
            if (dto.Hardware is { PortName: not null } || dto.Hardware?.ConsoleDevices.Count > 0)
                foreach (var n in dto.Nodes.Where(n => n.Type.StartsWith("Start", StringComparison.Ordinal))) n.Type = "Start (connections v1)";
            if (vm.RootGraph.QuickActions.Count > 0)
            {
                ValidateActions(vm.RootGraph);
                dto.QuickActions = vm.RootGraph.QuickActions.Select(a => new ProfileActionDto {
                    ExtensionData = GetExtensions(a),
                    Id = a.Id, Name = a.Name, Color = a.Color, Display = a.Display.ToString(),
                    Labels = a.Labels?.Copy(), StateBinding = a.StateBinding?.Copy(), Graph = ExportActionGraph(a.Graph), OffGraph = ExportActionGraph(a.OffGraph)
                }).ToList();
                foreach (var n in dto.Nodes.Where(n => n.Type.StartsWith("Start", StringComparison.Ordinal))) n.Type = "Start (actions v1)";
            }
            ValidateRegisterIndicators(vm.RootGraph);
            if (vm.RootGraph.RegisterIndicators.Count > 0)
            {
                dto.RegisterIndicators = vm.RootGraph.RegisterIndicators.Select(i => i.Copy()).ToList();
                foreach (var n in dto.Nodes.Where(n => n.Type.StartsWith("Start", StringComparison.Ordinal))) n.Type = "Start (indicators v1)";
            }
            vm.RootGraph.PingMonitor?.Validate();
            dto.PingMonitor = vm.RootGraph.PingMonitor?.Copy();
            if (dto.PingMonitor != null)
                foreach (var n in dto.Nodes.Where(n => n.Type.StartsWith("Start", StringComparison.Ordinal))) n.Type = "Start (ping v1)";
            if (vm.RootGraph.QuickActions.Any(a => a.StateBinding != null))
                foreach (var n in dto.Nodes.Where(n => n.Type.StartsWith("Start", StringComparison.Ordinal))) n.Type = "Start (action state v1)";
            if (vm.RootGraph.QuickActions.Any(a => a.Labels != null))
                foreach (var n in dto.Nodes.Where(n => n.Type.StartsWith("Start", StringComparison.Ordinal))) n.Type = "Start (action labels v1)";
            dto.ReportTemplate = ValidateReportTemplate(vm.RootGraph.ReportTemplate);
            if (dto.ReportTemplate != null)
                foreach (var n in dto.Nodes.Where(n => n.Type.StartsWith("Start", StringComparison.Ordinal))) n.Type = ReportStartType;
            dto.DeviceModel = vm.RootGraph.DeviceModel;
            dto.DeviceModelId = vm.RootGraph.DeviceModelId;
            dto.UseCatalogSerials = vm.RootGraph.UseCatalogSerials;
            dto.LabelVariant = vm.RootGraph.LabelVariant;
            if (dto.UseCatalogSerials)
                foreach (var n in dto.Nodes.Where(n => n.Type.StartsWith("Start", StringComparison.Ordinal))) n.Type = CatalogSerialsStartType;
            dto.ConfigurationName = configurationName ?? vm.RootGraph.ConfigurationName;
            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        private static ReportTemplate? ValidateReportTemplate(ReportTemplate? template)
        {
            if (template?.Validate() is { } error)
                throw new InvalidOperationException(error);
            return template;
        }

        private static bool RequiresBoardRuntime(GraphWorkspaceViewModel graph) => graph.Nodes.Any(n =>
            n is ModbusWriteNodeViewModel { RegisterArea: not TestBuilder.Domain.Modbus.ModbusArea.Holding } or
                CheckRegisterRangeNodeViewModel { RegisterArea: not TestBuilder.Domain.Modbus.ModbusArea.Holding } or
                WaitUntilNodeViewModel { RegisterArea: not TestBuilder.Domain.Modbus.ModbusArea.Holding } or
                CheckVariableEqualityNodeViewModel { ComparisonType: TestBuilder.Domain.Steps.VariableComparisonType.VersionAtLeast } or
                CompareVariablesNodeViewModel { ComparisonType: TestBuilder.Domain.Steps.VariableComparisonType.VersionAtLeast } or
                WaitVariableUntilNodeViewModel { ComparisonType: TestBuilder.Domain.Steps.VariableComparisonType.VersionAtLeast } ||
            n is ICompositeNodeViewModel c && RequiresBoardRuntime(c.BodyGraph));

        private static GraphDto SerializeGraph(GraphWorkspaceViewModel graph, string name)
        {
            var dto = new GraphDto { Name = name, ExtensionData = GetExtensions(graph) };
            var nodeIds = new Dictionary<NodeViewModel, string>();
            var requiresParallelRuntime = ContainsParallelConnections(graph, new HashSet<GraphWorkspaceViewModel>());
            var usedIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in graph.Nodes)
                if (OriginalNodes.TryGetValue(node, out var original) && usedIds.Add(original.Id))
                    nodeIds.Add(node, original.Id);
            var nextId = 0;
            foreach (var node in graph.Nodes.Where(n => !nodeIds.ContainsKey(n)))
            {
                string id;
                do { id = (nextId++).ToString(CultureInfo.InvariantCulture); } while (!usedIds.Add(id));
                nodeIds.Add(node, id);
            }

            foreach (var node in graph.Nodes)
            {
                OriginalNodes.TryGetValue(node, out var original);
                var n = new NodeDto
                {
                    Id = nodeIds[node],
                    ExtensionData = GetExtensions(node),
                    // Older executables reject this alias instead of silently dropping
                    // all but the last branch. It still renders as the ordinary Start.
                    Type = node.RunOnlyOnFirstRun ? ProfileOpenPrefix + GetNodeType(node) : requiresParallelRuntime && node is StartNodeViewModel ? "Start (parallel v1)"
                        : requiresParallelRuntime && node is BodyStartNodeViewModel ? "Body Start (parallel v1)"
                        : GetNodeType(node),
                    X = node.Location.X,
                    Y = node.Location.Y,
                    BoardElements = node.BoardElements,
                    CustomName = string.IsNullOrWhiteSpace(node.CustomName) ? null : node.CustomName.Trim(),
                    RunOnlyOnFirstRun = node.RunOnlyOnFirstRun,
                    Color = node.NodeColor
                };

                // An explicit empty operator name keeps the editor name from being read as a legacy shared name.
                var operatorName = node.OperatorName?.Trim() ?? string.Empty;
                if (n.CustomName != null || operatorName.Length > 0)
                    n.OperatorName = operatorName;

                switch (node)
                {
                    case SerialConsoleNodeViewModel opSerialConsole:
                        n.SerialConsole = opSerialConsole.Options with { ExtensionData = original?.OptionExtensions };
                        break;
                    case RunProgramNodeViewModel opRunProgram:
                        n.RunProgram = opRunProgram.Options with { ExtensionData = original?.OptionExtensions };
                        break;
                    case ParseResponseNodeViewModel opParseResponse:
                        n.ParseResponse = opParseResponse.Options with { ExtensionData = original?.OptionExtensions };
                        break;
                    case CheckLedColorNodeViewModel opCheckLedColor:
                        n.CheckLedColor = opCheckLedColor.Options with { ExtensionData = original?.OptionExtensions };
                        break;
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
                        n.RegisterArea = w.RegisterArea;
                        n.Value = w.Value;
                        n.UseCurrentSlaveId = w.UseCurrentSlaveId;
                        n.VerifyWrite = w.VerifyWrite;
                        break;

                    case CheckRegisterRangeNodeViewModel c:
                        n.SlaveId = c.SlaveId;
                        n.Address = c.Address;
                        n.RegisterArea = c.RegisterArea;
                        n.Min = c.Min;
                        n.Max = c.Max;
                        n.UseCurrentSlaveId = c.UseCurrentSlaveId;
                        n.LiveRead = c.LiveRead;
                        n.ReadAttempts = c.ReadAttempts;
                        n.ReadIntervalMs = c.ReadIntervalMs;
                        n.ReportName = c.ReportName;
                        n.ResultVariableName = string.IsNullOrWhiteSpace(c.ResultVariableName) ? null : c.ResultVariableName.Trim();
                        break;

                    case WaitUntilNodeViewModel w:
                        n.SlaveId = w.SlaveId;
                        n.Address = w.Address;
                        n.RegisterArea = w.RegisterArea;
                        n.ExpectedValue = w.ExpectedValue;
                        n.DurationMs = w.TimeoutMs;
                        n.UseCurrentSlaveId = w.UseCurrentSlaveId;
                        n.LiveRead = w.LiveRead;
                        break;

                    case OperatorActionNodeViewModel op:
                        n.OutputVariableName = op.InputVariable;
                        n.Text = op.Message;
                        break;

                    case SelfTestCheckNodeViewModel s:
                        n.Url = s.Url;
                        n.TimeoutMs = s.TimeoutMs;
                        n.SelfTestUseLegacyTimeout = s.UseLegacyTimeout ? null : false;
                        n.SelfTestBrowserSettleDelayMs = s.BrowserSettleDelayMs == SelfTestCheckStep.DefaultBrowserSettleDelayMs
                            ? null : s.BrowserSettleDelayMs;
                        n.PollIntervalMs = s.PollIntervalMs;
                        n.OutputPrefix = s.OutputPrefix;
                        n.ReadField = s.IsReadingField ? s.ReadField.Trim() : null;
                        n.ValidationRules = s.ValidationRules;
                        n.Psw2G6FHardwareChecks = s.Psw2G6FHardwareChecks;
                        n.FailOnError = s.FailOnError;
                        break;

                    case CheckVariableEqualityNodeViewModel v:
                        n.VariableName = v.VariableName;
                        n.ExpectedValue = v.ExpectedValue;
                        n.ComparisonType = v.ComparisonType.ToString();
                        n.FailMessage = v.FailMessage;
                        n.ResultVariableName = string.IsNullOrWhiteSpace(v.ResultVariableName) ? null : v.ResultVariableName.Trim();
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
                        n.ExpectedSerialPrefix = string.IsNullOrWhiteSpace(s.ExpectedSerialPrefix) ? null : s.ExpectedSerialPrefix.Trim();
                        n.CpuIdVariableName = s.CpuIdVariableName;
                        n.TimeoutMs = s.TimeoutMs;
                        n.RetryCount = s.RetryCount;
                        n.RetryDelayMs = s.RetryDelayMs;
                        n.OutputVariableName = s.OutputVariableName;
                        n.FailOnError = s.FailOnError;
                        n.UseFixedSerialNumber = s.UseFixedSerialNumber;
                        n.FixedSerialNumber = s.FixedSerialNumber;
                        break;

                    case SetProMacNodeViewModel proMac:
                        n.BatchPath = proMac.BatchPath;
                        n.MacVariableName = proMac.MacVariableName;
                        n.BoardVersion = proMac.BoardVersion;
                        n.TimeoutMs = proMac.TimeoutMs;
                        n.FailOnError = proMac.FailOnError;
                        break;

                    case SetPswMacNodeViewModel pswMac:
                        n.DestinationIp = pswMac.DestinationIp;
                        n.LocalIp = pswMac.LocalIp;
                        n.LocalPort = pswMac.LocalPort;
                        n.UdpPort = pswMac.UdpPort;
                        n.MacVariableName = pswMac.MacVariableName;
                        n.TimeoutMs = pswMac.TimeoutMs;
                        n.FailOnError = pswMac.FailOnError;
                        break;

                    case UpdatePswFirmwareNodeViewModel firmware:
                        n.BaseUrl = firmware.BaseUrl;
                        n.FirmwarePath = firmware.FirmwarePath;
                        n.TargetVersion = firmware.TargetVersion;
                        n.VersionVariable = firmware.VersionVariable;
                        n.ForceUpdate = firmware.ForceUpdate;
                        break;

                    case RunDataTestNodeViewModel d:
                        n.Mode = RunDataTestStep.NormalizeMode(d.Mode);
                        n.ExpectedPackets = d.ExpectedPackets;
                        n.PacketSizeBytes = d.PacketSizeBytes;
                        n.UdpPort = d.UdpPort;
                        n.MaxPortTestTimeMs = d.MaxPortTestTimeMs;
                        n.AllowGigabit = d.AllowGigabit;
                        n.TargetBandwidthMbps = RunDataTestStep.NormalizeBandwidth(d.TargetBandwidthMbps, d.AllowGigabit);
                        n.DurationMs = d.DurationMs;
                        n.WarmupMs = d.WarmupMs;
                        n.InterPairDelayMs = d.InterPairDelayMs;
                        n.AllowedLossPercent = d.AllowedLossPercent;
                        n.AllowedTxDeficitPercent = d.AllowedTxDeficitPercent;
                        n.Bidirectional = d.Bidirectional;
                        n.PortsText = NormalizeDataTestPortsText(d.PortsText, d.AllowGigabit);
                        if (original != null && original.PortsText == n.PortsText)
                            n.Ports = original.Ports;
                        n.DisabledPortsText = d.DisabledPortsText;
                        n.OutputVariableName = d.OutputVariableName;
                        n.FailOnError = d.FailOnError;
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
                        n.ResultVariableName = string.IsNullOrWhiteSpace(cv.ResultVariableName) ? null : cv.ResultVariableName.Trim();
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
                        n.ReportName = string.IsNullOrWhiteSpace(wait.ReportName) ? null : wait.ReportName.Trim();
                        n.ResultVariableName = string.IsNullOrWhiteSpace(wait.ResultVariableName) ? null : wait.ResultVariableName.Trim();
                        break;

                    case BuildTestReportNodeViewModel br:
                        n.ReportVariableName = br.ReportVariableName;
                        n.SerialVariableName = br.SerialVariableName;
                        n.ExpectedSerialPrefix = string.IsNullOrWhiteSpace(br.ExpectedSerialPrefix) ? null : br.ExpectedSerialPrefix.Trim();
                        n.TestType = br.TestType;
                        n.IncludeAllVariables = br.IncludeAllVariables;
                        break;

                    case PrintLabelNodeViewModel pl:
                        n.UseLastTestLabel = pl.UseLastTestLabel ? true : null;
                        n.PrinterName = pl.PrinterName;
                        n.UseDefaultPrinter = pl.UseDefaultPrinter ? true : null;
                        n.SerialVariableName = pl.SerialVariableName;
                        n.SerialShortVariableName = pl.SerialShortVariableName;
                        n.MacVariableName = pl.MacVariableName;
                        n.UseManualSerialNumber = pl.UseManualSerialNumber;
                        n.ManualSerialNumber = pl.ManualSerialNumber;
                        n.ManualMacAddress = pl.ManualMacAddress;
                        n.Copies = pl.Copies;
                        n.UseQtProZplFormat = pl.UseQtProZplFormat;
                        n.LabelModel = pl.LabelModel;
                        n.LabelTemplate = pl.LabelTemplate?.Copy();
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
                        if (s.FailureOrder is <= 0)
                            throw new InvalidOperationException("Порядок аварийного подтеста должен быть положительным числом.");
                        n.Name = s.Name;
                        n.Description = s.Description;
                        n.IsEnabled = s.IsEnabled;
                        n.StopOnError = s.StopOnError;
                        n.RunOnFailure = s.RunOnFailure;
                        n.FailureOrder = s.FailureOrder;
                        n.BodyGraph = SerializeGraph(s.BodyGraph, s.BodyGraph.Title);
                        break;

                    case ForEachSlaveNodeViewModel f:
                        n.FromSlaveId = f.FromSlaveId;
                        n.ToSlaveId = f.ToSlaveId;
                        n.Step = f.Step;
                        n.StopOnError = f.StopOnError;
                        n.RepeatOnSameSlave = f.RepeatOnSameSlave;
                        n.RepeatCount = f.RepeatCount;
                        n.IndexVariable = f.IndexVariable;
                        n.UntilSuccess = f.UntilSuccess;
                        n.RepeatDelayMs = f.RepeatDelayMs;
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
                    ExtensionData = GetExtensions(conn),
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
            if (vm.HasRunningQuickActions) throw new InvalidOperationException("Дождитесь завершения кнопок перед открытием профиля.");
            var dto = ReadProfileDto(json);
            var loaded = LoadProfileGraph(dto);
            vm.ResetPingMonitor();
            vm.ResetToRootGraph();
            vm.DiscardRootGraphContents();
            SetExtensions(vm.RootGraph, dto.ExtensionData);
            vm.RootGraph.PingMonitor = dto.PingMonitor?.Copy();
            vm.RootGraph.ReportTemplate = loaded.ReportTemplate;
            vm.RootGraph.Title = loaded.Title;
            vm.RootGraph.UpdatedAt = dto.UpdatedAt;
            vm.RootGraph.IsBodyGraph = false;
            vm.RootGraph.Hardware = dto.Hardware;
            vm.RootGraph.Board = dto.Board;
            vm.RootGraph.ProductKind = dto.ProductKind;
            foreach (var action in loaded.QuickActions) vm.RootGraph.QuickActions.Add(action);
            foreach (var indicator in loaded.RegisterIndicators) vm.RootGraph.RegisterIndicators.Add(indicator);
            foreach (var node in loaded.Nodes) vm.RootGraph.Nodes.Add(node);
            foreach (var connection in loaded.Connections) vm.RootGraph.Connections.Add(connection);
            var profile = new GraphProfile(string.Empty, dto.Name, dto.DeviceModel, dto.ConfigurationName, dto.DeviceModelId);
            vm.RootGraph.DeviceModel = profile.DeviceModel;
            vm.RootGraph.DeviceModelId = profile.DeviceModelId;
            vm.RootGraph.UseCatalogSerials = dto.UseCatalogSerials;
            vm.RootGraph.LabelVariant = dto.LabelVariant;
            vm.RootGraph.ConfigurationName = dto.ConfigurationName ??
                (profile.DeviceModel != null ? profile.ConfigurationName : null);

            vm.NotifyDeviceNameChanged();
            vm.ResetToRootGraph();
            // Board viewers need the complete graph to resolve region-to-node bindings.
            vm.NotifyProfileMetadataChanged();

            return dto.Name;
        }

        private static GraphDto ReadProfileDto(string json) =>
            JsonSerializer.Deserialize<GraphDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Не удалось прочитать JSON профиля.");

        internal static void ValidateProfile(string json) => LoadProfileGraph(ReadProfileDto(json)).DisposeNodes();

        internal static string UpdateSnapshotIdentity(string snapshot, GraphProfile profile)
        {
            var dto = ReadProfileDto(snapshot);
            dto.Name = profile.Name;
            dto.ConfigurationName = profile.ConfigurationName;
            dto.UpdatedAt = profile.UpdatedAt;
            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        private static GraphWorkspaceViewModel LoadProfileGraph(GraphDto dto)
        {
            dto.Hardware?.Validate();
            dto.Board?.Validate();
            dto.PingMonitor?.Validate();
            var reportTemplate = ValidateReportTemplate(dto.ReportTemplate);
            if (dto.ProductKind is not ("Switch" or "Board")) throw new InvalidOperationException("Неизвестный вид изделия.");
            RejectRemovedNodes(dto);
            // Parse the entire document before replacing the user's open graph.
            // Malformed links and unsupported runtime versions must not erase it.
            var loaded = new GraphWorkspaceViewModel();
            try
            {
                DeserializeGraph(dto, loaded, isBodyGraph: false);
                loaded.Hardware = dto.Hardware;
                loaded.ReportTemplate = reportTemplate;
                foreach (var indicator in dto.RegisterIndicators ?? new())
                {
                    if (indicator == null) throw new InvalidOperationException("Некорректный индикатор регистра.");
                    loaded.RegisterIndicators.Add(indicator.Copy());
                }
                ValidateRegisterIndicators(loaded);
                foreach (var action in dto.QuickActions ?? new())
                {
                    if (action == null || !Enum.TryParse<ProfileActionDisplay>(action.Display, out var display) || !Enum.IsDefined(display))
                        throw new InvalidOperationException("Неизвестный вид кнопки профиля.");
                    var definition = new ProfileActionDefinition { Id = action.Id, Name = action.Name, Color = action.Color, Display = display, StateBinding = action.StateBinding, Labels = action.Labels };
                    SetExtensions(definition, action.ExtensionData);
                    loaded.QuickActions.Add(definition);
                    definition.Graph = ImportActionGraph(action.Graph, dto.Hardware);
                    definition.OffGraph = ImportActionGraph(action.OffGraph, dto.Hardware);
                }
                ValidateActions(loaded);
                GraphCompiler.ValidateConnections(loaded, dto.Hardware);
            }
            catch
            {
                loaded.DisposeNodes();
                throw;
            }
            return loaded;
        }

        internal static GraphDto ExportActionGraph(GraphWorkspaceViewModel graph)
        {
            if (graph.QuickActions.Count != 0 || graph.RegisterIndicators.Count != 0 || graph.PingMonitor != null || graph.ReportTemplate != null)
                throw new InvalidOperationException("Кнопки, индикаторы, пинг и состав отчёта разрешены только в корневом профиле.");
            ValidateRegisterIndicators(graph);
            return SerializeGraph(graph, graph.Title);
        }
        internal static GraphWorkspaceViewModel ImportActionGraph(GraphDto dto, TestBuilder.Domain.Modbus.HardwareConfiguration? hardware = null)
        {
            if (dto == null || dto.QuickActions?.Count > 0 || dto.RegisterIndicators?.Count > 0 || dto.PingMonitor != null || dto.ReportTemplate != null)
                throw new InvalidOperationException("Некорректный граф кнопки.");
            RejectRemovedNodes(dto);
            var graph = new GraphWorkspaceViewModel();
            try { DeserializeGraph(dto, graph, false); graph.Hardware = hardware; GraphCompiler.ValidateConnections(graph, hardware); return graph; }
            catch { graph.DisposeNodes(); throw; }
        }
        private static void ValidateActions(GraphWorkspaceViewModel graph)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in graph.QuickActions)
            {
                action.Validate();
                if (!ids.Add(action.Id)) throw new InvalidOperationException("Повторяющийся ID кнопки профиля.");
                GraphCompiler.ValidateConnections(action.Graph, graph.Hardware);
                GraphCompiler.ValidateConnections(action.OffGraph, graph.Hardware);
            }
        }

        private static void ValidateRegisterIndicators(GraphWorkspaceViewModel graph)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var indicator in graph.RegisterIndicators)
            {
                if (indicator == null) throw new InvalidOperationException("Некорректный индикатор регистра.");
                indicator.Validate();
                if (!ids.Add(indicator.Id)) throw new InvalidOperationException("Повторяющийся ID индикатора регистра.");
            }
            foreach (var composite in graph.Nodes.OfType<ICompositeNodeViewModel>())
            {
                if (composite.BodyGraph.RegisterIndicators.Count > 0 || composite.BodyGraph.PingMonitor != null || composite.BodyGraph.ReportTemplate != null)
                    throw new InvalidOperationException("Индикаторы, пинг и состав отчёта разрешены только в корневом профиле.");
                ValidateRegisterIndicators(composite.BodyGraph);
            }
        }

        private static bool ContainsParallelConnections(GraphWorkspaceViewModel graph, HashSet<GraphWorkspaceViewModel> visited)
        {
            if (!visited.Add(graph))
                throw new InvalidOperationException("Нельзя сохранить рекурсивное вложение графов.");
            var found = graph.Connections.GroupBy(connection => connection.Source).Any(group => group.Count() > 1);
            foreach (var composite in graph.Nodes.OfType<ICompositeNodeViewModel>())
                found |= ContainsParallelConnections(composite.BodyGraph, visited);
            visited.Remove(graph);
            return found;
        }

        private static void RejectRemovedNodes(GraphDto dto)
        {
            if (dto.Nodes == null || dto.Connections == null)
                throw new InvalidOperationException("В профиле нужны массивы нод и связей.");
            foreach (var node in dto.Nodes)
            {
                if (node == null) throw new InvalidOperationException("Пустая нода в профиле.");
                var type = UnwrapNodeType(string.IsNullOrWhiteSpace(node.Type) ? node.NodeType : node.Type);
                if (type is "Check IO-2 Sensors and Relay" or "CHECK_IO2_SENSORS_AND_RELAY" or "Проверка Sensor1, Sensor2 и реле")
                    throw new InvalidOperationException("Отдельная нода проверки IO-2 удалена. Импортируйте обновлённый профиль: проверка выполняется обычными Write Register и Wait Variable Until.");
                var replacement = type switch
                {
                    "Check Register Equality" or "Проверка равенства" => "Check Register Range с одинаковыми Min и Max",
                    "Poll Register" or "Опрос регистра" => "явную последовательность проверок Check Register Range с задержками Delay",
                    "Get UPS Status" or "GET_UPS_STATUS" or "Получить UPS статус" or
                    "Get UPS Voltage" or "GET_UPS_VOLTAGE" or "Получить UPS напряжение" or
                    "Get IRP Status" or "GET_IRP_STATUS" or "Получить IRP статус" => "Read HTTP Variable с явно заданными endpoint и типом ответа",
                    _ => null
                };
                if (replacement != null)
                    throw new InvalidOperationException($"Нода '{type}' удалена. Обновите профиль: используйте {replacement}.");
                if ((type is "Wait Variable Until" or CaptureWaitVariableType or "Wait Variable Until (report v1)" or "WAIT_VARIABLE_UNTIL" or "Ожидание переменной") &&
                    node.PollAction?.Trim().ToLowerInvariant() is "getupsstatus" or "getupsvoltage" or "getirpstatus")
                    throw new InvalidOperationException($"Режим опроса '{node.PollAction}' удалён. Обновите Wait Variable Until: используйте HttpGet с явно заданными endpoint и типом ответа.");
                if (node.BodyGraph?.QuickActions?.Count > 0 || node.Body?.QuickActions?.Count > 0 ||
                    node.BodyGraph?.RegisterIndicators?.Count > 0 || node.Body?.RegisterIndicators?.Count > 0 ||
                    node.BodyGraph?.PingMonitor != null || node.Body?.PingMonitor != null ||
                    node.BodyGraph?.ReportTemplate != null || node.Body?.ReportTemplate != null)
                    throw new InvalidOperationException("Кнопки, индикаторы, пинг и состав отчёта разрешены только в корневом профиле.");
                if (node.BodyGraph != null) RejectRemovedNodes(node.BodyGraph);
                if (node.Body != null) RejectRemovedNodes(node.Body);
            }
        }

        private static void DeserializeGraph(GraphDto dto, GraphWorkspaceViewModel graph, bool isBodyGraph)
        {
            graph.DisposeNodes();
            graph.Clear();
            SetExtensions(graph, dto.ExtensionData);
            graph.Title = dto.Name;
            graph.IsBodyGraph = isBodyGraph;

            var nodeMap = new Dictionary<string, NodeViewModel>();

            foreach (var n in dto.Nodes)
            {
                var location = new Point(n.X, n.Y);
                var type = string.IsNullOrWhiteSpace(n.Type)
                    ? n.NodeType ?? string.Empty
                    : n.Type;

                var wrappedFirstRun = type.StartsWith(FirstRunPrefix, StringComparison.Ordinal) || type.StartsWith(ProfileOpenPrefix, StringComparison.Ordinal);
                type = UnwrapNodeType(type);
                NodeViewModel node = type switch
                {
                    "Start" or "Старт" or "Start (parallel v1)" or "Start (board v1)" or "Start (connections v1)" or "Start (actions v1)" or "Start (indicators v1)" or "Start (ping v1)" or "Start (action state v1)" or "Start (action labels v1)" or ReportStartType or CatalogSerialsStartType => new StartNodeViewModel { Location = location },

                    "End" or "Конец" => new EndNodeViewModel { Location = location },

                    "Body Start" or "Тело: начало" or "Body Start (parallel v1)" => new BodyStartNodeViewModel { Location = location },

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

                    "Check Register Range" or CaptureRegisterRangeType or "Проверка диапазона" => CreateCheckRangeNode(n, location),

                    ReadSelftestFieldType or "Selftest Check" or "Selftest Check (timing v1)" or "SELFTEST_CHECK" or "Проверка selftest" => new SelfTestCheckNodeViewModel
                    {
                        Location = location,
                        Url = n.Url ?? SelfTestCheckStep.DefaultUrl,
                        TimeoutMs = n.TimeoutMs ?? SelfTestCheckStep.DefaultTimeoutMs,
                        UseLegacyTimeout = n.SelfTestUseLegacyTimeout ?? true,
                        BrowserSettleDelayMs = n.SelfTestBrowserSettleDelayMs ?? SelfTestCheckStep.DefaultBrowserSettleDelayMs,
                        PollIntervalMs = n.PollIntervalMs ?? SelfTestCheckStep.DefaultPollIntervalMs,
                        OutputPrefix = n.OutputPrefix ?? SelfTestCheckStep.DefaultOutputPrefix,
                        ReadField = type == ReadSelftestFieldType
                            ? !string.IsNullOrWhiteSpace(n.ReadField) ? n.ReadField
                                : throw new InvalidOperationException("Для чтения поля selftest укажите его имя.")
                            : string.Empty,
                        ValidationRules = n.ValidationRules ?? SelfTestCheckStep.DefaultValidationRules,
                        Psw2G6FHardwareChecks = n.Psw2G6FHardwareChecks ?? false,
                        FailOnError = n.FailOnError ?? true
                    },

                    "Check Variable Equality" or CaptureVariableEqualityType or "CheckVariableEquality" or "CHECK_VARIABLE_EQUALITY" or "Проверка переменной" => new CheckVariableEqualityNodeViewModel
                    {
                        Location = location,
                        VariableName = n.VariableName ?? "Dut.init_ok",
                        ExpectedValue = GetExpectedValueAsString(n.ExpectedValue, "1"),
                        ComparisonType = ParseComparisonType(n.ComparisonType),
                        FailMessage = n.FailMessage ?? string.Empty,
                        ResultVariableName = GetCaptureName(n, CaptureVariableEqualityType)
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

                    "Get Serial Number" or SerialWithPrefixType or "GET_SERIAL_NUMBER_FROM_SERVER" or "Получить серийный номер" => new GetSerialNumberFromServerNodeViewModel
                    {
                        Location = location,
                        ServerBaseUrl = n.ServerBaseUrl ?? string.Empty,
                        DeviceType = GetObjectAsString(n.DeviceType, string.Empty),
                        ExpectedSerialPrefix = n.ExpectedSerialPrefix ?? string.Empty,
                        CpuIdVariableName = n.CpuIdVariableName ?? "Dut.cpu_id",
                        TimeoutMs = n.TimeoutMs ?? 30000,
                        RetryCount = n.RetryCount ?? 1,
                        RetryDelayMs = n.RetryDelayMs ?? 1000,
                        OutputVariableName = n.OutputVariableName ?? "SerialNumber",
                        FailOnError = n.FailOnError ?? true,
                        UseFixedSerialNumber = n.UseFixedSerialNumber ?? false,
                        FixedSerialNumber = n.FixedSerialNumber ?? 0
                    },

                    "Set Pro MAC" or "SET_PRO_MAC" or "Запись MAC Pro" or
                    "Send UDP Set MAC" or "SEND_UDP_SET_MAC_PACKET" or "UDP установка MAC" => new SetProMacNodeViewModel
                    {
                        Location = location,
                        BatchPath = n.BatchPath ?? "set_mac_pro.bat",
                        MacVariableName = n.MacVariableName ?? "Dut.NewMac",
                        BoardVersion = n.BoardVersion ?? GetObjectAsString(n.DeviceType, "PSW+UPS-Box 8x2Pro"),
                        TimeoutMs = n.TimeoutMs is >= 10000 ? n.TimeoutMs.Value : 60000,
                        FailOnError = n.FailOnError ?? true
                    },

                    "Set PSW MAC (UDP)" => new SetPswMacNodeViewModel
                    {
                        Location = location,
                        DestinationIp = n.DestinationIp ?? "192.168.0.1",
                        LocalIp = n.LocalIp ?? "192.168.0.2",
                        LocalPort = n.LocalPort ?? 6123,
                        UdpPort = n.UdpPort ?? 43962,
                        MacVariableName = n.MacVariableName ?? "Dut.NewMac",
                        TimeoutMs = n.TimeoutMs ?? 5000,
                        FailOnError = n.FailOnError ?? true
                    },

                    "Update PSW Firmware" => new UpdatePswFirmwareNodeViewModel
                    {
                        Location = location,
                        BaseUrl = n.BaseUrl ?? "http://192.168.0.1",
                        FirmwarePath = n.FirmwarePath ?? string.Empty,
                        TargetVersion = n.TargetVersion ?? "0.2.8",
                        VersionVariable = n.VersionVariable ?? "Dut.firmvare_vers",
                        ForceUpdate = n.ForceUpdate ?? false
                    },

                    "Run Data Test" or "RUN_DATA_TEST" or "Тест передачи данных" => new RunDataTestNodeViewModel
                    {
                        Location = location,
                        Mode = RunDataTestStep.NormalizeMode(n.Mode),
                        ExpectedPackets = n.ExpectedPackets ?? 10000,
                        PacketSizeBytes = n.PacketSizeBytes ?? 1514,
                        UdpPort = n.UdpPort ?? 43962,
                        MaxPortTestTimeMs = n.MaxPortTestTimeMs ?? 15000,
                        AllowGigabit = n.AllowGigabit ?? false,
                        TargetBandwidthMbps = RunDataTestStep.NormalizeBandwidth(n.TargetBandwidthMbps ?? 100, n.AllowGigabit ?? false),
                        DurationMs = n.DurationMs ?? 5000,
                        WarmupMs = n.WarmupMs ?? 500,
                        InterPairDelayMs = n.InterPairDelayMs ?? 5000,
                        AllowedLossPercent = n.AllowedLossPercent ?? 1.0,
                        AllowedTxDeficitPercent = n.AllowedTxDeficitPercent ?? 2.0,
                        Bidirectional = n.Bidirectional ?? true,
                        PortsText = NormalizeDataTestPortsText(n.PortsText ?? PortsToText(n.Ports, n.AllowGigabit ?? false), n.AllowGigabit ?? false),
                        DisabledPortsText = n.DisabledPortsText ?? "",
                        OutputVariableName = n.OutputVariableName ?? "DataTest",
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

                    "Compare Variables" or CaptureCompareVariablesType or "COMPARE_VARIABLES" or "Сравнить переменные" => new CompareVariablesNodeViewModel
                    {
                        Location = location,
                        LeftVariableName = n.LeftVariableName ?? "Dut.default_mac",
                        RightVariableName = n.RightVariableName ?? "Dut.NewMac",
                        ComparisonType = ParseComparisonType(n.ComparisonType),
                        FailMessage = n.FailMessage ?? string.Empty,
                        ResultVariableName = GetCaptureName(n, CaptureCompareVariablesType)
                    },

                    "Wait Variable Until" or CaptureWaitVariableType or "Wait Variable Until (report v1)" or "WAIT_VARIABLE_UNTIL" or "Ожидание переменной" => new WaitVariableUntilNodeViewModel
                    {
                        Location = location,
                        VariableName = n.VariableName ?? "Dut.ups_rez",
                        ExpectedValue = GetExpectedValueAsString(n.ExpectedValue, "1"),
                        ComparisonType = ParseComparisonType(n.ComparisonType),
                        PollAction = n.PollAction ?? "HttpGet",
                        BaseUrl = n.BaseUrl ?? "http://192.168.0.1",
                        Endpoint = n.Endpoint ?? "/api/getUpsStatus",
                        ResponseType = ParseHttpResponseValueType(n.ResponseType),
                        RequestTimeoutMs = n.RequestTimeoutMs ?? 5000,
                        TimeoutMs = n.TimeoutMs ?? 160000,
                        IntervalMs = n.IntervalMs ?? 5000,
                        FailOnTimeout = n.FailOnTimeout ?? true,
                        ReportName = n.ReportName ?? string.Empty,
                        ResultVariableName = GetCaptureName(n, CaptureWaitVariableType)
                    },

                    "Build Test Report" or ReportWithSerialPrefixType or "BUILD_TEST_REPORT" or "Собрать отчёт" => new BuildTestReportNodeViewModel
                    {
                        Location = location,
                        ReportVariableName = n.ReportVariableName ?? "TestReportText",
                        ExpectedSerialPrefix = n.ExpectedSerialPrefix ?? string.Empty,
                        SerialVariableName = string.Equals(
                            n.SerialVariableName,
                            "SerialShort",
                            StringComparison.OrdinalIgnoreCase)
                                ? "SerialNumber"
                                : n.SerialVariableName ?? "SerialNumber",
                        TestType = n.TestType ?? "production",
                        IncludeAllVariables = n.IncludeAllVariables ?? false
                    },

                    "Print Label" or "PRINT_LABEL" or "Печать этикетки" or "Print Label (last test v1)" => new PrintLabelNodeViewModel
                    {
                        Location = location,
                        UseLastTestLabel = n.UseLastTestLabel ?? (type == "Print Label (last test v1)"),
                        PrinterName = n.PrinterName ?? "TSC TE310",
                        UseDefaultPrinter = n.UseDefaultPrinter ?? false,
                        SerialVariableName = n.SerialVariableName ?? "SerialNumber",
                        SerialShortVariableName = n.SerialShortVariableName ?? "SerialShort",
                        MacVariableName = n.MacVariableName ?? "Dut.default_mac",
                        UseManualSerialNumber = n.UseManualSerialNumber ?? false,
                        ManualSerialNumber = n.ManualSerialNumber ?? string.Empty,
                        ManualMacAddress = n.ManualMacAddress ?? string.Empty,
                        Copies = n.Copies ?? 4,
                        UseQtProZplFormat = n.UseQtProZplFormat ?? false,
                        LabelModel = n.LabelModel ?? DeviceLabelModel.PswUpsBox8x2Pro,
                        LabelTemplate = n.LabelTemplate?.CopyUpgraded(),
                        FailOnPrinterError = n.FailOnPrinterError ?? true
                    },

                    "Send Test Report" or "SEND_TEST_REPORT" or "Отправить отчёт" or RtlReportType => new SendTestReportNodeViewModel
                    {
                        Location = location,
                        ServerBaseUrl = n.ServerBaseUrl ?? "http://server-address",
                        ReportVariableName = n.ReportVariableName ?? "TestReportText",
                        Endpoint = n.Endpoint ?? (UnwrapNodeType(n.Type) == RtlReportType ? RtlTestReport.Endpoint : "/api/Api.svc/result.json"),
                        TimeoutMs = n.TimeoutMs ?? 10000,
                        RetryCount = n.RetryCount ?? 1,
                        RetryDelayMs = n.RetryDelayMs ?? 1000,
                        SaveLocalCopy = n.SaveLocalCopy ?? true,
                        LocalReportsDirectory = n.LocalReportsDirectory ?? "reports",
                        FailOnError = n.FailOnError ?? false
                    },

                    "Serial Console (connect v1)" => SerialConsoleNodeViewModel.FromOptions((n.SerialConsole ?? new SerialConsoleOptions()) with { ConnectOnly = true }),
                    "Serial Console" => SerialConsoleNodeViewModel.FromOptions(n.SerialConsole ?? new SerialConsoleOptions()),
                    "Run Program" => RunProgramNodeViewModel.FromOptions(n.RunProgram ?? new RunProgramOptions()),
                    "Parse Response" => ParseResponseNodeViewModel.FromOptions(n.ParseResponse ?? new ParseResponseOptions()),
                    "Check LED Color" => CheckLedColorNodeViewModel.FromOptions(n.CheckLedColor ?? new CheckLedColorOptions()),
                    "Subtest" or "SUBTEST" or "Подтест" or "Subtest (cleanup order v1)" => CreateSubtestNode(n, location),

                    "For Slaves" or "For Slaves (repeat v1)" or "Цикл For" => CreateForEachSlaveNode(n, location),

                    "Wait Until" or "Ожидание значения" => CreateWaitUntilNode(n, location),

                    "Operator Action" or "Operator Input (v1)" or "Действие оператора" => new OperatorActionNodeViewModel
                    {
                        Location = location,
                        Message = n.Text ?? string.Empty,
                        InputVariable = n.OutputVariableName ?? string.Empty
                    },

                    _ => throw new InvalidOperationException($"Неизвестный тип ноды: {type}")
                };

                node.BoardElements = n.BoardElements ?? "";
                node.NodeColor = n.Color ?? "blue";
                node.CustomName = n.CustomName ?? string.Empty;
                node.OperatorName = (n.OperatorName ?? n.CustomName ?? string.Empty).Trim();
                node.RunOnlyOnFirstRun = n.RunOnlyOnFirstRun || wrappedFirstRun;
                if (node.RunOnlyOnFirstRun && !node.CanRunOnlyOnFirstRun)
                {
                    node.Dispose();
                    throw new InvalidOperationException($"Для ноды '{type}' нельзя задать выполнение при первом запуске.");
                }

                node.Location = location;
                node.BoardElements = n.BoardElements ?? "";
                if (!nodeMap.TryAdd(n.Id, node))
                {
                    node.Dispose();
                    throw new InvalidOperationException($"Граф '{dto.Name}': повторный ID ноды '{n.Id}'.");
                }
                var optionExtensions = node switch
                {
                    SerialConsoleNodeViewModel => n.SerialConsole?.ExtensionData,
                    RunProgramNodeViewModel => n.RunProgram?.ExtensionData,
                    ParseResponseNodeViewModel => n.ParseResponse?.ExtensionData,
                    CheckLedColorNodeViewModel => n.CheckLedColor?.ExtensionData,
                    _ => null
                };
                OriginalNodes.Add(node, new OriginalNodeData(n.Id, n.Ports, (node as RunDataTestNodeViewModel)?.PortsText, optionExtensions));
                SetExtensions(node, n.ExtensionData);
                graph.Nodes.Add(node);
            }

            foreach (var c in dto.Connections)
            {
                if (c == null) throw new InvalidOperationException("Пустая связь в профиле.");
                if (!nodeMap.TryGetValue(c.SourceNodeId, out var srcNode))
                    throw new InvalidOperationException($"Граф '{dto.Name}': связь ссылается на отсутствующую ноду '{c.SourceNodeId}'.");

                if (!nodeMap.TryGetValue(c.TargetNodeId, out var tgtNode))
                    throw new InvalidOperationException($"Граф '{dto.Name}': связь ссылается на отсутствующую ноду '{c.TargetNodeId}'.");

                var srcConn = FindConnector(
                    srcNode,
                    srcNode.Output,
                    c.SourceConnector);

                var tgtConn = FindConnector(
                    tgtNode,
                    tgtNode.Input,
                    c.TargetConnector);

                if (srcConn == null || tgtConn == null)
                    throw new InvalidOperationException($"Граф '{dto.Name}': неизвестный выход '{c.SourceConnector}' или вход '{c.TargetConnector}'.");

                var connection = new ConnectionViewModel(srcConn, tgtConn);
                SetExtensions(connection, c.ExtensionData);
                graph.Connections.Add(connection);
            }
        }

        private static ModbusWriteNodeViewModel CreateModbusWriteNode(NodeDto n, Point location)
        {
            var node = new ModbusWriteNodeViewModel
            {
                Location = location,
                SlaveId = n.SlaveId ?? 0,
                Address = n.Address ?? 0,
                RegisterArea = n.RegisterArea ?? TestBuilder.Domain.Modbus.ModbusArea.Holding,
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
                RegisterArea = n.RegisterArea ?? TestBuilder.Domain.Modbus.ModbusArea.Holding,
                Min = ToInt(n.Min),
                Max = ToInt(n.Max),
                UseCurrentSlaveId = n.UseCurrentSlaveId ?? false,
                LiveRead = n.LiveRead ?? false,
                ReadAttempts = n.ReadAttempts ?? 1,
                ReadIntervalMs = n.ReadIntervalMs ?? 0,
                ReportName = n.ReportName ?? "",
                ResultVariableName = GetCaptureName(n, CaptureRegisterRangeType)
            };

            node.RestoreSelections();

            return node;
        }

        private static string GetCaptureName(NodeDto node, string captureType)
        {
            if (!string.IsNullOrWhiteSpace(node.ResultVariableName)) return node.ResultVariableName.Trim();
            if (UnwrapNodeType(string.IsNullOrWhiteSpace(node.Type) ? node.NodeType : node.Type) == captureType)
                throw new InvalidOperationException($"Укажите переменную результата для ноды «{node.Name ?? node.Type}».");
            return string.Empty;
        }

        private static WaitUntilNodeViewModel CreateWaitUntilNode(NodeDto n, Point location)
        {
            var node = new WaitUntilNodeViewModel
            {
                Location = location,
                SlaveId = n.SlaveId ?? 0,
                Address = n.Address ?? 0,
                RegisterArea = n.RegisterArea ?? TestBuilder.Domain.Modbus.ModbusArea.Holding,
                ExpectedValue = GetExpectedValueAsInt(n.ExpectedValue),
                TimeoutMs = n.DurationMs ?? 5000,
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
                RepeatOnSameSlave = n.RepeatOnSameSlave ?? n.Type == "For Slaves (repeat v1)",
                RepeatCount = n.RepeatCount ?? 1,
                IndexVariable = n.IndexVariable ?? "iteration",
                UntilSuccess = n.UntilSuccess ?? false,
                RepeatDelayMs = n.RepeatDelayMs ?? 0,
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
            if (n.FailureOrder is <= 0 ||
                UnwrapNodeType(n.Type) == "Subtest (cleanup order v1)" && n.FailureOrder == null)
                throw new InvalidOperationException("Порядок аварийного подтеста должен быть положительным числом.");
            var node = new SubtestNodeViewModel
            {
                Location = location,
                Name = n.Name ?? "Подтест",
                Description = n.Description ?? string.Empty,
                IsEnabled = n.IsEnabled ?? true,
                StopOnError = n.StopOnError ?? true,
                RunOnFailure = n.RunOnFailure ?? false,
                FailureOrder = n.FailureOrder
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

        private static string PortsToText(List<DataTestPortDto>? ports, bool allowGigabit = false)
        {
            if (ports == null || ports.Count == 0)
            {
                return RunDataTestNodeViewModel.DefaultPortsText;
            }

            return string.Join(
                Environment.NewLine,
                ports.Select(port => port.BandwidthMbps.HasValue
                    ? $"{port.Name},{port.InIp},{port.OutIp},{RunDataTestStep.NormalizeBandwidth(port.BandwidthMbps.Value, allowGigabit)}"
                    : $"{port.Name},{port.InIp},{port.OutIp}"));
        }

        private static string NormalizeDataTestPortsText(string portsText, bool allowGigabit = false)
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
                        parts[3] = RunDataTestStep.NormalizeBandwidth(bandwidthMbps, allowGigabit).ToString(CultureInfo.InvariantCulture);
                    }

                    return string.Join(",", parts.Select(part => part.Trim()));
                }));
        }

        public static GraphProfile ReadProfile(string filePath, bool validateContents = false)
        {
            DateTimeOffset? fileModifiedAt = null;
            try { if (File.Exists(filePath)) fileModifiedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try
            {
                var dto = JsonSerializer.Deserialize<GraphDto>(File.ReadAllText(filePath), JsonOptions);
                var loadable = dto != null;
                if (dto != null && validateContents)
                {
                    try { LoadProfileGraph(dto).DisposeNodes(); }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or OverflowException)
                    { loadable = false; }
                }
                return new GraphProfile(filePath, dto?.Name ?? Path.GetFileNameWithoutExtension(filePath),
                    dto?.DeviceModel, dto?.ConfigurationName, dto?.DeviceModelId, dto?.ProductKind ?? "Switch", dto?.UpdatedAt ?? fileModifiedAt)
                    { HasReadableMetadata = loadable };
            }
            catch
            {
                // Keep unreadable profiles visible so loading can show its existing error state.
                return new GraphProfile(filePath, Path.GetFileNameWithoutExtension(filePath), updatedAt: fileModifiedAt)
                    { HasReadableMetadata = false };
            }
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
