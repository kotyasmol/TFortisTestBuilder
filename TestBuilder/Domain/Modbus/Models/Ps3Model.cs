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
                new RegisterItem { Address = 1200, Name = "Выход AC1", Value = 0, IsReadOnly = true, Category = "Выходы" },
                new RegisterItem { Address = 1201, Name = "Выход AC2", Value = 0, IsReadOnly = true, Category = "Выходы" },
                new RegisterItem { Address = 1202, Name = "Выход Sensor1", Value = 0, IsReadOnly = true, Category = "Сенсоры" },
                new RegisterItem { Address = 1203, Name = "Выход Sensor2", Value = 0, IsReadOnly = true, Category = "Сенсоры" },
                new RegisterItem { Address = 1204, Name = "Ключ зарядки", Value = 0, IsReadOnly = false, Category = "Зарядка" },
                new RegisterItem { Address = 1205, Name = "Напряжение зарядки, мВ", Value = 0, IsReadOnly = true, Category = "Зарядка" },
                new RegisterItem { Address = 1206, Name = "Ток зарядки, мА", Value = 0, IsReadOnly = true, Category = "Зарядка" },
                new RegisterItem { Address = 1207, Name = "Макс. напряжение зарядки, мВ", Value = 0, IsReadOnly = true, Category = "Статистика зарядки" },
                new RegisterItem { Address = 1208, Name = "Мин. напряжение зарядки, мВ", Value = 0, IsReadOnly = true, Category = "Статистика зарядки" },
                new RegisterItem { Address = 1209, Name = "Сопротивление контроля тока, Ом", Value = 0, IsReadOnly = false, Category = "Управление" },
                new RegisterItem { Address = 1210, Name = "Ключ разрядки", Value = 0, IsReadOnly = false, Category = "Разрядка" },
                new RegisterItem { Address = 1211, Name = "Напряжение разрядки, мВ", Value = 0, IsReadOnly = true, Category = "Разрядка" },
                new RegisterItem { Address = 1212, Name = "Ток разрядки, мА", Value = 0, IsReadOnly = true, Category = "Разрядка" },
                new RegisterItem { Address = 1213, Name = "Макс. напряжение разрядки, мВ", Value = 0, IsReadOnly = true, Category = "Статистика разрядки" },
                new RegisterItem { Address = 1214, Name = "Мин. напряжение разрядки, мВ", Value = 0, IsReadOnly = true, Category = "Статистика разрядки" },
                new RegisterItem { Address = 1215, Name = "Реле нагревателя 1", Value = 0, IsReadOnly = false, Category = "Нагреватели" },
                new RegisterItem { Address = 1216, Name = "Ток нагревателя 1, мА", Value = 0, IsReadOnly = true, Category = "Нагреватели" },
                new RegisterItem { Address = 1217, Name = "Температура радиатора ЭН", Value = 0, IsReadOnly = true, Category = "Температура" },
                new RegisterItem { Address = 1218, Name = "Макс. температура радиатора", Value = 0, IsReadOnly = false, Category = "Защита" },
                new RegisterItem { Address = 1219, Name = "Очистка статистики", Value = 0, IsReadOnly = false, Category = "Управление" },
                new RegisterItem { Address = 1220, Name = "Реле нагревателя 2", Value = 0, IsReadOnly = false, Category = "Нагреватели" },
                new RegisterItem { Address = 1221, Name = "Ток нагревателя 2, мА", Value = 0, IsReadOnly = true, Category = "Нагреватели" },
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