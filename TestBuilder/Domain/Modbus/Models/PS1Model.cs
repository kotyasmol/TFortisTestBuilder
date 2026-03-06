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
                new RegisterItem { Address = 1100, Name = "AC Output 1", Value = 0, IsReadOnly = false, Category = "AC Control" },
                new RegisterItem { Address = 1101, Name = "AC Output 2", Value = 0, IsReadOnly = false, Category = "AC Control" },
                new RegisterItem { Address = 1102, Name = "Sensor Output 1", Value = 0, IsReadOnly = false, Category = "Sensor Control" },
                new RegisterItem { Address = 1103, Name = "Sensor Output 2", Value = 0, IsReadOnly = false, Category = "Sensor Control" },
                new RegisterItem { Address = 1104, Name = "Load Resistor 1", Value = 0, IsReadOnly = false, Category = "Load Control" },

                new RegisterItem { Address = 1105, Name = "Battery Voltage Set (mV)", Value = 0, IsReadOnly = false, Category = "Battery Control" },
                new RegisterItem { Address = 1106, Name = "Charge/Discharge Current (mA)", Value = 0, IsReadOnly = true, Category = "Measurements" },
                new RegisterItem { Address = 1107, Name = "Heater Current (mA)", Value = 0, IsReadOnly = true, Category = "Measurements" },
                new RegisterItem { Address = 1108, Name = "Battery Voltage Measured", Value = 0, IsReadOnly = true, Category = "Measurements" },

                new RegisterItem { Address = 1109, Name = "Battery Output Enable", Value = 0, IsReadOnly = false, Category = "Battery Control" },
                new RegisterItem { Address = 1110, Name = "Polarity Mode", Value = 0, IsReadOnly = false, Category = "Battery Control" },
                new RegisterItem { Address = 1111, Name = "Min Charge Current (mA)", Value = 0, IsReadOnly = true, Category = "Statistics" },
                new RegisterItem { Address = 1112, Name = "Max Charge Current (mA)", Value = 0, IsReadOnly = true, Category = "Statistics" },
                new RegisterItem { Address = 1113, Name = "Clear Statistics", Value = 0, IsReadOnly = false, Category = "Statistics" },
                new RegisterItem { Address = 1114, Name = "Heater Relay Enable", Value = 0, IsReadOnly = false, Category = "Heater Control" },
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