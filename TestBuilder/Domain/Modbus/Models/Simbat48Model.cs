using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public class Simbat48Model : SlaveModelBase
    {
        public const ushort REG_START = 1700;
        public const ushort REG_COUNT = 17; // 1700–1716 включительно
        public override string DeviceType => "SIMBAT";

        // Свойства регистров
        public ushort ChargeSwitch { get; private set; }           // 1700
        public ushort ChargeVoltage { get; private set; }          // 1701
        public ushort ChargeCurrent { get; private set; }          // 1702
        public ushort ChargeVoltageMax { get; private set; }       // 1703
        public ushort ChargeVoltageMin { get; private set; }       // 1704
        public ushort CurrentControlResistance { get; private set; } // 1705
        public ushort DischargeSwitch { get; private set; }        // 1706
        public ushort DischargeVoltage { get; private set; }       // 1707
        public ushort DischargeCurrent { get; private set; }       // 1708
        public ushort DischargeVoltageMax { get; private set; }    // 1709
        public ushort DischargeVoltageMin { get; private set; }    // 1710
        public ushort Temperature1 { get; private set; }           // 1711
        public ushort Temperature2 { get; private set; }           // 1712
        public ushort TurboFanTurnOnTemp { get; private set; }     // 1713
        public ushort IntSignal { get; private set; }              // 1714
        public ushort TurboFanEnableSignal { get; private set; }   // 1715
        public ushort ClearStatistics { get; private set; }        // 1716

        public Simbat48Model(byte slaveId, IModbusService modbus) : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            RegisterItems = new ObservableCollection<RegisterItem>
            {
                new RegisterItem { Address = 1700, Name = "Charge Switch", Value = 0, IsReadOnly = false, Category = "Charge" },
                new RegisterItem { Address = 1701, Name = "Charge Voltage", Value = 0, IsReadOnly = true, Category = "Charge" },
                new RegisterItem { Address = 1702, Name = "Charge Current", Value = 0, IsReadOnly = true, Category = "Charge" },
                new RegisterItem { Address = 1703, Name = "Charge Voltage Max", Value = 0, IsReadOnly = true, Category = "Charge Statistics" },
                new RegisterItem { Address = 1704, Name = "Charge Voltage Min", Value = 0, IsReadOnly = true, Category = "Charge Statistics" },
                new RegisterItem { Address = 1705, Name = "Current Control Resistance", Value = 0, IsReadOnly = false, Category = "Control" },
                new RegisterItem { Address = 1706, Name = "Discharge Switch", Value = 0, IsReadOnly = false, Category = "Discharge" },
                new RegisterItem { Address = 1707, Name = "Discharge Voltage", Value = 0, IsReadOnly = true, Category = "Discharge" },
                new RegisterItem { Address = 1708, Name = "Discharge Current", Value = 0, IsReadOnly = true, Category = "Discharge" },
                new RegisterItem { Address = 1709, Name = "Discharge Voltage Max", Value = 0, IsReadOnly = true, Category = "Discharge Statistics" },
                new RegisterItem { Address = 1710, Name = "Discharge Voltage Min", Value = 0, IsReadOnly = true, Category = "Discharge Statistics" },
                new RegisterItem { Address = 1711, Name = "Temperature 1", Value = 0, IsReadOnly = true, Category = "Temperature" },
                new RegisterItem { Address = 1712, Name = "Temperature 2", Value = 0, IsReadOnly = true, Category = "Temperature" },
                new RegisterItem { Address = 1713, Name = "Turbo Fan Turn On Temp", Value = 0, IsReadOnly = false, Category = "Fan Control" },
                new RegisterItem { Address = 1714, Name = "INT Signal", Value = 0, IsReadOnly = true, Category = "Signals" },
                new RegisterItem { Address = 1715, Name = "Turbo Fan Enable Signal", Value = 0, IsReadOnly = true, Category = "Signals" },
                new RegisterItem { Address = 1716, Name = "Clear Statistics", Value = 0, IsReadOnly = false, Category = "Control" },
            };
        }

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);

            ChargeSwitch = regs[0];
            ChargeVoltage = regs[1];
            ChargeCurrent = regs[2];
            ChargeVoltageMax = regs[3];
            ChargeVoltageMin = regs[4];
            CurrentControlResistance = regs[5];
            DischargeSwitch = regs[6];
            DischargeVoltage = regs[7];
            DischargeCurrent = regs[8];
            DischargeVoltageMax = regs[9];
            DischargeVoltageMin = regs[10];
            Temperature1 = regs[11];
            Temperature2 = regs[12];
            TurboFanTurnOnTemp = regs[13];
            IntSignal = regs[14];
            TurboFanEnableSignal = regs[15];
            ClearStatistics = regs[16];

            await UpdateRegisterItemsAsync(regs);
        }

        public async Task WriteRegisterAsync(ushort address, ushort value)
        {
            await Modbus.WriteRegisterAsync(SlaveId, address, value);
            await PollAsync();
        }

        // Быстрые методы управления
        public Task SetChargeSwitch(bool enable) => WriteRegisterAsync(1700, (ushort)(enable ? 1 : 0));
        public Task SetCurrentControlResistance(ushort value) => WriteRegisterAsync(1705, value);
        public Task SetDischargeSwitch(bool enable) => WriteRegisterAsync(1706, (ushort)(enable ? 1 : 0));
        public Task SetTurboFanTurnOnTemp(ushort temperature) => WriteRegisterAsync(1713, temperature);
        public Task ClearStats() => WriteRegisterAsync(1716, 1);

        // Удобные свойства-обертки для bool
        public bool IsChargeEnabled => ChargeSwitch == 1;
        public bool IsDischargeEnabled => DischargeSwitch == 1;

        // Комбинированные методы
        public Task EnableChargeWithResistance(ushort resistance)
        {
            return SetCurrentControlResistance(resistance).ContinueWith(_ => SetChargeSwitch(true));
        }

        public Task StopCharge()
        {
            return SetChargeSwitch(false);
        }

        public Task EnableDischarge()
        {
            return SetDischargeSwitch(true);
        }

        public Task StopDischarge()
        {
            return SetDischargeSwitch(false);
        }

        public Task ResetAllStatistics()
        {
            return ClearStats();
        }

        // Проверка состояния сигналов
        public bool IsIntSignalActive => IntSignal == 1;
        public bool IsTurboFanEnabled => TurboFanEnableSignal == 1;

        // Сброс статистики с проверкой (если нужно записать 1, потом прочитать подтверждение)
        public async Task ResetStatisticsAndConfirm()
        {
            await ClearStats();
            await Task.Delay(100); // Небольшая задержка для применения
            await PollAsync(); // Обновить состояние
        }
    }
}