using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TestBuilder.Domain.Modbus.Models
{
    /// <summary>
    /// Модель одного регистра устройства.
    /// </summary>
    public class RegisterItem : INotifyPropertyChanged
    {
        private ushort _value;

        /// <summary>Адрес регистра на слейве</summary>
        public int Address { get; set; }

        /// <summary>Название регистра (для UI)</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Текущее значение регистра</summary>
        public ushort Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Признак доступности записи</summary>
        public bool IsReadOnly { get; set; }

        /// <summary>Категория/группа регистра (для UI)</summary>
        public string Category { get; set; } = string.Empty;

        public override string ToString() => $"{Address} — {Name}";

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        #endregion
    }
}