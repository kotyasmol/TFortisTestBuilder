using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services;

namespace TestBuilder.ViewModels.NodifyVM
{
    public partial class NodeViewModel : ObservableObject, IDisposable
    {
        private static readonly IReadOnlyList<NodeColorOption> ColorOptions = new NodeColorOption[]
        {
            new("blue", "Синий", Color.Parse("#3B82F6")),
            new("turquoise", "Бирюзовый", Color.Parse("#14B8A6")),
            new("green", "Зелёный", Color.Parse("#22C55E")),
            new("yellow", "Жёлтый", Color.Parse("#EAB308")),
            new("orange", "Оранжевый", Color.Parse("#F97316")),
            new("red", "Красный", Color.Parse("#EF4444")),
            new("purple", "Фиолетовый", Color.Parse("#A855F7"))
        };

        private static readonly IBrush DefaultBorderBrush =
            new SolidColorBrush(Color.FromRgb(99, 102, 241));

        private static readonly IBrush ExecutingBorderBrush =
            new SolidColorBrush(Color.FromRgb(255, 214, 10));

        private static readonly IBrush ErrorBorderBrush =
            new SolidColorBrush(Color.FromRgb(239, 68, 68));

        [ObservableProperty] private string title = string.Empty;
        [ObservableProperty] private Point location;
        [ObservableProperty] private bool isSelected;
        [ObservableProperty] private bool isExecuting;
        [ObservableProperty] private bool hasExecutionError;
        private NodeColorOption selectedColor = ColorOptions[0];

        public ObservableCollection<ConnectorViewModel> Input { get; } = new();
        public ObservableCollection<ConnectorViewModel> Output { get; } = new();

        // Список слейвов для ComboBox в нодах
        public ObservableCollection<SlaveModelBase> AvailableSlaves { get; } = new();
        public bool IsConnected => SlaveRegistry.Instance.IsConnected;
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
            set
            {
                if (SetProperty(ref selectedColor, value))
                {
                    OnPropertyChanged(nameof(NodeColor));
                    OnPropertyChanged(nameof(NodeColorBrush));
                    OnPropertyChanged(nameof(NodeHeaderTextBrush));
                }
            }
        }

        /// <summary>Стабильный ключ цвета для JSON-профиля.</summary>
        public string NodeColor
        {
            get => SelectedColor.Key;
            set => SelectedColor = ColorOptions.FirstOrDefault(option =>
                                      string.Equals(option.Key, value, StringComparison.OrdinalIgnoreCase))
                                  ?? ColorOptions[0];
        }

        public IBrush NodeColorBrush => SelectedColor.Brush;
        public IBrush NodeHeaderTextBrush => SelectedColor.HeaderTextBrush;

        [RelayCommand]
        private void SelectColor(string color) => NodeColor = color;

        public IBrush ExecutionBorderBrush =>
            HasExecutionError ? ErrorBorderBrush :
            IsExecuting ? ExecutingBorderBrush :
            DefaultBorderBrush;

        public Thickness ExecutionBorderThickness =>
            HasExecutionError || IsExecuting ? new Thickness(5) : new Thickness(2);

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

        protected NodeViewModel()
        {
            SlaveRegistry.Instance.PropertyChanged += OnSlaveRegistryPropertyChanged;

            if (SlaveRegistry.Instance.Slaves.Count > 0)
                SyncSlaves();
        }

        private void OnSlaveRegistryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SlaveRegistry.IsConnected))
            {
                OnPropertyChanged(nameof(IsConnected));
                SyncSlaves();
            }
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
            SlaveRegistry.Instance.PropertyChanged -= OnSlaveRegistryPropertyChanged;
        }
    }

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

        private static IBrush GetContrastBrush(Color color)
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
