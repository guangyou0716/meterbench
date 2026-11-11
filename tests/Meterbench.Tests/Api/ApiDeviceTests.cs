using System.Net;
using System.Text.Json;
using Meterbench.Core.Fleet;

namespace Meterbench.Tests.Api;

public class ApiDeviceTests
{
    [Fact]
    public async Task Devices_ReturnsTheSyntheticFleet()
    {
        using var api = new TempApi();
        var response = await api.Client.GetAsync("/api/devices");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonBody.ReadAsync(response);

        Assert.Equal(3, body.GetArrayLength());
        Assert.Equal("MTR-A1", body[0].GetProperty("id").GetString());
        Assert.Equal("electricityMeter", body[0].GetProperty("kind").GetString());
        Assert.Equal("Bay 1 / Line A", body[0].GetProperty("location").GetString());
        Assert.Equal("MTR-A2", body[1].GetProperty("id").GetString());
        Assert.Equal("Bay 1 / Line A", body[1].GetProperty("location").GetString());
        Assert.Equal("LC-B1", body[2].GetProperty("id").GetString());
        Assert.Equal("lineController", body[2].GetProperty("kind").GetString());
        Assert.Equal("Bay 1 / Line B", body[2].GetProperty("location").GetString());
        Assert.Contains("cabinetTemperature", Quantities(body[2]));
    }

    [Fact]
    public async Task Latest_UnknownDevice_Is404()
    {
        using var api = new TempApi();
        var response = await api.Client.GetAsync("/api/devices/NO-SUCH/latest");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        JsonBody.AssertError(await JsonBody.ReadAsync(response), "DeviceNotFound");
    }

    [Fact]
    public async Task History_UnknownDevice_Is404()
    {
        using var api = new TempApi();
        var response = await api.Client.GetAsync("/api/devices/NO-SUCH/history?limit=10");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        JsonBody.AssertError(await JsonBody.ReadAsync(response), "DeviceNotFound");
    }

    [Fact]
    public async Task Latest_AfterSeed_IsTickZero()
    {
        using var api = new TempApi();
        var response = await api.Client.GetAsync("/api/devices/MTR-A1/latest");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonBody.ReadAsync(response);

        Assert.Equal("MTR-A1", body.GetProperty("deviceId").GetString());
        Assert.Equal(0, body.GetProperty("tick").GetInt32());
        Assert.Equal(FleetCatalog.TimestampForTick(0), body.GetProperty("recordedAt").GetDateTimeOffset());
        var readings = body.GetProperty("readings");
        Assert.Equal(5, readings.GetArrayLength());
        Assert.Equal("voltageL1", readings[0].GetProperty("quantity").GetString());
        Assert.Equal("9.1.1.1.0.255", readings[0].GetProperty("obis").GetString());
        Assert.Equal("V", readings[0].GetProperty("unit").GetString());
        Assert.Equal(230d, readings[0].GetProperty("value").GetDouble());
    }

    private static IEnumerable<string> Quantities(JsonElement device)
    {
        return device.GetProperty("quantities").EnumerateArray().Select(item => item.GetString() ?? string.Empty);
    }
}
