using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TestBuilder.Domain.Execution;

namespace TestBuilder.ViewModels
{
    internal static class SelfTestPageFieldCategorizer
    {
        public const string Device = "device";
        public const string Ethernet = "ethernet";
        public const string PoeA = "poe-a";
        public const string PoeB = "poe-b";
        public const string Sfp = "sfp";
        public const string Power = "power";
        public const string Inputs = "inputs";
        public const string Climate = "climate";
        public const string Other = "other";

        private static readonly HashSet<string> DeviceFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "dev_type",
            "init_ok",
            "firmvare_vers",
            "hw_vers",
            "boot_vers",
            "serial_num",
            "default_mac",
            "cpu_id",
            "board_version",
            "poe_controller",
            "marvell_id"
        };

        public static string GetCategoryKey(string fieldName)
        {
            if (DeviceFields.Contains(fieldName))
                return Device;

            if (fieldName.StartsWith("link_", StringComparison.OrdinalIgnoreCase))
                return Ethernet;

            if (fieldName.StartsWith("poe_a_", StringComparison.OrdinalIgnoreCase))
                return PoeA;

            if (fieldName.StartsWith("poe_b_", StringComparison.OrdinalIgnoreCase))
                return PoeB;

            if (fieldName.StartsWith("sfp_", StringComparison.OrdinalIgnoreCase))
                return Sfp;

            if (fieldName.StartsWith("adc_", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("ups_", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("akb_", StringComparison.OrdinalIgnoreCase))
            {
                return Power;
            }

            if (fieldName.StartsWith("sensor_", StringComparison.OrdinalIgnoreCase) ||
                fieldName.StartsWith("input_", StringComparison.OrdinalIgnoreCase))
            {
                return Inputs;
            }

            if (fieldName.Equals("temperature", StringComparison.OrdinalIgnoreCase) ||
                fieldName.Equals("humidity", StringComparison.OrdinalIgnoreCase))
            {
                return Climate;
            }

            return Other;
        }

        public static int CompareFieldNames(string left, string right)
        {
            var leftIndex = 0;
            var rightIndex = 0;

            while (leftIndex < left.Length && rightIndex < right.Length)
            {
                if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex]))
                {
                    var leftStart = leftIndex;
                    var rightStart = rightIndex;

                    while (leftIndex < left.Length && char.IsDigit(left[leftIndex]))
                        leftIndex++;
                    while (rightIndex < right.Length && char.IsDigit(right[rightIndex]))
                        rightIndex++;

                    var leftNumber = long.Parse(left[leftStart..leftIndex]);
                    var rightNumber = long.Parse(right[rightStart..rightIndex]);
                    var numberComparison = leftNumber.CompareTo(rightNumber);
                    if (numberComparison != 0)
                        return numberComparison;

                    continue;
                }

                var characterComparison = char.ToUpperInvariant(left[leftIndex])
                    .CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (characterComparison != 0)
                    return characterComparison;

                leftIndex++;
                rightIndex++;
            }

            return left.Length.CompareTo(right.Length);
        }
    }

    public sealed class SelfTestPageParameterViewModel : ViewModelBase
    {
        private string _value;
        private string _previousValue = string.Empty;
        private bool _isChanged;

        public string Name { get; }

        public string Value
        {
            get => _value;
            private set => SetProperty(ref _value, value);
        }

        public string PreviousValue
        {
            get => _previousValue;
            private set
            {
                if (SetProperty(ref _previousValue, value))
                    OnPropertyChanged(nameof(ChangeDescription));
            }
        }

        public string ChangeDescription => string.IsNullOrEmpty(PreviousValue)
            ? "Новое поле"
            : $"Было: {PreviousValue}";

        public bool IsChanged
        {
            get => _isChanged;
            private set => SetProperty(ref _isChanged, value);
        }

        internal DateTimeOffset HighlightUntil { get; private set; }

        public SelfTestPageParameterViewModel(string name, string value)
        {
            Name = name;
            _value = value;
        }

        internal bool UpdateValue(string value, bool highlight, DateTimeOffset highlightUntil)
        {
            if (string.Equals(Value, value, StringComparison.Ordinal))
                return false;

            var previous = Value;
            Value = value;

            if (highlight)
            {
                PreviousValue = previous;
                HighlightUntil = highlightUntil;
                IsChanged = true;
            }

            return true;
        }

        internal void MarkAsNew(DateTimeOffset highlightUntil)
        {
            PreviousValue = string.Empty;
            HighlightUntil = highlightUntil;
            IsChanged = true;
        }

        internal void ClearChangeHighlight()
        {
            IsChanged = false;
            PreviousValue = string.Empty;
        }
    }

    public sealed class SelfTestPageCategoryViewModel : ViewModelBase
    {
        private bool _isExpanded;
        private int _recentChangeCount;

        public string Key { get; }
        public string Title { get; }
        public string Description { get; }
        public bool DefaultIsExpanded { get; }
        public ObservableCollection<SelfTestPageParameterViewModel> Parameters { get; } = new();
        public ICommand ToggleCommand { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                    OnPropertyChanged(nameof(ToggleIcon));
            }
        }

        public string ToggleIcon => IsExpanded ? "−" : "+";
        public bool HasItems => Parameters.Count > 0;
        public string CountLabel => $"{Parameters.Count} полей";

        public int RecentChangeCount
        {
            get => _recentChangeCount;
            private set
            {
                if (SetProperty(ref _recentChangeCount, value))
                    OnPropertyChanged(nameof(HasRecentChanges));
            }
        }

        public bool HasRecentChanges => RecentChangeCount > 0;

        public SelfTestPageCategoryViewModel(
            string key,
            string title,
            string description,
            bool isExpanded)
        {
            Key = key;
            Title = title;
            Description = description;
            DefaultIsExpanded = isExpanded;
            _isExpanded = isExpanded;
            ToggleCommand = new RelayCommand(() => IsExpanded = !IsExpanded);

            Parameters.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(CountLabel));
            };
        }

        internal void InsertSorted(SelfTestPageParameterViewModel parameter)
        {
            var index = 0;
            while (index < Parameters.Count &&
                   SelfTestPageFieldCategorizer.CompareFieldNames(Parameters[index].Name, parameter.Name) < 0)
            {
                index++;
            }

            Parameters.Insert(index, parameter);
        }

        internal void RefreshRecentChanges()
        {
            RecentChangeCount = Parameters.Count(parameter => parameter.IsChanged);
        }
    }

    public sealed class SelfTestPageViewModel : ViewModelBase, IDisposable
    {
        private static readonly TimeSpan ChangeHighlightDuration = TimeSpan.FromSeconds(5);

        private readonly SelfTestPageState _state;
        private readonly Dictionary<string, SelfTestPageCategoryViewModel> _categoriesByKey;
        private readonly DispatcherTimer _highlightTimer;
        private bool _disposed;
        private bool _hasSuccessfulSnapshot;
        private string _statusTitle = string.Empty;
        private string _statusDetails = string.Empty;
        private string _sourceUrl = string.Empty;
        private bool _hasParameters;
        private int _parameterCount;

        public ObservableCollection<SelfTestPageCategoryViewModel> Categories { get; } = new();
        public ICommand CollapseAllCommand { get; }
        public ICommand ShowMainCommand { get; }

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

        public int ParameterCount
        {
            get => _parameterCount;
            private set => SetProperty(ref _parameterCount, value);
        }

        public SelfTestPageViewModel(SelfTestPageState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));

            CollapseAllCommand = new RelayCommand(() =>
            {
                foreach (var category in Categories)
                    category.IsExpanded = false;
            });
            ShowMainCommand = new RelayCommand(() =>
            {
                foreach (var category in Categories)
                    category.IsExpanded = category.DefaultIsExpanded;
            });

            AddCategory(SelfTestPageFieldCategorizer.Device, "Устройство", "Идентификаторы и версии", true);
            AddCategory(SelfTestPageFieldCategorizer.Ethernet, "Ethernet", "Состояние физических портов", false);
            AddCategory(SelfTestPageFieldCategorizer.PoeA, "PoE · канал A", "Состояние, напряжение и ток", false);
            AddCategory(SelfTestPageFieldCategorizer.PoeB, "PoE · канал B", "Состояние, напряжение и ток", false);
            AddCategory(SelfTestPageFieldCategorizer.Sfp, "SFP", "Присутствие, сигнал и идентификация", false);
            AddCategory(SelfTestPageFieldCategorizer.Power, "Питание и UPS", "Внутренние линии, UPS и аккумулятор", true);
            AddCategory(SelfTestPageFieldCategorizer.Inputs, "Входы и датчики", "Дискретные входы и сухие контакты", false);
            AddCategory(SelfTestPageFieldCategorizer.Climate, "Климат", "Температура и влажность", true);
            AddCategory(SelfTestPageFieldCategorizer.Other, "Прочее", "Поля новой или неизвестной прошивки", true);

            _categoriesByKey = Categories.ToDictionary(category => category.Key, StringComparer.Ordinal);

            _highlightTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _highlightTimer.Tick += OnHighlightTimerTick;

            _state.SnapshotChanged += OnSnapshotChanged;
            ApplySnapshot(_state.Current);
        }

        private void AddCategory(string key, string title, string description, bool isExpanded)
        {
            Categories.Add(new SelfTestPageCategoryViewModel(key, title, description, isExpanded));
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

            var highlightChanges = snapshot.LoadState == SelfTestPageLoadState.Loaded && _hasSuccessfulSnapshot;
            SynchronizeParameters(snapshot.Fields, highlightChanges);

            if (snapshot.LoadState == SelfTestPageLoadState.Loaded)
                _hasSuccessfulSnapshot = true;
            else if (snapshot.LoadState == SelfTestPageLoadState.NotLoaded)
                _hasSuccessfulSnapshot = false;

            SourceUrl = snapshot.Url;

            switch (snapshot.LoadState)
            {
                case SelfTestPageLoadState.Loading:
                    StatusTitle = HasParameters
                        ? "Обновление тестовой страницы..."
                        : "Загрузка тестовой страницы...";
                    StatusDetails = HasParameters
                        ? $"Показан предыдущий снимок ({ParameterCount} полей) до завершения обновления."
                        : "Ожидание ответа DUT и разбора XML.";
                    break;

                case SelfTestPageLoadState.Loaded:
                    StatusTitle = "Тестовая страница загружена";
                    StatusDetails = snapshot.LoadedAt is { } loadedAt
                        ? $"{ParameterCount} полей · {loadedAt:HH:mm:ss} · префикс {snapshot.OutputPrefix}"
                        : $"Получено полей: {ParameterCount}.";
                    break;

                case SelfTestPageLoadState.Error:
                    StatusTitle = HasParameters
                        ? "Не удалось обновить тестовую страницу"
                        : "Тестовая страница не загружена";
                    StatusDetails = HasParameters
                        ? $"Показан последний успешный снимок. Ошибка: {snapshot.ErrorMessage}"
                        : $"Ошибка загрузки: {snapshot.ErrorMessage}";
                    break;

                default:
                    StatusTitle = "Тестовая страница ещё не загружена";
                    StatusDetails = "Параметры появятся после первого Selftest Check или SelftestSnapshot.";
                    break;
            }
        }

        private void SynchronizeParameters(
            IReadOnlyDictionary<string, string> fields,
            bool highlightChanges)
        {
            var fieldsByName = new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase);
            var existing = Categories
                .SelectMany(category => category.Parameters.Select(parameter => (category, parameter)))
                .ToDictionary(item => item.parameter.Name, StringComparer.OrdinalIgnoreCase);
            var highlightUntil = DateTimeOffset.UtcNow.Add(ChangeHighlightDuration);
            var anyHighlighted = false;

            foreach (var obsolete in existing.Values
                         .Where(item => !fieldsByName.ContainsKey(item.parameter.Name))
                         .ToList())
            {
                obsolete.category.Parameters.Remove(obsolete.parameter);
            }

            foreach (var field in fieldsByName)
            {
                if (existing.TryGetValue(field.Key, out var current))
                {
                    if (current.parameter.UpdateValue(field.Value, highlightChanges, highlightUntil) && highlightChanges)
                        anyHighlighted = true;

                    continue;
                }

                var categoryKey = SelfTestPageFieldCategorizer.GetCategoryKey(field.Key);
                var parameter = new SelfTestPageParameterViewModel(field.Key, field.Value);
                _categoriesByKey[categoryKey].InsertSorted(parameter);

                if (highlightChanges)
                {
                    parameter.MarkAsNew(highlightUntil);
                    anyHighlighted = true;
                }
            }

            ParameterCount = fieldsByName.Count;
            HasParameters = ParameterCount > 0;

            foreach (var category in Categories)
                category.RefreshRecentChanges();

            if (anyHighlighted && !_highlightTimer.IsEnabled)
                _highlightTimer.Start();
        }

        private void OnHighlightTimerTick(object? sender, EventArgs e)
        {
            var now = DateTimeOffset.UtcNow;
            var anyHighlighted = false;

            foreach (var category in Categories)
            {
                foreach (var parameter in category.Parameters.Where(parameter => parameter.IsChanged))
                {
                    if (parameter.HighlightUntil <= now)
                        parameter.ClearChangeHighlight();
                    else
                        anyHighlighted = true;
                }

                category.RefreshRecentChanges();
            }

            if (!anyHighlighted)
                _highlightTimer.Stop();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _state.SnapshotChanged -= OnSnapshotChanged;
            _highlightTimer.Stop();
            _highlightTimer.Tick -= OnHighlightTimerTick;
        }
    }
}
