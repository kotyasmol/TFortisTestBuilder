using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestBuilder.Services.Modbus
{
    public interface IModbusService
    {
        Task<ushort[]> ReadRegistersAsync(byte slaveId, ushort address, ushort count);
        Task<bool> WriteRegisterAsync(byte slaveId, ushort address, ushort value, bool verify = true);
    }
}
