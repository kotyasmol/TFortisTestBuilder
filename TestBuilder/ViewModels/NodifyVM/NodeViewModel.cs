using Avalonia;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestBuilder.ViewModels.NodifyVM
{
    public partial class NodeViewModel : ObservableObject
    {
        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private Point location;

        [ObservableProperty]
        private bool isSelected;

        public ObservableCollection<ConnectorViewModel> Input { get; } = new();
        public ObservableCollection<ConnectorViewModel> Output { get; } = new();

        public void AddInput(ConnectorViewModel connector)
        {
            connector.Parent = this;
            Input.Add(connector);
        }

        public void AddOutput(ConnectorViewModel connector)
        {
            connector.Parent = this;
            Output.Add(connector);
        }
    }
}