using System.Net;
using Meterbench.Core.Fleet;
using Meterbench.Core.Mqtt;

namespace Meterbench.Tests.Mqtt;

/// <summary>
/// Publishes one fleet tick to a real broker. Runs only when MQTT_SMOKE_BROKER is set to "ip:port"
/// (CI starts a throwaway Mosquitto for it); otherwise it returns immediately.
/// </summary>
public class MqttBrokerSmokeTests
{
    [Fact]
    [Trait("Category", "MqttSmoke")]
    public async Task PublishesOneFleetTickToARealBroker()
    {
        var target = Environment.GetEnvironmentVariable("MQTT_SMOKE_BROKER");
        if (string.IsNullOrWhiteSpace(target))
        {
            return;
        }

        var broker = IPEndPoint.Parse(target);
        await using var publisher = await MqttPublisher.ConnectAsync(broker, "meterbenchsmoke", TimeSpan.FromSeconds(10));
        await FleetMqttMessages.PublishFleetAsync(publisher, FleetCatalog.DefaultSeed, 5);
    }
}
