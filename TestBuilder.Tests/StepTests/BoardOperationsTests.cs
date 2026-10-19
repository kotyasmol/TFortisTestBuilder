using System.Diagnostics;
using System.Text.Json;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Modbus;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Models;
using TestBuilder.Services;
using TestBuilder.Tests.Support;

namespace TestBuilder.Tests.StepTests;

public class BoardOperationsTests
{
    static TestContext Context() => new(new RegisterState());
    [Theory]
    [InlineData("echo '{\"name\":\"sensor1\"}'\r\n{\"sensor1\":\"0\"}\r\nroot@TFortis:/#", "sensor1", "0")]
    [InlineData("log\n{\"release\":{\"version\":\"2.10.3\"}}\n#", "release.version", "2.10.3")]
    [InlineData("{\"channels\":[1,2,3]}", "channels[1]", "2")]
    public void JsonParser_ToleratesConsoleEchoAndNestedFields(string input,string path,string expected)
        => Assert.Equal(expected, ResponseParser.Parse(input,"Json",path,"String"));
    [Theory]
    [InlineData("{}", "absent")]
    [InlineData("{bad", "x")]
    [InlineData("{\"x\":null}", "x")]
    public void JsonParser_MissingOrMalformedIsNotSuccess(string input,string path)
        => Assert.Throws<InvalidOperationException>(()=>ResponseParser.Parse(input,"Json",path,"String"));
    [Fact] public void Parser_UsesInvariantNumbersAndNamedRegexGroups()
    {
        Assert.Equal(12.5, ResponseParser.Parse("{\"temp\":\"12.5\"}","Json","temp","Number"));
        Assert.Equal("AB12",ResponseParser.Parse("noise\nUID: AB12\n","Regex",@"UID: (?<value>[A-F0-9]+)","String"));
        Assert.Throws<InvalidOperationException>(()=>ResponseParser.Parse("NaN","Text","","Number"));
    }
    [Fact] public void Expansion_LeavesJsonAloneAndFailsMissingVariable()
    {
        var c=Context(); c.SetVariable("n",2);
        Assert.Equal("{\"value\":\"2\"}",ResponseParser.Expand("{\"value\":\"${n}\"}",c));
        Assert.Throws<InvalidOperationException>(()=>ResponseParser.Expand("${missing}",c));
    }
    [Theory]
    [InlineData(100,10,10,0,"red",true)]
    [InlineData(10,100,10,0,"green",true)]
    [InlineData(10,10,100,0,"blue",true)]
    [InlineData(50,50,50,51,"white",true)]
    [InlineData(100,80,10,0,"red",false)]
    [InlineData(50,0,0,0,"red",false)]
    [InlineData(50,70,50,51,"white",false)]
    [InlineData(50,50,50,50,"white",false)]
    public void Led_UsesExactOriginalStrictBoundaries(int r,int g,int b,int w,string color,bool expected)
        =>Assert.Equal(expected,CheckLedColorStep.Matches(r,g,b,w,color,20,50));
    [Fact] public async Task Led_ReadsPollCacheWithoutHardwareIo()
    {
        var c=Context();c.RegisterState.Update(1,2437,10);c.RegisterState.Update(1,2453,100);c.RegisterState.Update(1,2469,10);c.RegisterState.Update(1,2421,10);
        Assert.Equal(StepResult.True,await new CheckLedColorStep(new(),NullLogger.Instance).ExecuteAsync(c,default));
        Assert.True(c.GetVariable<bool>("Led.Passed"));Assert.True(Assert.Single(c.ReportEntries).IsSuccess);
        c.RegisterState.Clear();Assert.Equal(StepResult.False,await new CheckLedColorStep(new(),NullLogger.Instance).ExecuteAsync(c,default));
        Assert.False(c.Variables.ContainsKey("Led.Passed"));
    }
    [Fact] public async Task ParseFailure_ClearsOldResult_AndSupportsInPlaceInput()
    {
        var c=Context();c.SetVariable("x","ABCDEF");var o=new ParseResponseOptions{SourceVariableName="x",OutputVariableName="x",ParseMode="Regex",Selector="^[A-F]+$"};
        Assert.Equal(StepResult.True,await new ParseResponseStep(o,NullLogger.Instance).ExecuteAsync(c,default));
        Assert.Equal("ABCDEF",c.GetVariable<string>("x"));c.SetVariable("x","wrong");
        Assert.Equal(StepResult.False,await new ParseResponseStep(o,NullLogger.Instance).ExecuteAsync(c,default));Assert.False(c.Variables.ContainsKey("x"));
    }
    [Fact] public async Task ConsoleFailure_DoesNotLeaveStaleAnswer()
    {
        var c=Context();c.SetVariable("Console.Response","stale");
        Assert.Equal(StepResult.False,await new SerialConsoleStep(new(),new BadConsole(),NullLogger.Instance).ExecuteAsync(c,default));
        Assert.False(c.Variables.ContainsKey("Console.Response"));
    }
    [Fact] public async Task Input_CapturesTrimmedValue_AndClearsOnCancel()
    {
        var c=Context();c.OperatorInput=(_,_)=>Task.FromResult<string?>("  abc  ");
        var step=new OperatorActionStep("UID",NullLogger.Instance,"CpuId");
        Assert.Equal(StepResult.True,await step.ExecuteAsync(c,default));Assert.Equal("abc",c.GetVariable<string>("CpuId"));
        c.OperatorInput=(_,_)=>Task.FromResult<string?>(null);Assert.Equal(StepResult.False,await step.ExecuteAsync(c,default));Assert.False(c.Variables.ContainsKey("CpuId"));
    }
    [Fact] public async Task MinimumVersion_DoesNotUseLexicographicComparison()
    {
        var c=Context();c.SetVariable("v","1.10.0");
        Assert.Equal(StepResult.True,await new CheckVariableEqualityStep(NullLogger.Instance,"v","1.9",VariableComparisonType.VersionAtLeast,"").ExecuteAsync(c,default));
        Assert.Equal(StepResult.False,await new CheckVariableEqualityStep(NullLogger.Instance,"v","2.0",VariableComparisonType.VersionAtLeast,"").ExecuteAsync(c,default));
    }
    [Fact] public void AreasAndFreshness_AreIndependentAndPropagatedAcrossParallelJoin()
    {
        var c=Context();c.RequireFreshMonitoring=true;c.RegisterState.Update(1,10,7,ModbusArea.Holding,100);c.RegisterState.Update(1,10,1,ModbusArea.Coil,101);
        Assert.True(c.RegisterState.TryGet(1,10,out var coil,ModbusArea.Coil));Assert.Equal(1,coil);
        Assert.False(c.RegisterState.TryGet(1,10,out _,ModbusArea.Holding,101));
        c.RequiredPollAfter[1]=110;var a=c.CreateParallelBranch(default);var b=c.CreateParallelBranch(default);Assert.Equal(110,a.RequiredPollAfter[1]);
        a.RequiredPollAfter[1]=200;b.RequiredPollAfter[1]=150;b.RequiredPollAfter[2]=300;
        c.MergeParallelBranches(new[]{a,b},new[]{ExecutionStatus.Completed,ExecutionStatus.Completed},new Exception?[]{null,null});
        Assert.Equal(200,c.RequiredPollAfter[1]);Assert.Equal(300,c.RequiredPollAfter[2]);
    }
    [Fact] public async Task FreshnessBarrier_WaitsForPollStartedAfterWrite_WithoutDirectRead()
    {
        var c=Context();c.RequiredPollAfter[1]=200;c.RegisterState.Update(1,10,1,startedAt:100);
        var read=ModbusRegisterReadHelper.ReadAsync(c,null,1,10,false,default);
        Assert.False(read.IsCompleted);c.RegisterState.Update(1,10,2,startedAt:250);
        var result=await read.WaitAsync(TimeSpan.FromSeconds(2));Assert.True(result.Success);Assert.Equal(2,result.Value);
    }
    [Fact] public async Task WaitUntil_RespectsOwnTimeoutWhileWaitingForFreshPolling()
    {
        var c=Context();c.RequiredPollAfter[1]=200;c.RegisterState.Update(1,10,1,startedAt:100);
        var timer=Stopwatch.StartNew();
        Assert.Equal(StepResult.False,await new WaitUntilStep(1,10,1,100,NullLogger.Instance).ExecuteAsync(c,default));
        Assert.InRange(timer.ElapsedMilliseconds,70,1500);
    }
    [Fact] public async Task ConfiguredHardware_RejectsOldCacheEvenWithoutWriteBarrier()
    {
        var c=Context();c.RequireFreshMonitoring=true;c.MaxRegisterAgeMs=100;c.RegisterState.Update(1,10,1,startedAt:Stopwatch.GetTimestamp()-Stopwatch.Frequency);
        var result=await ModbusRegisterReadHelper.ReadAsync(c,null,1,10,false,default,freshnessTimeoutMs:50);
        Assert.False(result.Success);
        c.RegisterState.Update(1,10,2);
        Assert.Equal(2,(await ModbusRegisterReadHelper.ReadAsync(c,null,1,10,false,default)).Value);
    }
    [Fact] public void InvalidHardware_IsRejected_AndSameAddressInDifferentAreasIsValid()
    {
        var config=new HardwareConfiguration{Devices=new(){new(){Registers=new(){new(){Address=1,Name="h"},new(){Address=1,Area=ModbusArea.Coil,Name="c",Writable=true}}}}};config.Validate();
        config.Devices[0].Registers[1].Area=ModbusArea.Holding;Assert.Throws<InvalidOperationException>(config.Validate);
        config.Devices[0].Registers[1].Area=ModbusArea.Input;Assert.Throws<InvalidOperationException>(config.Validate);
    }
    [Fact] public void MalformedBoardAndImage_AreRejectedBeforeDecoding()
    {
        Assert.Throws<InvalidOperationException>(()=>new BoardDesign{Regions=new(){new(){X=double.NaN}}}.Validate());
        Assert.Throws<InvalidOperationException>(()=>BoardImage.Validate(new byte[]{1,2,3}));
        Assert.Throws<InvalidOperationException>(()=>new BoardDesign{Regions=new(){new(){Id="a"},new(){Id="a"}}}.Validate());
    }
    [Fact] public void LocalReport_DropsCredential_ButPreservesTestData()
    {
        var raw="test_result=true=1\r\nsession=true=secret-test-token\r\nserial_num=true=123\r\n";
        var safe=SendTestReportStep.RedactLocalReport(raw);Assert.DoesNotContain("secret-test-token",safe);Assert.Contains("serial_num=true=123",safe);
    }
    [Fact] public void NativeProcessArguments_AreNotShellCommands()
    {
        var info=ExternalProgramRunner.CreateStartInfo("tool",new[]{"a b","$(ignored)","x;y"},"");
        Assert.False(info.UseShellExecute);Assert.Equal(new[]{"a b","$(ignored)","x;y"},info.ArgumentList);
    }
    [Fact] public async Task ProcessRunner_CapturesOutputExitCodeAndTimeout()
    {
        if(OperatingSystem.IsWindows())return;
        var runner=new ExternalProgramRunner();var result=await runner.RunAsync("/bin/sh",new[]{"-c","printf hello; printf diagnostic >&2; exit 7"},"",3000,default);
        Assert.Equal(7,result.ExitCode);Assert.Equal("hello",result.Output);Assert.Equal("diagnostic",result.Error);
        await Assert.ThrowsAsync<TimeoutException>(()=>runner.RunAsync("/bin/sh",new[]{"-c","sleep 20"},"",150,default));
    }
    sealed class BadConsole:ISerialConsoleSession {public Task<string> ExchangeAsync(string p,int b,string c,string t,int q,int timeout,CancellationToken ct)=>throw new TimeoutException("offline test");}
}
