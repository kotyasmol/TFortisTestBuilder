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
        public ushort Address { get; set; }

        /// <summary>Название регистра (для UI)</summary>
        public string Name { get; set; }

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
        public string Category { get; set; }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        #endregion
    }
}
