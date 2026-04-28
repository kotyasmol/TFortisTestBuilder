using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public class Ps3Model : SlaveModelBase
    {
        public const ushort REG_START = 1200;
        public const ushort REG_COUNT = 22; // 1200–1221 включительно
        public override string DeviceType => "PS-3";

        // Свойства регистров
        public ushort OutputAc1 { get; private set; }      // 1200
        public ushort OutputAc2 { get; private set; }      // 1201
        public ushort OutputSensor1 { get; private set; }  // 1202
        public ushort OutputSensor2 { get; private set; }  // 1203
        public ushort ChargeSwitch { get; private set; }   // 1204
        public ushort ChargeVoltageMv { get; private set; } // 1205
        public ushort ChargeCurrentMa { get; private set; } // 1206
        public ushort ChargeVoltageMaxMv { get; private set; } // 1207
        public ushort ChargeVoltageMinMv { get; private set; } // 1208
        public ushort CurrentControlResistance { get; private set; } // 1209
        public ushort DischargeSwitch { get; private set; } // 1210
        public ushort DischargeVoltageMv { get; private set; } // 1211
        public ushort DischargeCurrentMa { get; private set; } // 1212
        public ushort DischargeVoltageMaxMv { get; private set; } // 1213
        public ushort DischargeVoltageMinMv { get; private set; } // 1214
        public ushort Heater1Relay { get; private set; }    // 1215
        public ushort Heater1CurrentMa { get; private set; } // 1216
        public ushort ElLoadHeatsinkTemp { get; private set; } // 1217
        public ushort MaxTemperatureSet { get; private set; } // 1218
        public ushort ClearStatistics { get; private set; }  // 1219
        public ushort Heater2Relay { get; private set; }     // 1220
        public ushort Heater2CurrentMa { get; private set; } // 1221

        public Ps3Model(byte slaveId, IModbusService modbus) : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            RegisterItems = new ObservableCollection<RegisterItem>
            {
                new RegisterItem { Address = 1200, Name = "Output AC1", Value = 0, IsReadOnly = false, Category = "Outputs" },
                new RegisterItem { Address = 1201, Name = "Output AC2", Value = 0, IsReadOnly = true, Category = "Outputs" },
                new RegisterItem { Address = 1202, Name = "Output Sensor1", Value = 0, IsReadOnly = true, Category = "Sensors" },
                new RegisterItem { Address = 1203, Name = "Output Sensor2", Value = 0, IsReadOnly = true, Category = "Sensors" },
                new RegisterItem { Address = 1204, Name = "Charge Switch", Value = 0, IsReadOnly = false, Category = "Charge" },
                new RegisterItem { Address = 1205, Name = "Charge Voltage (mV)", Value = 0, IsReadOnly = true, Category = "Charge" },
                new RegisterItem { Address = 1206, Name = "Charge Current (mA)", Value = 0, IsReadOnly = true, Category = "Charge" },
                new RegisterItem { Address = 1207, Name = "Charge Voltage Max (mV)", Value = 0, IsReadOnly = true, Category = "Charge Statistics" },
                new RegisterItem { Address = 1208, Name = "Charge Voltage Min (mV)", Value = 0, IsReadOnly = true, Category = "Charge Statistics" },
                new RegisterItem { Address = 1209, Name = "Current Control Resistance", Value = 0, IsReadOnly = false, Category = "Control" },
                new RegisterItem { Address = 1210, Name = "Discharge Switch", Value = 0, IsReadOnly = false, Category = "Discharge" },
                new RegisterItem { Address = 1211, Name = "Discharge Voltage (mV)", Value = 0, IsReadOnly = true, Category = "Discharge" },
                new RegisterItem { Address = 1212, Name = "Discharge Current (mA)", Value = 0, IsReadOnly = true, Category = "Discharge" },
                new RegisterItem { Address = 1213, Name = "Discharge Voltage Max (mV)", Value = 0, IsReadOnly = true, Category = "Discharge Statistics" },
                new RegisterItem { Address = 1214, Name = "Discharge Voltage Min (mV)", Value = 0, IsReadOnly = true, Category = "Discharge Statistics" },
                new RegisterItem { Address = 1215, Name = "Heater 1 Relay", Value = 0, IsReadOnly = false, Category = "Heaters" },
                new RegisterItem { Address = 1216, Name = "Heater 1 Current (mA)", Value = 0, IsReadOnly = true, Category = "Heaters" },
                new RegisterItem { Address = 1217, Name = "EL Load Heatsink Temp", Value = 0, IsReadOnly = true, Category = "Temperature" },
                new RegisterItem { Address = 1218, Name = "Max Temperature Set", Value = 0, IsReadOnly = false, Category = "Protection" },
                new RegisterItem { Address = 1219, Name = "Clear Statistics", Value = 0, IsReadOnly = false, Category = "Control" },
                new RegisterItem { Address = 1220, Name = "Heater 2 Relay", Value = 0, IsReadOnly = false, Category = "Heaters" },
                new RegisterItem { Address = 1221, Name = "Heater 2 Current (mA)", Value = 0, IsReadOnly = true, Category = "Heaters" },
            };
        }

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);

            OutputAc1 = regs[0];
            OutputAc2 = regs[1];
            OutputSensor1 = regs[2];
            OutputSensor2 = regs[3];
            ChargeSwitch = regs[4];
            ChargeVoltageMv = regs[5];
            ChargeCurrentMa = regs[6];
            ChargeVoltageMaxMv = regs[7];
            ChargeVoltageMinMv = regs[8];
            CurrentControlResistance = regs[9];
            DischargeSwitch = regs[10];
            DischargeVoltageMv = regs[11];
            DischargeCurrentMa = regs[12];
            DischargeVoltageMaxMv = regs[13];
            DischargeVoltageMinMv = regs[14];
            Heater1Relay = regs[15];
            Heater1CurrentMa = regs[16];
            ElLoadHeatsinkTemp = regs[17];
            MaxTemperatureSet = regs[18];
            ClearStatistics = regs[19];
            Heater2Relay = regs[20];
            Heater2CurrentMa = regs[21];

            await UpdateRegisterItemsAsync(regs);
        }

        public async Task WriteRegisterAsync(ushort address, ushort value)
        {
            await Modbus.WriteRegisterAsync(SlaveId, address, value);
            await PollAsync();
        }

        // Быстрые методы управления
        public Task SetChargeSwitch(bool enable) => WriteRegisterAsync(1204, (ushort)(enable ? 1 : 0));
        public Task SetCurrentControlResistance(ushort value) => WriteRegisterAsync(1209, value);
        public Task SetDischargeSwitch(bool enable) => WriteRegisterAsync(1210, (ushort)(enable ? 1 : 0));
        public Task SetHeater1Relay(bool enable) => WriteRegisterAsync(1215, (ushort)(enable ? 1 : 0));
        public Task SetHeater2Relay(bool enable) => WriteRegisterAsync(1220, (ushort)(enable ? 1 : 0));
        public Task SetMaxTemperature(ushort temp) => WriteRegisterAsync(1218, temp);
        public Task ClearStats() => WriteRegisterAsync(1219, 1);

        // Удобные свойства-обертки для bool
        public bool IsChargeEnabled => ChargeSwitch == 1;
        public bool IsDischargeEnabled => DischargeSwitch == 1;
        public bool IsHeater1Enabled => Heater1Relay == 1;
        public bool IsHeater2Enabled => Heater2Relay == 1;

        // Значения в нормальных единицах (если нужны double)
        public double ChargeVoltageVolts => ChargeVoltageMv / 1000.0;
        public double ChargeCurrentAmps => ChargeCurrentMa / 1000.0;
        public double ChargeVoltageMaxVolts => ChargeVoltageMaxMv / 1000.0;
        public double ChargeVoltageMinVolts => ChargeVoltageMinMv / 1000.0;
        public double DischargeVoltageVolts => DischargeVoltageMv / 1000.0;
        public double DischargeCurrentAmps => DischargeCurrentMa / 1000.0;
        public double DischargeVoltageMaxVolts => DischargeVoltageMaxMv / 1000.0;
        public double DischargeVoltageMinVolts => DischargeVoltageMinMv / 1000.0;
        public double Heater1CurrentAmps => Heater1CurrentMa / 1000.0;
        public double Heater2CurrentAmps => Heater2CurrentMa / 1000.0;
    }
}