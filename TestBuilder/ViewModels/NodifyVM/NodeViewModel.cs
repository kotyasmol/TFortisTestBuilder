using Avalonia;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services;

namespace TestBuilder.ViewModels.NodifyVM
{
    public partial class NodeViewModel : ObservableObject
    {
        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private Point location;
        [ObservableProperty] private bool isSelected;

        public ObservableCollection<ConnectorViewModel> Input { get; } = new();
        public ObservableCollection<ConnectorViewModel> Output { get; } = new();

        public ObservableCollection<SlaveModelBase> AvailableSlaves { get; } = new();

        public bool IsConnected => SlaveRegistry.Instance.IsConnected;

        protected NodeViewModel()
        {
            // Реагируем ТОЛЬКО на IsConnected — к этому моменту Slaves уже заполнены
            // НЕ подписываемся на CollectionChanged — иначе SyncSlaves вызывается
            // 13 раз (1 Clear + 12 Add) и каждый раз сбрасывает AvailableSlaves
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