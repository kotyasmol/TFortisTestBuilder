using System.Collections.Concurrent;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Services;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class ReportVariableHistoryTests
{
    [Fact]
    public void CapturesNamesFromMainAndParallelScopesWithoutValues()
    {
        var observed = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var context = new TestContext(new RegisterState());
        context.Variables.ObserveAssignment = name => observed.TryAdd(name, 0);
        context.SetVariable("Root.Value", "value-that-must-not-be-saved");
        var branch = context.CreateParallelBranch(CancellationToken.None);
        branch.Variables.Add("Branch.Temporary", 42);
        branch.Variables.Remove("Branch.Temporary");
        branch.Variables.Remove("NeverAssigned");
        context.SetVariable("Execution.Error", "failure-message-that-must-not-be-saved");
        context.SetVariable("BuildReport.Success", false);

        var directory = Directory.CreateTempSubdirectory("tfortis-report-history-");
        try
        {
            var profile = Path.Combine(directory.FullName, "profile.json");
            var writer = new ReportVariableHistory(directory.FullName);
            writer.Remember(profile, observed.Keys);
            var restarted = new ReportVariableHistory(directory.FullName);
            Assert.Equal(new[] { "Branch.Temporary", "Execution.Error", "Root.Value" }, restarted.Read(profile));
            Assert.Empty(restarted.Read(Path.Combine(directory.FullName, "another-profile.json")));
            var catalog = File.ReadAllText(Assert.Single(directory.GetFiles("*.json")).FullName);
            Assert.DoesNotContain("must-not-be-saved", catalog);

            restarted.Remember(profile, new[] { "AnotherRun" });
            Assert.Equal(new[] { "AnotherRun", "Branch.Temporary", "Execution.Error", "Root.Value" },
                new ReportVariableHistory(directory.FullName).Read(profile));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void CombinesObservedVariablesWithProfileSourcesAndExcludesReportText()
    {
        var graph = new GraphWorkspaceViewModel();
        graph.Nodes.Add(new BuildTestReportNodeViewModel { ReportVariableName = "GeneratedReport", IncludeAllVariables = true });
        graph.Nodes.Add(new RunDataTestNodeViewModel { OutputVariableName = "Network" });

        var variables = ReportSourceCatalog.Collect(graph, ReportSourceKind.Variable,
            new[] { "Selftest.UnknownField", "GeneratedReport", "SendReport.RawResponse", "Network.Port0.Passed" });

        Assert.Contains("Selftest.UnknownField", variables);
        Assert.Contains("Network.Port0.Passed", variables);
        Assert.DoesNotContain("GeneratedReport", variables);
        Assert.DoesNotContain("SendReport.RawResponse", variables);
        Assert.Equal(variables.Count, variables.Distinct(StringComparer.Ordinal).Count());
        Assert.True(ReportSourceCatalog.UsesAllVariables(graph));
        graph.Nodes.Add(new BuildTestReportNodeViewModel { IncludeAllVariables = false });
        Assert.False(ReportSourceCatalog.UsesAllVariables(graph));
    }
}
