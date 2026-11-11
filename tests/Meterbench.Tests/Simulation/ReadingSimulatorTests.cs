using Meterbench.Core.Alarms;
using Meterbench.Core.Fleet;
using Meterbench.Core.Simulation;

namespace Meterbench.Tests.Simulation;

public class ReadingSimulatorTests
{
    [Fact]
    public void RandomWalk_RepeatsForTheSameSeed_AndChangesWithSeedOrTick()
    {
        var voltage = Quantity("MTR-A1", QuantityCodes.VoltageL1);
        var first = Series(1, "MTR-A1", voltage, 20);
        var repeat = Series(1, "MTR-A1", voltage, 20);
        var otherSeed = Series(2, "MTR-A1", voltage, 20);
        var otherDevice = Series(1, "MTR-A2", voltage, 20);

        Assert.Equal(first, repeat);
        Assert.NotEqual(first, otherSeed);
        Assert.NotEqual(first, otherDevice);
        Assert.Equal(voltage.Baseline, first[0]);
    }

    [Fact]
    public void CumulativeEnergy_AddsTheInventedStep()
    {
        var energy = Quantity("MTR-A1", QuantityCodes.ActiveEnergyKwh);
        Assert.Equal(1000d, ReadingSimulator.ValueAt(FleetCatalog.DefaultSeed, "MTR-A1", energy, 0));
        Assert.Equal(1001.25d, ReadingSimulator.ValueAt(FleetCatalog.DefaultSeed, "MTR-A1", energy, 5));
    }

    [Fact]
    public void CabinetTemperature_FollowsTheScriptedExcursion()
    {
        var temperature = Quantity("LC-B1", QuantityCodes.CabinetTemperature);
        Assert.Equal(25.0, Value(temperature, 0));
        Assert.Equal(25.4, Value(temperature, 4));
        Assert.Equal(41.0, Value(temperature, 5));
        Assert.Equal(42.5, Value(temperature, 6));
        Assert.Equal(39.0, Value(temperature, 7));
        Assert.Equal(38.5, Value(temperature, 8));
        Assert.Equal(25.0, Value(temperature, 9));
        Assert.Equal(25.0, Value(temperature, 40));
    }

    [Fact]
    public void TickZero_IsInsideEveryThreshold()
    {
        foreach (var device in FleetCatalog.Devices)
        {
            foreach (var quantity in device.Quantities)
            {
                var value = ReadingSimulator.ValueAt(FleetCatalog.DefaultSeed, device.Id, quantity, 0);
                var decision = AlarmEngine.Evaluate(
                    value,
                    0,
                    quantity.Low,
                    quantity.High,
                    quantity.Hysteresis,
                    active: null);
                Assert.IsType<AlarmDecision.None>(decision);
            }
        }
    }

    [Fact]
    public void ShortWalk_StaysInsideThresholds_ExceptTheScriptedTemperature()
    {
        for (var tick = 0; tick <= 20; tick++)
        {
            foreach (var device in FleetCatalog.Devices)
            {
                foreach (var quantity in device.Quantities)
                {
                    if (quantity.Script == QuantityScript.CabinetTemperature)
                    {
                        continue;
                    }

                    var value = ReadingSimulator.ValueAt(FleetCatalog.DefaultSeed, device.Id, quantity, tick);
                    if (quantity.Low is not null)
                    {
                        Assert.True(value >= quantity.Low.Value, $"{device.Id} {quantity.Code} tick {tick} is {value}");
                    }

                    if (quantity.High is not null)
                    {
                        Assert.True(value <= quantity.High.Value, $"{device.Id} {quantity.Code} tick {tick} is {value}");
                    }
                }
            }
        }
    }

    private static QuantityDefinition Quantity(string deviceId, string code)
    {
        var device = FleetCatalog.Find(deviceId);
        Assert.NotNull(device);
        return device.Quantities.Single(quantity => quantity.Code == code);
    }

    private static double Value(QuantityDefinition quantity, int tick)
    {
        return ReadingSimulator.ValueAt(FleetCatalog.DefaultSeed, "LC-B1", quantity, tick);
    }

    private static double[] Series(int seed, string deviceId, QuantityDefinition quantity, int ticks)
    {
        return Enumerable.Range(0, ticks).Select(tick => ReadingSimulator.ValueAt(seed, deviceId, quantity, tick)).ToArray();
    }
}
