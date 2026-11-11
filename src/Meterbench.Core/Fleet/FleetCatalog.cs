namespace Meterbench.Core.Fleet;

public enum QuantityScript
{
    RandomWalk,
    CumulativeEnergy,
    CabinetTemperature
}

public static class DeviceKinds
{
    public const string ElectricityMeter = "electricityMeter";
    public const string LineController = "lineController";
}

public static class QuantityCodes
{
    public const string VoltageL1 = "voltageL1";
    public const string CurrentL1 = "currentL1";
    public const string ActivePowerKw = "activePowerKw";
    public const string FrequencyHz = "frequencyHz";
    public const string ActiveEnergyKwh = "activeEnergyKwh";
    public const string LineVoltage = "lineVoltage";
    public const string LoadCurrent = "loadCurrent";
    public const string CabinetTemperature = "cabinetTemperature";
}

/// <summary>
/// One invented channel. HoldingAddress is the first of two invented holding registers
/// (scaled int32, high word first) served by the loopback Modbus TCP simulator. Scale is the power of ten for the tutorial frame
/// (engineering = scaledInteger * 10^Scale).
/// </summary>
public sealed record QuantityDefinition(
    string Code,
    string Obis,
    string Unit,
    sbyte Scale,
    ushort HoldingAddress,
    double Baseline,
    double Step,
    double Min,
    double Max,
    double? Low,
    double? High,
    double Hysteresis,
    QuantityScript Script);

public sealed record DeviceDefinition(
    string Id,
    string Name,
    string Kind,
    string Location,
    IReadOnlyList<QuantityDefinition> Quantities);

/// <summary>
/// Synthetic three-device fleet. Nothing here is a vendor register map or a customer site.
/// </summary>
public static class FleetCatalog
{
    public const int DefaultSeed = 20261111;

    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<DeviceDefinition> Fleet =
    [
        new DeviceDefinition(
            "MTR-A1",
            "Electricity meter A1",
            DeviceKinds.ElectricityMeter,
            "Bay 1 / Line A",
            MeterQuantities(voltage: 230.0, current: 8.0, powerKw: 1.84, energyKwh: 1000.0)),
        new DeviceDefinition(
            "MTR-A2",
            "Electricity meter A2",
            DeviceKinds.ElectricityMeter,
            "Bay 1 / Line A",
            MeterQuantities(voltage: 229.5, current: 5.5, powerKw: 1.26, energyKwh: 2500.0)),
        new DeviceDefinition(
            "LC-B1",
            "Line controller B1",
            DeviceKinds.LineController,
            "Bay 1 / Line B",
            LineControllerQuantities())
    ];

    public static IReadOnlyList<DeviceDefinition> Devices => Fleet;

    public static DeviceDefinition? Find(string id)
    {
        foreach (var device in Fleet)
        {
            if (string.Equals(device.Id, id, StringComparison.Ordinal))
            {
                return device;
            }
        }

        return null;
    }

    public static DateTimeOffset TimestampForTick(int tick)
    {
        return Epoch.AddMinutes(tick);
    }

    private static IReadOnlyList<QuantityDefinition> MeterQuantities(
        double voltage,
        double current,
        double powerKw,
        double energyKwh)
    {
        return
        [
            new QuantityDefinition(
                QuantityCodes.VoltageL1,
                "9.1.1.1.0.255",
                "V",
                -1,
                0,
                voltage,
                0.35,
                210,
                250,
                216,
                244,
                1.0,
                QuantityScript.RandomWalk),
            new QuantityDefinition(
                QuantityCodes.CurrentL1,
                "9.1.1.2.0.255",
                "A",
                -2,
                2,
                current,
                0.25,
                0,
                80,
                null,
                60,
                1.0,
                QuantityScript.RandomWalk),
            new QuantityDefinition(
                QuantityCodes.ActivePowerKw,
                "9.1.1.3.0.255",
                "kW",
                -3,
                4,
                powerKw,
                0.05,
                0,
                25,
                null,
                20,
                0.2,
                QuantityScript.RandomWalk),
            new QuantityDefinition(
                QuantityCodes.FrequencyHz,
                "9.1.1.4.0.255",
                "Hz",
                -2,
                6,
                50.0,
                0.01,
                49,
                51,
                49.5,
                50.5,
                0.05,
                QuantityScript.RandomWalk),
            new QuantityDefinition(
                QuantityCodes.ActiveEnergyKwh,
                "9.1.1.5.0.255",
                "kWh",
                -2,
                8,
                energyKwh,
                0.25,
                0,
                1_000_000,
                null,
                null,
                0,
                QuantityScript.CumulativeEnergy)
        ];
    }

    private static IReadOnlyList<QuantityDefinition> LineControllerQuantities()
    {
        return
        [
            new QuantityDefinition(
                QuantityCodes.LineVoltage,
                "9.2.1.1.0.255",
                "V",
                -1,
                0,
                400.0,
                0.5,
                360,
                440,
                380,
                420,
                2.0,
                QuantityScript.RandomWalk),
            new QuantityDefinition(
                QuantityCodes.LoadCurrent,
                "9.2.1.2.0.255",
                "A",
                -2,
                2,
                12.0,
                0.3,
                0,
                60,
                null,
                40,
                1.0,
                QuantityScript.RandomWalk),
            new QuantityDefinition(
                QuantityCodes.CabinetTemperature,
                "9.2.1.3.0.255",
                "C",
                -1,
                4,
                25.0,
                0,
                0,
                80,
                null,
                40,
                1.5,
                QuantityScript.CabinetTemperature)
        ];
    }
}
