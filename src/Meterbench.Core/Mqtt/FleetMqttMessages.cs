using System.Globalization;
using System.Text.Json;
using Meterbench.Core.Fleet;
using Meterbench.Core.Simulation;

namespace Meterbench.Core.Mqtt;

/// <summary>One MQTT message ready to publish.</summary>
public sealed record MqttMessage(string Topic, byte[] Payload);

/// <summary>
/// Builds one "latest reading" message per device for a (seed, tick): topic <c>{prefix}/{deviceId}/latest</c>,
/// JSON payload <c>{"deviceId","tick","recordedAt","readings":[{"quantity","unit","value"}]}</c>.
/// Values come from <see cref="ReadingSimulator"/>, so the same seed and tick always give the same bytes.
/// </summary>
public static class FleetMqttMessages
{
    public const string DefaultTopicPrefix = "meterbench";

    public static string TopicFor(string prefix, string deviceId)
    {
        var topic = $"{prefix}/{deviceId}/latest";
        MqttCodec.ValidatePublishTopic(topic);
        return topic;
    }

    public static MqttMessage Build(int seed, int tick, DeviceDefinition device, string prefix = DefaultTopicPrefix)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        var topic = TopicFor(prefix, device.Id);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("deviceId", device.Id);
            json.WriteNumber("tick", tick);
            json.WriteString("recordedAt", FleetCatalog.TimestampForTick(tick).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            json.WriteStartArray("readings");
            foreach (var quantity in device.Quantities)
            {
                json.WriteStartObject();
                json.WriteString("quantity", quantity.Code);
                json.WriteString("unit", quantity.Unit);
                json.WriteNumber("value", ReadingSimulator.ValueAt(seed, device.Id, quantity, tick));
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return new MqttMessage(topic, buffer.ToArray());
    }

    public static IReadOnlyList<MqttMessage> BuildFleet(int seed, int tick, string prefix = DefaultTopicPrefix)
    {
        return FleetCatalog.Devices.Select(device => Build(seed, tick, device, prefix)).ToArray();
    }

    /// <summary>Publishes every device's message for (seed, tick) in fleet order.</summary>
    public static async Task PublishFleetAsync(
        Adapters.IMqttReadingPublisher publisher,
        int seed,
        int tick,
        string prefix = DefaultTopicPrefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        foreach (var message in BuildFleet(seed, tick, prefix))
        {
            await publisher.PublishAsync(message.Topic, message.Payload, cancellationToken).ConfigureAwait(false);
        }
    }
}
