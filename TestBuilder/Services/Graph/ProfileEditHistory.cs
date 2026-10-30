using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services.Graph;

/// <summary>One history per document; navigation and execution state are not edits.</summary>
public sealed class ProfileEditHistory : IDisposable
{
    private static readonly HashSet<string> StoredProperties = new()
    {
        "Arguments", "BaudRate", "BlueAddress", "ColorMargin", "Command", "ExpectedColor", "ExpectedExitCode", "GreenAddress", "MinIntensity", "OutputPrefix", "OutputVariableName", "ParseMode", "PortName", "ProgramPath", "QuietMs", "RedAddress", "ResponseTerminator", "Selector", "SlaveId", "SourceVariableName", "TimeoutMs", "UseCurrentSlaveId", "ValueType", "WhiteAddress", "WorkingDirectory",
        "Address", "AllowGigabit", "AllowedLossPercent", "AllowedTxDeficitPercent", "Arguments", "ArpdBatPath",
        "BaseUrl", "BatchPath", "Bidirectional", "BoardVersion", "Command", "ComparisonType",
        "Copies", "CpuIdVariableName", "CustomName", "Description", "DestinationIp", "DeviceType", "DurationMs",
        "Endpoint", "ExpectedPackets", "ExpectedValue", "FailMessage", "FailOnError", "FailOnPrinterError",
        "FailOnTimeout", "FirmwarePath", "FixedSerialNumber", "ForceUpdate", "FromSlaveId", "IncludeAllVariables",
        "Inclusive", "InterPairDelayMs", "IntervalMs", "IsEnabled", "LabelHeight", "LabelModel", "LabelTemplate",
        "LabelWidth", "LeftVariableName", "LiveRead", "LocalIp", "LocalPort", "LocalReportsDirectory",
        "Location", "MacPrefix", "MacVariableName", "ManualMacAddress", "ManualSerialNumber", "Max",
        "MaxPortTestTimeMs", "Message", "InputVariable", "Milliseconds", "Min", "Mode", "Name",
        "NodeColor", "NormalizeBandwidth", "OutputPrefix", "OutputVariableName", "PacketSizeBytes", "PollAction",
        "PollIntervalMs", "PrinterName", "Psw2G6FHardwareChecks", "ReadAttempts", "ReadIntervalMs", "ReportName", "ReportVariableName",
        "RequestTimeoutMs", "ResponseType", "RetryCount", "RetryDelayMs", "RightVariableName", "RunArpdBat",
        "RunOnFailure", "FailureOrder", "SaveLocalCopy", "SerialOffset", "SerialShortVariableName", "SerialVariableName", "ServerBaseUrl",
        "SlaveId", "Step", "StopOnError", "TargetVersion", "TestType", "Text",
        "TimeoutMs", "ToSlaveId", "UdpPort", "Url", "UseCurrentSlaveId", "UseFixedSerialNumber",
        "UseManualSerialNumber", "UseQtProZplFormat", "ValidationRules", "Value", "VariableName", "VerifyWrite",
        "RunOnlyOnFirstRun", "VersionVariable", "WarmupMs", "RegisterArea", "BoardElements", "RepeatOnSameSlave", "RepeatCount", "IndexVariable", "UntilSuccess", "RepeatDelayMs"
    };
    private readonly GraphWorkspaceViewModel _root;
    private readonly Func<bool> _canEdit;
    private readonly Action<GraphWorkspaceViewModel> _navigate;
    private readonly Dictionary<GraphWorkspaceViewModel, NotifyCollectionChangedEventHandler> _graphs = new();
    private readonly Dictionary<NodeViewModel, (GraphWorkspaceViewModel Graph, Dictionary<PropertyInfo, object?> Values)> _nodes = new();
    private PropertyEdit? _lastProperty;
    private DateTime _lastChange;
    public UndoRedoManager Manager { get; } = new();

    public ProfileEditHistory(GraphWorkspaceViewModel root, Func<bool> canEdit, Action<GraphWorkspaceViewModel> navigate)
    { _root = root; _canEdit = canEdit; _navigate = navigate; Synchronize(); }

    public void Execute(GraphWorkspaceViewModel graph, IGraphCommand command)
    {
        BreakMerge();
        Manager.Execute(new GraphEdit(command, graph, _navigate));
    }
    public void BreakMerge() => _lastProperty = null;
    public void Clear() { BreakMerge(); Manager.Clear(); Synchronize(); }

    private void Synchronize()
    {
        var graphs = new HashSet<GraphWorkspaceViewModel>();
        var nodes = new Dictionary<NodeViewModel, GraphWorkspaceViewModel>();
        void Visit(GraphWorkspaceViewModel graph)
        {
            if (!graphs.Add(graph)) return;
            foreach (var node in graph.Nodes)
            {
                nodes[node] = graph;
                if (node is ICompositeNodeViewModel c) Visit(c.BodyGraph);
            }
        }
        Visit(_root);
        foreach (var graph in _graphs.Keys.Where(g => !graphs.Contains(g)).ToArray())
        { graph.Nodes.CollectionChanged -= _graphs[graph]; _graphs.Remove(graph); }
        foreach (var graph in graphs.Where(g => !_graphs.ContainsKey(g)))
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => Synchronize();
            _graphs.Add(graph, handler); graph.Nodes.CollectionChanged += handler;
            graph.UndoRedo = Manager;
        }
        foreach (var node in _nodes.Keys.Where(n => !nodes.ContainsKey(n)).ToArray())
        { node.PropertyChanged -= Changed; _nodes.Remove(node); }
        foreach (var (node, graph) in nodes.Where(p => !_nodes.ContainsKey(p.Key)))
        {
            var values = node.GetType().GetProperties().Where(p => p.CanRead && p.CanWrite && StoredProperties.Contains(p.Name))
                .ToDictionary(p => p, p => p.GetValue(node));
            _nodes.Add(node, (graph, values)); node.PropertyChanged += Changed;
        }
    }

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NodeViewModel node || !_nodes.TryGetValue(node, out var state)) return;
        var property = state.Values.Keys.FirstOrDefault(p => p.Name == e.PropertyName);
        if (property == null) return;
        var old = state.Values[property]; var value = property.GetValue(node);
        state.Values[property] = value;
        if (Equals(old, value) || Manager.IsApplying || !_canEdit()) return;
        var now = DateTime.UtcNow;
        if (_lastProperty != null && ReferenceEquals(_lastProperty.Target, node) && _lastProperty.Property == property &&
            now - _lastChange < TimeSpan.FromMilliseconds(750) && !Manager.CanRedo)
            _lastProperty.NewValue = value;
        else
        {
            _lastProperty = new PropertyEdit(node, property, old, value);
            Manager.Record(new GraphEdit(_lastProperty, state.Graph, _navigate, alreadyApplied: true));
        }
        _lastChange = now;
    }

    public void Dispose()
    {
        foreach (var (g,h) in _graphs) g.Nodes.CollectionChanged -= h;
        foreach (var n in _nodes.Keys) n.PropertyChanged -= Changed;
        _graphs.Clear(); _nodes.Clear(); Manager.Clear();
    }

    private sealed class PropertyEdit(object target, PropertyInfo property, object? oldValue, object? newValue) : IGraphCommand
    {
        public object Target { get; } = target;
        public PropertyInfo Property { get; } = property;
        public object? NewValue { get; set; } = newValue;
        public void Execute() => Property.SetValue(Target, NewValue);
        public void Undo() => Property.SetValue(Target, oldValue);
    }
    private sealed class GraphEdit(IGraphCommand command, GraphWorkspaceViewModel graph, Action<GraphWorkspaceViewModel> navigate, bool alreadyApplied = false) : IGraphCommand
    {
        private bool _first = !alreadyApplied;
        public void Execute() { if (!_first) navigate(graph); _first = false; command.Execute(); }
        public void Undo() { navigate(graph); command.Undo(); }
    }
}
