using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using TestBuilder.Domain.Execution;

namespace TestBuilder.ViewModels
{
    public sealed class SelfTestPageParameterViewModel
    {
        public string Name { get; }
        public string Value { get; }

        public SelfTestPageParameterViewModel(string name, string value)
        {
            Name = name;
            Value = value;
        }
    }

    public sealed class SelfTestPageViewModel : ViewModelBase, IDisposable
    {
        private readonly SelfTestPageState _state;
        private bool _disposed;
        private string _statusTitle = string.Empty;
        private string _statusDetails = string.Empty;
        private string _sourceUrl = string.Empty;
        private bool _hasParameters;

        public ObservableCollection<SelfTestPageParameterViewModel> Parameters { get; } = new();

        public string StatusTitle
        {
            get => _statusTitle;
            private set => SetProperty(ref _statusTitle, value);
        }

        public string StatusDetails
        {
            get => _statusDetails;
            private set => SetProperty(ref _statusDetails, value);
        }

        public string SourceUrl
        {
            get => _sourceUrl;
            private set
            {
                if (SetProperty(ref _sourceUrl, value))
                    OnPropertyChanged(nameof(HasSourceUrl));
            }
        }

        public bool HasSourceUrl => !string.IsNullOrWhiteSpace(SourceUrl);

        public bool HasParameters
        {
            get => _hasParameters;
            private set => SetProperty(ref _hasParameters, value);
        }

        public SelfTestPageViewModel(SelfTestPageState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _state.SnapshotChanged += OnSnapshotChanged;
            ApplySnapshot(_state.Current);
        }

        private void OnSnapshotChanged(SelfTestPageSnapshot snapshot)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                ApplySnapshot(snapshot);
                return;
            }

            Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
        }

        private void ApplySnapshot(SelfTestPageSnapshot snapshot)
        {
            if (_disposed)
                return;

            Parameters.Clear();
            foreach (var field in snapshot.Fields.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                Parameters.Add(new SelfTestPageParameterViewModel(field.Key, field.Value));
            }

            HasParameters = Parameters.Count > 0;
            SourceUrl = snapshot.Url;

            switch (snapshot.LoadState)
            {
                case SelfTestPageLoadState.Loading:
                    StatusTitle = HasParameters
                        ? "Обновление тестовой страницы..."
                        : "Загрузка тестовой страницы...";
                    StatusDetails = HasParameters
                        ? $"Показан предыдущий снимок ({Parameters.Count} полей) до завершения обновления."
                        : "Ожидание ответа DUT и разбора XML.";
                    break;

                case SelfTestPageLoadState.Loaded:
                    StatusTitle = "Тестовая страница загружена";
                    StatusDetails = snapshot.LoadedAt is { } loadedAt
                        ? $"Получено полей: {Parameters.Count}. Обновлено: {loadedAt:dd.MM.yyyy HH:mm:ss}. Префикс контекста: {snapshot.OutputPrefix}."
                        : $"Получено полей: {Parameters.Count}.";
                    break;

                case SelfTestPageLoadState.Error:
                    StatusTitle = HasParameters
                        ? "Не удалось обновить тестовую страницу"
                        : "Тестовая страница не загружена";
                    StatusDetails = HasParameters
                        ? $"Показан последний успешный снимок ({Parameters.Count} полей). Ошибка: {snapshot.ErrorMessage}"
                        : $"Ошибка загрузки: {snapshot.ErrorMessage}";
                    break;

                default:
                    StatusTitle = "Тестовая страница ещё не загружена";
                    StatusDetails = "Параметры появятся после первого успешного Selftest Check или SelftestSnapshot в текущем запуске.";
                    break;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _state.SnapshotChanged -= OnSnapshotChanged;
        }
    }
}
