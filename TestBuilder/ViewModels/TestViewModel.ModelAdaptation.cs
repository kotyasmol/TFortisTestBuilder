using System.Linq;
using TestBuilder.Services.Graph;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels;

public partial class TestViewModel
{
    private readonly ProfileNodePresentation _nodePresentation;
    public bool HasModelReview => (_nodePresentation?.ReviewCount ?? 0) > 0;
    public string ModelReviewSummary => $"Проверить ноды: {_nodePresentation?.ReviewCount ?? 0}";
    public string ModelReviewDetails => _nodePresentation?.Details ?? "";
    private void NotifyModelReview()
    {
        OnPropertyChanged(nameof(HasModelReview));
        OnPropertyChanged(nameof(ModelReviewSummary));
        OnPropertyChanged(nameof(ModelReviewDetails));
    }
    private void ConfirmNodeModelReview(NodeViewModel node)
    {
        if (!CanEditGraph || !node.HasModelReview) return;
        var children = node is ICompositeNodeViewModel composite ? ProfileModelAdaptation.All(composite.BodyGraph) : Enumerable.Empty<NodeViewModel>();
        var notes = children.Prepend(node).Where(n => n.ModelReview != null).Select(n => (Node: n, Note: n.ModelReview)).ToArray();
        _history.Execute(CurrentGraph, new ProfileMetadataCommand(() =>
        {
            using var batch = _nodePresentation.Defer();
            foreach (var entry in notes) entry.Node.ModelReview = null;
        }, () =>
        {
            using var batch = _nodePresentation.Defer();
            foreach (var entry in notes) entry.Node.ModelReview = entry.Note;
        }));
    }
}
