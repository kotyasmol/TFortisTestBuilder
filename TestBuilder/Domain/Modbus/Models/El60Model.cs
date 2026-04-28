using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            RegisterItems = new ObservableCollection<RegisterItem>
            {
                new RegisterItem { Address = 1000, Name = "Текущий ток, мА", Value = 0, IsReadOnly = true, Category = "Измерения" },
                new RegisterItem { Address = 1001, Name = "Текущее напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Измерения" },
                new RegisterItem { Address = 1002, Name = "Макс. ток, мА", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1003, Name = "Макс. напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1004, Name = "Мин. ток, мА", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1005, Name = "Мин. напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1006, Name = "Очистка мин/макс", Value = 0, IsReadOnly = false, Category = "Управление" },
                new RegisterItem { Address = 1007, Name = "Установка мощности, мВт", Value = 0, IsReadOnly = false, Category = "Управление" },
                new RegisterItem { Address = 1008, Name = "Включение нагрузки", Value = 0, IsReadOnly = false, Category = "Управление" },
                new RegisterItem { Address = 1009, Name = "Температура радиатора", Value = 0, IsReadOnly = true, Category = "Температура" },
                new RegisterItem { Address = 1010, Name = "Включение вентилятора", Value = 0, IsReadOnly = false, Category = "Управление вентилятором" },
                new RegisterItem { Address = 1011, Name = "Диагностика вентилятора", Value = 0, IsReadOnly = true, Category = "Управление вентилятором" },
                new RegisterItem { Address = 1012, Name = "Регулировка вентилятора (ШИМ)", Value = 0, IsReadOnly = false, Category = "Управление вентилятором" },
                new RegisterItem { Address = 1013, Name = "Включение светодиода RUN", Value = 0, IsReadOnly = false, Category = "Индикаторы" },
                new RegisterItem { Address = 1014, Name = "Автономный режим", Value = 0, IsReadOnly = false, Category = "Автоуправление" },
                new RegisterItem { Address = 1015, Name = "Нагрузка для автономного режима, мА", Value = 0, IsReadOnly = false, Category = "Автоуправление" },
                new RegisterItem { Address = 1016, Name = "Включение реле Passive PoE", Value = 0, IsReadOnly = false, Category = "Управление PoE" },
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

            await UpdateRegisterItemsAsync(regs);
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