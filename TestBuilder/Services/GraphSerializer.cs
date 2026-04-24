using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TestBuilder.Serialization;
using TestBuilder.ViewModels;
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

        // ─── СОХРАНЕНИЕ ───────────────────────────────────────────────────────

        public static string Serialize(TestViewModel vm, string profileName)
        {
            var dto = new GraphDto { Name = profileName };

            var nodeIds = new Dictionary<NodeViewModel, string>();
            for (int i = 0; i < vm.Nodes.Count; i++)
                nodeIds[vm.Nodes[i]] = i.ToString();

            foreach (var node in vm.Nodes)
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
                        break;
                    case CheckRegisterRangeNodeViewModel c:
                        n.SlaveId = c.SlaveId;
                        n.Address = c.Address;
                        n.Min = c.Min;
                        n.Max = c.Max;
                        break;
                }

                dto.Nodes.Add(n);
            }

            foreach (var conn in vm.Connections)
            {
                var src = conn.Source.Parent;
                var tgt = conn.Target.Parent;
                if (src == null || tgt == null) continue;
                if (!nodeIds.ContainsKey(src) || !nodeIds.ContainsKey(tgt)) continue;

                dto.Connections.Add(new ConnectionDto
                {
                    SourceNodeId = nodeIds[src],
                    SourceConnector = conn.Source.Title,
                    TargetNodeId = nodeIds[tgt],
                    TargetConnector = conn.Target.Title
                });
            }

            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        // ─── ЗАГРУЗКА ─────────────────────────────────────────────────────────

        public static string Deserialize(string json, TestViewModel vm)
        {
            var dto = JsonSerializer.Deserialize<GraphDto>(json, JsonOptions)
                      ?? throw new InvalidOperationException("Не удалось прочитать JSON");

            vm.ClearGraph();

            var nodeMap = new Dictionary<string, NodeViewModel>();

            foreach (var n in dto.Nodes)
            {
                var location = new Point(n.X, n.Y);

                NodeViewModel node = n.Type switch
                {
                    "Start" => new StartNodeViewModel { Location = location },
                    "End" => new EndNodeViewModel { Location = location },

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
                        Value = n.Value ?? 0
                    },

                    "Check Register Range" => new CheckRegisterRangeNodeViewModel
                    {
                        Location = location,
                        SlaveId = n.SlaveId ?? 0,
                        Address = n.Address ?? 0,
                        Min = n.Min ?? 0,
                        Max = n.Max ?? 0
                    },

                    _ => throw new InvalidOperationException($"Неизвестный тип ноды: {n.Type}")
                };

                nodeMap[n.Id] = node;
                vm.Nodes.Add(node);
            }

            foreach (var c in dto.Connections)
            {
                if (!nodeMap.TryGetValue(c.SourceNodeId, out var srcNode)) continue;
                if (!nodeMap.TryGetValue(c.TargetNodeId, out var tgtNode)) continue;

                var srcConn = srcNode.Output.Concat(srcNode.Input)
                    .FirstOrDefault(x => x.Title == c.SourceConnector);
                var tgtConn = tgtNode.Input.Concat(tgtNode.Output)
                    .FirstOrDefault(x => x.Title == c.TargetConnector);

                if (srcConn == null || tgtConn == null) continue;

                vm.Connect(srcConn, tgtConn);
            }

            // Возвращаем имя профиля чтобы показать в UI
            return dto.Name;
        }

        // ─── ЧТЕНИЕ ТОЛЬКО ИМЕНИ (без загрузки графа) ────────────────────────

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