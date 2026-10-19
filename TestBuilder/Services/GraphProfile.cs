using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TestBuilder.Services
{
    /// <summary>
    /// Элемент списка профилей в левой панели.
    /// Содержит путь к файлу и красивое имя из поля "name" внутри JSON.
    /// </summary>
    public class GraphProfile : ObservableObject
    {
        public string FilePath { get; }
        public string Name { get; }
        public string ProductKind { get; }
        public string? DeviceModel { get; }
        public string? DeviceModelId { get; }
        public string ConfigurationName { get; }
        private DateTimeOffset? _updatedAt;
        public DateTimeOffset? UpdatedAt => _updatedAt;
        public bool HasUpdatedAt => UpdatedAt.HasValue;
        public string UpdatedAtText => UpdatedAt?.ToLocalTime().ToString("dd.MM.yyyy · HH:mm", CultureInfo.InvariantCulture) ?? "";
        public string UpdatedAtTooltip => HasUpdatedAt ? "Последнее изменение: " + UpdatedAtText : "";

        internal void RefreshUpdatedAt(DateTimeOffset? value)
        {
            if (SetProperty(ref _updatedAt, value, nameof(UpdatedAt)))
            {
                OnPropertyChanged(nameof(HasUpdatedAt));
                OnPropertyChanged(nameof(UpdatedAtText));
                OnPropertyChanged(nameof(UpdatedAtTooltip));
            }
        }
        public string ModelGroup => DeviceModel ?? (DeviceModelId == null ? "Без модели" : "Модель " + DeviceModelId);
        public string ModelGroupKey => DeviceModelId != null ? "id:" + DeviceModelId.ToUpperInvariant() : "name:" + EquipmentCatalog.Normalize(ModelGroup);
        public string StationName => Name switch
        {
            "PSW_2G6F_plus_full_algorithm" => "PSW-2G6F+",
            "PSW_UPS_Box_8x2Pro_full_algorithm_polling" => "PSW+UPS-Box 8x2Pro",
            "Ручная печать 4 этикеток PSW+UPS-Box 8x2Pro" => "Ручная печать",
            "Демонстрация пульта · без оборудования" => "Демонстрация пульта",
            _ => DisplayName
        };
        public string DisplayName => Name switch
        {
            "PSW_2G6F_plus_full_algorithm" => "PSW-2G6F+ · Полная проверка",
            "PSW_UPS_Box_8x2Pro_full_algorithm_polling" => "PSW+UPS-Box 8x2Pro · Полная проверка",
            _ => Name.Replace('_', ' ')
        };

        public GraphProfile(string filePath, string name, string? deviceModel = null, string? configurationName = null, string? deviceModelId = null, string productKind = "Switch", DateTimeOffset? updatedAt = null)
        {
            FilePath = filePath;
            _updatedAt = updatedAt;
            ProductKind = productKind;
            DeviceModelId = deviceModelId;
            Name = name;
            // Existing profiles remain grouped even before their metadata is saved.
            DeviceModel = Clean(deviceModel) ?? name switch
            {
                "PSW_2G6F_plus_full_algorithm" => "PSW-2G6F+",
                "PSW_UPS_Box_8x2Pro_full_algorithm_polling" or
                "Ручная печать 4 этикеток PSW+UPS-Box 8x2Pro" => "PSW+UPS-Box 8x2Pro",
                _ => null
            };
            ConfigurationName = Clean(configurationName) ?? name switch
            {
                "PSW_2G6F_plus_full_algorithm" or "PSW_UPS_Box_8x2Pro_full_algorithm_polling" => "Полная проверка",
                "Ручная печать 4 этикеток PSW+UPS-Box 8x2Pro" => "Ручная печать этикеток",
                "Демонстрация пульта · без оборудования" => "Демонстрация пульта",
                _ => DisplayName
            };
        }

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // Отображается в ListBox напрямую
        public override string ToString() => Name;
    }
}
