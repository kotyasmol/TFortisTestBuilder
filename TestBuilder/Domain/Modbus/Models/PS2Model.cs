using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public class PS2Model : SlaveModelBase
    {
        public const ushort REG_START = 1200;
        public const ushort REG_COUNT = 20; // 1200–1219
        public override string DeviceType => "PS-2";
        // ===== Управление выходами =====
        public byte AcOutput1 { get; private set; }            // 1200
        public byte AcOutput2 { get; private set; }            // 1201
        public byte SensorOutput1 { get; private set; }        // 1202
        public byte SensorOutput2 { get; private set; }        // 1203
        public byte ChargeCurrentKey { get; private set; }     // 1204

        // ===== Зарядка =====
        public ushort ChargeVoltage { get; private set; }      // 1205 mV
        public ushort ChargeCurrent { get; private set; }      // 1206 mA
        public ushort ChargeVoltageMax { get; private set; }   // 1207 mV
        public ushort ChargeVoltageMin { get; private set; }   // 1208 mV

        public ushort ChargeControlResistance { get; private set; } // 1209 Ohm

        // ===== Разрядка =====
        public byte DischargeCurrentKey { get; private set; }  // 1210
        public ushort DischargeVoltage { get; private set; }   // 1211 mV
        public ushort DischargeCurrent { get; private set; }   // 1212 mA
        public ushort DischargeVoltageMax { get; private set; }// 1213 mV
        public ushort DischargeVoltageMin { get; private set; }// 1214 mV

        // ===== Нагреватели =====
        public byte HeaterRelayEnable { get; private set; }    // 1215
        public ushort HeaterCurrent { get; private set; }      // 1216 mA

        // ===== Температура =====
        public sbyte RadiatorTemperature { get; private set; } // 1217 i8
        public byte MaxRadiatorTemperature { get; private set; } // 1218

        // ===== Статистика =====
        public byte ClearStatistics { get; private set; }      // 1219

        public PS2Model(byte slaveId, IModbusService modbus)
            : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            RegisterItems = new ObservableCollection<RegisterItem>
            {
                new RegisterItem { Address = 1200, Name = "Выход AC1", Value = 0, IsReadOnly = false, Category = "Управление AC" },
                new RegisterItem { Address = 1201, Name = "Выход AC2", Value = 0, IsReadOnly = false, Category = "Управление AC" },
                new RegisterItem { Address = 1202, Name = "Выход Sensor1", Value = 0, IsReadOnly = false, Category = "Управление сенсором" },
                new RegisterItem { Address = 1203, Name = "Выход Sensor2", Value = 0, IsReadOnly = false, Category = "Управление сенсором" },
                new RegisterItem { Address = 1204, Name = "Ключ измерения тока зарядки", Value = 0, IsReadOnly = false, Category = "Управление зарядкой" },

                new RegisterItem { Address = 1205, Name = "Напряжение зарядки, мВ", Value = 0, IsReadOnly = true, Category = "Измерения зарядки" },
                new RegisterItem { Address = 1206, Name = "Ток зарядки, мА", Value = 0, IsReadOnly = true, Category = "Измерения зарядки" },
                new RegisterItem { Address = 1207, Name = "Макс. напряжение зарядки, мВ", Value = 0, IsReadOnly = true, Category = "Статистика зарядки" },
                new RegisterItem { Address = 1208, Name = "Мин. напряжение зарядки, мВ", Value = 0, IsReadOnly = true, Category = "Статистика зарядки" },
                new RegisterItem { Address = 1209, Name = "Сопротивление контроля тока, Ом", Value = 0, IsReadOnly = false, Category = "Управление зарядкой" },

                new RegisterItem { Address = 1210, Name = "Ключ измерения тока разрядки", Value = 0, IsReadOnly = true, Category = "Управление разрядкой" },
                new RegisterItem { Address = 1211, Name = "Напряжение разрядки, мВ", Value = 0, IsReadOnly = true, Category = "Измерения разрядки" },
                new RegisterItem { Address = 1212, Name = "Ток разрядки, мА", Value = 0, IsReadOnly = true, Category = "Измерения разрядки" },
                new RegisterItem { Address = 1213, Name = "Макс. напряжение разрядки, мВ", Value = 0, IsReadOnly = true, Category = "Статистика разрядки" },
                new RegisterItem { Address = 1214, Name = "Мин. напряжение разрядки, мВ", Value = 0, IsReadOnly = true, Category = "Статистика разрядки" },

                new RegisterItem { Address = 1215, Name = "Реле нагревателя", Value = 0, IsReadOnly = false, Category = "Управление нагревателем" },
                new RegisterItem { Address = 1216, Name = "Ток нагревателей АКБ, мА", Value = 0, IsReadOnly = true, Category = "Измерения нагревателя" },

                new RegisterItem { Address = 1217, Name = "Температура радиатора, °C", Value = 0, IsReadOnly = true, Category = "Температура" },
                new RegisterItem { Address = 1218, Name = "Макс. температура радиатора, °C", Value = 0, IsReadOnly = false, Category = "Управление температурой" },

                new RegisterItem { Address = 1219, Name = "Очистка статистики", Value = 0, IsReadOnly = false, Category = "Статистика" },
            };
        }

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);

            AcOutput1 = (byte)regs[0];
            AcOutput2 = (byte)regs[1];
            SensorOutput1 = (byte)regs[2];
            SensorOutput2 = (byte)regs[3];
            ChargeCurrentKey = (byte)regs[4];

            ChargeVoltage = regs[5];
            ChargeCurrent = regs[6];
            ChargeVoltageMax = regs[7];
            ChargeVoltageMin = regs[8];
            ChargeControlResistance = regs[9];

            DischargeCurrentKey = (byte)regs[10];
            DischargeVoltage = regs[11];
            DischargeCurrent = regs[12];
            DischargeVoltageMax = regs[13];
            DischargeVoltageMin = regs[14];

            HeaterRelayEnable = (byte)regs[15];
            HeaterCurrent = regs[16];

            RadiatorTemperature = unchecked((sbyte)regs[17]);
            MaxRadiatorTemperature = (byte)regs[18];

            ClearStatistics = (byte)regs[19];

            await UpdateRegisterItemsAsync(regs);
        }

        public async Task WriteRegisterAsync(ushort address, ushort value)
        {
            await Modbus.WriteRegisterAsync(SlaveId, address, value);
            await PollAsync();
        }

        // ===== Быстрое управление =====

        public Task SetChargeResistance(ushort ohm) =>
            WriteRegisterAsync(1209, ohm);

        public Task EnableHeaterRelay(bool enable) =>
            WriteRegisterAsync(1215, (ushort)(enable ? 1 : 0));

        public Task SetMaxRadiatorTemperature(byte temp) =>
            WriteRegisterAsync(1218, temp);

        public Task ClearStats() =>
            WriteRegisterAsync(1219, 1);
    }
}