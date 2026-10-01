namespace TestBuilder.Services
{
    /// <summary>
    /// Элемент списка профилей в левой панели.
    /// Содержит путь к файлу и красивое имя из поля "name" внутри JSON.
    /// </summary>
    public class GraphProfile
    {
        public string FilePath { get; }
        public string Name { get; }
        public string? DeviceModel { get; }
        public string ConfigurationName { get; }
        public string ModelGroup => DeviceModel ?? "Без модели";
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

        public GraphProfile(string filePath, string name, string? deviceModel = null, string? configurationName = null)
        {
            FilePath = filePath;
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
