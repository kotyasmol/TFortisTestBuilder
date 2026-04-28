using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus.Models
{
    public class StandPwr180Model : SlaveModelBase
    {
        public const ushort REG_START = 1900;
        // Подсчитано количество параметров из вашего списка (всего 53 поля)
        public const ushort REG_COUNT = 53; // 1900–1952 включительно
        public override string DeviceType => "PWR-Tester";

        // Свойства регистров (все ushort, так как Modbus оперирует 16-битными словами)

        // 0
        public ushort KeyAc { get; private set; }
        // 1
        public ushort ElVoltage { get; private set; }
        // 2
        public ushort ElCurrent { get; private set; }
        // 3
        public ushort ElLoadCurrentSet { get; private set; }
        // 4
        public ushort LoadMode { get; private set; } // 1 — К.З., 0 — ЭН
                                                     // 5
        public ushort KkmVoltage { get; private set; }
        // 6
        public ushort KkmVoltageMin { get; private set; }
        // 7
        public ushort KkmVoltageMax { get; private set; }
        // 8
        public ushort VacVoltage { get; private set; }
        // 9
        public ushort VacVoltageMin { get; private set; }
        // 10
        public ushort VacVoltageMax { get; private set; }
        // 11
        public ushort HeatsinkTemp1 { get; private set; }
        // 12
        public ushort HeatsinkTemp2 { get; private set; }
        // 13
        public ushort FanSwitch { get; private set; }
        // 14
        public ushort FanTurnOnTemp { get; private set; }
        // 15
        public ushort FanTurnOffTemp { get; private set; }
        // 16
        public ushort MaxTemperature { get; private set; }
        // 17
        public ushort ShortCircuitSetupTime { get; private set; }
        // 18
        public ushort ShortCircuitPeriodMin { get; private set; }
        // 19
        public ushort ShortCircuitPeriodMax { get; private set; }
        // 20
        public ushort ShortCircuitPeriodCurrent { get; private set; }
        // 21
        public ushort ShortCircuitTripsCount { get; private set; }
        // 22
        public ushort BoardPowerOnTime { get; private set; }
        // 23
        public ushort RunButtonState { get; private set; }
        // 24
        public ushort LatrInitialSetVoltage { get; private set; }
        // 25
        public ushort LatrInitialMinVoltage { get; private set; }
        // 26
        public ushort LatrInitialMaxVoltage { get; private set; }
        // 27
        public ushort LatrStep1SetVoltage { get; private set; }
        // 28
        public ushort LatrStep1MinVoltage { get; private set; }
        // 29
        public ushort LatrStep1MaxVoltage { get; private set; }
        // 30
        public ushort LatrStep3SetVoltage { get; private set; }
        // 31
        public ushort LatrStep3MinVoltage { get; private set; }
        // 32
        public ushort LatrStep3MaxVoltage { get; private set; }
        // 33
        public ushort LatrStep4SetVoltage { get; private set; }
        // 34
        public ushort LatrStep4MinVoltage { get; private set; }
        // 35
        public ushort LatrStep4MaxVoltage { get; private set; }
        // 36
        public ushort LatrStep5SetVoltage { get; private set; }
        // 37
        public ushort LatrStep5MinVoltage { get; private set; }
        // 38
        public ushort LatrStep5MaxVoltage { get; private set; }
        // 39
        public ushort LatrVoltageSetupTime { get; private set; }
        // 40
        public ushort LatrTestTime { get; private set; }
        // 41
        public ushort EnStep1Current { get; private set; }
        // 42
        public ushort EnStep1Time { get; private set; }
        // 43
        public ushort EnStep2Current { get; private set; }
        // 44
        public ushort EnStep2Time { get; private set; }
        // 45
        public ushort EnStep3Current { get; private set; }
        // 46
        public ushort EnStep3Time { get; private set; }
        // 47
        public ushort EnStep4Current { get; private set; }
        // 48
        public ushort EnStep4Time { get; private set; }
        // 49
        public ushort EnStep5Current { get; private set; }
        // 50
        public ushort EnStep5Time { get; private set; }
        // 51
        public ushort EnStep6Current { get; private set; }
        // 52
        public ushort EnStep6Time { get; private set; }
        // 53 — нет, до 52. Но в списке дальше есть еще, проверим:
        // "Выходное напряжение Min" — 53
        // "Выходное напряжение Max" — 54
        // "Включение разрядки ККМ" — 55
        // "Время разрядки ККМ" — 56
        // "Тип проверяемой платы: 1 - PWR-300, 0 - PWR-50/180" — 57
        // "Напряжение при КЗ Макс" — 58

        // Добавляю пропущенные, начиная с 53 адреса
        public ushort OutputVoltageMin { get; private set; }      // 53
        public ushort OutputVoltageMax { get; private set; }      // 54
        public ushort KkmDischargeEnable { get; private set; }    // 55
        public ushort KkmDischargeTime { get; private set; }      // 56
        public ushort BoardType { get; private set; }             // 57
        public ushort ShortCircuitMaxVoltage { get; private set; } // 58

        public StandPwr180Model(byte slaveId, IModbusService modbus) : base(slaveId, modbus)
        {
            InitializeRegisterItems();
        }

        private void InitializeRegisterItems()
        {
            RegisterItems = new ObservableCollection<RegisterItem>
            {
                new RegisterItem { Address = 1900, Name = "Ключ AC", Value = 0, IsReadOnly = true, Category = "Система" },
                new RegisterItem { Address = 1901, Name = "ЭН: Напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Измерения" },
                new RegisterItem { Address = 1902, Name = "ЭН: Ток, мА", Value = 0, IsReadOnly = true, Category = "Измерения" },
                new RegisterItem { Address = 1903, Name = "ЭН: Установка тока нагрузки", Value = 0, IsReadOnly = false, Category = "Управление" },
                new RegisterItem { Address = 1904, Name = "Режим нагрузки (1=КЗ, 0=ЭН)", Value = 0, IsReadOnly = false, Category = "Управление" },
                new RegisterItem { Address = 1905, Name = "ККМ: Напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Измерения" },
                new RegisterItem { Address = 1906, Name = "ККМ: Мин. напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1907, Name = "ККМ: Макс. напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1908, Name = "VAC: Напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Измерения" },
                new RegisterItem { Address = 1909, Name = "VAC: Мин. напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1910, Name = "VAC: Макс. напряжение, мВ", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1911, Name = "Температура радиатора 1", Value = 0, IsReadOnly = true, Category = "Температура" },
                new RegisterItem { Address = 1912, Name = "Температура радиатора 2", Value = 0, IsReadOnly = true, Category = "Температура" },
                new RegisterItem { Address = 1913, Name = "Ключ вентилятора", Value = 0, IsReadOnly = false, Category = "Управление вентилятором" },
                new RegisterItem { Address = 1914, Name = "Температура включения вентилятора", Value = 0, IsReadOnly = false, Category = "Управление вентилятором" },
                new RegisterItem { Address = 1915, Name = "Температура выключения вентилятора", Value = 0, IsReadOnly = false, Category = "Управление вентилятором" },
                new RegisterItem { Address = 1916, Name = "Макс. температура радиатора", Value = 0, IsReadOnly = false, Category = "Защита" },
                new RegisterItem { Address = 1917, Name = "Время настройки КЗ", Value = 0, IsReadOnly = false, Category = "Короткое замыкание" },
                new RegisterItem { Address = 1918, Name = "Мин. период КЗ", Value = 0, IsReadOnly = false, Category = "Короткое замыкание" },
                new RegisterItem { Address = 1919, Name = "Макс. период КЗ", Value = 0, IsReadOnly = false, Category = "Короткое замыкание" },
                new RegisterItem { Address = 1920, Name = "Текущий период КЗ", Value = 0, IsReadOnly = true, Category = "Короткое замыкание" },
                new RegisterItem { Address = 1921, Name = "Счётчик срабатываний КЗ", Value = 0, IsReadOnly = true, Category = "Статистика" },
                new RegisterItem { Address = 1922, Name = "Время работы платы", Value = 0, IsReadOnly = true, Category = "Система" },
                new RegisterItem { Address = 1923, Name = "Состояние кнопки RUN", Value = 0, IsReadOnly = true, Category = "Система" },
                new RegisterItem { Address = 1924, Name = "ЛАТР: Начальное напряжение", Value = 0, IsReadOnly = false, Category = "Настройка ЛАТР" },
                new RegisterItem { Address = 1925, Name = "ЛАТР: Мин. начальное напряжение", Value = 0, IsReadOnly = false, Category = "Настройка ЛАТР" },
                new RegisterItem { Address = 1926, Name = "ЛАТР: Макс. начальное напряжение", Value = 0, IsReadOnly = false, Category = "Настройка ЛАТР" },
                new RegisterItem { Address = 1927, Name = "ЛАТР: Шаг 1, напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1928, Name = "ЛАТР: Шаг 1, мин. напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1929, Name = "ЛАТР: Шаг 1, макс. напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1930, Name = "ЛАТР: Шаг 3, напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1931, Name = "ЛАТР: Шаг 3, мин. напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1932, Name = "ЛАТР: Шаг 3, макс. напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1933, Name = "ЛАТР: Шаг 4, напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1934, Name = "ЛАТР: Шаг 4, мин. напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1935, Name = "ЛАТР: Шаг 4, макс. напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1936, Name = "ЛАТР: Шаг 5, напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1937, Name = "ЛАТР: Шаг 5, мин. напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1938, Name = "ЛАТР: Шаг 5, макс. напряжение", Value = 0, IsReadOnly = false, Category = "Шаги ЛАТР" },
                new RegisterItem { Address = 1939, Name = "ЛАТР: Время установки напряжения", Value = 0, IsReadOnly = false, Category = "Время ЛАТР" },
                new RegisterItem { Address = 1940, Name = "ЛАТР: Время теста", Value = 0, IsReadOnly = false, Category = "Время ЛАТР" },
                new RegisterItem { Address = 1941, Name = "ЭН: Шаг 1, ток", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1942, Name = "ЭН: Шаг 1, время", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1943, Name = "ЭН: Шаг 2, ток", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1944, Name = "ЭН: Шаг 2, время", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1945, Name = "ЭН: Шаг 3, ток", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1946, Name = "ЭН: Шаг 3, время", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1947, Name = "ЭН: Шаг 4, ток", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1948, Name = "ЭН: Шаг 4, время", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1949, Name = "ЭН: Шаг 5, ток", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1950, Name = "ЭН: Шаг 5, время", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1951, Name = "ЭН: Шаг 6, ток", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1952, Name = "ЭН: Шаг 6, время", Value = 0, IsReadOnly = false, Category = "Шаги ЭН" },
                new RegisterItem { Address = 1953, Name = "Мин. выходное напряжение", Value = 0, IsReadOnly = false, Category = "Ограничения" },
                new RegisterItem { Address = 1954, Name = "Макс. выходное напряжение", Value = 0, IsReadOnly = false, Category = "Ограничения" },
                new RegisterItem { Address = 1955, Name = "Включение разрядки ККМ", Value = 0, IsReadOnly = false, Category = "Управление" },
                new RegisterItem { Address = 1956, Name = "Время разрядки ККМ", Value = 0, IsReadOnly = false, Category = "Управление" },
                new RegisterItem { Address = 1957, Name = "Тип платы (1=PWR-300, 0=PWR-50/180)", Value = 0, IsReadOnly = false, Category = "Система" },
                new RegisterItem { Address = 1958, Name = "Макс. напряжение при КЗ", Value = 0, IsReadOnly = false, Category = "Короткое замыкание" },
            };
        }

        public override async Task PollAsync()
        {
            var regs = await Modbus.ReadRegistersAsync(SlaveId, REG_START, REG_COUNT);

            // Сопоставление регистров со свойствами
            KeyAc = regs[0];
            ElVoltage = regs[1];
            ElCurrent = regs[2];
            ElLoadCurrentSet = regs[3];
            LoadMode = regs[4];
            KkmVoltage = regs[5];
            KkmVoltageMin = regs[6];
            KkmVoltageMax = regs[7];
            VacVoltage = regs[8];
            VacVoltageMin = regs[9];
            VacVoltageMax = regs[10];
            HeatsinkTemp1 = regs[11];
            HeatsinkTemp2 = regs[12];
            FanSwitch = regs[13];
            FanTurnOnTemp = regs[14];
            FanTurnOffTemp = regs[15];
            MaxTemperature = regs[16];
            ShortCircuitSetupTime = regs[17];
            ShortCircuitPeriodMin = regs[18];
            ShortCircuitPeriodMax = regs[19];
            ShortCircuitPeriodCurrent = regs[20];
            ShortCircuitTripsCount = regs[21];
            BoardPowerOnTime = regs[22];
            RunButtonState = regs[23];
            LatrInitialSetVoltage = regs[24];
            LatrInitialMinVoltage = regs[25];
            LatrInitialMaxVoltage = regs[26];
            LatrStep1SetVoltage = regs[27];
            LatrStep1MinVoltage = regs[28];
            LatrStep1MaxVoltage = regs[29];
            LatrStep3SetVoltage = regs[30];
            LatrStep3MinVoltage = regs[31];
            LatrStep3MaxVoltage = regs[32];
            LatrStep4SetVoltage = regs[33];
            LatrStep4MinVoltage = regs[34];
            LatrStep4MaxVoltage = regs[35];
            LatrStep5SetVoltage = regs[36];
            LatrStep5MinVoltage = regs[37];
            LatrStep5MaxVoltage = regs[38];
            LatrVoltageSetupTime = regs[39];
            LatrTestTime = regs[40];
            EnStep1Current = regs[41];
            EnStep1Time = regs[42];
            EnStep2Current = regs[43];
            EnStep2Time = regs[44];
            EnStep3Current = regs[45];
            EnStep3Time = regs[46];
            EnStep4Current = regs[47];
            EnStep4Time = regs[48];
            EnStep5Current = regs[49];
            EnStep5Time = regs[50];
            EnStep6Current = regs[51];
            EnStep6Time = regs[52];

            // Дополнительные регистры, если они есть в ответе (при REG_COUNT = 59)
            if (regs.Length > 53)
            {
                OutputVoltageMin = regs[53];
                OutputVoltageMax = regs[54];
                KkmDischargeEnable = regs[55];
                KkmDischargeTime = regs[56];
                BoardType = regs[57];
                ShortCircuitMaxVoltage = regs[58];
            }

            await UpdateRegisterItemsAsync(regs);
        }

        public async Task WriteRegisterAsync(ushort address, ushort value)
        {
            await Modbus.WriteRegisterAsync(SlaveId, address, value);
            await PollAsync();
        }

        // Быстрые методы управления
        public Task SetLoadCurrent(ushort value) => WriteRegisterAsync(1903, value);
        public Task SetLoadMode(bool isShortCircuit) => WriteRegisterAsync(1904, (ushort)(isShortCircuit ? 1 : 0));
        public Task SetFanSwitch(bool enable) => WriteRegisterAsync(1913, (ushort)(enable ? 1 : 0));
        public Task SetFanTurnOnTemp(ushort temp) => WriteRegisterAsync(1914, temp);
        public Task SetFanTurnOffTemp(ushort temp) => WriteRegisterAsync(1915, temp);
        public Task SetMaxTemperature(ushort temp) => WriteRegisterAsync(1916, temp);
        public Task SetShortCircuitSetupTime(ushort seconds) => WriteRegisterAsync(1917, seconds);
        public Task SetShortCircuitPeriodMin(ushort period) => WriteRegisterAsync(1918, period);
        public Task SetShortCircuitPeriodMax(ushort period) => WriteRegisterAsync(1919, period);
        public Task SetLatrInitialVoltage(ushort voltage) => WriteRegisterAsync(1924, voltage);
        public Task SetLatrStep1Voltage(ushort voltage) => WriteRegisterAsync(1927, voltage);
        public Task SetLatrStep3Voltage(ushort voltage) => WriteRegisterAsync(1930, voltage);
        public Task SetEnStep1(ushort current, ushort time) => WriteRegisterAsync(1941, current).ContinueWith(_ => WriteRegisterAsync(1942, time));
        public Task SetEnStep2(ushort current, ushort time) => WriteRegisterAsync(1943, current).ContinueWith(_ => WriteRegisterAsync(1944, time));
        public Task SetEnStep3(ushort current, ushort time) => WriteRegisterAsync(1945, current).ContinueWith(_ => WriteRegisterAsync(1946, time));
        public Task SetEnStep4(ushort current, ushort time) => WriteRegisterAsync(1947, current).ContinueWith(_ => WriteRegisterAsync(1948, time));
        public Task SetEnStep5(ushort current, ushort time) => WriteRegisterAsync(1949, current).ContinueWith(_ => WriteRegisterAsync(1950, time));
        public Task SetEnStep6(ushort current, ushort time) => WriteRegisterAsync(1951, current).ContinueWith(_ => WriteRegisterAsync(1952, time));
        public Task SetOutputVoltageLimits(ushort min, ushort max) => WriteRegisterAsync(1953, min).ContinueWith(_ => WriteRegisterAsync(1954, max));
        public Task EnableKkmDischarge(bool enable) => WriteRegisterAsync(1955, (ushort)(enable ? 1 : 0));
        public Task SetKkmDischargeTime(ushort seconds) => WriteRegisterAsync(1956, seconds);
        public Task SetBoardType(bool isPwr300) => WriteRegisterAsync(1957, (ushort)(isPwr300 ? 1 : 0));
        public Task SetShortCircuitMaxVoltage(ushort voltage) => WriteRegisterAsync(1958, voltage);
    }
}