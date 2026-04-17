using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.ViewModels.NodifyVM;
using Tmds.DBus.Protocol;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class CheckRegisterRangeNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private byte slaveId;

        [ObservableProperty]
        private ushort address;

        [ObservableProperty]
        private int min;

        [ObservableProperty]
        private int max;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public CheckRegisterRangeNodeViewModel()
        {
            Title = "Check Register Range";

            In = new ConnectorViewModel { Title = "In" };
            TrueOut = new ConnectorViewModel { Title = "True" };
            FalseOut = new ConnectorViewModel { Title = "False" };

            AddInput(In);
            AddOutput(TrueOut);
            AddOutput(FalseOut);
        }

        public ITestStep CreateStep()
        {
            return new CheckRegisterRangeStep(SlaveId, Address, Min, Max);
        }
    }
}