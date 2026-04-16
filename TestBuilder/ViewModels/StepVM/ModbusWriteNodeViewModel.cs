using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class ModbusWriteNodeViewModel : NodeViewModel
    {
        [ObservableProperty]
        private byte slaveId;

        [ObservableProperty]
        private ushort address;

        [ObservableProperty]
        private ushort value;

        public ConnectorViewModel In { get; }
        public ConnectorViewModel TrueOut { get; }
        public ConnectorViewModel FalseOut { get; }

        public ModbusWriteNodeViewModel()
        {
            Title = "Write Register";

            In = new ConnectorViewModel { Title = "In" };
            TrueOut = new ConnectorViewModel { Title = "True" };
            FalseOut = new ConnectorViewModel { Title = "False" };

            AddInput(In);
            AddOutput(TrueOut);
            AddOutput(FalseOut);
        }

        public ITestStep CreateStep(IModbusService modbusService)
        {
            return new ModbusWriteStep(modbusService, SlaveId, Address, Value);
        }
    }
}