using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Specialized;
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

        [ObservableProperty]
        private int stepCount;

        [ObservableProperty]
        private int currentStepIndex;

        [ObservableProperty]
        private string progressText = string.Empty;

        public bool HasProgress => !string.IsNullOrWhiteSpace(ProgressText);

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
            BodyGraph.Nodes.CollectionChanged += OnBodyGraphNodesChanged;
            RefreshStepCount();
        }

        partial void OnNameChanged(string value)
        {
            var title = string.IsNullOrWhiteSpace(value) ? "Подтест" : value;
            Title = title;
            BodyGraph.Title = title;
        }

        partial void OnProgressTextChanged(string value)
        {
            OnPropertyChanged(nameof(HasProgress));
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

            RefreshStepCount();
        }

        public void RefreshStepCount()
        {
            StepCount = BodyGraph.Nodes.Count(IsCountedStep);
        }

        public void BeginProgress()
        {
            RefreshStepCount();
            CurrentStepIndex = 0;
            ProgressText = StepCount == 0
                ? "\u041D\u0435\u0442 \u0448\u0430\u0433\u043E\u0432"
                : $"0/{StepCount}";
        }

        public void UpdateProgress(NodeViewModel node)
        {
            if (!BodyGraph.Nodes.Contains(node) || !IsCountedStep(node))
                return;

            RefreshStepCount();
            CurrentStepIndex = CurrentStepIndex >= StepCount
                ? StepCount
                : CurrentStepIndex + 1;
            ProgressText = $"{CurrentStepIndex}/{StepCount}: {node.Title}";
        }

        public void EndProgress()
        {
            CurrentStepIndex = 0;
            ProgressText = string.Empty;
        }

        private void OnBodyGraphNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshStepCount();
        }

        private static bool IsCountedStep(NodeViewModel node) =>
            node is not StartNodeViewModel &&
            node is not EndNodeViewModel &&
            node is not BodyStartNodeViewModel &&
            node is not BodyEndNodeViewModel;

        public override NodeViewModel Clone() => new SubtestNodeViewModel
        {
            Name = Name,
            Description = Description,
            IsEnabled = IsEnabled,
            StopOnError = StopOnError
        };
    }
}
