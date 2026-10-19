using TestBuilder.Domain.Modbus;
namespace TestBuilder.Domain.Monitoring;
public readonly record struct RegisterKey(byte SlaveId, int Address, ModbusArea Area = ModbusArea.Holding);
