using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Services;

internal static class ReportSourceCatalog
{
    internal static bool UsesAllVariables(GraphWorkspaceViewModel root)
    {
        var visited = new HashSet<GraphWorkspaceViewModel>();
        var foundReport = false;
        return Visit(root) && foundReport;

        bool Visit(GraphWorkspaceViewModel graph)
        {
            if (!visited.Add(graph)) return true;
            foreach (var node in graph.Nodes)
            {
                if (node is BuildTestReportNodeViewModel report)
                {
                    foundReport = true;
                    if (!report.IncludeAllVariables) return false;
                }
                if (node is ICompositeNodeViewModel composite && !Visit(composite.BodyGraph))
                    return false;
            }
            return true;
        }
    }

    public static IReadOnlyList<string> Collect(GraphWorkspaceViewModel root, ReportSourceKind source,
        IEnumerable<string>? observedNames = null)
    {
        if (source == ReportSourceKind.TestType) return Array.Empty<string>();

        var entries = new HashSet<string>(StringComparer.Ordinal);
        var variables = new HashSet<string>(StringComparer.Ordinal);
        var statuses = new HashSet<string>(StringComparer.Ordinal);
        var reportOutputs = new HashSet<string>(StringComparer.Ordinal);
        var path = new HashSet<GraphWorkspaceViewModel>();

        void Visit(GraphWorkspaceViewModel graph, IReadOnlyList<byte> slaves)
        {
            if (!path.Add(graph)) return;
            foreach (var node in graph.Nodes)
            {
                switch (node)
                {
                    case SubtestNodeViewModel subtest:
                        entries.Add(subtest.Name);
                        break;
                    case CheckRegisterRangeNodeViewModel register:
                        entries.Add(register.ReportName);
                        Capture(register.ResultVariableName, register.UseCurrentSlaveId ? slaves : new[] { register.SlaveId });
                        break;
                    case CheckVariableEqualityNodeViewModel equality:
                        Capture(equality.ResultVariableName, slaves);
                        break;
                    case CompareVariablesNodeViewModel comparison:
                        Capture(comparison.ResultVariableName, slaves);
                        break;
                    case WaitVariableUntilNodeViewModel wait:
                        entries.Add(wait.ReportName);
                        Capture(wait.ResultVariableName, slaves);
                        break;
                    case RunDataTestNodeViewModel dataTest:
                        AddDataTest(dataTest);
                        break;
                    case BuildTestReportNodeViewModel build:
                        reportOutputs.Add(string.IsNullOrWhiteSpace(build.ReportVariableName)
                            ? "TestReportText" : build.ReportVariableName.Trim());
                        break;
                    case SelfTestCheckNodeViewModel { IsReadingField: false }:
                        AddSelftest();
                        break;
                    case ReadHttpVariableNodeViewModel read:
                        variables.Add(read.OutputVariableName);
                        if (read.ResponseType == HttpResponseValueType.Boolean)
                            statuses.Add(read.OutputVariableName);
                        break;
                    case PrintLabelNodeViewModel:
                        entries.Add("Печать этикеток");
                        AddStatus("PrintLabel.Success");
                        break;
                    case SetProMacNodeViewModel or SetPswMacNodeViewModel:
                        AddStatus("SetMac.Success");
                        break;
                }

                if (node is ForEachSlaveNodeViewModel { RepeatOnSameSlave: false } loop)
                {
                    var ids = Enumerable.Range(loop.FromSlaveId, Math.Max(0, loop.ToSlaveId - loop.FromSlaveId + 1))
                        .Where(id => loop.Step != 0 && (id - loop.FromSlaveId) % loop.Step == 0)
                        .Select(id => (byte)id).ToArray();
                    Visit(loop.BodyGraph, ids);
                }
                else if (node is ICompositeNodeViewModel composite)
                    Visit(composite.BodyGraph, slaves);
            }
            path.Remove(graph);
        }

        void Capture(string prefix, IReadOnlyList<byte> slaves)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return;
            prefix = prefix.Trim();
            if (!prefix.Contains("{slaveId}", StringComparison.Ordinal))
            {
                AddMeasurement(prefix);
                return;
            }
            foreach (var slave in slaves)
                AddMeasurement(prefix.Replace("{slaveId}", slave.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        void AddMeasurement(string prefix)
        {
            variables.Add(prefix + ".Value");
            AddStatus(prefix + ".Passed");
        }

        void AddStatus(string key)
        {
            variables.Add(key);
            statuses.Add(key);
        }

        void AddDataTest(RunDataTestNodeViewModel node)
        {
            var prefix = string.IsNullOrWhiteSpace(node.OutputVariableName) ? "DataTest" : node.OutputVariableName.Trim();
            AddStatus(prefix + ".Passed");
            var ports = node.ParsePorts().ToArray();
            for (var i = 0; i < ports.Length; i++)
            {
                entries.Add("Передача данных " + ports[i].Name);
                var port = prefix + ".Port" + i.ToString(CultureInfo.InvariantCulture);
                AddStatus(port + ".Passed");
                variables.Add(port + ".ActualTxMbps");
                variables.Add(port + ".ActualRxMbps");
                variables.Add(port + ".LossPercent");
            }
        }

        void AddSelftest()
        {
            entries.UnionWith(ProductionReportFields.SourceNames);
            for (var i = 0; i < 10; i++)
            {
                entries.Add($"link_{i}");
                entries.Add($"poe_a_st_{i}");
                entries.Add($"poe_a_v_{i}");
                entries.Add($"poe_a_c_{i}");
                entries.Add($"присутствие SFP{i + 1}");
            }
        }

        Visit(root, Array.Empty<byte>());
        if (observedNames != null && source == ReportSourceKind.Variable)
            variables.UnionWith(observedNames);
        foreach (var field in root.ReportTemplate?.Fields ?? Array.Empty<ReportField>())
        {
            if (field.Source == ReportSourceKind.Entry) entries.Add(field.Key);
            if (field.Source == ReportSourceKind.Variable)
            {
                variables.Add(field.Key);
                if (field.Format is ReportValueFormat.BooleanText or ReportValueFormat.BooleanNumber)
                    statuses.Add(field.Key);
            }
            if (field.Source == ReportSourceKind.AllPassed)
                statuses.UnionWith(field.Key.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
            statuses.Add(field.StatusKey);
        }

        var options = source switch
        {
            ReportSourceKind.Entry => entries,
            ReportSourceKind.AllPassed => statuses,
            _ => variables
        };
        return options.Where(key => source == ReportSourceKind.Entry
                ? !string.IsNullOrWhiteSpace(key)
                : ReportTemplate.CanSelectVariable(key) && !reportOutputs.Contains(key))
            .OrderBy(key => key, StringComparer.CurrentCulture).ToArray();
    }
}
