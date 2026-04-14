using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.Services.Modbus;
using TestBuilder.ViewModels.NodifyVM;
using Tmds.DBus.Protocol;

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

        public ModbusWriteNodeViewModel()
        {
            Title = "Write Register";

            Input.Add(new ConnectorViewModel { Title = "In" });
            Output.Add(new ConnectorViewModel { Title = "Out" });
        }

        public ITestStep CreateStep(IModbusService modbusService)
        {
            return new ModbusWriteStep(modbusService, SlaveId, Address, Value);
        }
    }
}
