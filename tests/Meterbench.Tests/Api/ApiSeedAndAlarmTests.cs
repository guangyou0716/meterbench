using System.Net;
using System.Text;

namespace Meterbench.Tests.Api;

public class ApiSeedAndAlarmTests
{
    [Fact]
    public async Task Reset_ThenTheSameTickCount_RepeatsReadings()
    {
        using var api = new TempApi();
        var firstReset = await JsonBody.ReadAsync(await TempApi.PostAsync(api.Client, "/api/seed/reset"));
        Assert.Equal(20261111, firstReset.GetProperty("seed").GetInt32());
        Assert.Equal(0, firstReset.GetProperty("tick").GetInt32());
        Assert.Equal(3, firstReset.GetProperty("deviceCount").GetInt32());

        await Tick(api, 15);
        var first = await SnapshotAsync(api.Client);

        var secondReset = await JsonBody.ReadAsync(await TempApi.PostAsync(api.Client, "/api/seed/reset"));
        Assert.Equal(0, secondReset.GetProperty("tick").GetInt32());
        await Tick(api, 15);
        var second = await SnapshotAsync(api.Client);

        Assert.Equal(first, second);

        var meterA = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/devices/MTR-A1/latest"));
        var meterB = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/devices/MTR-A2/latest"));
        Assert.Equal(15, meterA.GetProperty("tick").GetInt32());
        Assert.NotEqual(
            meterA.GetProperty("readings")[0].GetProperty("value").GetDouble(),
            meterB.GetProperty("readings")[0].GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task AckFlow_RaisesOnce_Acknowledges_ThenClearsOnHysteresis()
    {
        using var api = new TempApi();
        Assert.Equal(HttpStatusCode.OK, (await TempApi.PostAsync(api.Client, "/api/seed/reset")).StatusCode);

        var quiet = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/alarms?active=true"));
        Assert.Equal(0, quiet.GetArrayLength());

        await Tick(api, 4);
        var stillQuiet = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/alarms?active=true"));
        Assert.Equal(0, stillQuiet.GetArrayLength());

        await Tick(api, 1);
        var raised = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/alarms?active=true"));
        Assert.Equal(1, raised.GetArrayLength());
        var alarm = raised[0];
        Assert.Equal("LC-B1", alarm.GetProperty("deviceId").GetString());
        Assert.Equal("cabinetTemperature", alarm.GetProperty("quantity").GetString());
        Assert.Equal("high", alarm.GetProperty("direction").GetString());
        Assert.Equal(40d, alarm.GetProperty("threshold").GetDouble());
        Assert.Equal(1.5d, alarm.GetProperty("hysteresis").GetDouble());
        Assert.Equal(41d, alarm.GetProperty("triggerValue").GetDouble());
        Assert.Equal(5, alarm.GetProperty("raisedAtTick").GetInt32());
        Assert.True(alarm.GetProperty("active").GetBoolean());
        Assert.False(alarm.GetProperty("acknowledged").GetBoolean());
        var id = alarm.GetProperty("id").GetInt64();

        var missing = await TempApi.PostAsync(api.Client, "/api/alarms/999999/ack");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        JsonBody.AssertError(await JsonBody.ReadAsync(missing), "AlarmNotFound");

        var ack = await TempApi.PostAsync(api.Client, $"/api/alarms/{id}/ack");
        Assert.Equal(HttpStatusCode.OK, ack.StatusCode);
        var acked = await JsonBody.ReadAsync(ack);
        Assert.True(acked.GetProperty("acknowledged").GetBoolean());
        Assert.True(acked.GetProperty("active").GetBoolean());
        var acknowledgedAt = acked.GetProperty("acknowledgedAt").GetDateTimeOffset();

        var ackAgain = await JsonBody.ReadAsync(await TempApi.PostAsync(api.Client, $"/api/alarms/{id}/ack"));
        Assert.Equal(acknowledgedAt, ackAgain.GetProperty("acknowledgedAt").GetDateTimeOffset());

        await Tick(api, 2);
        var held = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/alarms?active=true"));
        Assert.Equal(1, held.GetArrayLength());
        Assert.Equal(id, held[0].GetProperty("id").GetInt64());
        Assert.True(held[0].GetProperty("acknowledged").GetBoolean());

        await Tick(api, 1);
        var active = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/alarms?active=true"));
        Assert.Equal(0, active.GetArrayLength());

        var inactive = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/alarms?active=false"));
        Assert.Equal(1, inactive.GetArrayLength());
        Assert.False(inactive[0].GetProperty("active").GetBoolean());
        Assert.True(inactive[0].GetProperty("acknowledged").GetBoolean());
        Assert.Equal(8, inactive[0].GetProperty("clearedAtTick").GetInt32());
        Assert.Equal(38.5d, inactive[0].GetProperty("clearValue").GetDouble());

        var all = await JsonBody.ReadAsync(await api.Client.GetAsync("/api/alarms"));
        Assert.Equal(1, all.GetArrayLength());
    }

    private static async Task Tick(TempApi api, int count)
    {
        var response = await TempApi.PostAsync(api.Client, $"/api/simulator/tick?count={count}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> SnapshotAsync(HttpClient client)
    {
        var builder = new StringBuilder();
        foreach (var id in new[] { "MTR-A1", "MTR-A2", "LC-B1" })
        {
            var latest = await client.GetAsync($"/api/devices/{id}/latest");
            latest.EnsureSuccessStatusCode();
            builder.Append(id);
            builder.Append('=');
            builder.AppendLine(await latest.Content.ReadAsStringAsync());
        }

        var history = await client.GetAsync("/api/devices/MTR-A1/history?limit=15");
        history.EnsureSuccessStatusCode();
        builder.AppendLine(await history.Content.ReadAsStringAsync());
        return builder.ToString();
    }
}
