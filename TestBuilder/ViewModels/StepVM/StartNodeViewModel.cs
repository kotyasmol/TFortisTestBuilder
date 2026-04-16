using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public class StartNodeViewModel : NodeViewModel
    {
        public StartNodeViewModel()
        {
            Title = "Start";
            Output.Add(new ConnectorViewModel { Title = "Out" });
        }

        public ITestStep CreateStep()
        {
            return new StartStep();
        }
    }
}
