using System.Threading.Tasks;
using Avalonia.Threading;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public class El60v5Model : SlaveModelBase
    {
        public const ushort REG_START = 1400;
        public const ushort REG_COUNT = 31;
        public override string DeviceType => "EL-60v5";

        public ushort CurrentA { get; private set; }
        public ushort CurrentB { get; private set; }
        public ushort VoltageA { get; private set; }
        public ushort VoltageB { get; private set; }
        public ushort MaxCurrentA { get; private set; }
        public ushort MaxCurrentB { get; private set; }
        public ushort MaxVoltageA { get; private set; }
        public ushort MaxVoltageB { get; private set; }
        public ushort MinCurrentA { get; private set; }
        public ushort MinCurrentB { get; private set; }
        public ushort MinVoltageA { get; private set; }
        public ushort MinVoltageB { get; private set; }
        public ushort LoadSetA { get; private set; }
        public ushort LoadSetB { get; private set; }
        public byte LoadEnableA { get; private set; }
        public byte LoadEnableB { get; private set; }
        public ushort TempA { get; private set; }
        public ushort TempB { get; private set; }
        public ushort MaxTempA { get; private set; }
        public ushort MaxTempB { get; private set; }
        public byte AutoLoadEnableA { get; private set; }
        public byte AutoLoadEnableB { get; private set; }
        public ushort AutoLoadSetA { get; private set; }
        public ushort AutoLoadSetB { get; private set; }
        public byte PassivePoeA { get; private set; }
        public byte PassivePoeB { get; private set; }
        public ushort T2PA { get; private set; }
        public ushort T2PB { get; private set; }
        public ushort AlertA { get; private set; }
        public ushort AlertB { get; private set; }
        public byte ClearStatistics { get; private set; }

        public El60v5Model(byte slaveId, IModbusService modbus) : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            RegisterItems.Clear();
            RegisterItems.Add(new RegisterItem { Address = 1400, Name = "Текущий ток, канал A, мА", IsReadOnly = true, Category = "Измерения" });
            RegisterItems.Add(new RegisterItem { Address = 1401, Name = "Текущий ток, канал B, мА", IsReadOnly = true, Category = "Измерения" });
            RegisterItems.Add(new RegisterItem { Address = 1402, Name = "Текущее напряжение, канал A, мВ", IsReadOnly = true, Category = "Измерения" });
            RegisterItems.Add(new RegisterItem { Address = 1403, Name = "Текущее напряжение, канал B, мВ", IsReadOnly = true, Category = "Измерения" });
            RegisterItems.Add(new RegisterItem { Address = 1404, Name = "Макс. ток, канал A, мА", IsReadOnly = true, Category = "Статистика" });
            RegisterItems.Add(new RegisterItem { Address = 1405, Name = "Макс. ток, канал B, мА", IsReadOnly = true, Category = "Статистика" });
            RegisterItems.Add(new RegisterItem { Address = 1406, Name = "Макс. напряжение, канал A", IsReadOnly = true, Category = "Статистика" });
            RegisterItems.Add(new RegisterItem { Address = 1407, Name = "Макс. напряжение, канал B", IsReadOnly = true, Category = "Статистика" });
            RegisterItems.Add(new RegisterItem { Address = 1408, Name = "Мин. ток, канал A, мА", IsReadOnly = false, Category = "Статистика" });
            RegisterItems.Add(new RegisterItem { Address = 1409, Name = "Мин. ток, канал B, мА", IsReadOnly = true, Category = "Статистика" });
            RegisterItems.Add(new RegisterItem { Address = 1410, Name = "Мин. напряжение, канал A", IsReadOnly = true, Category = "Статистика" });
            RegisterItems.Add(new RegisterItem { Address = 1411, Name = "Мин. напряжение, канал B", IsReadOnly = true, Category = "Статистика" });
            RegisterItems.Add(new RegisterItem { Address = 1412, Name = "Установка нагрузки, канал A, мВт", IsReadOnly = false, Category = "Управление" });
            RegisterItems.Add(new RegisterItem { Address = 1413, Name = "Установка нагрузки, канал B, мВт", IsReadOnly = false, Category = "Управление" });
            RegisterItems.Add(new RegisterItem { Address = 1414, Name = "Включение нагрузки, канал A", IsReadOnly = false, Category = "Управление" });
            RegisterItems.Add(new RegisterItem { Address = 1415, Name = "Включение нагрузки, канал B", IsReadOnly = false, Category = "Управление" });
            RegisterItems.Add(new RegisterItem { Address = 1416, Name = "Температура радиатора, канал A", IsReadOnly = true, Category = "Температура" });
            RegisterItems.Add(new RegisterItem { Address = 1417, Name = "Температура радиатора, канал B", IsReadOnly = true, Category = "Температура" });
            RegisterItems.Add(new RegisterItem { Address = 1418, Name = "Макс. температура радиатора, канал A", IsReadOnly = false, Category = "Управление температурой" });
            RegisterItems.Add(new RegisterItem { Address = 1419, Name = "Макс. температура радиатора, канал B", IsReadOnly = false, Category = "Управление температурой" });
            RegisterItems.Add(new RegisterItem { Address = 1420, Name = "Авт. включение нагрузки, канал A", IsReadOnly = false, Category = "Автоуправление" });
            RegisterItems.Add(new RegisterItem { Address = 1421, Name = "Авт. включение нагрузки, канал B", IsReadOnly = false, Category = "Автоуправление" });
            RegisterItems.Add(new RegisterItem { Address = 1422, Name = "Авт. установка нагрузки, канал A", IsReadOnly = false, Category = "Автоуправление" });
            RegisterItems.Add(new RegisterItem { Address = 1423, Name = "Авт. установка нагрузки, канал B", IsReadOnly = false, Category = "Автоуправление" });
            RegisterItems.Add(new RegisterItem { Address = 1424, Name = "Включение Passive PoE, канал A", IsReadOnly = false, Category = "Управление PoE" });
            RegisterItems.Add(new RegisterItem { Address = 1425, Name = "Включение Passive PoE, канал B", IsReadOnly = false, Category = "Управление PoE" });
            RegisterItems.Add(new RegisterItem { Address = 1426, Name = "T2P, канал A", IsReadOnly = true, Category = "Измерения" });
            RegisterItems.Add(new RegisterItem { Address = 1427, Name = "T2P, канал B", IsReadOnly = true, Category = "Измерения" });
            RegisterItems.Add(new RegisterItem { Address = 1428, Name = "ALERT, канал A", IsReadOnly = true, Category = "Измерения" });
            RegisterItems.Add(new RegisterItem { Address = 1429, Name = "ALERT, канал B", IsReadOnly = true, Category = "Измерения" });
            RegisterItems.Add(new RegisterItem { Address = 1430, Name = "Очистка статистики", IsReadOnly = false, Category = "Статистика" });
        }

        private bool IsReadOnly(ushort addr) =>
            addr switch
            {
                1400 or 1401 or 1402 or 1403 or
                1404 or 1405 or 1406 or 1407 or
                1409 or 1410 or 1411 or
                1416 or 1417 or
                1426 or 1427 or 1428 or 1429 => true,
                _ => false
            };

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentA = regs[0];
                CurrentB = regs[1];
                VoltageA = regs[2];
                VoltageB = regs[3];
                MaxCurrentA = regs[4];
                MaxCurrentB = regs[5];
                MaxVoltageA = regs[6];
                MaxVoltageB = regs[7];
                MinCurrentA = regs[8];
                MinCurrentB = regs[9];
                MinVoltageA = regs[10];
                MinVoltageB = regs[11];
                LoadSetA = regs[12];
                LoadSetB = regs[13];
                LoadEnableA = (byte)regs[14];
                LoadEnableB = (byte)regs[15];
                TempA = regs[16];
                TempB = regs[17];
                MaxTempA = regs[18];
                MaxTempB = regs[19];
                AutoLoadEnableA = (byte)regs[20];
                AutoLoadEnableB = (byte)regs[21];
                AutoLoadSetA = regs[22];
                AutoLoadSetB = regs[23];
                PassivePoeA = (byte)regs[24];
                PassivePoeB = (byte)regs[25];
                T2PA = regs[26];
                T2PB = regs[27];
                AlertA = regs[28];
                AlertB = regs[29];
                ClearStatistics = (byte)regs[30];

                for (int i = 0; i < REG_COUNT && i < RegisterItems.Count; i++)
                    RegisterItems[i].Value = regs[i];

                OnPropertyChanged(string.Empty);
                OnPropertyChanged(nameof(RegisterItems));
            });
        }
    }
}