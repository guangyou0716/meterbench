using System.Text;
using System.Text.Json;
using Meterbench.Core.Fleet;
using Meterbench.Core.Mqtt;

namespace Meterbench.Tests.Mqtt;

public class FleetMqttMessagesTests
{
    [Fact]
    public void Build_IsDeterministicForSeedAndTick()
    {
        var a = FleetMqttMessages.BuildFleet(FleetCatalog.DefaultSeed, 12);
        var b = FleetMqttMessages.BuildFleet(FleetCatalog.DefaultSeed, 12);
        Assert.Equal(a.Select(m => Encoding.UTF8.GetString(m.Payload)), b.Select(m => Encoding.UTF8.GetString(m.Payload)));
    }

    [Fact]
    public void Build_PayloadHasDeviceTickTimestampAndEveryQuantity()
    {
        var device = FleetCatalog.Find("MTR-A1")!;
        var message = FleetMqttMessages.Build(FleetCatalog.DefaultSeed, 3, device);
        Assert.Equal("meterbench/MTR-A1/latest", message.Topic);

        using var doc = JsonDocument.Parse(message.Payload);
        var root = doc.RootElement;
        Assert.Equal("MTR-A1", root.GetProperty("deviceId").GetString());
        Assert.Equal(3, root.GetProperty("tick").GetInt32());
        Assert.Equal("2026-01-01T00:03:00Z", root.GetProperty("recordedAt").GetString());
        var readings = root.GetProperty("readings").EnumerateArray().ToArray();
        Assert.Equal(device.Quantities.Select(q => q.Code), readings.Select(r => r.GetProperty("quantity").GetString()));
        Assert.Equal(device.Quantities.Select(q => q.Unit), readings.Select(r => r.GetProperty("unit").GetString()));
    }

    [Fact]
    public void Build_CustomPrefix_IsUsed()
    {
        var message = FleetMqttMessages.Build(1, 0, FleetCatalog.Devices[0], "lab/bench");
        Assert.Equal("lab/bench/MTR-A1/latest", message.Topic);
    }

    [Fact]
    public void Build_WildcardPrefix_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => FleetMqttMessages.Build(1, 0, FleetCatalog.Devices[0], "lab/+"));
    }

    [Fact]
    public void Build_NegativeTick_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FleetMqttMessages.Build(1, -1, FleetCatalog.Devices[0]));
    }
}
