using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public class StandRpsModel : SlaveModelBase
    {
        public const ushort REG_START = 1300;
        public const ushort REG_COUNT = 20; // 1300–1319
        public override string DeviceType => "Stand Rps";

        // ===== Управление подключениями =====
        public byte Ac230Enable { get; private set; }          // 1300
        public byte LatrEnable { get; private set; }           // 1301
        public byte BatteryEnable { get; private set; }        // 1302
        public byte BatteryPolarity { get; private set; }      // 1303

        public sbyte ThermoEmulator { get; private set; }     // 1304 (-40,-35,-30)

        // ===== Реле =====
        public byte Relay1State { get; private set; }         // 1305 (AC_OK)
        public byte Relay2State { get; private set; }         // 1306

        // ===== Нагрузка =====
        public byte LoadKey { get; private set; }             // 1307
        public ushort ChargeControlResistance { get; private set; } // 1308 (Ohm)

        // ===== Измерения АКБ =====
        public ushort BatteryVoltage { get; private set; }    // 1309 mV
        public ushort BatteryCurrent { get; private set; }    // 1310 mA

        // ===== Сеть 230V =====
        public byte Input230Present { get; private set; }     // 1311
        public byte Output230Present { get; private set; }    // 1312

        // ===== Температуры =====
        public sbyte SensorTemp1 { get; private set; }        // 1313
        public sbyte SensorTemp2 { get; private set; }        // 1314

        // ===== Вентиляторы =====
        public byte FanControlKey { get; private set; }       // 1315
        public ushort FanOffTemperature { get; private set; } // 1316
        public byte FanOnTemperature { get; private set; }    // 1317

        public byte MaxRadiatorTemperature { get; private set; } // 1318
        public byte ClearStatistics { get; private set; }        // 1319

        // Используем ObservableCollection из базового класса (не скрываем свойство)
        public StandRpsModel(byte slaveId, IModbusService modbus)
            : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            // Заполняем базовое свойство ObservableCollection
            RegisterItems = new ObservableCollection<RegisterItem>
            {
                new RegisterItem { Address = 1300, Name = "AC 230V Enable", Value = 0, IsReadOnly = false, Category = "Power Control" },
                new RegisterItem { Address = 1301, Name = "LATR Enable", Value = 0, IsReadOnly = false, Category = "Power Control" },
                new RegisterItem { Address = 1302, Name = "Battery Enable", Value = 0, IsReadOnly = false, Category = "Power Control" },
                new RegisterItem { Address = 1303, Name = "Battery Polarity", Value = 0, IsReadOnly = false, Category = "Power Control" },
                new RegisterItem { Address = 1304, Name = "Thermo Emulator", Value = 0, IsReadOnly = false, Category = "Temperature Control" },

                new RegisterItem { Address = 1305, Name = "Relay 1 State (AC_OK)", Value = 0, IsReadOnly = true, Category = "Relay State" },
                new RegisterItem { Address = 1306, Name = "Relay 2 State", Value = 0, IsReadOnly = true, Category = "Relay State" },

                new RegisterItem { Address = 1307, Name = "Load Key", Value = 0, IsReadOnly = false, Category = "Load Control" },
                new RegisterItem { Address = 1308, Name = "Charge Control Resistance (Ohm)", Value = 0, IsReadOnly = false, Category = "Load Control" },

                new RegisterItem { Address = 1309, Name = "Battery Voltage (mV)", Value = 0, IsReadOnly = true, Category = "Measurements" },
                new RegisterItem { Address = 1310, Name = "Battery Current (mA)", Value = 0, IsReadOnly = true, Category = "Measurements" },

                new RegisterItem { Address = 1311, Name = "Input 230V Present", Value = 0, IsReadOnly = true, Category = "Power Status" },
                new RegisterItem { Address = 1312, Name = "Output 230V Present", Value = 0, IsReadOnly = true, Category = "Power Status" },

                new RegisterItem { Address = 1313, Name = "Sensor Temp 1 (°C)", Value = 0, IsReadOnly = true, Category = "Temperature" },
                new RegisterItem { Address = 1314, Name = "Sensor Temp 2 (°C)", Value = 0, IsReadOnly = true, Category = "Temperature" },

                new RegisterItem { Address = 1315, Name = "Fan Control Key", Value = 0, IsReadOnly = false, Category = "Fan Control" },
                new RegisterItem { Address = 1316, Name = "Fan Off Temperature", Value = 0, IsReadOnly = false, Category = "Fan Control" },
                new RegisterItem { Address = 1317, Name = "Fan On Temperature", Value = 0, IsReadOnly = false, Category = "Fan Control" },

                new RegisterItem { Address = 1318, Name = "Max Radiator Temperature", Value = 0, IsReadOnly = false, Category = "Temperature Control" },
                new RegisterItem { Address = 1319, Name = "Clear Statistics", Value = 0, IsReadOnly = false, Category = "Statistics" },
            };
        }

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);
            // Обновляем значения в существующих RegisterItems, ожидая, что RegisterItem.Value вызывает PropertyChanged
            await UpdateRegisterItemsAsync(regs);
        }
    }
}
