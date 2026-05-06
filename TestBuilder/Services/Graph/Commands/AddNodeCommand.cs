using System.Collections.ObjectModel;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Services.Graph.Commands
{
    public class AddNodeCommand : IGraphCommand
    {
        private readonly ObservableCollection<NodeViewModel> _nodes;
        private readonly NodeViewModel _node;

        public AddNodeCommand(ObservableCollection<NodeViewModel> nodes, NodeViewModel node)
        {
            _nodes = nodes;
            _node = node;
        }

        public void Execute() => _nodes.Add(_node);
        public void Undo() => _nodes.Remove(_node);
    }
}
