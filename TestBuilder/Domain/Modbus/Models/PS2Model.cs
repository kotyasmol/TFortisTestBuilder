using System.Collections.Generic;
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

        public List<RegisterItem> RegisterItems { get; private set; } = new();

        public PS2Model(byte slaveId, IModbusService modbus)
            : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            RegisterItems = new List<RegisterItem>
            {
                new RegisterItem { Address = 1200, Name = "AC Output 1", Value = 0, IsReadOnly = false, Category = "AC Control" },
                new RegisterItem { Address = 1201, Name = "AC Output 2", Value = 0, IsReadOnly = false, Category = "AC Control" },
                new RegisterItem { Address = 1202, Name = "Sensor Output 1", Value = 0, IsReadOnly = false, Category = "Sensor Control" },
                new RegisterItem { Address = 1203, Name = "Sensor Output 2", Value = 0, IsReadOnly = false, Category = "Sensor Control" },
                new RegisterItem { Address = 1204, Name = "Charge Current Key", Value = 0, IsReadOnly = false, Category = "Charge Control" },

                new RegisterItem { Address = 1205, Name = "Charge Voltage (mV)", Value = 0, IsReadOnly = true, Category = "Charge Measurements" },
                new RegisterItem { Address = 1206, Name = "Charge Current (mA)", Value = 0, IsReadOnly = true, Category = "Charge Measurements" },
                new RegisterItem { Address = 1207, Name = "Charge Voltage Max (mV)", Value = 0, IsReadOnly = true, Category = "Charge Statistics" },
                new RegisterItem { Address = 1208, Name = "Charge Voltage Min (mV)", Value = 0, IsReadOnly = true, Category = "Charge Statistics" },
                new RegisterItem { Address = 1209, Name = "Charge Control Resistance (Ohm)", Value = 0, IsReadOnly = false, Category = "Charge Control" },

                new RegisterItem { Address = 1210, Name = "Discharge Current Key", Value = 0, IsReadOnly = true, Category = "Discharge Control" },
                new RegisterItem { Address = 1211, Name = "Discharge Voltage (mV)", Value = 0, IsReadOnly = true, Category = "Discharge Measurements" },
                new RegisterItem { Address = 1212, Name = "Discharge Current (mA)", Value = 0, IsReadOnly = true, Category = "Discharge Measurements" },
                new RegisterItem { Address = 1213, Name = "Discharge Voltage Max (mV)", Value = 0, IsReadOnly = true, Category = "Discharge Statistics" },
                new RegisterItem { Address = 1214, Name = "Discharge Voltage Min (mV)", Value = 0, IsReadOnly = true, Category = "Discharge Statistics" },

                new RegisterItem { Address = 1215, Name = "Heater Relay Enable", Value = 0, IsReadOnly = false, Category = "Heater Control" },
                new RegisterItem { Address = 1216, Name = "Heater Current (mA)", Value = 0, IsReadOnly = true, Category = "Heater Measurements" },

                new RegisterItem { Address = 1217, Name = "Radiator Temperature (°C)", Value = 0, IsReadOnly = true, Category = "Temperature" },
                new RegisterItem { Address = 1218, Name = "Max Radiator Temperature (°C)", Value = 0, IsReadOnly = false, Category = "Temperature Control" },

                new RegisterItem { Address = 1219, Name = "Clear Statistics", Value = 0, IsReadOnly = false, Category = "Statistics" },
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

            for (int i = 0; i < REG_COUNT && i < RegisterItems.Count; i++)
                RegisterItems[i].Value = regs[i];

            OnPropertyChanged(string.Empty);
            OnPropertyChanged(nameof(RegisterItems));
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
