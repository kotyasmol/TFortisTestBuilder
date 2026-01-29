using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TestBuilder.Domain.Execution;

namespace TestBuilder.Domain.Steps
{
    /// <summary>
    /// Шаг теста, который записывает указанное значение в регистр тестового контекста.
    /// Используется для подготовки состояния регистров перед проверками или другими шагами.
    /// </summary>
    public class RegisterWriteStep : ITestStep
    {
        /// <summary>
        /// Имя регистра, в который будет записано значение.
        /// </summary>
        public string RegisterName { get; }

        /// <summary>
        /// Значение, которое будет записано в регистр.
        /// </summary>
        public int Value { get; }

        /// <summary>
        /// Создаёт новый шаг записи в регистр.
        /// </summary>
        /// <param name="registerName">Имя регистра для записи.</param>
        /// <param name="value">Значение для записи.</param>
        public RegisterWriteStep(string registerName, int value)
        {
            RegisterName = registerName;
            Value = value;
        }

        /// <summary>
        /// Выполняет шаг: записывает значение в словарь регистров контекста теста.
        /// </summary>
        /// <param name="context">Контекст теста, содержащий состояние всех регистров.</param>
        /// <param name="cancellationToken">Токен отмены для прерывания выполнения.</param>
        /// <returns>
        /// Всегда возвращает <see cref="StepResult.Next"/>, чтобы Executor продолжил выполнение следующего шага.
        /// </returns>
        public Task<StepResult> ExecuteAsync(TestContext context, CancellationToken cancellationToken)
        {
            
            context.RegisterState.Update(RegisterName, Value);

            // Всегда продолжаем
            return Task.FromResult(StepResult.Next);
        }
    }
}
