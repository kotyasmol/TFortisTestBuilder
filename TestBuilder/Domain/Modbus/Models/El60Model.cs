using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public class El60Model : SlaveModelBase
    {
        public const ushort REG_START = 1000;
        public const ushort REG_COUNT = 17; // 1000–1018 включительно
        public override string DeviceType => "EL-60";

        // Свойства регистров
        public ushort Current { get; private set; }
        public ushort Voltage { get; private set; }
        public ushort MaxCurrent { get; private set; }
        public ushort MaxVoltage { get; private set; }
        public ushort MinCurrent { get; private set; }
        public ushort MinVoltage { get; private set; }
        public ushort ClearMinMax { get; private set; }
        public ushort LoadSet { get; private set; }
        public ushort LoadEnable { get; private set; }
        public ushort Temp { get; private set; }
        public byte FanEnable { get; private set; }
        public byte FanDiag { get; private set; }
        public byte FanPwm { get; private set; }
        public byte LedRun { get; private set; }
        public byte AutoMode { get; private set; }
        public ushort AutoLoadSet { get; private set; }
        public byte PassivePoEEnable { get; private set; }



        public El60Model(byte slaveId, IModbusService modbus) : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            RegisterItems = new List<RegisterItem>
            {
                new RegisterItem { Address = 1000, Name = "Current", Value = 0, IsReadOnly = true, Category = "Measurements" },
                new RegisterItem { Address = 1001, Name = "Voltage", Value = 0, IsReadOnly = true, Category = "Measurements" },
                new RegisterItem { Address = 1002, Name = "Max Current", Value = 0, IsReadOnly = true, Category = "Statistics" },
                new RegisterItem { Address = 1003, Name = "Max Voltage", Value = 0, IsReadOnly = true, Category = "Statistics" },
                new RegisterItem { Address = 1004, Name = "Min Current", Value = 0, IsReadOnly = true, Category = "Statistics" },
                new RegisterItem { Address = 1005, Name = "Min Voltage", Value = 0, IsReadOnly = true, Category = "Statistics" },
                new RegisterItem { Address = 1006, Name = "Clear Min/Max", Value = 0, IsReadOnly = false, Category = "Control" },
                new RegisterItem { Address = 1007, Name = "Load Set", Value = 0, IsReadOnly = false, Category = "Control" },
                new RegisterItem { Address = 1008, Name = "Load Enable", Value = 0, IsReadOnly = false, Category = "Control" },
                new RegisterItem { Address = 1009, Name = "Temp", Value = 0, IsReadOnly = true, Category = "Temperature" },
                new RegisterItem { Address = 1010, Name = "Fan Enable", Value = 0, IsReadOnly = false, Category = "Fan Control" },
                new RegisterItem { Address = 1011, Name = "Fan Diag", Value = 0, IsReadOnly = true, Category = "Fan Control" },
                new RegisterItem { Address = 1012, Name = "Fan PWM", Value = 0, IsReadOnly = false, Category = "Fan Control" },
                new RegisterItem { Address = 1013, Name = "LED RUN", Value = 0, IsReadOnly = false, Category = "Indicators" },
                new RegisterItem { Address = 1014, Name = "Auto Mode", Value = 0, IsReadOnly = false, Category = "Auto Control" },
                new RegisterItem { Address = 1015, Name = "Auto Load Set", Value = 0, IsReadOnly = false, Category = "Auto Control" },
                new RegisterItem { Address = 1016, Name = "Passive PoE Enable", Value = 0, IsReadOnly = false, Category = "PoE Control" },
            };
        }

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);

            Current = regs[0];
            Voltage = regs[1];
            MaxCurrent = regs[2];
            MaxVoltage = regs[3];
            MinCurrent = regs[4];
            MinVoltage = regs[5];
            ClearMinMax = regs[6];
            LoadSet = regs[7];
            LoadEnable = regs[8];
            Temp = regs[9];
            FanEnable = (byte)regs[10];
            FanDiag = (byte)regs[11];
            FanPwm = (byte)regs[12];
            LedRun = (byte)regs[13];
            AutoMode = (byte)regs[14];
            AutoLoadSet = regs[15];
            PassivePoEEnable = (byte)regs[16];

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

        // Быстрое управление
        public Task EnableLoad(bool enable) => WriteRegisterAsync(1008, (ushort)(enable ? 1 : 0));
        public Task SetLoad(ushort value) => WriteRegisterAsync(1007, value);
        public Task ClearStatistics() => WriteRegisterAsync(1006, 1);
        public Task EnableFan(bool enable) => WriteRegisterAsync(1010, (ushort)(enable ? 1 : 0));
        public Task SetFanPwm(byte value) => WriteRegisterAsync(1012, value);
        public Task EnableLedRun(bool enable) => WriteRegisterAsync(1013, (ushort)(enable ? 1 : 0));
        public Task SetAutoMode(bool enable) => WriteRegisterAsync(1014, (ushort)(enable ? 1 : 0));
        public Task SetAutoLoad(ushort value) => WriteRegisterAsync(1015, value);
        public Task EnablePassivePoE(bool enable) => WriteRegisterAsync(1016, (ushort)(enable ? 1 : 0));
    }
}
