using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Services;
using TestBuilder.Services.Http;
using TestBuilder.Services.Modbus;
using TestBuilder.Tests.SerializationTests;
using TestBuilder.Tests.Support;
using TestBuilder.ViewModels;
using TestBuilder.ViewModels.StepVM;
namespace TestBuilder.Tests.StepTests;

public class BoardAlgorithmExecutionTests
{
    [Theory]
    [InlineData("RTL_SW_board.json")][InlineData("RTL_POE_board.json")]
    public async Task CompleteHardwareAlgorithm_WithControlledInputs_PassesAndPowersOff(string filename)
    {
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var uiBus=new ModbusService();using var vm=new TestViewModel(uiBus,new SlaveManager(uiBus));
        LoadFast(vm,filename);
        var ctx=new TestContext(new RegisterState()){RequireFreshMonitoring=true,ModbusRegisters=new(vm.RootGraph.Hardware!.Devices.Select(d=>new TestBuilder.Domain.Modbus.Models.ConfiguredSlaveModel(d,uiBus))),OperatorPrompt=_=>Task.FromResult(true),OperatorInput=(_,_)=>Task.FromResult<string?>("413148580e5939514c53698b")};
        var rig=new FakeRig(ctx);using var compiler=new GraphCompiler(rig,rig,NullLogger.Instance,rig);
        var polling=rig.PollAsync(cts.Token);
        try
        {
            var result=await new TestExecutor().ExecuteAsync(compiler.Compile(vm.RootGraph).StartNode,ctx,cts.Token);
            Assert.True(result == ExecutionStatus.Completed,ctx.Failure?.Summary + "\n" + string.Join("\n",ctx.ReportEntries.Where(x=>!x.IsSuccess).Select(x=>x.Name+": "+x.Value)));Assert.False(ctx.HasCriticalError);
            Assert.All(ctx.ReportEntries,e=>Assert.True(e.IsSuccess,e.Name+": "+e.Value));
            Assert.Equal(123456,ctx.GetVariable<int>("SerialNumber"));
            var power=filename.Contains("SW")?new[]{2301,2302,2307,2385}:new[]{2403,2492,2489,2511};
            Assert.All(power,a=>Assert.Equal(0,rig.Values[a]));
            if(filename.Contains("SW"))Assert.Contains(rig.Commands,c=>c.Contains("sensorTemperature"));
            else Assert.Equal(16,ctx.ReportEntries.Count(e=>e.Name.StartsWith("Цвет Led")));
        }
        finally {cts.Cancel();await polling;}
    }
    [Theory]
    [InlineData("RTL_POE_board.json",false)][InlineData("RTL_POE_board.json",true)]
    [InlineData("RTL_SW_board.json",false)][InlineData("RTL_SW_board.json",true)]
    public async Task FailureOrCancellation_KeepsCleanupExecutable(string filename,bool cancel)
    {
        using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(10));using var pollingCts=new CancellationTokenSource();
        using var uiBus=new ModbusService();using var vm=new TestViewModel(uiBus,new SlaveManager(uiBus));LoadFast(vm,filename);
        var ctx=new TestContext(new RegisterState()){RequireFreshMonitoring=true,ModbusRegisters=new(vm.RootGraph.Hardware!.Devices.Select(d=>new TestBuilder.Domain.Modbus.Models.ConfiguredSlaveModel(d,uiBus))),OperatorPrompt=_=>Task.FromResult(true),OperatorInput=(_,_)=>Task.FromResult<string?>("413148580e5939514c53698b")};
        var rig=new FakeRig(ctx);if(filename.Contains("SW")){rig.Values[2324]=cancel?2:3;rig.CancelAddress=2301;}else rig.Values[2405]=0;if(cancel)rig.CancelOnReset=cts;
        using var compiler=new GraphCompiler(rig,rig,NullLogger.Instance,rig);var polling=rig.PollAsync(pollingCts.Token);
        try
        {
            var run=new TestExecutor().ExecuteAsync(compiler.Compile(vm.RootGraph).StartNode,ctx,cts.Token);
            if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run);
            else {Assert.Equal(ExecutionStatus.Failed,await run);if(filename.Contains("SW")){Assert.Contains(ctx.ReportEntries,r=>r.Name=="K5 VMAIN"&&!r.IsSuccess);Assert.Contains(ctx.ReportEntries,r=>r.Name=="K5 VRES"&&r.IsSuccess);}else{Assert.Contains(ctx.ReportEntries,r=>r.Name=="PoE 1A"&&!r.IsSuccess);Assert.Contains(ctx.ReportEntries,r=>r.Name=="PoE 8B"&&r.IsSuccess);}}
            rig.CancelOnReset=null;
            var cleanup=Assert.Single(vm.RootGraph.Nodes.OfType<SubtestNodeViewModel>().Where(s=>s.RunOnFailure&&s.Name.StartsWith("Снятие питания")));
            Assert.Equal(ExecutionStatus.Completed,await new TestExecutor().ExecuteAsync(compiler.Compile(cleanup.BodyGraph).StartNode,ctx,CancellationToken.None));
            Assert.All(filename.Contains("SW")?new[]{2301,2302,2307,2385}:new[]{2403,2492,2489,2511},a=>Assert.Equal(0,rig.Values[a]));
        }
        finally{pollingCts.Cancel();await polling;}
    }
    static void LoadFast(TestViewModel vm,string filename)
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../profiles",filename));GraphSerializer.Deserialize(File.ReadAllText(path),vm);
        // Only time and external delivery are removed. All hardware operations and decisions remain.
        foreach(var node in BoardProfileTests.All(vm.RootGraph))
        {
            if(node is DelayNodeViewModel delay)delay.Milliseconds=0;
            if(node is ForEachSlaveNodeViewModel loop)loop.RepeatDelayMs=0;
            if(node is SubtestNodeViewModel sub && (sub.Name.StartsWith("Отчёт")||sub.Name.StartsWith("Этикетка")))sub.IsEnabled=false;
            if(node is GetSerialNumberFromServerNodeViewModel serial)serial.ServerBaseUrl="http://offline.test";
        }
    }
    sealed class FakeRig:IModbusService,IHttpRequestService,ISerialConsoleSession
    {
        readonly TestContext context;public ConcurrentDictionary<int,int> Values {get;}=new();public List<string> Commands{get;}=new();public CancellationTokenSource? CancelOnReset;public int CancelAddress=2489;
        public FakeRig(TestContext c)
        {
            context=c;for(int a=2300;a<=2511;a++)Values[a]=0;
            Values[2317]=Values[2318]=3000;foreach(var a in new[]{2324,2326,2328,2331,2494})Values[a]=2;Values[2404]=3300;Values[2486]=3;
            for(int i=0;i<16;i++){Values[2405+i]=1;Values[2421+i]=100;Values[2437+i]=10;Values[2453+i]=100;Values[2469+i]=10;Values[2495+i]=51000;}
        }
        public async Task PollAsync(CancellationToken ct){try{while(!ct.IsCancellationRequested){var time=System.Diagnostics.Stopwatch.GetTimestamp();foreach(var (k,v) in Values)context.RegisterState.Update(1,k,v,startedAt:time);await Task.Delay(5,ct);}}catch(OperationCanceledException){}}
        public Task<bool> WriteRegisterAsync(byte slave,ushort address,ushort value,bool verify=true,CancellationToken cancellationToken=default)
        {cancellationToken.ThrowIfCancellationRequested();Values[address]=value;if(address==CancelAddress&&value==1)CancelOnReset?.Cancel();return Task.FromResult(true);}
        public Task<ushort[]> ReadRegistersAsync(byte s,ushort a,ushort count,CancellationToken ct=default)=>throw new InvalidOperationException("Graph must use polling cache, not a separate read.");
        public Task<bool> CheckPortAsync(CancellationToken ct=default)=>Task.FromResult(true);
        public void SubscribeRegister(byte s,ushort a,Action<ushort[]> cb){}
        public Task<HttpRequestResult> GetAsync(string url,TimeSpan timeout,CancellationToken ct)=>Task.FromResult(HttpRequestResult.Success(200,"123456",TimeSpan.Zero));
        public Task<string> ExchangeAsync(string p,int b,string command,string term,int quiet,int timeout,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();Commands.Add(command);
            if(command.Contains("system board"))return Task.FromResult("{\"release\":{\"version\":\"1.2.3\"}}");
            if(command.Contains("poe info"))return Task.FromResult("{\"firmware\":\"SET_PRODUCTION_FIRMWARE\",\"mcu\":\"SET_PRODUCTION_MCU\"}");
            var name=Regex.Match(command,"\"name\":\"([^\"]+)\"").Groups[1].Value;
            if(command.Contains("setParam")){Values[2306]=int.Parse(Regex.Match(command,"\"value\":\"([01])\"").Groups[1].Value);return Task.FromResult("{}");}
            var value=name switch{"sensor1"=>Values[2303].ToString(),"sensor2"=>Values[2304].ToString(),"tamper"=>Values[2305].ToString(),"sensorConnected" or "upsModeAvalible"=>"1","sensorTemperature"=>"23.5","SW_VERS"=>"1.0.0","DEVICE_ID"=>"413148580e5939514c53698b",_=>"0"};
            return Task.FromResult(command+"\n"+JsonSerializer.Serialize(new Dictionary<string,string>{{name,value}})+"\nroot@TFortis:/#");
        }
    }
}
