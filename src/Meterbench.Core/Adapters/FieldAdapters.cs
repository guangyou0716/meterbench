namespace Meterbench.Core.Adapters;

/// <summary>
/// Implemented by <see cref="Meterbench.Core.Modbus.FleetHoldingRegisters"/> and served over
/// loopback-only Modbus TCP by <see cref="Meterbench.Core.Modbus.ModbusTcpSimulator"/>.
/// Holding addresses in the fleet catalog are invented, not a vendor register map.
/// </summary>
public interface IModbusHoldingRegisterSource
{
    IReadOnlyList<ushort> ReadHoldingRegisters(string deviceId, ushort startAddress, ushort count);
}

/// <summary>
/// Later increment. Increment 1 has no MQTT client and does not contact a broker.
/// </summary>
public interface IMqttReadingPublisher
{
    void PublishReading(string topic, ReadOnlyMemory<byte> payload);
}
