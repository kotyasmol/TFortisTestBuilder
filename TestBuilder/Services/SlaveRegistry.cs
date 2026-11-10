using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using TestBuilder.Domain.Modbus.Models;

namespace TestBuilder.Services
{
    public class SlaveRegistry : INotifyPropertyChanged
    {
        public static readonly SlaveRegistry Instance = new();
        private SlaveRegistry() { }

        // A node's callback survives only while the node is owned by a graph or its history.
        private readonly ConditionalWeakTable<object, Action> _connectionObservers = new();

        internal void ObserveConnection(object owner, Action onChanged) => _connectionObservers.Add(owner, onChanged);
        internal void StopObservingConnection(object owner) => _connectionObservers.Remove(owner);

        public ObservableCollection<SlaveModelBase> Slaves { get; } = new();

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set
            {
                if (_isConnected == value) return;
                _isConnected = value;
                OnPropertyChanged();
                foreach (var observer in _connectionObservers)
                    observer.Value();
            }
        }

        public void SyncSlaves(ObservableCollection<SlaveModelBase> source)
        {
            Slaves.Clear();
            foreach (var s in source)
                Slaves.Add(s);
        }

        public void NotifyConnected(bool isConnected)
        {
            IsConnected = isConnected;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
