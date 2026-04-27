using Avalonia;
using Avalonia.Media;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services;

namespace TestBuilder.ViewModels.NodifyVM
{
    public partial class NodeViewModel : ObservableObject
    {
        private static readonly IBrush DefaultBorderBrush =
            new SolidColorBrush(Color.FromRgb(99, 102, 241));

        private static readonly IBrush ExecutingBorderBrush =
            new SolidColorBrush(Color.FromRgb(255, 214, 10));

        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private Point location;
        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private bool isExecuting;

        public ObservableCollection<ConnectorViewModel> Input { get; } = new();
        public ObservableCollection<ConnectorViewModel> Output { get; } = new();

        // Список слейвов для ComboBox в нодах
        public ObservableCollection<SlaveModelBase> AvailableSlaves { get; } = new();
        public bool IsConnected => SlaveRegistry.Instance.IsConnected;

        public IBrush ExecutionBorderBrush =>
            IsExecuting ? ExecutingBorderBrush : DefaultBorderBrush;

        public Thickness ExecutionBorderThickness =>
            IsExecuting ? new Thickness(5) : new Thickness(2);

        partial void OnIsExecutingChanged(bool value)
        {
            OnPropertyChanged(nameof(ExecutionBorderBrush));
            OnPropertyChanged(nameof(ExecutionBorderThickness));
        }

        protected NodeViewModel()
        {
            SlaveRegistry.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SlaveRegistry.IsConnected))
                {
                    OnPropertyChanged(nameof(IsConnected));
                    SyncSlaves();
                }
            };

            if (SlaveRegistry.Instance.Slaves.Count > 0)
                SyncSlaves();
        }

        public void SyncSlaves()
        {
            AvailableSlaves.Clear();
            foreach (var s in SlaveRegistry.Instance.Slaves)
                AvailableSlaves.Add(s);
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