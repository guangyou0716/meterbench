using Meterbench.Core.Adapters;
using Meterbench.Core.Fleet;
using Meterbench.Core.Simulation;

namespace Meterbench.Core.Modbus;

/// <summary>
/// Read-only holding-register view of the synthetic fleet at the current tick.
/// Each quantity occupies two registers starting at its invented HoldingAddress:
/// the scaled int32 (engineering / 10^scale, rounded away from zero), high word first.
/// Addresses that no quantity covers are illegal. Nothing here is a vendor register map.
/// </summary>
public sealed class FleetHoldingRegisters : IModbusHoldingRegisterSource
{
    public const int RegistersPerQuantity = 2;

    private readonly Func<(int Seed, int Tick)> _snapshot;

    public FleetHoldingRegisters(int seed, Func<int> currentTick)
    {
        ArgumentNullException.ThrowIfNull(currentTick);
        _snapshot = () => (seed, currentTick());
    }

    /// <summary>
    /// Reads seed and tick together from one snapshot so a reset cannot pair an old seed
    /// with a new tick inside a single request.
    /// </summary>
    public FleetHoldingRegisters(Func<(int Seed, int Tick)> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshot = snapshot;
    }

    public IReadOnlyList<ushort> ReadHoldingRegisters(string deviceId, ushort startAddress, ushort count)
    {
        if (TryRead(deviceId, startAddress, count, out var registers, out var error, out var message))
        {
            return registers;
        }

        throw new ModbusReadException(error, message);
    }

    public bool TryRead(
        string deviceId,
        ushort startAddress,
        ushort count,
        out ushort[] registers,
        out ModbusExceptionCode error,
        out string message)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        registers = [];
        error = default;
        message = string.Empty;

        var device = FleetCatalog.Find(deviceId);
        if (device is null)
        {
            error = ModbusExceptionCode.GatewayTargetFailedToRespond;
            message = $"Unknown device '{deviceId}'.";
            return false;
        }

        if (count == 0)
        {
            error = ModbusExceptionCode.IllegalDataValue;
            message = "Register count must be at least 1.";
            return false;
        }

        var (seed, tick) = _snapshot();
        if (tick < 0)
        {
            error = ModbusExceptionCode.ServerDeviceFailure;
            message = "Current tick is negative.";
            return false;
        }

        var result = new ushort[count];
        var cache = new Dictionary<string, (ushort High, ushort Low)>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var address = startAddress + i;
            var quantity = FindQuantity(device, address);
            if (quantity is null)
            {
                error = ModbusExceptionCode.IllegalDataAddress;
                message = $"Address {address} is not mapped for {device.Id}.";
                return false;
            }

            if (!cache.TryGetValue(quantity.Code, out var words))
            {
                var value = ReadingSimulator.ValueAt(seed, device.Id, quantity, tick);
                if (!TryScale(value, quantity.Scale, out var scaled))
                {
                    error = ModbusExceptionCode.ServerDeviceFailure;
                    message = $"{quantity.Code} value {value} does not fit an int32 at scale {quantity.Scale}.";
                    return false;
                }

                var raw = unchecked((uint)scaled);
                words = ((ushort)(raw >> 16), (ushort)(raw & 0xFFFF));
                cache[quantity.Code] = words;
            }

            result[i] = address == quantity.HoldingAddress ? words.High : words.Low;
        }

        registers = result;
        return true;
    }

    /// <summary>Scaled int32 = engineering / 10^scale, rounded away from zero. False on overflow.</summary>
    public static bool TryScale(double engineering, sbyte scale, out int scaled)
    {
        scaled = 0;
        if (double.IsNaN(engineering) || double.IsInfinity(engineering))
        {
            return false;
        }

        decimal value;
        try
        {
            value = (decimal)engineering;
            var exponent = Math.Abs((int)scale);
            for (var i = 0; i < exponent; i++)
            {
                value = scale < 0 ? value * 10m : value / 10m;
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        value = Math.Round(value, 0, MidpointRounding.AwayFromZero);
        if (value < int.MinValue || value > int.MaxValue)
        {
            return false;
        }

        scaled = (int)value;
        return true;
    }

    private static QuantityDefinition? FindQuantity(DeviceDefinition device, int address)
    {
        foreach (var quantity in device.Quantities)
        {
            if (address >= quantity.HoldingAddress && address < quantity.HoldingAddress + RegistersPerQuantity)
            {
                return quantity;
            }
        }

        return null;
    }
}
