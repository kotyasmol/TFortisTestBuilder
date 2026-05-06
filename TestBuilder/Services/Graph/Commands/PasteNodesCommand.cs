using System.Collections.Generic;
using System.Collections.ObjectModel;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Services.Graph.Commands
{
    public class PasteNodesCommand : IGraphCommand
    {
        private readonly ObservableCollection<NodeViewModel> _nodes;
        private readonly ObservableCollection<ConnectionViewModel> _connections;
        private readonly List<NodeViewModel> _pastedNodes;
        private readonly List<ConnectionViewModel> _pastedConnections;

        public PasteNodesCommand(
            ObservableCollection<NodeViewModel> nodes,
            ObservableCollection<ConnectionViewModel> connections,
            List<NodeViewModel> pastedNodes,
            List<ConnectionViewModel> pastedConnections)
        {
            _nodes = nodes;
            _connections = connections;
            _pastedNodes = pastedNodes;
            _pastedConnections = pastedConnections;
        }

        public void Execute()
        {
            foreach (var node in _pastedNodes)
                _nodes.Add(node);

            foreach (var conn in _pastedConnections)
                _connections.Add(conn);
        }

        public void Undo()
        {
            foreach (var conn in _pastedConnections)
                _connections.Remove(conn);

            foreach (var node in _pastedNodes)
                _nodes.Remove(node);
        }
    }
}
