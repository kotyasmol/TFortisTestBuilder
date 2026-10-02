using System.Text.Json.Nodes;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.Tests.SerializationTests;

public class ParallelGraphCompilationTests
{
    private const string SourceProfile = "PSW_UPS_Box_8x2Pro_full_algorithm_polling.json";
    private const string ParallelProfile = "PSW_UPS_Box_8x2Pro_parallel_start.json";

    [Fact]
    public void SameOutputFanout_CompilesBothBranchesAndNearestJoin()
    {
        var start = new StartNodeViewModel();
        var left = new DelayNodeViewModel();
        var right = new DelayNodeViewModel();
        var join = new DelayNodeViewModel();
        var end = new EndNodeViewModel();
        var graph = Graph(start, left, right, join, end);
        Connect(graph, start, left);
        Connect(graph, start, right);
        Connect(graph, left, join);
        Connect(graph, right, join);
        Connect(graph, join, end);

        var compiled = Compile(graph);
        var fork = compiled.StartNode.ParallelTransitions[StepResult.Next];

        Assert.Equal(new NodeViewModel[] { left, right }, fork.Branches.Select(b => b.Source));
        Assert.Same(join, fork.JoinNode.Source);
        Assert.Same(end, fork.JoinNode.Next!.Source);
    }

    [Fact]
    public void TrueAndFalseOutputs_RemainAlternativeTransitions()
    {
        var start = new StartNodeViewModel();
        var condition = new CheckVariableEqualityNodeViewModel();
        var yes = new DelayNodeViewModel();
        var no = new DelayNodeViewModel();
        var end = new EndNodeViewModel();
        var graph = Graph(start, condition, yes, no, end);
        Connect(graph, start, condition);
        Connect(graph, condition, yes, condition.TrueOut);
        Connect(graph, condition, no, condition.FalseOut);
        Connect(graph, yes, end);
        Connect(graph, no, end);

        var compiledCondition = Compile(graph).StartNode.Next!;

        Assert.Empty(compiledCondition.ParallelTransitions);
        Assert.Same(yes, compiledCondition.OnTrue!.Source);
        Assert.Same(no, compiledCondition.OnFalse!.Source);
    }

    [Fact]
    public void TrueOutputFanout_DoesNotChangeSeparateFalseTransition()
    {
        var start = new StartNodeViewModel();
        var condition = new CheckVariableEqualityNodeViewModel();
        var left = new DelayNodeViewModel();
        var right = new DelayNodeViewModel();
        var join = new DelayNodeViewModel();
        var failure = new EndNodeViewModel();
        var end = new EndNodeViewModel();
        var graph = Graph(start, condition, left, right, join, failure, end);
        Connect(graph, start, condition);
        Connect(graph, condition, left, condition.TrueOut);
        Connect(graph, condition, right, condition.TrueOut);
        Connect(graph, condition, failure, condition.FalseOut);
        Connect(graph, left, join);
        Connect(graph, right, join);
        Connect(graph, join, end);

        var compiledCondition = Compile(graph).StartNode.Next!;

        Assert.Same(join, compiledCondition.ParallelTransitions[StepResult.True].JoinNode.Source);
        Assert.Same(failure, compiledCondition.OnFalse!.Source);
        Assert.False(compiledCondition.ParallelTransitions.ContainsKey(StepResult.False));
    }

    [Fact]
    public void ParallelBranchesWithoutJoin_AreRejected()
    {
        var start = new StartNodeViewModel();
        var left = new EndNodeViewModel();
        var right = new EndNodeViewModel();
        var graph = Graph(start, left, right);
        Connect(graph, start, left);
        Connect(graph, start, right);

        Assert.Throws<InvalidOperationException>(() => Compile(graph));
    }

    [Fact]
    public void DuplicateConnection_IsRejectedInsteadOfExecutingBranchTwice()
    {
        var start = new StartNodeViewModel();
        var end = new EndNodeViewModel();
        var graph = Graph(start, end);
        Connect(graph, start, end);
        Connect(graph, start, end);

        Assert.Throws<InvalidOperationException>(() => Compile(graph));
    }

    [Fact]
    public void ConditionalBranchesWithTwoNearestCommonNodes_AreRejected()
    {
        var start = new StartNodeViewModel();
        var left = new CheckVariableEqualityNodeViewModel();
        var right = new CheckVariableEqualityNodeViewModel();
        var firstJoin = new DelayNodeViewModel();
        var secondJoin = new DelayNodeViewModel();
        var end = new EndNodeViewModel();
        var graph = Graph(start, left, right, firstJoin, secondJoin, end);
        Connect(graph, start, left);
        Connect(graph, start, right);
        foreach (var condition in new[] { left, right })
        {
            Connect(graph, condition, firstJoin, condition.TrueOut);
            Connect(graph, condition, secondJoin, condition.FalseOut);
        }
        Connect(graph, firstJoin, end);
        Connect(graph, secondJoin, end);

        Assert.Throws<InvalidOperationException>(() => Compile(graph));
    }

    [Fact]
    public void CrossBranchSharedNodeBeforeMandatoryJoin_IsRejected()
    {
        var start = new StartNodeViewModel();
        var left = new DelayNodeViewModel();
        var right = new CheckVariableEqualityNodeViewModel();
        var shared = new DelayNodeViewModel();
        var join = new EndNodeViewModel();
        var graph = Graph(start, left, right, shared, join);
        Connect(graph, start, left);
        Connect(graph, start, right);
        Connect(graph, left, shared);
        Connect(graph, right, shared, right.TrueOut);
        Connect(graph, right, join, right.FalseOut);
        Connect(graph, shared, join);

        Assert.Throws<InvalidOperationException>(() => Compile(graph));
    }

    [Fact]
    public void ExternalEntranceIntoParallelBranch_IsRejected()
    {
        var start = new StartNodeViewModel();
        var left = new DelayNodeViewModel();
        var right = new DelayNodeViewModel();
        var external = new DelayNodeViewModel();
        var join = new EndNodeViewModel();
        var graph = Graph(start, left, right, external, join);
        Connect(graph, start, left);
        Connect(graph, start, right);
        Connect(graph, left, join);
        Connect(graph, right, join);
        Connect(graph, external, left);

        Assert.Throws<InvalidOperationException>(() => Compile(graph));
    }

    [Fact]
    public void CycleInsideParallelBranch_IsRejected()
    {
        var start = new StartNodeViewModel();
        var condition = new CheckVariableEqualityNodeViewModel();
        var repeat = new DelayNodeViewModel();
        var right = new DelayNodeViewModel();
        var join = new EndNodeViewModel();
        var graph = Graph(start, condition, repeat, right, join);
        Connect(graph, start, condition);
        Connect(graph, start, right);
        Connect(graph, condition, repeat, condition.TrueOut);
        Connect(graph, repeat, condition);
        Connect(graph, condition, join, condition.FalseOut);
        Connect(graph, right, join);

        Assert.Throws<InvalidOperationException>(() => Compile(graph));
    }

    [Fact]
    public void SerialLegacyCycle_RemainsSupported()
    {
        var start = new StartNodeViewModel();
        var condition = new CheckVariableEqualityNodeViewModel();
        var repeat = new DelayNodeViewModel();
        var end = new EndNodeViewModel();
        var graph = Graph(start, condition, repeat, end);
        Connect(graph, start, condition);
        Connect(graph, condition, repeat, condition.TrueOut);
        Connect(graph, repeat, condition);
        Connect(graph, condition, end, condition.FalseOut);

        var compiledCondition = Compile(graph).StartNode.Next!;

        Assert.Empty(compiledCondition.ParallelTransitions);
        Assert.Same(compiledCondition, compiledCondition.OnTrue!.Next);
    }

    [Fact]
    public void SuccessfulExitBeforeJoin_IsRejected()
    {
        var start = new StartNodeViewModel();
        var condition = new CheckVariableEqualityNodeViewModel();
        var deadEnd = new DelayNodeViewModel();
        var right = new DelayNodeViewModel();
        var join = new EndNodeViewModel();
        var graph = Graph(start, condition, deadEnd, right, join);
        Connect(graph, start, condition);
        Connect(graph, start, right);
        Connect(graph, condition, join, condition.TrueOut);
        Connect(graph, condition, deadEnd, condition.FalseOut);
        Connect(graph, right, join);

        Assert.Throws<InvalidOperationException>(() => Compile(graph));
    }

    [Fact]
    public void UnconnectedFalseFailureExit_DoesNotPreventValidJoin()
    {
        var start = new StartNodeViewModel();
        var condition = new CheckVariableEqualityNodeViewModel();
        var right = new DelayNodeViewModel();
        var join = new EndNodeViewModel();
        var graph = Graph(start, condition, right, join);
        Connect(graph, start, condition);
        Connect(graph, start, right);
        Connect(graph, condition, join, condition.TrueOut);
        Connect(graph, right, join);

        var fork = Compile(graph).StartNode.ParallelTransitions[StepResult.Next];

        Assert.Same(join, fork.JoinNode.Source);
        Assert.Null(fork.Branches[0].OnFalse);
    }

    [Fact]
    public void NestedDiamonds_HaveSeparateInnerAndOuterJoins()
    {
        var start = new StartNodeViewModel();
        var innerFork = new DelayNodeViewModel();
        var right = new DelayNodeViewModel();
        var innerLeft = new DelayNodeViewModel();
        var innerRight = new DelayNodeViewModel();
        var innerJoin = new DelayNodeViewModel();
        var outerJoin = new EndNodeViewModel();
        var graph = Graph(start, innerFork, right, innerLeft, innerRight, innerJoin, outerJoin);
        Connect(graph, start, innerFork);
        Connect(graph, start, right);
        Connect(graph, innerFork, innerLeft);
        Connect(graph, innerFork, innerRight);
        Connect(graph, innerLeft, innerJoin);
        Connect(graph, innerRight, innerJoin);
        Connect(graph, innerJoin, outerJoin);
        Connect(graph, right, outerJoin);

        var outer = Compile(graph).StartNode.ParallelTransitions[StepResult.Next];
        var inner = outer.Branches[0].ParallelTransitions[StepResult.Next];

        Assert.Same(outerJoin, outer.JoinNode.Source);
        Assert.Same(innerJoin, inner.JoinNode.Source);
        Assert.Equal(2, inner.Branches.Count);
    }

    [Fact]
    public void SameModbusSlaveInDifferentBranches_IsRejected()
    {
        var first = new ModbusWriteNodeViewModel { SlaveId = 23, Address = 1215, Value = 1 };
        var second = new ModbusWriteNodeViewModel { SlaveId = 23, Address = 1220, Value = 1 };

        Assert.Throws<InvalidOperationException>(() => Compile(TwoBranchGraph(first, second)));
    }

    [Fact]
    public void DutNetworkAndPowerChangeInDifferentBranches_AreRejected()
    {
        var selftest = new SelfTestCheckNodeViewModel();
        var power = new ModbusWriteNodeViewModel { SlaveId = 23, Address = 1200, Value = 0 };

        Assert.Throws<InvalidOperationException>(() => Compile(TwoBranchGraph(selftest, power)));
    }

    [Fact]
    public void DutNetworkAndHeaterInDifferentBranches_AreAllowed()
    {
        var selftest = new SelfTestCheckNodeViewModel();
        var heater = new ModbusWriteNodeViewModel { SlaveId = 23, Address = 1215, Value = 1 };

        var compiled = Compile(TwoBranchGraph(selftest, heater));

        Assert.Equal(2, compiled.StartNode.ParallelTransitions[StepResult.Next].Branches.Count);
    }

    [Fact]
    public void OperatorPromptInsideParallelBranch_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Compile(TwoBranchGraph(
            new OperatorActionNodeViewModel(), new DelayNodeViewModel())));
    }

    [Fact]
    public void CycleInsideEnabledSubtestBody_InParallelBranchIsRejected()
    {
        var subtest = SubtestWithBodyCycle();

        Assert.Throws<InvalidOperationException>(() => Compile(TwoBranchGraph(
            subtest, new DelayNodeViewModel())));
    }

    [Fact]
    public void SameSubtestBodyCycle_OutsideParallelRegionRemainsSupported()
    {
        var start = new StartNodeViewModel();
        var subtest = SubtestWithBodyCycle();
        var end = new EndNodeViewModel();
        var graph = Graph(start, subtest, end);
        Connect(graph, start, subtest);
        Connect(graph, subtest, end);

        var compiled = Compile(graph);

        Assert.Same(subtest, compiled.StartNode.Next!.Source);
        Assert.Empty(compiled.StartNode.Next.ParallelTransitions);
    }

    [Fact]
    public void BoundedForSlaves_WithAcyclicBodyRemainsSupportedInsideParallelBranch()
    {
        var loop = new ForEachSlaveNodeViewModel { FromSlaveId = 1, ToSlaveId = 3, Step = 2 };
        var bodyStart = loop.BodyGraph.Nodes.OfType<BodyStartNodeViewModel>().Single();
        var bodyEnd = loop.BodyGraph.Nodes.OfType<BodyEndNodeViewModel>().Single();
        var load = new ModbusWriteNodeViewModel { UseCurrentSlaveId = true, Address = 1414, Value = 1 };
        loop.BodyGraph.Nodes.Add(load);
        Connect(loop.BodyGraph, bodyStart, load);
        Connect(loop.BodyGraph, load, bodyEnd);
        var independentLoad = new ModbusWriteNodeViewModel { SlaveId = 5, Address = 1414, Value = 1 };

        var compiled = Compile(TwoBranchGraph(loop, independentLoad));

        Assert.Equal(2, compiled.StartNode.ParallelTransitions[StepResult.Next].Branches.Count);
    }

    [Theory]
    [InlineData(1500)]
    [InlineData(1501)]
    public void SensorWriteAndDutNetworkInDifferentBranches_AreRejected(int address)
    {
        var sensor = new ModbusWriteNodeViewModel { SlaveId = 21, Address = (ushort)address, Value = 1 };

        Assert.Throws<InvalidOperationException>(() => Compile(TwoBranchGraph(
            sensor, new SelfTestCheckNodeViewModel())));
    }

    [Theory]
    [InlineData(1100)]
    [InlineData(1101)]
    [InlineData(1109)]
    [InlineData(1210)]
    [InlineData(1300)]
    [InlineData(1301)]
    [InlineData(1302)]
    [InlineData(1303)]
    public void OtherModelPowerWrites_AreRejectedInsideParallelRegion(int address)
    {
        var power = new ModbusWriteNodeViewModel { SlaveId = 23, Address = (ushort)address, Value = 0 };

        Assert.Throws<InvalidOperationException>(() => Compile(TwoBranchGraph(
            power, new DelayNodeViewModel())));
    }

    [Theory]
    [InlineData(1412)]
    [InlineData(1413)]
    [InlineData(1414)]
    [InlineData(1415)]
    [InlineData(1420)]
    [InlineData(1421)]
    [InlineData(1430)]
    public void PoeLoadChangesAndDutNetworkInDifferentBranches_AreRejected(int address)
    {
        var load = new ModbusWriteNodeViewModel { SlaveId = 1, Address = (ushort)address, Value = 1 };

        Assert.Throws<InvalidOperationException>(() => Compile(TwoBranchGraph(
            load, new SelfTestCheckNodeViewModel())));
    }

    [Fact]
    public void IndependentPoeLoadDevices_CanBeConfiguredInDifferentBranches()
    {
        var first = new ModbusWriteNodeViewModel { SlaveId = 1, Address = 1412, Value = 15000 };
        var second = new ModbusWriteNodeViewModel { SlaveId = 3, Address = 1414, Value = 1 };

        var compiled = Compile(TwoBranchGraph(first, second));

        Assert.Equal(2, compiled.StartNode.ParallelTransitions[StepResult.Next].Branches.Count);
    }

    [Fact]
    public void ParallelProfile_OnlyRewiresStartupAndKeepsOriginalHardwareSettings()
    {
        var original = JsonNode.Parse(ReadProfile(SourceProfile))!;
        var parallel = JsonNode.Parse(ReadProfile(ParallelProfile))!;
        Assert.Equal(original["deviceModel"]!.GetValue<string>(), parallel["deviceModel"]!.GetValue<string>());
        Assert.NotEqual(original["name"]!.GetValue<string>(), parallel["name"]!.GetValue<string>());
        Assert.Contains("параллельный старт", parallel["configurationName"]!.GetValue<string>());

        var originalNodes = original["nodes"]!.AsArray();
        var parallelNodes = parallel["nodes"]!.AsArray();
        Assert.Equal(originalNodes.Count, parallelNodes.Count);
        foreach (var node in originalNodes)
        {
            var id = node!["id"]!.GetValue<string>();
            var copiedNode = parallelNodes.Single(n => n!["id"]!.GetValue<string>() == id)!;
            var expected = node.DeepClone().AsObject();
            var actual = copiedNode.DeepClone().AsObject();
            foreach (var ignored in new[] { "x", "y" })
            {
                expected.Remove(ignored);
                actual.Remove(ignored);
            }
            if (id == "0") actual["type"] = "Start";
            if (id == "17")
                foreach (var labelField in new[] { "text", "labelWidth", "labelHeight" })
                {
                    expected.Remove(labelField);
                    actual.Remove(labelField);
                }
            Assert.True(JsonNode.DeepEquals(expected, actual), $"Changed original settings for node {id}.");
        }

        var expectedConnections = original["connections"]!.AsArray().DeepClone().AsArray();
        expectedConnections.Single(c => c!["sourceNodeId"]!.GetValue<string>() == "6")!["targetNodeId"] = "22";
        expectedConnections.Add(JsonNode.Parse("""
            { "sourceNodeId": "4", "sourceConnector": "Success", "targetNodeId": "7", "targetConnector": "In" }
            """));
        Assert.Equal(ConnectionKeys(expectedConnections), ConnectionKeys(parallel["connections"]!.AsArray()));
        Assert.Equal("Start", originalNodes[0]!["type"]!.GetValue<string>());
        Assert.Equal("Start (parallel v1)", parallelNodes[0]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void ParallelProfile_RoundTripKeepsForkJoinAndOldVersionGuard()
    {
        using var modbus = new ModbusService();
        var originalVm = new TestViewModel(modbus, new SlaveManager(modbus));
        var profileName = GraphSerializer.Deserialize(ReadProfile(ParallelProfile), originalVm);
        AssertProFork(Compile(originalVm.RootGraph));

        var saved = GraphSerializer.Serialize(originalVm, profileName);
        Assert.Contains("\"type\": \"Start (parallel v1)\"", saved);
        var loadedVm = new TestViewModel(modbus, new SlaveManager(modbus));
        GraphSerializer.Deserialize(saved, loadedVm);

        AssertProFork(Compile(loadedVm.RootGraph));
        Assert.Equal(originalVm.RootGraph.ConfigurationName, loadedVm.RootGraph.ConfigurationName);
        Assert.Equal(originalVm.RootGraph.DeviceModel, loadedVm.RootGraph.DeviceModel);
        Assert.Equal(originalVm.RootGraph.Connections.Count, loadedVm.RootGraph.Connections.Count);
        Assert.Equal(2, loadedVm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Count(n => n.RunOnFailure));
    }

    [Fact]
    public void SerialProfile_SerializationKeepsLegacyStartType()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var name = GraphSerializer.Deserialize(ReadProfile(SourceProfile), vm);

        var json = GraphSerializer.Serialize(vm, name);

        Assert.DoesNotContain("parallel v1", json);
        Assert.Empty(Compile(vm.RootGraph).StartNode.ParallelTransitions);
    }

    [Fact]
    public void FutureParallelVersion_IsRejectedInsteadOfSilentlyChangingExecution()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var existingNodes = vm.RootGraph.Nodes.ToArray();
        var json = ReadProfile(ParallelProfile).Replace("Start (parallel v1)", "Start (parallel v2)");

        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(json, vm));
        Assert.Equal(existingNodes, vm.RootGraph.Nodes);
    }

    [Fact]
    public void ParallelLoopBody_SerializationGuardsBothBodyAndContainingRoot()
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        vm.RootGraph.Clear();
        var start = new StartNodeViewModel();
        var loop = new ForEachSlaveNodeViewModel { FromSlaveId = 1, ToSlaveId = 1 };
        var end = new EndNodeViewModel();
        foreach (var node in new NodeViewModel[] { start, loop, end }) vm.RootGraph.Nodes.Add(node);
        Connect(vm.RootGraph, start, loop);
        Connect(vm.RootGraph, loop, end);
        var bodyStart = loop.BodyGraph.Nodes.OfType<BodyStartNodeViewModel>().Single();
        var bodyEnd = loop.BodyGraph.Nodes.OfType<BodyEndNodeViewModel>().Single();
        var first = new DelayNodeViewModel();
        var second = new DelayNodeViewModel();
        loop.BodyGraph.Nodes.Add(first);
        loop.BodyGraph.Nodes.Add(second);
        Connect(loop.BodyGraph, bodyStart, first);
        Connect(loop.BodyGraph, bodyStart, second);
        Connect(loop.BodyGraph, first, bodyEnd);
        Connect(loop.BodyGraph, second, bodyEnd);

        var json = GraphSerializer.Serialize(vm, "Nested parallel");

        Assert.Contains("\"type\": \"Start (parallel v1)\"", json);
        Assert.Contains("\"type\": \"Body Start (parallel v1)\"", json);
        var loaded = new TestViewModel(modbus, new SlaveManager(modbus));
        GraphSerializer.Deserialize(json, loaded);
        var body = loaded.RootGraph.Nodes.OfType<ForEachSlaveNodeViewModel>().Single().BodyGraph;
        var fork = Compile(body).StartNode.ParallelTransitions[StepResult.Next];
        Assert.Equal(2, fork.Branches.Count);
        Assert.IsType<BodyEndNodeViewModel>(fork.JoinNode.Source);
    }

    [Theory]
    [InlineData("sourceNodeId", "missing")]
    [InlineData("targetNodeId", "missing")]
    [InlineData("sourceConnector", "missing")]
    [InlineData("targetConnector", "missing")]
    public void MalformedSavedConnection_IsRejectedInsteadOfDroppingBranch(string property, string value)
    {
        using var modbus = new ModbusService();
        var vm = new TestViewModel(modbus, new SlaveManager(modbus));
        var existingNodes = vm.RootGraph.Nodes.ToArray();
        var existingConnections = vm.RootGraph.Connections.ToArray();
        vm.RootGraph.Title = "Несохранённый граф";
        vm.RootGraph.DeviceModel = "Исходная модель";
        var profile = JsonNode.Parse(ReadProfile(ParallelProfile))!;
        var connection = profile["connections"]!.AsArray()
            .First(c => c!["sourceNodeId"]!.GetValue<string>() == "4")!;
        connection[property] = value;

        Assert.Throws<InvalidOperationException>(() => GraphSerializer.Deserialize(profile.ToJsonString(), vm));
        Assert.Equal(existingNodes, vm.RootGraph.Nodes);
        Assert.Equal(existingConnections, vm.RootGraph.Connections);
        Assert.Equal("Несохранённый граф", vm.RootGraph.Title);
        Assert.Equal("Исходная модель", vm.RootGraph.DeviceModel);
    }

    private static void AssertProFork(CompiledGraph graph)
    {
        var power = FindNode(graph.StartNode, n => n.Source is SubtestNodeViewModel s && s.Name.StartsWith("05."));
        var fork = power.ParallelTransitions[StepResult.True];
        Assert.Equal(new[] { "06. Ожидание загрузки DUT и selftest", "08. Проверка нагревателя 1" },
            fork.Branches.Select(n => ((SubtestNodeViewModel)n.Source!).Name));
        Assert.Equal("09a. Проверка Sensor1 и Sensor2", ((SubtestNodeViewModel)fork.JoinNode.Source!).Name);
        Assert.Equal("07. Проверка selftest DUT", ((SubtestNodeViewModel)fork.Branches[0].OnTrue!.Source!).Name);
        Assert.Equal("09. Проверка нагревателя 2", ((SubtestNodeViewModel)fork.Branches[1].OnTrue!.Source!).Name);
        Assert.Same(fork.JoinNode, fork.Branches[0].OnTrue!.OnTrue);
        Assert.Same(fork.JoinNode, fork.Branches[1].OnTrue!.OnTrue);
    }

    private static TestNode FindNode(TestNode start, Func<TestNode, bool> predicate)
    {
        var pending = new Stack<TestNode>();
        var visited = new HashSet<TestNode>();
        pending.Push(start);
        while (pending.TryPop(out var node))
        {
            if (!visited.Add(node)) continue;
            if (predicate(node)) return node;
            foreach (var next in new[] { node.Next, node.OnTrue, node.OnFalse })
                if (next != null) pending.Push(next);
            foreach (var fork in node.ParallelTransitions.Values)
            {
                pending.Push(fork.JoinNode);
                foreach (var branch in fork.Branches) pending.Push(branch);
            }
        }
        throw new InvalidOperationException("Expected node was not compiled.");
    }

    private static GraphWorkspaceViewModel TwoBranchGraph(NodeViewModel left, NodeViewModel right)
    {
        var start = new StartNodeViewModel();
        var end = new EndNodeViewModel();
        var graph = Graph(start, left, right, end);
        Connect(graph, start, left);
        Connect(graph, start, right);
        Connect(graph, left, end);
        Connect(graph, right, end);
        return graph;
    }

    private static SubtestNodeViewModel SubtestWithBodyCycle()
    {
        var subtest = new SubtestNodeViewModel { Name = "Циклический подтест", IsEnabled = true };
        var start = subtest.BodyGraph.Nodes.OfType<StartNodeViewModel>().Single();
        var end = subtest.BodyGraph.Nodes.OfType<EndNodeViewModel>().Single();
        var condition = new CheckVariableEqualityNodeViewModel();
        var repeat = new DelayNodeViewModel { Milliseconds = 0 };
        subtest.BodyGraph.Nodes.Add(condition);
        subtest.BodyGraph.Nodes.Add(repeat);
        Connect(subtest.BodyGraph, start, condition);
        Connect(subtest.BodyGraph, condition, repeat, condition.TrueOut);
        Connect(subtest.BodyGraph, repeat, condition);
        Connect(subtest.BodyGraph, condition, end, condition.FalseOut);
        return subtest;
    }

    private static GraphWorkspaceViewModel Graph(params NodeViewModel[] nodes)
    {
        var graph = new GraphWorkspaceViewModel();
        foreach (var node in nodes) graph.Nodes.Add(node);
        return graph;
    }

    private static void Connect(GraphWorkspaceViewModel graph, NodeViewModel source, NodeViewModel target,
        ConnectorViewModel? output = null) => graph.Connections.Add(new ConnectionViewModel(
            output ?? source.Output[0], target.Input[0]));

    private static CompiledGraph Compile(GraphWorkspaceViewModel graph)
    {
        using var modbus = new ModbusService();
        using var http = new HttpRequestService();
        using var compiler = new GraphCompiler(modbus, http, NullLogger.Instance);
        return compiler.Compile(graph);
    }

    private static string ReadProfile(string filename) => File.ReadAllText(Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "profiles", filename)));

    private static string[] ConnectionKeys(JsonArray connections) => connections.Select(c =>
        string.Join("|", new[] { "sourceNodeId", "sourceConnector", "targetNodeId", "targetConnector" }
            .Select(key => c![key]!.GetValue<string>()))).OrderBy(key => key, StringComparer.Ordinal).ToArray();
}
