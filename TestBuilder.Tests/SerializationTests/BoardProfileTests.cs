using TestBuilder.Domain.Modbus;
using TestBuilder.Services;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.ViewModels.StepVM;
namespace TestBuilder.Tests.SerializationTests;
public class BoardProfileTests
{
    static string Profiles=>Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../profiles"));
    [Theory]
    [InlineData("RTL_SW_board.json")][InlineData("RTL_POE_board.json")][InlineData("RTL_LED_color_template.json")][InlineData("board_manual_label.json")]
    public void BoardProfile_LoadSaveLoadCompile_PreservesHardwareLayoutAndOperations(string file)
    {
        using var bus=new ModbusService();using var vm=new TestViewModel(bus,new SlaveManager(bus));
        var name=GraphSerializer.Deserialize(File.ReadAllText(Path.Combine(Profiles,file)),vm);Assert.Equal("Board",vm.RootGraph.ProductKind);
        var nodeCount=All(vm.RootGraph).Count();var regions=vm.RootGraph.Board?.Regions.Count;
        using var compiler=new GraphCompiler(bus,NullLogger.Instance);Assert.NotNull(compiler.Compile(vm.RootGraph));
        var saved=GraphSerializer.Serialize(vm,name);Assert.Contains("Start (board v1)",saved);
        GraphSerializer.Deserialize(saved,vm);Assert.Equal(nodeCount,All(vm.RootGraph).Count());Assert.Equal(regions,vm.RootGraph.Board?.Regions.Count);
        Assert.NotNull(compiler.Compile(vm.RootGraph));
        if(file is "RTL_SW_board.json" or "RTL_POE_board.json")
        {
            Assert.NotEmpty(vm.RootGraph.Board!.Regions);Assert.Single(vm.RootGraph.Hardware!.Devices);Assert.Contains(vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>(),s=>s.RunOnFailure);
            var ids=vm.RootGraph.Board.Regions.Select(r=>r.Id).ToHashSet();
            Assert.All(All(vm.RootGraph).SelectMany(n=>n.BoardElements.Split(',',StringSplitOptions.RemoveEmptyEntries)),id=>Assert.Contains(id,ids));
            Assert.All(All(vm.RootGraph).OfType<CheckRegisterRangeNodeViewModel>(),n=>Assert.False(n.LiveRead));
            var bound = All(vm.RootGraph).SelectMany(n => n.BoardElements.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet();
            Assert.True(ids.SetEquals(bound), "Every source board region must be associated with a test stage.");
            Assert.All(All(vm.RootGraph).OfType<SendTestReportNodeViewModel>(), n => Assert.False(n.FailOnError));
        }
    }
    [Fact] public void PoE_HasExactly16ColorChecks_AndOriginalAddresses()
    {
        using var bus=new ModbusService();using var vm=new TestViewModel(bus,new SlaveManager(bus));GraphSerializer.Deserialize(File.ReadAllText(Path.Combine(Profiles,"RTL_POE_board.json")),vm);
        var leds=All(vm.RootGraph).OfType<CheckLedColorNodeViewModel>().ToArray();Assert.Equal(16,leds.Length);
        Assert.Equal(Enumerable.Range(2437,16).Select(x=>(ushort)x),leds.Select(x=>x.RedAddress));
        Assert.Equal(Enumerable.Range(2421,16).Select(x=>(ushort)x),leds.Select(x=>x.WhiteAddress));
        Assert.All(leds,x=>Assert.Equal(20,x.ColorMargin));
    }
    [Fact] public void SW_PreservesK5StageOrder_AndNeverProgramsCalibrationRegisters()
    {
        using var bus=new ModbusService();using var vm=new TestViewModel(bus,new SlaveManager(bus));GraphSerializer.Deserialize(File.ReadAllText(Path.Combine(Profiles,"RTL_SW_board.json")),vm);
        var writes=All(vm.RootGraph).OfType<ModbusWriteNodeViewModel>().ToArray();
        Assert.Equal(new ushort[]{2323,2325,2327,2325,2323},writes.Where(x=>new[]{2323,2325,2327}.Contains(x.Address)).Select(x=>x.Address));
        Assert.DoesNotContain(writes,w=>new[]{2335,2338,2343,2346,2349,2352,2355,2358,2361,2364,2367,2370}.Contains(w.Address));
    }
    [Fact] public void NonHoldingAreaFailsClosedForOlderRuntime_AndInvalidMapDoesNotEraseGraph()
    {
        using var bus=new ModbusService();using var vm=new TestViewModel(bus,new SlaveManager(bus));
        vm.RootGraph.Nodes.Add(new StartNodeViewModel());vm.RootGraph.Nodes.Add(new ModbusWriteNodeViewModel{SlaveId=1,Address=10,RegisterArea=ModbusArea.Coil});
        var json=GraphSerializer.Serialize(vm,"custom");Assert.Contains("Start (board v1)",json);
        var count=vm.RootGraph.Nodes.Count;
        Assert.Throws<InvalidOperationException>(()=>GraphSerializer.Deserialize("{\"hardware\":{\"Devices\":[]},\"nodes\":[]}",vm));
        Assert.Equal(count,vm.RootGraph.Nodes.Count);
    }
    [Fact] public void LedOnlyGraphRequiresConnection()
    {
        var graph=new GraphWorkspaceViewModel();graph.Nodes.Add(new CheckLedColorNodeViewModel());Assert.True(GraphConnectionRequirements.RequiresStandConnection(graph));
    }
    public static IEnumerable<NodeViewModel> All(GraphWorkspaceViewModel g){foreach(var n in g.Nodes){yield return n;if(n is ICompositeNodeViewModel c)foreach(var ch in All(c.BodyGraph))yield return ch;}}
}
