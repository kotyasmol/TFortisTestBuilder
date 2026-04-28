using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;


namespace TestBuilder.Domain.Modbus.Models
{
    public class PS1Model : SlaveModelBase
    {
        public const ushort REG_START = 1100;
        public const ushort REG_COUNT = 15; // 1100–1114
        public override string DeviceType => "PS-1";
        // ===== Регистры =====

        public byte AcOutput1 { get; private set; }          // 1100
        public byte AcOutput2 { get; private set; }          // 1101
        public byte SensorOutput1 { get; private set; }      // 1102
        public byte SensorOutput2 { get; private set; }      // 1103
        public byte LoadResistor1 { get; private set; }      // 1104

        public ushort BatteryVoltageSet { get; private set; }    // 1105 mV
        public ushort ChargeDischargeCurrent { get; private set; } // 1106 mA
        public ushort HeaterCurrent { get; private set; }        // 1107 mA

        public byte BatteryVoltageMeasured { get; private set; } // 1108
        public byte BatteryOutputEnable { get; private set; }    // 1109

        public byte PolarityMode { get; private set; }           // 1110
        public ushort MinChargeCurrent { get; private set; }     // 1111
        public ushort MaxChargeCurrent { get; private set; }     // 1112

        public byte ClearStatistics { get; private set; }        // 1113
        public byte HeaterRelayEnable { get; private set; }      // 1114

        // Используем базовое ObservableCollection<RegisterItem> из SlaveModelBase

        public PS1Model(byte slaveId, IModbusService modbus)
            : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            RegisterItems = new ObservableCollection<RegisterItem>
            {
                new RegisterItem { Address = 1100, Name = "Выход AC1", Value = 0, IsReadOnly = false, Category = "Управление AC" },
                new RegisterItem { Address = 1101, Name = "Выход AC2", Value = 0, IsReadOnly = false, Category = "Управление AC" },
                new RegisterItem { Address = 1102, Name = "Выход Sensor1", Value = 0, IsReadOnly = false, Category = "Управление сенсором" },
                new RegisterItem { Address = 1103, Name = "Выход Sensor2", Value = 0, IsReadOnly = false, Category = "Управление сенсором" },
                new RegisterItem { Address = 1104, Name = "Нагрузочный резистор", Value = 0, IsReadOnly = false, Category = "Управление нагрузкой" },

                new RegisterItem { Address = 1105, Name = "Установка напряжения АКБ, мВ", Value = 0, IsReadOnly = false, Category = "Управление АКБ" },
                new RegisterItem { Address = 1106, Name = "Ток зарядки/разрядки АКБ, мА", Value = 0, IsReadOnly = true, Category = "Измерения" },
                new RegisterItem { Address = 1107, Name = "Ток нагревателей АКБ, мА", Value = 0, IsReadOnly = true, Category = "Измерения" },
                new RegisterItem { Address = 1108, Name = "Напряжение на выходе АКБ", Value = 0, IsReadOnly = true, Category = "Измерения" },

                new RegisterItem { Address = 1109, Name = "Подключение выхода АКБ", Value = 0, IsReadOnly = false, Category = "Управление АКБ" },
                new RegisterItem { Address = 1110, Name = "Полярность измерений", Value = 0, IsReadOnly = false, Category = "Управление АКБ" },
                new RegisterItem { Address = 1111, Name = "Мин. ток заряда, мА", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1112, Name = "Макс. ток заряда, мА", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1113, Name = "Очистка статистики", Value = 0, IsReadOnly = false, Category = "Статистика" },
                new RegisterItem { Address = 1114, Name = "Реле нагревателя", Value = 0, IsReadOnly = false, Category = "Управление нагревателем" },
            };
        }

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);

            AcOutput1 = (byte)regs[0];
            AcOutput2 = (byte)regs[1];
            SensorOutput1 = (byte)regs[2];
            SensorOutput2 = (byte)regs[3];
            LoadResistor1 = (byte)regs[4];

            BatteryVoltageSet = regs[5];
            ChargeDischargeCurrent = regs[6];
            HeaterCurrent = regs[7];

            BatteryVoltageMeasured = (byte)regs[8];
            BatteryOutputEnable = (byte)regs[9];

            PolarityMode = (byte)regs[10];
            MinChargeCurrent = regs[11];
            MaxChargeCurrent = regs[12];

            ClearStatistics = (byte)regs[13];
            HeaterRelayEnable = (byte)regs[14];

            await UpdateRegisterItemsAsync(regs);
        }

        public async Task WriteRegisterAsync(ushort address, ushort value)
        {
            await Modbus.WriteRegisterAsync(SlaveId, address, value);
            await PollAsync();
        }

        // ===== Быстрое управление =====

        public Task EnableBatteryOutput(bool enable) =>
            WriteRegisterAsync(1109, (ushort)(enable ? 1 : 0));

        public Task SetBatteryVoltage(ushort millivolts) =>
            WriteRegisterAsync(1105, millivolts);

        public Task SetPolarity(byte mode) =>  // 0/1/2
            WriteRegisterAsync(1110, mode);

        public Task ClearStats() =>
            WriteRegisterAsync(1113, 1);

        public Task EnableHeaterRelay(bool enable) =>
            WriteRegisterAsync(1114, (ushort)(enable ? 1 : 0));
    }
}