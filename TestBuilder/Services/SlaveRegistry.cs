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

        // Одна постоянная коллекция — никогда не подменяется
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
            }
        }

        // Копируем слейвы из SlaveManager.Slaves в нашу коллекцию
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