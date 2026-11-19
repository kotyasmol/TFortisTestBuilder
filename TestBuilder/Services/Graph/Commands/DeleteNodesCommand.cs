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
        private readonly List<(ConnectionViewModel Connection, NodeViewModel Target)> _recoveries = new();

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
            foreach (var connection in connections)
                if (connection.RecoversFailureOf is { } target && deletedNodes.Contains(target) && !deletedConnections.Contains(connection))
                    _recoveries.Add((connection, target));
        }

        public void Execute()
        {
            foreach (var (connection, _) in _recoveries) connection.RecoversFailureOf = null;
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
            foreach (var (connection, target) in _recoveries) connection.RecoversFailureOf = target;
        }
    }
}
