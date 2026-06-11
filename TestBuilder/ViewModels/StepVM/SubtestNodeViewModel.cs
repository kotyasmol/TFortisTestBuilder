using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;
using TestBuilder.ViewModels.NodifyVM;

namespace TestBuilder.ViewModels.StepVM
{
    public partial class SubtestNodeViewModel : CompositeNodeViewModel
    {
        [ObservableProperty]
        private string name = "Подтест";

        [ObservableProperty]
        private string description = string.Empty;

        [ObservableProperty]
        private bool isEnabled = true;

        [ObservableProperty]
        private bool stopOnError = true;

        public ConnectorViewModel In { get; }

        public ConnectorViewModel SuccessOut { get; }

        public ConnectorViewModel ErrorOut { get; }

        public SubtestNodeViewModel()
        {
            Title = Name;
            BodyGraph.Title = Name;

            In = new ConnectorViewModel { Title = "In" };
            SuccessOut = new ConnectorViewModel { Title = "Success" };
            ErrorOut = new ConnectorViewModel { Title = "Error" };

            AddInput(In);
            AddOutput(SuccessOut);
            AddOutput(ErrorOut);

            EnsureDefaultBodyNodes();
        }

        partial void OnNameChanged(string value)
        {
            var title = string.IsNullOrWhiteSpace(value) ? "Подтест" : value;
            Title = title;
            BodyGraph.Title = title;
        }

        public override void EnsureDefaultBodyNodes()
        {
            if (!BodyGraph.Nodes.Any(n => n is StartNodeViewModel))
            {
                BodyGraph.Nodes.Add(new StartNodeViewModel
                {
                    Location = new Point(100, 120)
                });
            }

            if (!BodyGraph.Nodes.Any(n => n is EndNodeViewModel))
            {
                BodyGraph.Nodes.Add(new EndNodeViewModel
                {
                    Location = new Point(560, 120)
                });
            }
        }

        public override NodeViewModel Clone() => new SubtestNodeViewModel
        {
            Name = Name,
            Description = Description,
            IsEnabled = IsEnabled,
            StopOnError = StopOnError
        };
    }
}
