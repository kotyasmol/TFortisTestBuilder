using TestBuilder.ViewModels;
namespace TestBuilder.Tests.Views;
public class StandPortSelectionTests
{
    [Fact]
    public void ExplicitPortDoesNotFallBackToUnrelatedPort()
    {
        var ports = new[] { "COM1", "COM3" };
        Assert.Equal(new[] { "COM3" }, TestViewModel.GetStandPortCandidates(null, ports, " com3 "));
        Assert.Empty(TestViewModel.GetStandPortCandidates(null, ports, "COM9"));
        Assert.Equal(ports, TestViewModel.GetStandPortCandidates(null, ports, ""));
    }
}
