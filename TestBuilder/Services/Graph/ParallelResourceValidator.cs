using System;
using System.Collections.Generic;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services.Graph;

/// <summary>
/// Conservative, explicit eligibility rules. The COM queue only protects a single
/// request, not a write/wait/read sequence or the physical state of a device.
/// Unknown node types must declare their effects here before running in parallel.
/// </summary>
internal static class ParallelResourceValidator
{
    private sealed class Resources
    {
        public HashSet<byte> Slaves { get; } = new();
        public bool AnySlave { get; set; }
        public bool Network { get; set; }
        public bool ChangesPoeLoad { get; set; }
    }

    public static void Validate(TestNode fork, IReadOnlyList<HashSet<TestNode>> regions)
    {
        var resources = new List<Resources>();
        foreach (var region in regions)
        {
            var usage = new Resources();
            foreach (var node in region)
                if (node.Source is NodeViewModel vm) Collect(vm, usage, null);
            resources.Add(usage);
        }
        for (var i = 0; i < resources.Count; i++)
        for (var j = i + 1; j < resources.Count; j++)
        {
            var a = resources[i];
            var b = resources[j];
            var modbusConflict = a.Slaves.Overlaps(b.Slaves)
                || (a.AnySlave && (b.AnySlave || b.Slaves.Count > 0))
                || (b.AnySlave && a.Slaves.Count > 0);
            var networkConflict = a.Network && b.Network;
            var loadConflict = (a.Network && b.ChangesPoeLoad) || (b.Network && a.ChangesPoeLoad);
            if (modbusConflict || networkConflict || loadConflict)
                throw new InvalidOperationException($"Параллельные ветви '{ParallelGraphPlanner.Name(fork)}': " +
                    $"ветви {i + 1} и {j + 1} используют общий ресурс " +
                    (modbusConflict ? "Modbus slave. Разделите устройства или выполняйте проверки последовательно."
                        : loadConflict ? "питания PoE и сетевых измерений DUT. Настройте нагрузки до сетевых проверок."
                        : "сети DUT/selftest/DataTest. Сетевые операции разных ветвей должны выполняться последовательно."));
        }
    }

    private static void Collect(NodeViewModel node, Resources usage, IReadOnlyCollection<byte>? currentSlaves)
    {
        switch (node)
        {
            case SubtestNodeViewModel subtest:
                if (!subtest.IsEnabled) return;
                if (subtest.RunOnFailure) Reject(node, "аварийную очистку следует оставить вне параллельных ветвей");
                CollectGraph(subtest.BodyGraph, usage, currentSlaves);
                return;
            case ForEachSlaveNodeViewModel loop:
                if (loop.Step <= 0 || loop.FromSlaveId > loop.ToSlaveId)
                    Reject(node, "некорректный диапазон slave");
                var slaves = new List<byte>();
                for (var id = (int)loop.FromSlaveId; id <= loop.ToSlaveId; id += loop.Step)
                    slaves.Add((byte)id);
                CollectGraph(loop.BodyGraph, usage, slaves);
                return;
            case ModbusWriteNodeViewModel write:
                // Explicitly allow only known independent sequences. A denylist of
                // AC registers misses PS-1/RPS power, discharge and IO sensor controls.
                switch (write.Address)
                {
                    case 1215 or 1220: // PS-3 heater channels, independent of DUT boot.
                        break;
                    case 1412 or 1413 or 1414 or 1415 or 1420 or 1421 or 1430: // EL60 load setup/reset.
                        usage.ChangesPoeLoad = true;
                        break;
                    default:
                        Reject(node, $"запись регистра {write.Address} не разрешена параллельно; " +
                            "поддержаны нагреватели 1215/1220 и настройка EL60. Питание, датчики и прочие воздействия вынесите за разветвление");
                        break;
                }
                AddSlave(usage, write.SlaveId, write.UseCurrentSlaveId, currentSlaves);
                return;
            case CheckRegisterRangeNodeViewModel range:
                AddSlave(usage, range.SlaveId, range.UseCurrentSlaveId, currentSlaves); return;
            case WaitUntilNodeViewModel wait:
                AddSlave(usage, wait.SlaveId, wait.UseCurrentSlaveId, currentSlaves); return;
            case WaitVariableUntilNodeViewModel waitVariable:
                if (!string.Equals(waitVariable.PollAction, "None", StringComparison.OrdinalIgnoreCase)) usage.Network = true;
                return;
            case SelfTestCheckNodeViewModel or ClearArpCacheNodeViewModel or ReadHttpVariableNodeViewModel
                or RunDataTestNodeViewModel:
                usage.Network = true;
                return;
            case StartNodeViewModel or EndNodeViewModel or BodyStartNodeViewModel or BodyEndNodeViewModel
                or DelayNodeViewModel or LabelNodeViewModel or CheckVariableEqualityNodeViewModel
                or CheckVariableRangeNodeViewModel or CompareVariablesNodeViewModel or BuildMacFromSerialNodeViewModel:
                return;
            default:
                Reject(node, "действия оператора, выдача серийника, запись MAC/прошивки, печать и отчёт выполняются вне параллельных ветвей; для других типов требуется явная поддержка ресурсов");
                return;
        }
    }

    private static void CollectGraph(GraphWorkspaceViewModel graph, Resources usage, IReadOnlyCollection<byte>? slaves)
    {
        var start = graph.Nodes.FirstOrDefault(n => n is StartNodeViewModel or BodyStartNodeViewModel);
        if (start == null) return; // GraphCompiler supplies the precise structural diagnostic.
        var visited = new HashSet<NodeViewModel>();
        var visiting = new HashSet<NodeViewModel>();
        void Visit(NodeViewModel node)
        {
            if (visiting.Contains(node)) Reject(node, "цикл связей во вложенном графе параллельной ветви недопустим");
            if (!visited.Add(node)) return;
            visiting.Add(node);
            Collect(node, usage, slaves);
            foreach (var connection in graph.Connections.Where(c => ReferenceEquals(c.Source.Parent, node)))
                if (connection.Target.Parent is NodeViewModel next) Visit(next);
            visiting.Remove(node);
        }
        Visit(start);
    }

    private static void AddSlave(Resources usage, byte slave, bool useCurrent, IReadOnlyCollection<byte>? current)
    {
        if (!useCurrent) usage.Slaves.Add(slave);
        else if (current == null) usage.AnySlave = true;
        else usage.Slaves.UnionWith(current);
    }

    private static void Reject(NodeViewModel node, string reason) =>
        throw new InvalidOperationException($"Нода '{node.Title}' внутри параллельной ветви: {reason}.");
}
