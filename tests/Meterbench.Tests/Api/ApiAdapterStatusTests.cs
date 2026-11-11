using Meterbench.Tests.Mqtt;

namespace Meterbench.Tests.Api;

public class ApiAdapterStatusTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Defaults_ReportBothAdaptersDisabled()
    {
        using var api = new TempApi();
        var body = await GetStatusAsync(api);

        Assert.Equal(new[] { "modbus", "mqtt" }, body.EnumerateObject().Select(p => p.Name).ToArray());
        var modbus = body.GetProperty("modbus");
        Assert.Equal(new[] { "enabled", "listening", "port" }, modbus.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.False(modbus.GetProperty("enabled").GetBoolean());
        Assert.False(modbus.GetProperty("listening").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, modbus.GetProperty("port").ValueKind);

        var mqtt = body.GetProperty("mqtt");
        Assert.Equal(
            new[] { "enabled", "connected", "broker", "topicPrefix", "publishedTicks" },
            mqtt.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.False(mqtt.GetProperty("enabled").GetBoolean());
        Assert.False(mqtt.GetProperty("connected").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, mqtt.GetProperty("broker").ValueKind);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, mqtt.GetProperty("topicPrefix").ValueKind);
        Assert.Equal(0, mqtt.GetProperty("publishedTicks").GetInt64());
    }

    [Fact]
    public async Task ModbusEnabled_ReportsListeningPort()
    {
        using var api = new TempApi(new Dictionary<string, string?>
        {
            ["Meterbench:Modbus:Enabled"] = "true",
            ["Meterbench:Modbus:Port"] = "0"
        });
        var modbus = (await GetStatusAsync(api)).GetProperty("modbus");

        Assert.True(modbus.GetProperty("enabled").GetBoolean());
        Assert.True(modbus.GetProperty("listening").GetBoolean());
        Assert.InRange(modbus.GetProperty("port").GetInt32(), 1, 65535);
    }

    [Fact]
    public async Task MqttEnabled_ReportsConnectedAndPublishedTicks()
    {
        await using var broker = new FakeMqttBroker();
        using var api = new TempApi(MqttEnabled(broker.EndPoint.Port));

        var mqtt = await WaitForMqttAsync(api, m => m.GetProperty("publishedTicks").GetInt64() >= 1);
        Assert.True(mqtt.GetProperty("enabled").GetBoolean());
        Assert.True(mqtt.GetProperty("connected").GetBoolean());
        Assert.Equal($"127.0.0.1:{broker.EndPoint.Port}", mqtt.GetProperty("broker").GetString());
        Assert.Equal("meterbench", mqtt.GetProperty("topicPrefix").GetString());
    }

    [Fact]
    public async Task MqttEnabled_BrokerMissing_ReportsNotConnected()
    {
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var api = new TempApi(MqttEnabled(port));
        await Task.Delay(150);
        var mqtt = (await GetStatusAsync(api)).GetProperty("mqtt");

        Assert.True(mqtt.GetProperty("enabled").GetBoolean());
        Assert.False(mqtt.GetProperty("connected").GetBoolean());
        Assert.Equal(0, mqtt.GetProperty("publishedTicks").GetInt64());
    }

    [Fact]
    public async Task MqttBrokerStops_ConnectedReturnsToFalse()
    {
        var broker = new FakeMqttBroker();
        using var api = new TempApi(MqttEnabled(broker.EndPoint.Port));
        await WaitForMqttAsync(api, m => m.GetProperty("connected").GetBoolean());

        await broker.DisposeAsync();
        // Force a publish so the dropped connection is noticed.
        (await TempApi.PostAsync(api.Client, "/api/simulator/tick?count=1")).EnsureSuccessStatusCode();

        var mqtt = await WaitForMqttAsync(api, m => !m.GetProperty("connected").GetBoolean());
        Assert.False(mqtt.GetProperty("connected").GetBoolean());
    }

    private static Dictionary<string, string?> MqttEnabled(int port) => new()
    {
        ["Meterbench:Mqtt:Enabled"] = "true",
        ["Meterbench:Mqtt:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Meterbench:Mqtt:ClientId"] = "meterbenchstatus",
        ["Meterbench:Mqtt:PollMilliseconds"] = "20",
        ["Meterbench:Mqtt:RetryMilliseconds"] = "50",
        ["Meterbench:Mqtt:ConnectTimeoutMilliseconds"] = "500"
    };

    private static async Task<System.Text.Json.JsonElement> GetStatusAsync(TempApi api)
    {
        var response = await api.Client.GetAsync("/api/adapters");
        response.EnsureSuccessStatusCode();
        return await JsonBody.ReadAsync(response);
    }

    private static async Task<System.Text.Json.JsonElement> WaitForMqttAsync(
        TempApi api, Func<System.Text.Json.JsonElement, bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var mqtt = (await GetStatusAsync(api)).GetProperty("mqtt");
            if (condition(mqtt))
            {
                return mqtt;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"MQTT status condition not met: {mqtt}");
            }

            await Task.Delay(25);
        }
    }
}
