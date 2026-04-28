using System.Collections.ObjectModel;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public class IO2Model : SlaveModelBase
    {
        public const ushort REG_START = 1500;
        public const ushort REG_COUNT = 30; // 1500–1529
        public override string DeviceType => "IO-2";
        // ===== Выходы =====
        public byte Output1 { get; private set; } // 1500
        public byte Output2 { get; private set; } // 1501
        public byte Output3 { get; private set; } // 1502
        public byte Output4 { get; private set; } // 1503
        public byte Output5 { get; private set; } // 1504
        public byte Output6 { get; private set; } // 1505
        public byte Output7 { get; private set; } // 1506

        // ===== Входы =====
        public byte Input1 { get; private set; }  // 1507
        public byte Input2 { get; private set; }  // 1508
        public byte Input3 { get; private set; }  // 1509
        public byte Input4 { get; private set; }  // 1510
        public byte Input5 { get; private set; }  // 1511
        public byte Input6 { get; private set; }  // 1512
        public byte Input7 { get; private set; }  // 1513
        public byte Input8 { get; private set; }  // 1514
        public byte Input9 { get; private set; }  // 1515
        public byte Input10 { get; private set; } // 1516
        public byte Input11 { get; private set; } // 1517

        // ===== I2C =====
        public ushort I2cChipAddr { get; private set; }   // 1518
        public ushort I2cRegAddr { get; private set; }    // 1519
        public ushort I2cData { get; private set; }       // 1520
        public byte I2cReady { get; private set; }        // 1521
        public byte I2cRead { get; private set; }         // 1522
        public byte I2cWrite { get; private set; }        // 1523

        // ===== RS485 =====
        public ushort Rs485ChipAddr { get; private set; } // 1524
        public ushort Rs485RegAddr { get; private set; }  // 1525
        public ushort Rs485Data { get; private set; }     // 1526
        public byte Rs485Ready { get; private set; }      // 1527
        public byte Rs485Read { get; private set; }       // 1528
        public byte Rs485Write { get; private set; }      // 1529



        public IO2Model(byte slaveId, IModbusService modbus)
            : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            RegisterItems.Clear();
            // Выходы
            RegisterItems.Add(new RegisterItem { Address = 1500, Name = "Выход 1", IsReadOnly = false, Category = "Выходы" });
            RegisterItems.Add(new RegisterItem { Address = 1501, Name = "Выход 2", IsReadOnly = false, Category = "Выходы" });
            RegisterItems.Add(new RegisterItem { Address = 1502, Name = "Выход 3", IsReadOnly = false, Category = "Выходы" });
            RegisterItems.Add(new RegisterItem { Address = 1503, Name = "Выход 4", IsReadOnly = false, Category = "Выходы" });
            RegisterItems.Add(new RegisterItem { Address = 1504, Name = "Выход 5", IsReadOnly = false, Category = "Выходы" });
            RegisterItems.Add(new RegisterItem { Address = 1505, Name = "Выход 6", IsReadOnly = false, Category = "Выходы" });
            RegisterItems.Add(new RegisterItem { Address = 1506, Name = "Выход 7", IsReadOnly = false, Category = "Выходы" });
            // Входы
            RegisterItems.Add(new RegisterItem { Address = 1507, Name = "Состояние входа 1", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1508, Name = "Состояние входа 2", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1509, Name = "Состояние входа 3", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1510, Name = "Состояние входа 4", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1511, Name = "Состояние входа 5", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1512, Name = "Состояние входа 6", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1513, Name = "Состояние входа 7", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1514, Name = "Состояние входа 8", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1515, Name = "Состояние входа 9", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1516, Name = "Состояние входа 10", IsReadOnly = true, Category = "Входы" });
            RegisterItems.Add(new RegisterItem { Address = 1517, Name = "Состояние входа 11", IsReadOnly = true, Category = "Входы" });
            // I2C
            RegisterItems.Add(new RegisterItem { Address = 1518, Name = "I2C адрес чипа", IsReadOnly = false, Category = "I2C" });
            RegisterItems.Add(new RegisterItem { Address = 1519, Name = "I2C адрес регистра", IsReadOnly = false, Category = "I2C" });
            RegisterItems.Add(new RegisterItem { Address = 1520, Name = "I2C данные", IsReadOnly = false, Category = "I2C" });
            RegisterItems.Add(new RegisterItem { Address = 1521, Name = "I2C флаг готовности", IsReadOnly = true, Category = "I2C" });
            RegisterItems.Add(new RegisterItem { Address = 1522, Name = "I2C старт чтения", IsReadOnly = false, Category = "I2C" });
            RegisterItems.Add(new RegisterItem { Address = 1523, Name = "I2C старт записи", IsReadOnly = false, Category = "I2C" });
            // RS485
            RegisterItems.Add(new RegisterItem { Address = 1524, Name = "RS485 адрес Modbus ID", IsReadOnly = false, Category = "RS485" });
            RegisterItems.Add(new RegisterItem { Address = 1525, Name = "RS485 адрес регистра", IsReadOnly = false, Category = "RS485" });
            RegisterItems.Add(new RegisterItem { Address = 1526, Name = "RS485 данные", IsReadOnly = false, Category = "RS485" });
            RegisterItems.Add(new RegisterItem { Address = 1527, Name = "RS485 флаг готовности", IsReadOnly = true, Category = "RS485" });
            RegisterItems.Add(new RegisterItem { Address = 1528, Name = "RS485 старт чтения", IsReadOnly = false, Category = "RS485" });
            RegisterItems.Add(new RegisterItem { Address = 1529, Name = "RS485 старт записи", IsReadOnly = false, Category = "RS485" });
        }

        private bool IsReadOnly(ushort addr)
        {
            return addr switch
            {
                1507 or 1508 or 1509 or 1510 or 1511 or 1512 or
                1513 or 1514 or 1515 or 1516 or 1517 or
                1521 or 1527 => true,
                _ => false
            };
        }

        private string GetCategory(ushort addr)
        {
            return addr switch
            {
                >= 1500 and <= 1506 => "Outputs",
                >= 1507 and <= 1517 => "Inputs",
                >= 1518 and <= 1523 => "I2C",
                >= 1524 and <= 1529 => "RS485",
                _ => "IO2"
            };
        }

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);

            Output1 = (byte)regs[0];
            Output2 = (byte)regs[1];
            Output3 = (byte)regs[2];
            Output4 = (byte)regs[3];
            Output5 = (byte)regs[4];
            Output6 = (byte)regs[5];
            Output7 = (byte)regs[6];

            Input1 = (byte)regs[7];
            Input2 = (byte)regs[8];
            Input3 = (byte)regs[9];
            Input4 = (byte)regs[10];
            Input5 = (byte)regs[11];
            Input6 = (byte)regs[12];
            Input7 = (byte)regs[13];
            Input8 = (byte)regs[14];
            Input9 = (byte)regs[15];
            Input10 = (byte)regs[16];
            Input11 = (byte)regs[17];

            I2cChipAddr = regs[18];
            I2cRegAddr = regs[19];
            I2cData = regs[20];
            I2cReady = (byte)regs[21];
            I2cRead = (byte)regs[22];
            I2cWrite = (byte)regs[23];

            Rs485ChipAddr = regs[24];
            Rs485RegAddr = regs[25];
            Rs485Data = regs[26];
            Rs485Ready = (byte)regs[27];
            Rs485Read = (byte)regs[28];
            Rs485Write = (byte)regs[29];

            await UpdateRegisterItemsAsync(regs);
        }

        public async Task WriteRegisterAsync(ushort address, ushort value)
        {
            await Modbus.WriteRegisterAsync(SlaveId, address, value);
            await PollAsync();
        }

        // ===== Быстрое управление =====

        public Task SetOutput(int index, bool state)
        {
            if (index < 1 || index > 7)
                throw new System.ArgumentOutOfRangeException(nameof(index));

            return WriteRegisterAsync((ushort)(1500 + index - 1),
                (ushort)(state ? 1 : 0));
        }

        public Task StartI2cRead() => WriteRegisterAsync(1522, 1);
        public Task StartI2cWrite() => WriteRegisterAsync(1523, 1);

        public Task StartRs485Read() => WriteRegisterAsync(1528, 1);
        public Task StartRs485Write() => WriteRegisterAsync(1529, 1);
    }
}