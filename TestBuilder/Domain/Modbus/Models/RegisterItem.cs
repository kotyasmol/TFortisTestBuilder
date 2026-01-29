using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Modbus.Models
{
    /// <summary>
    /// Модель одного регистра устройства.
    /// </summary>
    public class RegisterItem
    {
        /// <summary>Адрес регистра на слейве</summary>
        public ushort Address { get; set; }

        /// <summary>Название регистра (для UI)</summary>
        public string Name { get; set; }

        /// <summary>Текущее значение регистра</summary>
        public ushort Value { get; set; }

        /// <summary>Признак доступности записи</summary>
        public bool IsReadOnly { get; set; }

        /// <summary>Категория/группа регистра (для UI)</summary>
        public string Category { get; set; }
    }
}
