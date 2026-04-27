using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    /// <summary>
    /// Составная нода цикла по диапазону Modbus slave-устройств.
    /// Снаружи выглядит как один блок, внутри содержит отдельный редактируемый граф действий.
    /// </summary>
    public partial class ForEachSlaveNodeViewModel : NodeViewModel, ICompositeNodeViewModel
    {
        [ObservableProperty]
        private byte fromSlaveId = 1;

        [ObservableProperty]
        private byte toSlaveId = 20;

        [ObservableProperty]
        private byte step = 1;

        [ObservableProperty]
        private bool stopOnError = true;

        public ConnectorViewModel In { get; }

        public ConnectorViewModel SuccessOut { get; }

        public ConnectorViewModel ErrorOut { get; }

        public GraphWorkspaceViewModel BodyGraph { get; } = new()
        {
            Title = "Тело цикла For Slaves",
            IsBodyGraph = true
        };

        public ForEachSlaveNodeViewModel()
        {
            Title = "For Slaves";

            In = new ConnectorViewModel { Title = "In" };
            SuccessOut = new ConnectorViewModel { Title = "Success" };
            ErrorOut = new ConnectorViewModel { Title = "Error" };

            AddInput(In);
            AddOutput(SuccessOut);
            AddOutput(ErrorOut);

            EnsureDefaultBodyNodes();
        }

        public void EnsureDefaultBodyNodes()
        {
            if (BodyGraph.Nodes.Count > 0)
                return;

            BodyGraph.Nodes.Add(new BodyStartNodeViewModel
            {
                Location = new Point(100, 120)
            });

            BodyGraph.Nodes.Add(new BodyEndNodeViewModel
            {
                Location = new Point(560, 120)
            });
        }
    }
}
