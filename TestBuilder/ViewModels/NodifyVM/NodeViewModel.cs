using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services;
using TestBuilder.ViewModels.StepVM;

namespace TestBuilder.ViewModels.NodifyVM
{
    public partial class NodeViewModel : ObservableObject, IDisposable
    {
        private static readonly IReadOnlyList<NodeColorOption> ColorOptions = new NodeColorOption[]
        {
            new("blue", "Голубой", Color.Parse("#38BDF8")),
            new("turquoise", "Бирюзовый", Color.Parse("#14B8A6")),
            new("green", "Зелёный", Color.Parse("#22C55E")),
            new("yellow", "Жёлтый", Color.Parse("#EAB308")),
            new("orange", "Оранжевый", Color.Parse("#F97316")),
            new("red", "Красный", Color.Parse("#EF4444")),
            new("purple", "Фиолетовый", Color.Parse("#A855F7"))
        };

        private static readonly IBrush DefaultBorderBrush =
            new SolidColorBrush(Color.Parse("#38BDF8"));

        private static readonly IBrush ExecutingBorderBrush =
            new SolidColorBrush(Color.FromRgb(255, 214, 10));

        private static readonly IBrush ErrorBorderBrush =
            new SolidColorBrush(Color.FromRgb(239, 68, 68));

        private static readonly IBrush SucceededBorderBrush =
            new SolidColorBrush(Color.FromRgb(34, 197, 94));

        private static readonly IBrush InactiveHeaderTextBrush = NodeColorOption.GetContrastBrush(Colors.Gray);

        [ObservableProperty] private bool runOnlyOnFirstRun;
        public bool CanRunOnlyOnFirstRun => Output.Count > 0 && this is not
            (TestBuilder.ViewModels.StepVM.StartNodeViewModel or
             TestBuilder.ViewModels.StepVM.BodyStartNodeViewModel or
             TestBuilder.ViewModels.StepVM.LabelNodeViewModel);
        [ObservableProperty] private string boardElements = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayTitle))]
        private string customName = string.Empty;
        [ObservableProperty] private string operatorName = string.Empty;
        public bool CanSetOperatorName => this is not
            (StartNodeViewModel or EndNodeViewModel or BodyStartNodeViewModel or BodyEndNodeViewModel or LabelNodeViewModel);
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayTitle))]
        private string title = string.Empty;
        public string DisplayTitle => string.IsNullOrWhiteSpace(CustomName)
            ? TestBuilder.Presentation.RussianUiText.Display(Title) : CustomName.Trim();
        [ObservableProperty] private Point location;
        [ObservableProperty] private bool isSelected;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsRunStart))]
        private string runStartLabel = string.Empty;
        public bool IsRunStart => !string.IsNullOrEmpty(RunStartLabel);
        [ObservableProperty] private bool isExecuting;
        [ObservableProperty] private bool hasExecutionError;
        [ObservableProperty] private bool hasExecutionSucceeded;
        private NodeColorOption selectedColor = ColorOptions[0];
        private bool _customColor;
        private bool _failureHandling;
        private int _descendantReviews;
        [ObservableProperty]
        private TestBuilder.Models.NodeModelReview? modelReview;
        public bool HasDirectModelReview => ModelReview != null;
        public bool HasModelReview => HasDirectModelReview || _descendantReviews > 0;
        public string ModelReviewText => ModelReview is { } review
            ? $"Для «{review.ModelName}»:\n{review.Message}" + (_descendantReviews > 0 ? $"\nТакже проверьте вложенные шаги ({_descendantReviews})." : "")
            : _descendantReviews > 0 ? $"Внутри есть шаги для проверки после смены модели ({_descendantReviews}). Откройте тело ноды." : "";
        public string ColorHint => HasModelReview ? ModelReviewText :
            _failureHandling ? "Обработка ошибки" : "Цвет ноды";
        public bool CanConfirmModelReview => HasModelReview;
        public string ConfirmModelReviewCaption => this is SubtestNodeViewModel ? "Подтест проверен для этой модели" :
            this is ICompositeNodeViewModel ? "Цикл проверен для этой модели" : "Нода проверена для этой модели";
        public bool IsFailureHandling => _failureHandling;
        internal Action<NodeViewModel>? ConfirmReviewRequested { get; set; }
        [RelayCommand]
        private void AcknowledgeModelReview() => ConfirmReviewRequested?.Invoke(this);

        partial void OnModelReviewChanged(TestBuilder.Models.NodeModelReview? value) => NotifyPresentation();
        internal void SetPresentation(bool failureHandling, int descendantReviews)
        {
            if (_failureHandling == failureHandling && _descendantReviews == descendantReviews) return;
            _failureHandling = failureHandling;
            _descendantReviews = descendantReviews;
            NotifyPresentation();
        }
        private void NotifyPresentation()
        {
            foreach (var name in new[] { nameof(HasDirectModelReview), nameof(HasModelReview), nameof(ModelReviewText),
                nameof(ColorHint), nameof(CanConfirmModelReview), nameof(IsFailureHandling), nameof(NodeColorBrush), nameof(NodeHeaderTextBrush) })
                OnPropertyChanged(name);
        }

        public ObservableCollection<ConnectorViewModel> Input { get; } = new();
        public ObservableCollection<ConnectorViewModel> Output { get; } = new();

        // Список слейвов для ComboBox в нодах
        public ObservableCollection<SlaveModelBase> AvailableSlaves { get; } = new();
        public bool IsConnected => SlaveRegistry.Instance.IsConnected;
        private TestBuilder.Domain.Modbus.HardwareConfiguration? _registerReferenceHardware;
        internal TestBuilder.Domain.Modbus.HardwareConfiguration? RegisterReferenceHardware
        {
            get => _registerReferenceHardware;
            set
            {
                if (ReferenceEquals(_registerReferenceHardware, value)) return;
                _registerReferenceHardware = value;
                OnSlavesLoaded();
            }
        }
        public string HelpText => NodeHelpTextProvider.GetHelp(GetType());
        public string HelpSummary => NodeHelpTextProvider.GetSummary(GetType());
        public IReadOnlyList<string> HelpDetails => NodeHelpTextProvider.GetDetails(GetType());
        public bool HasHelpText => !string.IsNullOrWhiteSpace(HelpText);
        public bool HasHelpDetails => HelpDetails.Count > 0;
        public IReadOnlyList<NodeColorOption> NodeColors => ColorOptions;

        /// <summary>Выбранный пользователем цвет ноды.</summary>
        public NodeColorOption SelectedColor
        {
            get => selectedColor;
            set => ColorSelection = new(value.Key, true);
        }

        // One history entry restores both the manual color and its automatic/manual mode.
        public NodeColorSelection ColorSelection
        {
            get => new(selectedColor.Key, _customColor);
            set
            {
                var option = ColorOptions.FirstOrDefault(c => c.Key.Equals(value.Key, StringComparison.OrdinalIgnoreCase)) ?? ColorOptions[0];
                if (selectedColor == option && _customColor == value.IsCustom) return;
                selectedColor = option; _customColor = value.IsCustom;
                OnPropertyChanged(nameof(ColorSelection)); OnPropertyChanged(nameof(SelectedColor));
                OnPropertyChanged(nameof(NodeColor)); NotifyPresentation();
            }
        }

        /// <summary>Стабильный ключ цвета для JSON-профиля.</summary>
        public string NodeColor
        {
            get => SelectedColor.Key;
            set => ColorSelection = new(value, true);
        }

        public virtual bool IsInactive => false;
        private NodeColorOption DisplayColor => HasModelReview ? ColorOptions[3] :
            _failureHandling ? ColorOptions[5] : _customColor ? selectedColor : ColorOptions[0];
        public IBrush NodeColorBrush => IsInactive ? Brushes.Gray : DisplayColor.Brush;
        public IBrush NodeHeaderTextBrush => IsInactive ? InactiveHeaderTextBrush : DisplayColor.HeaderTextBrush;

        [RelayCommand]
        private void SelectColor(string color) => NodeColor = color;
        [RelayCommand]
        private void ResetColor() => ColorSelection = new(NodeColor, false);

        public IBrush ExecutionBorderBrush =>
            HasExecutionError ? ErrorBorderBrush :
            IsExecuting ? ExecutingBorderBrush :
            HasExecutionSucceeded ? SucceededBorderBrush :
            DefaultBorderBrush;

        public Thickness ExecutionBorderThickness =>
            HasExecutionError || IsExecuting || HasExecutionSucceeded ? new Thickness(5) : new Thickness(2);

        partial void OnIsExecutingChanged(bool value)
        {
            OnPropertyChanged(nameof(ExecutionBorderBrush));
            OnPropertyChanged(nameof(ExecutionBorderThickness));
        }

        partial void OnHasExecutionErrorChanged(bool value)
        {
            OnPropertyChanged(nameof(ExecutionBorderBrush));
            OnPropertyChanged(nameof(ExecutionBorderThickness));
        }

        partial void OnHasExecutionSucceededChanged(bool value)
        {
            OnPropertyChanged(nameof(ExecutionBorderBrush));
            OnPropertyChanged(nameof(ExecutionBorderThickness));
        }

        protected NodeViewModel()
        {
            SlaveRegistry.Instance.Observe(this, OnSlaveRegistryChanged);

            if (SlaveRegistry.Instance.Slaves.Count > 0)
                SyncSlaves();
        }

        private void OnSlaveRegistryChanged()
        {
            OnPropertyChanged(nameof(IsConnected));
            SyncSlaves();
        }

        public void SyncSlaves()
        {
            AvailableSlaves.Clear();
            foreach (var s in SlaveRegistry.Instance.Slaves)
                AvailableSlaves.Add(s);

            // Даём подклассам возможность восстановить выбранные элементы
            OnSlavesLoaded();
        }

        /// <summary>Вызывается после загрузки слейвов — подклассы переопределяют для восстановления выборок</summary>
        protected virtual void OnSlavesLoaded() { }

        public void AddInput(ConnectorViewModel connector)
        {
            connector.Parent = this;
            Input.Add(connector);
        }

        public void AddOutput(ConnectorViewModel connector)
        {
            connector.Parent = this;
            Output.Add(connector);
        }

        public virtual NodeViewModel Clone() => throw new System.NotImplementedException($"Clone() not implemented for {GetType().Name}");

        public void Dispose()
        {
            SlaveRegistry.Instance.StopObserving(this);
            if (this is ICompositeNodeViewModel composite) composite.BodyGraph.DisposeNodes();
        }
    }

    public sealed record NodeColorSelection(string Key, bool IsCustom);

    public sealed class NodeColorOption
    {
        private static readonly IBrush DarkTextBrush =
            new SolidColorBrush(Color.Parse("#0F172A"));

        private static readonly IBrush LightTextBrush =
            new SolidColorBrush(Color.Parse("#F8FAFC"));

        public NodeColorOption(string key, string name, Color color)
        {
            Key = key;
            Name = name;
            Brush = new SolidColorBrush(color);
            HeaderTextBrush = GetContrastBrush(color);
        }

        public string Key { get; }
        public string Name { get; }
        public IBrush Brush { get; }
        public IBrush HeaderTextBrush { get; }

        internal static IBrush GetContrastBrush(Color color)
        {
            var luminance = GetRelativeLuminance(color);
            const double darkLuminance = 0.009;
            const double lightLuminance = 0.956;

            var darkContrast = (Math.Max(luminance, darkLuminance) + 0.05) /
                               (Math.Min(luminance, darkLuminance) + 0.05);
            var lightContrast = (Math.Max(luminance, lightLuminance) + 0.05) /
                                (Math.Min(luminance, lightLuminance) + 0.05);

            return darkContrast >= lightContrast ? DarkTextBrush : LightTextBrush;
        }

        private static double GetRelativeLuminance(Color color)
        {
            static double ToLinear(byte component)
            {
                var value = component / 255.0;
                return value <= 0.04045
                    ? value / 12.92
                    : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * ToLinear(color.R)
                 + 0.7152 * ToLinear(color.G)
                 + 0.0722 * ToLinear(color.B);
        }
    }
}
