using Meterbench.Core.Fleet;

namespace Meterbench.Core.Modbus;

/// <summary>
/// Invented Modbus unit ids for the synthetic fleet. Not a real gateway map.
/// </summary>
public static class ModbusUnits
{
    private static readonly IReadOnlyDictionary<byte, string> UnitToDevice = new Dictionary<byte, string>
    {
        [1] = "MTR-A1",
        [2] = "MTR-A2",
        [3] = "LC-B1"
    };

    public static IReadOnlyDictionary<byte, string> Map => UnitToDevice;

    public static string? DeviceFor(byte unitId)
    {
        return UnitToDevice.TryGetValue(unitId, out var id) && FleetCatalog.Find(id) is not null ? id : null;
    }
}
