using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus
{
    /// <summary>
    /// Менеджер слейвов Modbus. Скандирует устройства и хранит их модели.
    /// </summary>
    public class SlaveManager
    {
        private readonly IModbusService _modbus;

        public ObservableCollection<SlaveModelBase> Slaves { get; } = new();

        public SlaveManager(IModbusService modbus)
        {
            _modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
        }

        /// <summary>
        /// Сканирование доступных устройств.
        /// </summary>
        public async Task ScanAsync()
        {
            Slaves.Clear();

            for (byte slaveId = 1; slaveId <= 30; slaveId++)
            {
                try
                {
                    ushort typeValue = (await _modbus.ReadRegistersAsync(slaveId, 0, 1))[0];

                    string deviceType = typeValue switch
                    {
                        1 => "EL-60",
                        2 => "PS-1",
                        3 => "PS-2",
                        4 => "EL-60v5",
                        5 => "IO-02",
                        6 => "STAND_RPS-01",
                        7 => "PWR180_STAND",
                        8 => "PS-3",
                        9 => "SIMBAT",
                        10 => "SIMBAT",
                        _ => $"Неизвестный тип ({typeValue})"
                    };

                    SlaveModelBase model = typeValue switch
                    {
                        1 => new El60Model(slaveId, _modbus),
                        2 => new PS1Model(slaveId, _modbus),
                        3 => new PS2Model(slaveId, _modbus),
                        4 => new El60v5Model(slaveId, _modbus),
                        5 => new IO2Model(slaveId, _modbus),
                        6 => new StandRpsModel(slaveId, _modbus),
                        //7 => new StandPwr180Model(slave, _modbus),
                        //8 => new Ps3Model(slave, _modbus),
                        //9 => new SimbatModel(slave, _modbus),
                        _ => null
                    };

                    if (model != null)
                        Slaves.Add(model);
                }
                catch
                {
                    // timeout / нет ответа — можно логировать
                }
            }
        }
    }
}
