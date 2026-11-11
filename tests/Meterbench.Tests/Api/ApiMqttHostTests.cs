using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Meterbench.Api.Hosting;
using Meterbench.Core.Mqtt;
using Meterbench.Tests.Mqtt;
using Microsoft.Extensions.DependencyInjection;

namespace Meterbench.Tests.Api;

public class ApiMqttHostTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static Dictionary<string, string?> Enabled(int port) => new()
    {
        ["Meterbench:Mqtt:Enabled"] = "true",
        ["Meterbench:Mqtt:Port"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Meterbench:Mqtt:ClientId"] = "meterbenchapi",
        ["Meterbench:Mqtt:PollMilliseconds"] = "20",
        ["Meterbench:Mqtt:RetryMilliseconds"] = "50",
        ["Meterbench:Mqtt:ConnectTimeoutMilliseconds"] = "2000"
    };

    [Fact]
    public void DisabledByDefault_PublishesNothing()
    {
        using var api = new TempApi();
        Assert.Equal(0, api.Services.GetRequiredService<MqttPublisherHostedService>().PublishedTicks);
    }

    [Fact]
    public async Task Enabled_PublishesOnConnectAndAfterTicksAndReset()
    {
        await using var broker = new FakeMqttBroker();
        using var api = new TempApi(Enabled(broker.EndPoint.Port));

        var initial = await WaitForPublishesAsync(broker, 3);
        Assert.All(initial, p => Assert.Equal(0, TickOf(p)));
        Assert.Equal("meterbench/MTR-A1/latest", MqttCodec.ReadPublish(initial[0]).Topic);

        (await TempApi.PostAsync(api.Client, "/api/simulator/tick?count=5")).EnsureSuccessStatusCode();
        var afterTick = await WaitForPublishesAsync(broker, 6);
        Assert.All(afterTick.Skip(3), p => Assert.Equal(5, TickOf(p)));

        (await TempApi.PostAsync(api.Client, "/api/seed/reset")).EnsureSuccessStatusCode();
        var afterReset = await WaitForPublishesAsync(broker, 9);
        Assert.All(afterReset.Skip(6), p => Assert.Equal(0, TickOf(p)));

        var connects = broker.Snapshot().Count(p => p.Type == MqttPacketType.Connect);
        Assert.Equal(1, connects);
    }

    [Fact]
    public async Task BrokerStartedLater_IsReachedByRetry()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var api = new TempApi(Enabled(port));
        await Task.Delay(150);
        Assert.Equal(0, api.Services.GetRequiredService<MqttPublisherHostedService>().PublishedTicks);

        await using var broker = new FakeMqttBroker(port: port);
        var published = await WaitForPublishesAsync(broker, 3);
        Assert.Equal(3, published.Length);
    }

    [Theory]
    [InlineData("Meterbench:Mqtt:Host", "10.0.0.5")]
    [InlineData("Meterbench:Mqtt:Host", "broker.example.com")]
    [InlineData("Meterbench:Mqtt:Port", "0")]
    [InlineData("Meterbench:Mqtt:ClientId", "has-dash")]
    [InlineData("Meterbench:Mqtt:TopicPrefix", "lab/#")]
    [InlineData("Meterbench:Mqtt:PollMilliseconds", "1")]
    public void InvalidSettings_FailStartup(string key, string value)
    {
        var settings = new Dictionary<string, string?> { ["Meterbench:Mqtt:Enabled"] = "true", [key] = value };
        var path = Path.Combine(Path.GetTempPath(), "meterbench-" + Guid.NewGuid().ToString("N") + ".db");
        using var factory = new MeterbenchApiFactory(path, settings);
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static int TickOf(MqttPacket publish)
    {
        using var doc = JsonDocument.Parse(MqttCodec.ReadPublish(publish).Payload);
        return doc.RootElement.GetProperty("tick").GetInt32();
    }

    private static async Task<MqttPacket[]> WaitForPublishesAsync(FakeMqttBroker broker, int count)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            var publishes = broker.Snapshot().Where(p => p.Type == MqttPacketType.Publish).ToArray();
            if (publishes.Length >= count)
            {
                return publishes;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Expected {count} publishes, got {publishes.Length}.");
            await Task.Delay(20);
        }
    }
}
