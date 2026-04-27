using System.Collections.ObjectModel;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.Graphs
{
    /// <summary>
    /// Рабочая область графа.
    /// Один экземпляр используется для основного графа, другие — для тел составных нод.
    /// </summary>
    public sealed class GraphWorkspaceViewModel
    {
        public string Title { get; set; } = "Граф";

        public bool IsBodyGraph { get; set; }

        public ObservableCollection<NodeViewModel> Nodes { get; } = new();

        public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

        public ObservableCollection<NodeViewModel> SelectedNodes { get; } = new();

        public void Clear()
        {
            foreach (var node in Nodes)
            {
                foreach (var connector in node.Input)
                    connector.IsConnected = false;

                foreach (var connector in node.Output)
                    connector.IsConnected = false;
            }

            Connections.Clear();
            SelectedNodes.Clear();
            Nodes.Clear();
        }
    }
}
