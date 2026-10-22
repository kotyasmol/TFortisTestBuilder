using System.Collections.ObjectModel;
using TestBuilder.Services.Graph;
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
        public System.DateTimeOffset? UpdatedAt { get; set; }

        private TestBuilder.Domain.Modbus.HardwareConfiguration? _hardware;
        private TestBuilder.Domain.Modbus.HardwareConfiguration? _referenceHardware;
        public TestBuilder.Domain.Modbus.HardwareConfiguration? Hardware
        {
            get => _hardware;
            set { _hardware = value; ApplyReferenceHardware(value, new()); }
        }

        public GraphWorkspaceViewModel()
        {
            Nodes.CollectionChanged += (_, e) =>
            {
                if (e.NewItems == null) return;
                foreach (NodeViewModel node in e.NewItems)
                    ApplyNodeReference(node, _referenceHardware, new() { this });
            };
        }

        private void ApplyReferenceHardware(TestBuilder.Domain.Modbus.HardwareConfiguration? hardware,
            System.Collections.Generic.HashSet<GraphWorkspaceViewModel> visited)
        {
            if (!visited.Add(this)) return;
            _referenceHardware = hardware;
            foreach (var node in Nodes) ApplyNodeReference(node, hardware, visited);
        }

        private static void ApplyNodeReference(NodeViewModel node,
            TestBuilder.Domain.Modbus.HardwareConfiguration? hardware,
            System.Collections.Generic.HashSet<GraphWorkspaceViewModel> visited)
        {
            node.RegisterReferenceHardware = hardware;
            if (node is ICompositeNodeViewModel composite)
                composite.BodyGraph.ApplyReferenceHardware(hardware, visited);
        }
        public TestBuilder.Models.BoardDesign? Board { get; set; }
        public string ProductKind { get; set; } = "Switch";
        public string? DeviceModel { get; set; }
        public string? DeviceModelId { get; set; }

        public string? ConfigurationName { get; set; }

        public bool IsBodyGraph { get; set; }

        public bool UsesBodyBoundaryNodes { get; set; }

        public UndoRedoManager UndoRedo { get; internal set; } = new();

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
