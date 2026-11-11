namespace Meterbench.Core.Adapters;

/// <summary>
/// Later increment. Increment 1 has no Modbus transport and does not open a socket.
/// Holding addresses in the fleet catalog are invented placeholders only.
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
