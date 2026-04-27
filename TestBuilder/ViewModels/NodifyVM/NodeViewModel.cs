using Avalonia;
using Avalonia.Media;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestBuilder.ViewModels.NodifyVM
{
    public partial class NodeViewModel : ObservableObject
    {
        private static readonly IBrush DefaultBorderBrush =
            new SolidColorBrush(Color.FromRgb(99, 102, 241));

        private static readonly IBrush ExecutingBorderBrush =
            new SolidColorBrush(Color.FromRgb(255, 214, 10));

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private Point location;

        [ObservableProperty]
        private bool isSelected;

        [ObservableProperty]
        private bool isExecuting;

        public ObservableCollection<ConnectorViewModel> Input { get; } = new();

        public ObservableCollection<ConnectorViewModel> Output { get; } = new();

        public IBrush ExecutionBorderBrush =>
            IsExecuting ? ExecutingBorderBrush : DefaultBorderBrush;

        public Thickness ExecutionBorderThickness =>
            IsExecuting ? new Thickness(5) : new Thickness(2);

        partial void OnIsExecutingChanged(bool value)
        {
            OnPropertyChanged(nameof(ExecutionBorderBrush));
            OnPropertyChanged(nameof(ExecutionBorderThickness));
        }

        public void AddInput(ConnectorViewModel connector)
        {
            connector.Parent = this;
            Input.Add(connector);
        }

        public void AddOutput(ConnectorViewModel connector)
        {
            connector.Parent = this;
            Output.Add(connector);
        }
    }
}