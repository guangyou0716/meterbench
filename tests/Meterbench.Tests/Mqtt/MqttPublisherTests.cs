using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Meterbench.Core.Fleet;
using Meterbench.Core.Mqtt;

namespace Meterbench.Tests.Mqtt;

public class MqttPublisherTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task PublishFleet_SendsConnectThreePublishesAndDisconnect()
    {
        await using var broker = new FakeMqttBroker();
        await using (var publisher = await MqttPublisher.ConnectAsync(broker.EndPoint, "meterbenchtest", Timeout))
        {
            await FleetMqttMessages.PublishFleetAsync(publisher, FleetCatalog.DefaultSeed, 5);
        }

        await broker.Finished.Task.WaitAsync(Timeout);
        Assert.Equal(
            [MqttPacketType.Connect, MqttPacketType.Publish, MqttPacketType.Publish, MqttPacketType.Publish, MqttPacketType.Disconnect],
            broker.Received.Select(p => p.Type));

        var publishes = broker.Received.Where(p => p.Type == MqttPacketType.Publish).Select(MqttCodec.ReadPublish).ToArray();
        Assert.Equal(["meterbench/MTR-A1/latest", "meterbench/MTR-A2/latest", "meterbench/LC-B1/latest"], publishes.Select(p => p.Topic));

        using var doc = JsonDocument.Parse(publishes[2].Payload);
        var temperature = doc.RootElement.GetProperty("readings").EnumerateArray()
            .Single(r => r.GetProperty("quantity").GetString() == QuantityCodes.CabinetTemperature);
        Assert.Equal(41.0, temperature.GetProperty("value").GetDouble());
    }

    [Fact]
    public async Task RefusedConnAck_ThrowsWithReturnCode()
    {
        await using var broker = new FakeMqttBroker(connAckCode: 5);
        var ex = await Assert.ThrowsAsync<MqttConnectException>(() => MqttPublisher.ConnectAsync(broker.EndPoint, "mb", Timeout));
        Assert.Equal((byte)5, ex.ReturnCode);
    }

    [Fact]
    public async Task SilentBroker_TimesOut()
    {
        await using var broker = new FakeMqttBroker(connAckCode: null);
        var ex = await Assert.ThrowsAsync<MqttConnectException>(
            () => MqttPublisher.ConnectAsync(broker.EndPoint, "mb", TimeSpan.FromMilliseconds(200)));
        Assert.Null(ex.ReturnCode);
        Assert.Contains("No CONNACK", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonConnAckReply_IsAProtocolError()
    {
        await using var broker = new FakeMqttBroker(rawReply: MqttCodec.EncodePublish("x", Encoding.ASCII.GetBytes("?")));
        var ex = await Assert.ThrowsAsync<MqttConnectException>(() => MqttPublisher.ConnectAsync(broker.EndPoint, "mb", Timeout));
        Assert.Contains("did not answer CONNECT with a CONNACK", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoListener_FailsToConnect()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var closedPort = (IPEndPoint)probe.LocalEndpoint;
        probe.Stop();

        await Assert.ThrowsAsync<SocketException>(() => MqttPublisher.ConnectAsync(closedPort, "mb", Timeout));
    }

    [Fact]
    public async Task PublishAfterDispose_Throws()
    {
        await using var broker = new FakeMqttBroker();
        var publisher = await MqttPublisher.ConnectAsync(broker.EndPoint, "mb", Timeout);
        await publisher.DisposeAsync();
        await publisher.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => publisher.PublishAsync("t", new byte[1]).AsTask());
    }
}

public class MqttPublisherPingTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Ping_IsAnsweredWithPingResp()
    {
        await using var broker = new FakeMqttBroker();
        await using (var publisher = await MqttPublisher.ConnectAsync(broker.EndPoint, "mb", Timeout))
        {
            await publisher.PingAsync(Timeout);
            await publisher.PingAsync(Timeout);
        }

        await broker.Finished.Task.WaitAsync(Timeout);
        Assert.Equal(
            [MqttPacketType.Connect, MqttPacketType.PingReq, MqttPacketType.PingReq, MqttPacketType.Disconnect],
            broker.Snapshot().Select(p => p.Type));
    }

    [Fact]
    public async Task Ping_Unanswered_TimesOutAsIOException()
    {
        await using var broker = new FakeMqttBroker(answerPings: false);
        await using var publisher = await MqttPublisher.ConnectAsync(broker.EndPoint, "mb", Timeout);
        var ex = await Assert.ThrowsAsync<IOException>(() => publisher.PingAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Contains("No PINGRESP", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ping_AnsweredWithOtherPacket_IsIOException()
    {
        await using var broker = new FakeMqttBroker(pingReply: [0x20, 0x02, 0x00, 0x00]);
        await using var publisher = await MqttPublisher.ConnectAsync(broker.EndPoint, "mb", Timeout);
        var ex = await Assert.ThrowsAsync<IOException>(() => publisher.PingAsync(Timeout));
        Assert.Contains("instead of PINGRESP", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ping_MalformedReply_IsIOException()
    {
        await using var broker = new FakeMqttBroker(pingReply: [0x80, 0x00]);
        await using var publisher = await MqttPublisher.ConnectAsync(broker.EndPoint, "mb", Timeout);
        var ex = await Assert.ThrowsAsync<IOException>(() => publisher.PingAsync(Timeout));
        Assert.Contains("malformed reply before PINGRESP", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ping_BrokerClosedConnection_IsIOException()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var serve = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var buffer = new byte[256];
                _ = await stream.ReadAsync(buffer);
                await stream.WriteAsync(new byte[] { 0x20, 0x02, 0x00, 0x00 });
            });

            await using var publisher = await MqttPublisher.ConnectAsync((IPEndPoint)listener.LocalEndpoint, "mb", Timeout);
            await serve.WaitAsync(Timeout);
            await Assert.ThrowsAnyAsync<IOException>(() => publisher.PingAsync(Timeout));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task PingAfterDispose_Throws()
    {
        await using var broker = new FakeMqttBroker();
        var publisher = await MqttPublisher.ConnectAsync(broker.EndPoint, "mb", Timeout);
        await publisher.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => publisher.PingAsync(Timeout));
    }
}

public class MqttPublisherMalformedReplyTests
{
    [Fact]
    public async Task UnknownPacketTypeBeforeConnAck_IsAConnectError()
    {
        await using var broker = new FakeMqttBroker(rawReply: [0x80, 0x00]);
        var ex = await Assert.ThrowsAsync<MqttConnectException>(
            () => MqttPublisher.ConnectAsync(broker.EndPoint, "mb", TimeSpan.FromSeconds(5)));
        Assert.Contains("malformed", ex.Message, StringComparison.Ordinal);
    }
}
