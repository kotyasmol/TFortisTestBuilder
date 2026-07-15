using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Threading;

using TestBuilder.Domain.Modbus.Models;
using TestBuilder.Services.Modbus;

namespace TestBuilder.Domain.Modbus
{
    public class SlaveManager
    {
        private readonly IModbusService _modbus;
        private readonly object _slavesLock = new();

        public ObservableCollection<SlaveModelBase> Slaves { get; } = new();

        public SlaveManager(IModbusService modbus)
        {
            _modbus = modbus ?? throw new ArgumentNullException(nameof(modbus));
        }

        public async Task<int> ScanAsync()
        {
            var foundModels = new List<SlaveModelBase>();

            for (byte slaveId = 1; slaveId <= 35; slaveId += 2)
            {
                try
                {
                    ushort typeValue = (await _modbus.ReadRegistersAsync(slaveId, 0, 1))[0];

                    SlaveModelBase? model = typeValue switch
                    {
                        1 => new El60Model(slaveId, _modbus),
                        2 => new PS1Model(slaveId, _modbus),
                        3 => new PS2Model(slaveId, _modbus),
                        4 => new El60v5Model(slaveId, _modbus),
                        5 => new IO2Model(slaveId, _modbus),
                        6 => new StandRpsModel(slaveId, _modbus),
                        7 => new StandPwr180Model(slaveId, _modbus),
                        8 => new Ps3Model(slaveId, _modbus),
                        9 => new Simbat24Model(slaveId, _modbus),
                        10 => new Simbat48Model(slaveId, _modbus),
                        _ => null
                    };

                    if (model != null)
                    {
                        foundModels.Add(model);
                    }
                    else
                    {
                        Console.WriteLine($"[MODBUS SCAN] slave={slaveId}, unknown type={typeValue}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MODBUS SCAN] slave={slaveId}, error={ex.GetType().Name}: {ex.Message}");
                }

                await Task.Delay(150);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                lock (_slavesLock)
                {
                    Slaves.Clear();
                    foreach (var model in foundModels)
                        Slaves.Add(model);
                }
            });

            return foundModels.Count;
        }

        public IReadOnlyList<SlaveModelBase> GetSlavesSnapshot()
        {
            lock (_slavesLock)
            {
                return Slaves.ToList();
            }
        }
    }
}
