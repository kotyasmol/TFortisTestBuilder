using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TestBuilder.Domain.Modbus.Models;

namespace TestBuilder.Services
{
    public class SlaveRegistry : INotifyPropertyChanged
    {
        public static readonly SlaveRegistry Instance = new();
        private SlaveRegistry() { }

        // A node's callback survives only while the node is owned by a graph or its history.
        private readonly ConditionalWeakTable<object, Action> _observers = new();

        internal void Observe(object owner, Action onChanged) => _observers.Add(owner, onChanged);
        internal void StopObserving(object owner) => _observers.Remove(owner);

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
                NotifyObservers();
            }
        }

        public void SyncSlaves(ObservableCollection<SlaveModelBase> source)
        {
            if (Slaves.SequenceEqual(source)) return;
            Slaves.Clear();
            foreach (var s in source)
                Slaves.Add(s);
            NotifyObservers();
        }

        private void NotifyObservers()
        {
            foreach (var observer in _observers) observer.Value();
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
