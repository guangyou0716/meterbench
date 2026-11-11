using Meterbench.Core.Fleet;

namespace Meterbench.Core.Simulation;

/// <summary>
/// In-process readings. No sockets. Each value is a pure function of seed, device, quantity, and tick.
/// The walk is recomputed from the baseline so a stored reading is never the next input.
/// </summary>
public static class ReadingSimulator
{
    /// <summary>
    /// Scripted cabinet temperature so alarm raise, hold, and hysteresis clear land on known ticks.
    /// Index is the tick. Ticks past the end stay at the last idle value.
    /// </summary>
    private static readonly double[] TemperatureByTick =
    [
        25.0,
        25.1,
        25.2,
        25.3,
        25.4,
        41.0,
        42.5,
        39.0,
        38.5,
        25.0
    ];

    public static IReadOnlyList<double> CabinetTemperatureScript => TemperatureByTick;

    public static double ValueAt(int seed, string deviceId, QuantityDefinition quantity, int tick)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(quantity);
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick cannot be negative.");
        }

        return quantity.Script switch
        {
            QuantityScript.CabinetTemperature => TemperatureAt(tick),
            QuantityScript.CumulativeEnergy => Math.Round(
                quantity.Baseline + (tick * quantity.Step),
                3,
                MidpointRounding.AwayFromZero),
            QuantityScript.RandomWalk => Walk(seed, deviceId, quantity, tick),
            _ => throw new InvalidOperationException($"Unknown quantity script '{quantity.Script}'.")
        };
    }

    public static double TemperatureAt(int tick)
    {
        if (tick < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick cannot be negative.");
        }

        return tick < TemperatureByTick.Length
            ? TemperatureByTick[tick]
            : TemperatureByTick[^1];
    }

    private static double Walk(int seed, string deviceId, QuantityDefinition quantity, int tick)
    {
        var rng = new DeterministicRng(SeedMix.Mix(seed, deviceId, quantity.Code));
        var value = quantity.Baseline;
        for (var step = 1; step <= tick; step++)
        {
            var unit = rng.NextUnit();
            var delta = (unit - 0.5d) * 2d * quantity.Step;
            value = Math.Clamp(value + delta, quantity.Min, quantity.Max);
        }

        return Math.Round(value, 3, MidpointRounding.AwayFromZero);
    }
}
