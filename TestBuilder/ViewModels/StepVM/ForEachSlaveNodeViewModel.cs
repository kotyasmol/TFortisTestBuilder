using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;
using TestBuilder.ViewModels.Graphs;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    /// <summary>
    /// Составная нода цикла по диапазону Modbus slave-устройств.
    /// Снаружи выглядит как один блок, внутри содержит отдельный редактируемый граф действий.
    /// </summary>
    public partial class ForEachSlaveNodeViewModel : CompositeNodeViewModel
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

        public ForEachSlaveNodeViewModel()
        {
            Title = "Цикл For";
            BodyGraph.Title = "Тело цикла For Slaves";
            BodyGraph.UsesBodyBoundaryNodes = true;

            In = new ConnectorViewModel { Title = "In" };
            SuccessOut = new ConnectorViewModel { Title = "True" };
            ErrorOut = new ConnectorViewModel { Title = "False" };

            AddInput(In);
            AddOutput(SuccessOut);
            AddOutput(ErrorOut);

            EnsureDefaultBodyNodes();
        }

        public override void EnsureDefaultBodyNodes()
        {
            if (!BodyGraph.Nodes.Any(n => n is BodyStartNodeViewModel))
            {
                BodyGraph.Nodes.Add(new BodyStartNodeViewModel
                {
                    Location = new Point(100, 120)
                });
            }

            if (!BodyGraph.Nodes.Any(n => n is BodyEndNodeViewModel))
            {
                BodyGraph.Nodes.Add(new BodyEndNodeViewModel
                {
                    Location = new Point(560, 120)
                });
            }
        }

        public override NodeViewModel Clone() => new ForEachSlaveNodeViewModel
        {
            FromSlaveId = FromSlaveId,
            ToSlaveId = ToSlaveId,
            Step = Step,
            StopOnError = StopOnError
        };
    }
}
