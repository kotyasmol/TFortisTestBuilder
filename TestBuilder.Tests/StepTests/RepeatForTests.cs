using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Monitoring;
using TestBuilder.Domain.Steps;
using TestBuilder.Tests.Support;
namespace TestBuilder.Tests.StepTests;
public class RepeatForTests
{
    static ForEachSlaveStep Repeat(ITestStep step,int count=3,bool until=false)=>new(1,20,1,true,new(new TestNode(step)),NullLogger.Instance,true,count,"attempt",until,0);
    [Fact] public async Task RepeatsSameSlaveAndRestoresNestedCounter()
    {
        var c=new TestContext(new RegisterState()){CurrentSlaveId=9};c.SetVariable("attempt",99);var seen=new List<int>();
        var f=Repeat(new ActionStep((ctx,ct)=>{Assert.Equal((byte)9,ctx.CurrentSlaveId);seen.Add(ctx.GetVariable<int>("attempt"));return Task.FromResult(StepResult.Next);}));
        Assert.Equal(StepResult.Next,await f.ExecuteAsync(c,default));Assert.Equal(new[]{1,2,3},seen);Assert.Equal(99,c.GetVariable<int>("attempt"));Assert.Equal((byte)9,c.CurrentSlaveId);
    }
    [Fact] public async Task RetryDiscardsFailedAttemptReportAndStopsAfterSuccess()
    {
        var c=new TestContext(new RegisterState());c.AddReportEntry("before",true,"");var attempts=0;
        var f=Repeat(new ActionStep((ctx,ct)=>{var success=++attempts==2;ctx.AddReportEntry("check",success,"");ctx.HasCriticalError=!success;return Task.FromResult(success?StepResult.Next:StepResult.False);}),5,true);
        Assert.Equal(StepResult.Next,await f.ExecuteAsync(c,default));Assert.Equal(2,attempts);Assert.All(c.ReportEntries,r=>Assert.True(r.IsSuccess));Assert.False(c.HasCriticalError);Assert.False(c.Variables.ContainsKey("attempt"));
    }
    [Fact] public async Task ExhaustedRetryAndReportedFailuresCannotReturnSuccess()
    {
        var c=new TestContext(new RegisterState());var attempts=0;
        var f=Repeat(new ActionStep((ctx,ct)=>{attempts++;ctx.AddReportEntry("failed",false,"");return Task.FromResult(StepResult.Next);}),2,true);
        Assert.Equal(StepResult.False,await f.ExecuteAsync(c,default));Assert.Equal(2,attempts);Assert.Single(c.ReportEntries);
    }
    [Fact] public async Task CancellationRestoresCounter()
    {
        var c=new TestContext(new RegisterState()){CurrentSlaveId=7};c.SetVariable("attempt",5);using var cts=new CancellationTokenSource();
        var f=Repeat(new ActionStep((ctx,ct)=>{cts.Cancel();ct.ThrowIfCancellationRequested();return Task.FromResult(StepResult.Next);}));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.ExecuteAsync(c,cts.Token));Assert.Equal(5,c.GetVariable<int>("attempt"));Assert.Equal((byte)7,c.CurrentSlaveId);
    }
    sealed class ActionStep(Func<TestContext,CancellationToken,Task<StepResult>> action):ITestStep{public Task<StepResult> ExecuteAsync(TestContext c,CancellationToken ct)=>action(c,ct);}
}
