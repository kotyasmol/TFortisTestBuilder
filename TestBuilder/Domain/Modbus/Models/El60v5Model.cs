using System.Collections.Generic;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public class El60v5Model : SlaveModelBase
    {
        public const ushort REG_START = 1400;
        public const ushort REG_COUNT = 31; // 1400–1430
        public override string DeviceType => "EL-60v5";
        // ===== Текущие измерения =====
        public ushort CurrentA { get; private set; }        // 1400
        public ushort CurrentB { get; private set; }        // 1401
        public ushort VoltageA { get; private set; }        // 1402
        public ushort VoltageB { get; private set; }        // 1403

        // ===== Максимумы =====
        public ushort MaxCurrentA { get; private set; }     // 1404
        public ushort MaxCurrentB { get; private set; }     // 1405
        public ushort MaxVoltageA { get; private set; }     // 1406
        public ushort MaxVoltageB { get; private set; }     // 1407

        // ===== Минимумы =====
        public ushort MinCurrentA { get; private set; }     // 1408 (RW)
        public ushort MinCurrentB { get; private set; }     // 1409
        public ushort MinVoltageA { get; private set; }     // 1410
        public ushort MinVoltageB { get; private set; }     // 1411

        // ===== Нагрузка =====
        public ushort LoadSetA { get; private set; }        // 1412
        public ushort LoadSetB { get; private set; }        // 1413
        public byte LoadEnableA { get; private set; }       // 1414
        public byte LoadEnableB { get; private set; }       // 1415

        // ===== Температура =====
        public ushort TempA { get; private set; }           // 1416
        public ushort TempB { get; private set; }           // 1417
        public ushort MaxTempA { get; private set; }        // 1418
        public ushort MaxTempB { get; private set; }        // 1419

        // ===== Автономный режим =====
        public byte AutoLoadEnableA { get; private set; }   // 1420
        public byte AutoLoadEnableB { get; private set; }   // 1421
        public ushort AutoLoadSetA { get; private set; }    // 1422
        public ushort AutoLoadSetB { get; private set; }    // 1423

        // ===== Passive PoE =====
        public byte PassivePoeA { get; private set; }       // 1424
        public byte PassivePoeB { get; private set; }       // 1425

        // ===== Диагностика =====
        public ushort T2PA { get; private set; }            // 1426
        public ushort T2PB { get; private set; }            // 1427
        public ushort AlertA { get; private set; }          // 1428
        public ushort AlertB { get; private set; }          // 1429

        public byte ClearStatistics { get; private set; }   // 1430

        public List<RegisterItem> RegisterItems { get; private set; } = new();

        public El60v5Model(byte slaveId, IModbusService modbus)
            : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            for (ushort addr = REG_START; addr < REG_START + REG_COUNT; addr++)
            {
                RegisterItems.Add(new RegisterItem
                {
                    Address = addr,
                    Name = $"Register {addr}",
                    Value = 0,
                    IsReadOnly = IsReadOnly(addr),
                    Category = "EL60v5"
                });
            }
        }

        private bool IsReadOnly(ushort addr)
        {
            return addr switch
            {
                1400 or 1401 or 1402 or 1403 or
                1404 or 1405 or 1406 or 1407 or
                1409 or 1410 or 1411 or
                1416 or 1417 or
                1426 or 1427 or 1428 or 1429 => true,
                _ => false
            };
        }

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);

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
        }

        public async Task WriteRegisterAsync(ushort address, ushort value)
        {
            await Modbus.WriteRegisterAsync(SlaveId, address, value);
            await PollAsync();
        }

        // ===== Быстрое управление =====

        public Task EnableLoadA(bool enable) =>
            WriteRegisterAsync(1414, (ushort)(enable ? 1 : 0));

        public Task EnableLoadB(bool enable) =>
            WriteRegisterAsync(1415, (ushort)(enable ? 1 : 0));

        public Task SetLoadA(ushort value) =>
            WriteRegisterAsync(1412, value);

        public Task SetLoadB(ushort value) =>
            WriteRegisterAsync(1413, value);

        public Task ClearStats() =>
            WriteRegisterAsync(1430, 1);
    }
}
