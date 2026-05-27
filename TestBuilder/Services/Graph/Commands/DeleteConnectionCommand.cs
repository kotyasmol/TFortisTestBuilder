using System.Collections.ObjectModel;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.Services.Graph.Commands
{
    public class DeleteConnectionCommand : IGraphCommand
    {
        private readonly ObservableCollection<ConnectionViewModel> _connections;
        private readonly ConnectionViewModel _connection;

        public DeleteConnectionCommand(ObservableCollection<ConnectionViewModel> connections, ConnectionViewModel connection)
        {
            _connections = connections;
            _connection = connection;
        }

        public void Execute() => _connections.Remove(_connection);
        public void Undo() => _connections.Add(_connection);
    }
}
