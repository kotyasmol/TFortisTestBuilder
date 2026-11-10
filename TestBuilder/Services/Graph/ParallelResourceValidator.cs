using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
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

    public static void Validate(TestNode fork, IReadOnlyList<HashSet<TestNode>> regions,
        HardwareConfiguration? hardware, IReadOnlyCollection<byte>? currentSlaves)
    {
        var resources = new List<Resources>();
        foreach (var region in regions)
        {
            var usage = new Resources();
            foreach (var node in region)
                if (node.Source is NodeViewModel vm) Collect(vm, usage, currentSlaves, hardware);
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

    private static void Collect(NodeViewModel node, Resources usage, IReadOnlyCollection<byte>? currentSlaves,
        HardwareConfiguration? hardware)
    {
        switch (node)
        {
            case SubtestNodeViewModel subtest:
                if (!subtest.IsEnabled) return;
                if (subtest.RunOnFailure) Reject(node, "аварийную очистку следует оставить вне параллельных ветвей");
                CollectGraph(subtest.BodyGraph, usage, currentSlaves, hardware);
                return;
            case ForEachSlaveNodeViewModel loop:
                var slaves = GetLoopSlaves(loop, currentSlaves);
                if (!loop.RepeatOnSameSlave && slaves == null)
                    Reject(node, "некорректный диапазон slave");
                CollectGraph(loop.BodyGraph, usage, slaves, hardware);
                return;
            case ModbusWriteNodeViewModel write:
                ValidateWrite(write, usage, write.UseCurrentSlaveId ? currentSlaves : new[] { write.SlaveId }, hardware);
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

    internal static IReadOnlyCollection<byte>? GetLoopSlaves(ForEachSlaveNodeViewModel loop,
        IReadOnlyCollection<byte>? currentSlaves)
    {
        if (loop.RepeatOnSameSlave) return currentSlaves;
        if (loop.Step <= 0 || loop.FromSlaveId > loop.ToSlaveId) return null;
        var slaves = new List<byte>();
        for (var id = (int)loop.FromSlaveId; id <= loop.ToSlaveId; id += loop.Step)
            slaves.Add((byte)id);
        return slaves;
    }

    private static void ValidateWrite(ModbusWriteNodeViewModel write, Resources usage, IReadOnlyCollection<byte>? slaves,
        HardwareConfiguration? hardware)
    {
        if (write.RegisterArea != ModbusArea.Holding || slaves == null || hardware == null)
            Reject(write, "для параллельной записи нужен известный slave и карта Holding-регистров оборудования");

        foreach (var slave in slaves)
        {
            var device = hardware.Devices.SingleOrDefault(d => d.SlaveId == slave);
            var register = device?.Registers.SingleOrDefault(r => r.Area == write.RegisterArea && r.Address == write.Address);
            // The identification value is checked against the real device during connection.
            // Use the physical role defined by the model's register map, not the editable name.
            var model = device is { IdentificationAddress: 0 } && device.ExpectedIdentificationValues.Count == 1
                ? device.ExpectedIdentificationValues[0] : (ushort)0;
            if (register is { Writable: true })
            {
                if (model == 8 && write.Address is 1215 or 1220) continue; // PS-3 heater channels.
                if (model == 4 && write.Address is 1412 or 1413 or 1414 or 1415 or 1420 or 1421 or 1430)
                {
                    usage.ChangesPoeLoad = true; // EL-60v5 load setup/reset.
                    continue;
                }
            }
            Reject(write, $"запись slave {slave}, регистр {write.Address}, не разрешена параллельно. " +
                "Поддержаны нагреватели PS-3 и настройка нагрузки EL-60v5 с подтверждённым типом устройства");
        }
    }

    private static void CollectGraph(GraphWorkspaceViewModel graph, Resources usage, IReadOnlyCollection<byte>? slaves,
        HardwareConfiguration? hardware)
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
            Collect(node, usage, slaves, hardware);
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

    [DoesNotReturn]
    private static void Reject(NodeViewModel node, string reason) =>
        throw new InvalidOperationException($"Нода '{node.DisplayTitle}' внутри параллельной ветви: {reason}.");
}
