namespace Meterbench.Core.Modbus;

/// <summary>Standard Modbus exception codes used by the simulator.</summary>
public enum ModbusExceptionCode : byte
{
    IllegalFunction = 0x01,
    IllegalDataAddress = 0x02,
    IllegalDataValue = 0x03,
    ServerDeviceFailure = 0x04,
    GatewayTargetFailedToRespond = 0x0B
}

/// <summary>Thrown by <see cref="FleetHoldingRegisters.ReadHoldingRegisters"/> when a read cannot be served.</summary>
public sealed class ModbusReadException(ModbusExceptionCode code, string message) : Exception(message)
{
    public ModbusExceptionCode Code { get; } = code;
}
