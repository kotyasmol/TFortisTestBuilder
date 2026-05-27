using System.Collections.Generic;
using System.Collections.ObjectModel;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Services.Graph.Commands
{
    public class DeleteNodesCommand : IGraphCommand
    {
        private readonly ObservableCollection<NodeViewModel> _nodes;
        private readonly ObservableCollection<ConnectionViewModel> _connections;
        private readonly List<NodeViewModel> _deletedNodes;
        private readonly List<ConnectionViewModel> _deletedConnections;

        public DeleteNodesCommand(
            ObservableCollection<NodeViewModel> nodes,
            ObservableCollection<ConnectionViewModel> connections,
            List<NodeViewModel> deletedNodes,
            List<ConnectionViewModel> deletedConnections)
        {
            _nodes = nodes;
            _connections = connections;
            _deletedNodes = deletedNodes;
            _deletedConnections = deletedConnections;
        }

        public void Execute()
        {
            foreach (var conn in _deletedConnections)
                _connections.Remove(conn);

            foreach (var node in _deletedNodes)
                _nodes.Remove(node);
        }

        public void Undo()
        {
            foreach (var node in _deletedNodes)
                _nodes.Add(node);

            foreach (var conn in _deletedConnections)
                _connections.Add(conn);
        }
    }
}
