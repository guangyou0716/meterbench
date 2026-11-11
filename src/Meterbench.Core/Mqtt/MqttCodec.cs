using System.Buffers.Binary;
using System.Text;

namespace Meterbench.Core.Mqtt;

/// <summary>MQTT 3.1.1 control packet types this project uses (high nibble of the fixed header).</summary>
public enum MqttPacketType : byte
{
    Connect = 1,
    ConnAck = 2,
    Publish = 3,
    PingReq = 12,
    PingResp = 13,
    Disconnect = 14,
}

/// <summary>One decoded control packet: type, the fixed-header flag nibble, and the variable header + payload.</summary>
public readonly record struct MqttPacket(MqttPacketType Type, byte Flags, ReadOnlyMemory<byte> Body);

/// <summary>
/// Minimal MQTT 3.1.1 codec for a publish-only client: CONNECT (clean session, no credentials, no will),
/// CONNACK, PUBLISH at QoS 0 without retain, PINGREQ and DISCONNECT. Nothing else is supported.
/// </summary>
public static class MqttCodec
{
    /// <summary>Largest value the variable-length "remaining length" field can carry (4 bytes).</summary>
    public const int MaxRemainingLength = 268_435_455;

    /// <summary>Protocol level 4 is MQTT 3.1.1.</summary>
    public const byte ProtocolLevel = 4;

    private const byte CleanSessionFlag = 0x02;

    /// <summary>Writes the variable-length remaining length (1 to 4 bytes, 7 bits each, low group first).</summary>
    public static byte[] EncodeRemainingLength(int length)
    {
        if (length is < 0 or > MaxRemainingLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, $"Remaining length must be 0..{MaxRemainingLength}.");
        }

        var bytes = new List<byte>(4);
        do
        {
            var digit = (byte)(length % 128);
            length /= 128;
            if (length > 0)
            {
                digit |= 0x80;
            }

            bytes.Add(digit);
        }
        while (length > 0);

        return bytes.ToArray();
    }

    /// <summary>
    /// Reads a remaining length that starts at <paramref name="source"/>[0]. Returns false when more bytes
    /// are needed; throws <see cref="FormatException"/> for a field longer than four bytes.
    /// </summary>
    public static bool TryDecodeRemainingLength(ReadOnlySpan<byte> source, out int length, out int bytesUsed)
    {
        length = 0;
        bytesUsed = 0;
        var multiplier = 1;
        for (var i = 0; i < 4; i++)
        {
            if (i >= source.Length)
            {
                return false;
            }

            var digit = source[i];
            length += (digit & 0x7F) * multiplier;
            if ((digit & 0x80) == 0)
            {
                bytesUsed = i + 1;
                return true;
            }

            multiplier *= 128;
        }

        throw new FormatException("MQTT remaining length is longer than four bytes.");
    }

    /// <summary>
    /// Client identifier rules this client enforces: 1..23 characters of 0-9, a-z, A-Z, which every
    /// 3.1.1 broker must accept.
    /// </summary>
    public static void ValidateClientId(string clientId)
    {
        ArgumentNullException.ThrowIfNull(clientId);
        if (clientId.Length is < 1 or > 23 || !clientId.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("MQTT client id must be 1..23 ASCII letters or digits.", nameof(clientId));
        }
    }

    /// <summary>A publish topic: non-empty, no wildcards (+ #), no NUL, at most 65535 UTF-8 bytes.</summary>
    public static void ValidatePublishTopic(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        if (topic.Length == 0 || topic.IndexOfAny(['+', '#', '\0']) >= 0 || Encoding.UTF8.GetByteCount(topic) > ushort.MaxValue)
        {
            throw new ArgumentException("MQTT publish topic must be non-empty UTF-8 without + # or NUL, at most 65535 bytes.", nameof(topic));
        }
    }

    public static byte[] EncodeConnect(string clientId, ushort keepAliveSeconds)
    {
        ValidateClientId(clientId);
        var body = new List<byte>();
        AppendString(body, "MQTT");
        body.Add(ProtocolLevel);
        body.Add(CleanSessionFlag);
        AppendUInt16(body, keepAliveSeconds);
        AppendString(body, clientId);
        return Frame(MqttPacketType.Connect, 0, body);
    }

    /// <summary>PUBLISH at QoS 0, DUP 0, RETAIN 0: topic name then payload, no packet identifier.</summary>
    public static byte[] EncodePublish(string topic, ReadOnlySpan<byte> payload)
    {
        ValidatePublishTopic(topic);
        var body = new List<byte>(payload.Length + topic.Length + 2);
        AppendString(body, topic);
        body.AddRange(payload.ToArray());
        return Frame(MqttPacketType.Publish, 0, body);
    }

    public static byte[] EncodePingReq() => [(byte)MqttPacketType.PingReq << 4, 0];

    public static byte[] EncodeDisconnect() => [(byte)MqttPacketType.Disconnect << 4, 0];

    /// <summary>
    /// Splits one complete packet off the front of <paramref name="buffer"/>. Returns false when more
    /// bytes are needed. Throws <see cref="FormatException"/> for an unknown type or a bad length field.
    /// </summary>
    public static bool TryReadPacket(ReadOnlyMemory<byte> buffer, out MqttPacket packet, out int consumed)
    {
        packet = default;
        consumed = 0;
        var span = buffer.Span;
        if (span.Length < 2)
        {
            return false;
        }

        var type = (MqttPacketType)(span[0] >> 4);
        if (!Enum.IsDefined(type))
        {
            throw new FormatException($"Unsupported MQTT packet type {span[0] >> 4}.");
        }

        if (!TryDecodeRemainingLength(span[1..], out var remaining, out var lengthBytes))
        {
            return false;
        }

        var total = 1 + lengthBytes + remaining;
        if (span.Length < total)
        {
            return false;
        }

        packet = new MqttPacket(type, (byte)(span[0] & 0x0F), buffer.Slice(1 + lengthBytes, remaining));
        consumed = total;
        return true;
    }

    /// <summary>
    /// CONNACK return code (0 = accepted). Throws <see cref="FormatException"/> when the packet is not a
    /// well-formed CONNACK.
    /// </summary>
    public static byte ReadConnAckReturnCode(MqttPacket packet)
    {
        if (packet.Type != MqttPacketType.ConnAck || packet.Flags != 0 || packet.Body.Length != 2)
        {
            throw new FormatException("Expected a 2-byte CONNACK.");
        }

        return packet.Body.Span[1];
    }

    /// <summary>Topic and payload of a QoS 0 PUBLISH (used by tests and the in-test broker).</summary>
    public static (string Topic, ReadOnlyMemory<byte> Payload) ReadPublish(MqttPacket packet)
    {
        if (packet.Type != MqttPacketType.Publish || (packet.Flags & 0x06) != 0)
        {
            throw new FormatException("Expected a QoS 0 PUBLISH.");
        }

        var span = packet.Body.Span;
        if (span.Length < 2)
        {
            throw new FormatException("PUBLISH is missing its topic length.");
        }

        var topicLength = BinaryPrimitives.ReadUInt16BigEndian(span);
        if (span.Length < 2 + topicLength)
        {
            throw new FormatException("PUBLISH topic is truncated.");
        }

        var topic = Encoding.UTF8.GetString(span.Slice(2, topicLength));
        return (topic, packet.Body[(2 + topicLength)..]);
    }

    private static byte[] Frame(MqttPacketType type, byte flags, List<byte> body)
    {
        var length = EncodeRemainingLength(body.Count);
        var frame = new byte[1 + length.Length + body.Count];
        frame[0] = (byte)(((byte)type << 4) | (flags & 0x0F));
        length.CopyTo(frame, 1);
        body.CopyTo(frame, 1 + length.Length);
        return frame;
    }

    private static void AppendUInt16(List<byte> target, ushort value)
    {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    private static void AppendString(List<byte> target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        AppendUInt16(target, checked((ushort)bytes.Length));
        target.AddRange(bytes);
    }
}
