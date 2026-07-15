using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services;

namespace TestBuilder.ViewModels.NodifyVM
{
    public partial class NodeViewModel : ObservableObject, IDisposable
    {
        private static readonly IBrush DefaultBorderBrush =
            new SolidColorBrush(Color.FromRgb(99, 102, 241));

        private static readonly IBrush ExecutingBorderBrush =
            new SolidColorBrush(Color.FromRgb(255, 214, 10));

        private static readonly IBrush ErrorBorderBrush =
            new SolidColorBrush(Color.FromRgb(239, 68, 68));

        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private Point location;
        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private bool isExecuting;
        [ObservableProperty] private bool hasExecutionError;

        public ObservableCollection<ConnectorViewModel> Input { get; } = new();
        public ObservableCollection<ConnectorViewModel> Output { get; } = new();

        // Список слейвов для ComboBox в нодах
        public ObservableCollection<SlaveModelBase> AvailableSlaves { get; } = new();
        public bool IsConnected => SlaveRegistry.Instance.IsConnected;
        public string HelpText => NodeHelpTextProvider.GetHelp(GetType());
        public string HelpSummary => NodeHelpTextProvider.GetSummary(GetType());
        public IReadOnlyList<string> HelpDetails => NodeHelpTextProvider.GetDetails(GetType());
        public bool HasHelpText => !string.IsNullOrWhiteSpace(HelpText);
        public bool HasHelpDetails => HelpDetails.Count > 0;

        public IBrush ExecutionBorderBrush =>
            HasExecutionError ? ErrorBorderBrush :
            IsExecuting ? ExecutingBorderBrush :
            DefaultBorderBrush;

        public Thickness ExecutionBorderThickness =>
            HasExecutionError || IsExecuting ? new Thickness(5) : new Thickness(2);

        partial void OnIsExecutingChanged(bool value)
        {
            OnPropertyChanged(nameof(ExecutionBorderBrush));
            OnPropertyChanged(nameof(ExecutionBorderThickness));
        }

        partial void OnHasExecutionErrorChanged(bool value)
        {
            OnPropertyChanged(nameof(ExecutionBorderBrush));
            OnPropertyChanged(nameof(ExecutionBorderThickness));
        }

        protected NodeViewModel()
        {
            SlaveRegistry.Instance.PropertyChanged += OnSlaveRegistryPropertyChanged;

            if (SlaveRegistry.Instance.Slaves.Count > 0)
                SyncSlaves();
        }

        private void OnSlaveRegistryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SlaveRegistry.IsConnected))
            {
                OnPropertyChanged(nameof(IsConnected));
                SyncSlaves();
            }
        }

        public void SyncSlaves()
        {
            AvailableSlaves.Clear();
            foreach (var s in SlaveRegistry.Instance.Slaves)
                AvailableSlaves.Add(s);

            // Даём подклассам возможность восстановить выбранные элементы
            OnSlavesLoaded();
        }

        /// <summary>Вызывается после загрузки слейвов — подклассы переопределяют для восстановления выборок</summary>
        protected virtual void OnSlavesLoaded() { }

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

        public virtual NodeViewModel Clone() => throw new System.NotImplementedException($"Clone() not implemented for {GetType().Name}");

        public void Dispose()
        {
            SlaveRegistry.Instance.PropertyChanged -= OnSlaveRegistryPropertyChanged;
        }
    }
}
