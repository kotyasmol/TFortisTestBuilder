using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TestBuilder.Serialization;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services
{
    public static class GraphSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string Serialize(TestViewModel vm, string profileName)
        {
            var dto = SerializeGraph(vm.RootGraph, profileName);

            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        private static GraphDto SerializeGraph(GraphWorkspaceViewModel graph, string name)
        {
            var dto = new GraphDto
            {
                Name = name
            };

            var nodeIds = new Dictionary<NodeViewModel, string>();

            for (var i = 0; i < graph.Nodes.Count; i++)
            {
                nodeIds[graph.Nodes[i]] = i.ToString();
            }

            foreach (var node in graph.Nodes)
            {
                var n = new NodeDto
                {
                    Id = nodeIds[node],
                    Type = node.Title,
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

                    case HttpRequestNodeViewModel h:
                        n.Url = h.Url;
                        n.TimeoutMs = h.TimeoutMs;
                        n.OutputVariableName = h.OutputVariableName;
                        n.RequireSuccessStatusCode = h.RequireSuccessStatusCode;
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
                {
                    continue;
                }

                if (!nodeIds.ContainsKey(src) || !nodeIds.ContainsKey(tgt))
                {
                    continue;
                }

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

                NodeViewModel node = n.Type switch
                {
                    "Start" => new StartNodeViewModel
                    {
                        Location = location
                    },

                    "End" => new EndNodeViewModel
                    {
                        Location = location
                    },

                    "Body Start" => new BodyStartNodeViewModel
                    {
                        Location = location
                    },

                    "Body End" => new BodyEndNodeViewModel
                    {
                        Location = location
                    },

                    "Delay" => new DelayNodeViewModel
                    {
                        Location = location,
                        Milliseconds = n.Milliseconds ?? 1000
                    },

                    "Label" => new LabelNodeViewModel
                    {
                        Location = location,
                        Text = n.Text ?? "Этап"
                    },

                    "Write Register" => new ModbusWriteNodeViewModel
                    {
                        Location = location,
                        SlaveId = n.SlaveId ?? 0,
                        Address = n.Address ?? 0,
                        Value = n.Value ?? 0,
                        UseCurrentSlaveId = n.UseCurrentSlaveId ?? false
                    },

                    "Check Register Range" => new CheckRegisterRangeNodeViewModel
                    {
                        Location = location,
                        SlaveId = n.SlaveId ?? 0,
                        Address = n.Address ?? 0,
                        Min = n.Min ?? 0,
                        Max = n.Max ?? 0,
                        UseCurrentSlaveId = n.UseCurrentSlaveId ?? false
                    },

                    "HTTP Request" => new HttpRequestNodeViewModel
                    {
                        Location = location,
                        Url = n.Url ?? "http://192.168.0.1/test.shtml",
                        TimeoutMs = n.TimeoutMs ?? 30000,
                        OutputVariableName = n.OutputVariableName ?? "testPageHtml",
                        RequireSuccessStatusCode = n.RequireSuccessStatusCode ?? true
                    },

                    "For Slaves" => CreateForEachSlaveNode(n, location),

                    _ => throw new InvalidOperationException($"Неизвестный тип ноды: {n.Type}")
                };

                nodeMap[n.Id] = node;
                graph.Nodes.Add(node);
            }

            foreach (var c in dto.Connections)
            {
                if (!nodeMap.TryGetValue(c.SourceNodeId, out var srcNode))
                {
                    continue;
                }

                if (!nodeMap.TryGetValue(c.TargetNodeId, out var tgtNode))
                {
                    continue;
                }

                var srcConn = srcNode.Output.Concat(srcNode.Input)
                    .FirstOrDefault(x => x.Title == c.SourceConnector);

                var tgtConn = tgtNode.Input.Concat(tgtNode.Output)
                    .FirstOrDefault(x => x.Title == c.TargetConnector);

                if (srcConn == null || tgtConn == null)
                {
                    continue;
                }

                graph.Connections.Add(new ConnectionViewModel(srcConn, tgtConn));
            }
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
            {
                DeserializeGraph(n.Body, node.BodyGraph, isBodyGraph: true);
            }
            else
            {
                node.EnsureDefaultBodyNodes();
            }

            return node;
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
