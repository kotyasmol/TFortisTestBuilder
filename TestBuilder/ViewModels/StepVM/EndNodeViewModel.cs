using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;
using TestBuilder.Domain.Steps;
using TestBuilder.ViewModels.NodifyVM;
using TestBuilder.Services.Logging;

namespace TestBuilder.ViewModels.StepVM
{
    public class EndNodeViewModel : NodeViewModel
    {
        public EndNodeViewModel()
        {
            Title = "End";
            AddInput(new ConnectorViewModel { Title = "In" });
        }

        public ITestStep CreateStep(ILogger logger)
        {
            return new EndStep(logger);
        }
    }
}
