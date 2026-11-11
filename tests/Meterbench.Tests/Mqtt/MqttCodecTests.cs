using System.Text;
using Meterbench.Core.Mqtt;

namespace Meterbench.Tests.Mqtt;

public class MqttCodecTests
{
    [Theory]
    [InlineData(0, new byte[] { 0x00 })]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0x80, 0x01 })]
    [InlineData(16_383, new byte[] { 0xFF, 0x7F })]
    [InlineData(16_384, new byte[] { 0x80, 0x80, 0x01 })]
    [InlineData(2_097_152, new byte[] { 0x80, 0x80, 0x80, 0x01 })]
    [InlineData(268_435_455, new byte[] { 0xFF, 0xFF, 0xFF, 0x7F })]
    public void RemainingLength_MatchesSpecTableAndRoundTrips(int value, byte[] expected)
    {
        Assert.Equal(expected, MqttCodec.EncodeRemainingLength(value));
        Assert.True(MqttCodec.TryDecodeRemainingLength(expected, out var decoded, out var used));
        Assert.Equal(value, decoded);
        Assert.Equal(expected.Length, used);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(268_435_456)]
    public void RemainingLength_OutOfRange_Throws(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MqttCodec.EncodeRemainingLength(value));
    }

    [Fact]
    public void RemainingLength_FiveBytes_IsMalformed()
    {
        Assert.Throws<FormatException>(() => MqttCodec.TryDecodeRemainingLength(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x01 }, out _, out _));
    }

    [Fact]
    public void RemainingLength_Truncated_NeedsMoreBytes()
    {
        Assert.False(MqttCodec.TryDecodeRemainingLength(new byte[] { 0x80, 0x80 }, out _, out _));
    }

    [Fact]
    public void Connect_IsCleanSessionMqtt311WithoutCredentials()
    {
        var frame = MqttCodec.EncodeConnect("mb1", 30);
        byte[] expected =
        [
            0x10, 15,
            0x00, 0x04, (byte)'M', (byte)'Q', (byte)'T', (byte)'T',
            0x04, 0x02, 0x00, 0x1E,
            0x00, 0x03, (byte)'m', (byte)'b', (byte)'1',
        ];
        Assert.Equal(expected, frame);
    }

    [Fact]
    public void Publish_IsQos0WithoutPacketId()
    {
        var frame = MqttCodec.EncodePublish("a/b", Encoding.ASCII.GetBytes("hi"));
        Assert.Equal(new byte[] { 0x30, 7, 0x00, 0x03, (byte)'a', (byte)'/', (byte)'b', (byte)'h', (byte)'i' }, frame);

        Assert.True(MqttCodec.TryReadPacket(frame, out var packet, out var consumed));
        Assert.Equal(frame.Length, consumed);
        var (topic, payload) = MqttCodec.ReadPublish(packet);
        Assert.Equal("a/b", topic);
        Assert.Equal("hi", Encoding.ASCII.GetString(payload.Span));
    }

    [Fact]
    public void Publish_LargePayload_UsesMultiByteLength()
    {
        var payload = new byte[300];
        var frame = MqttCodec.EncodePublish("t", payload);
        // 2 (topic length) + 1 (topic) + 300 = 303 = 0xAF 0x02
        Assert.Equal(new byte[] { 0x30, 0xAF, 0x02 }, frame[..3]);
        Assert.Equal(3 + 303, frame.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("meterbench/+/latest")]
    [InlineData("meterbench/#")]
    [InlineData("bad\0topic")]
    public void Publish_RejectsInvalidTopics(string topic)
    {
        Assert.Throws<ArgumentException>(() => MqttCodec.EncodePublish(topic, ReadOnlySpan<byte>.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has-dash")]
    [InlineData("abcdefghijklmnopqrstuvwx")]
    public void Connect_RejectsClientIdsOutsideTheGuaranteedSet(string clientId)
    {
        Assert.Throws<ArgumentException>(() => MqttCodec.EncodeConnect(clientId, 60));
    }

    [Fact]
    public void Disconnect_AndPingReq_AreTwoBytes()
    {
        Assert.Equal(new byte[] { 0xE0, 0x00 }, MqttCodec.EncodeDisconnect());
        Assert.Equal(new byte[] { 0xC0, 0x00 }, MqttCodec.EncodePingReq());
    }

    [Fact]
    public void ConnAck_ReturnCodeIsRead()
    {
        Assert.True(MqttCodec.TryReadPacket(new byte[] { 0x20, 0x02, 0x00, 0x05 }, out var packet, out _));
        Assert.Equal((byte)5, MqttCodec.ReadConnAckReturnCode(packet));
    }

    [Fact]
    public void ConnAck_WrongShape_Throws()
    {
        Assert.True(MqttCodec.TryReadPacket(new byte[] { 0x20, 0x01, 0x00 }, out var packet, out _));
        Assert.Throws<FormatException>(() => MqttCodec.ReadConnAckReturnCode(packet));
    }

    [Fact]
    public void ReadPacket_WaitsForWholeFrame_AndLeavesTheRest()
    {
        var first = MqttCodec.EncodePublish("x", new byte[] { 1, 2, 3 });
        Assert.False(MqttCodec.TryReadPacket(first.AsMemory(0, first.Length - 1), out _, out _));

        var both = first.Concat(MqttCodec.EncodeDisconnect()).ToArray();
        Assert.True(MqttCodec.TryReadPacket(both, out var packet, out var consumed));
        Assert.Equal(MqttPacketType.Publish, packet.Type);
        Assert.Equal(first.Length, consumed);
        Assert.True(MqttCodec.TryReadPacket(both.AsMemory(consumed), out var next, out _));
        Assert.Equal(MqttPacketType.Disconnect, next.Type);
    }

    [Fact]
    public void ReadPacket_UnknownType_Throws()
    {
        Assert.Throws<FormatException>(() => MqttCodec.TryReadPacket(new byte[] { 0x80, 0x00 }, out _, out _));
    }

    [Fact]
    public void ReadPublish_RejectsQos1()
    {
        Assert.True(MqttCodec.TryReadPacket(new byte[] { 0x32, 0x03, 0x00, 0x01, (byte)'t' }, out var packet, out _));
        Assert.Throws<FormatException>(() => MqttCodec.ReadPublish(packet));
    }
}
