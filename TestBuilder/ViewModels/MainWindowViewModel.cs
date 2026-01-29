using CommunityToolkit.Mvvm.Input;
using Nodify;
using System.Collections.ObjectModel;

namespace TestBuilder.ViewModels

{
    public class MainWindowViewModel : ViewModelBase
    {
        public ObservableCollection<NodeViewModel> Nodes { get; } = new();
        public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

        public PendingConnectionViewModel PendingConnection { get; }


        public MainWindowViewModel()
        {
            PendingConnection = new PendingConnectionViewModel(this);

            var node1 = new NodeViewModel
            {
                Title = "Welcome",
                Input = { new ConnectorViewModel { Title = "In" } },
                Output = { new ConnectorViewModel { Title = "Out" } }
            };

            var node2 = new NodeViewModel
            {
                Title = "To Nodify",
                Input = { new ConnectorViewModel { Title = "In" } }
            };

            Nodes.Add(node1);
            Nodes.Add(node2);
        }

        public void Connect(ConnectorViewModel source, ConnectorViewModel target)
        {
            Connections.Add(new ConnectionViewModel(source, target));
        }
    }
}
