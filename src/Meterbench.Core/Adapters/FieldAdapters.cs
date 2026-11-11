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
/// Implemented by <see cref="Meterbench.Core.Mqtt.MqttPublisher"/>: MQTT 3.1.1 QoS 0 publishes to a
/// local broker. Payloads are built by <see cref="Meterbench.Core.Mqtt.FleetMqttMessages"/>.
/// </summary>
public interface IMqttReadingPublisher
{
    ValueTask PublishAsync(string topic, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
}
