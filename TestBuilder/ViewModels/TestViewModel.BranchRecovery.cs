using System.Collections.Generic;
using System.Linq;
using TestBuilder.Services.Graph;
using TestBuilder.Services.Graph.Commands;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    internal IReadOnlyList<NodeViewModel> RecoveryCandidates(ConnectionViewModel edge) =>
        BranchRecoveryValidation.Candidates(CurrentGraph, edge);

    internal void SetBranchRecovery(ConnectionViewModel edge, NodeViewModel? source)
    {
        if (!CanEditGraph || !Connections.Contains(edge)) return;
        if (source != null && !RecoveryCandidates(edge).Contains(source)) return;
        _history.Execute(CurrentGraph, new SetBranchRecoveryCommand(edge, source));
        StatusMessage = source == null ? "Подтверждение устранения ошибки отключено для связи." :
            $"Успешный переход подтверждает устранение ошибки «{source.DisplayTitle}».";
    }
}
