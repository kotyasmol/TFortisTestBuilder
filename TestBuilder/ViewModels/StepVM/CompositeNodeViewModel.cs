using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public abstract class CompositeNodeViewModel : NodeViewModel, ICompositeNodeViewModel
    {
        public GraphWorkspaceViewModel BodyGraph { get; } = new()
        {
            IsBodyGraph = true
        };

        public abstract void EnsureDefaultBodyNodes();
    }
}
