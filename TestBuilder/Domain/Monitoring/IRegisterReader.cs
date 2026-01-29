using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestBuilder.Domain.Monitoring
{
    /// <summary>
    /// Абстракция источника регистров.
    /// Используется мониторингом и шагами теста.
    /// Не знает о Modbus, UI или конкретных устройствах.
    /// </summary>
    public interface IRegisterReader
    {
        /// <summary>
        /// Асинхронно читает значение регистра.
        /// </summary>
        /// <param name="registerName">
        /// Логическое имя регистра (например "CR2032", "V12", "Temp").
        /// </param>
        /// <param name="cancellationToken">
        /// Токен отмены теста.
        /// </param>
        Task<int> ReadAsync(
            string registerName,
            CancellationToken cancellationToken);
    }
}
