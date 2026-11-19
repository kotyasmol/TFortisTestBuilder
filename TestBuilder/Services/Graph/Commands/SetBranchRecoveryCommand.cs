using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Services.Graph.Commands;

internal sealed class SetBranchRecoveryCommand(ConnectionViewModel connection, NodeViewModel? target) : IGraphCommand
{
    private readonly NodeViewModel? _before = connection.RecoversFailureOf;
    public void Execute() => connection.RecoversFailureOf = target;
    public void Undo() => connection.RecoversFailureOf = _before;
}
