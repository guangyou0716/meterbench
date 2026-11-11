using System.Buffers.Binary;
using System.Text;

namespace Meterbench.Core.Framing;

/// <summary>
/// Unofficial DLMS-like tutorial frame. Not DLMS/COSEM, HDLC, or a meter vendor frame.
/// Layout: 0x7E | length | payload | CRC-16/X-25 little-endian | 0x7E.
/// Length is the payload size only. CRC covers the length byte and the payload.
/// Payload: id length (1..16), ASCII device id, 6-byte OBIS-like code, int8 scale, int32 big-endian scaled value.
/// </summary>
public static class FrameCodec
{
    public const byte Flag = 0x7E;
    public const int MinDeviceIdLength = 1;
    public const int MaxDeviceIdLength = 16;
    public const int FixedPayloadTail = 6 + 1 + 4;

    public static byte[] Encode(string deviceId, ObisCode obis, sbyte scale, int scaledValue)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        ArgumentNullException.ThrowIfNull(obis);
        if (deviceId.Length < MinDeviceIdLength || deviceId.Length > MaxDeviceIdLength)
        {
            throw new ArgumentException("Device id length must be 1..16.", nameof(deviceId));
        }

        foreach (var ch in deviceId)
        {
            if (ch is < (char)0x20 or > (char)0x7E)
            {
                throw new ArgumentException("Device id must be printable ASCII.", nameof(deviceId));
            }
        }

        var idBytes = Encoding.ASCII.GetBytes(deviceId);
        var payloadLength = 1 + idBytes.Length + FixedPayloadTail;
        var payload = new byte[payloadLength];
        payload[0] = (byte)idBytes.Length;
        idBytes.CopyTo(payload, 1);
        var offset = 1 + idBytes.Length;
        payload[offset++] = obis.A;
        payload[offset++] = obis.B;
        payload[offset++] = obis.C;
        payload[offset++] = obis.D;
        payload[offset++] = obis.E;
        payload[offset++] = obis.F;
        payload[offset++] = unchecked((byte)scale);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset), scaledValue);

        var frame = new byte[1 + 1 + payloadLength + 2 + 1];
        frame[0] = Flag;
        frame[1] = (byte)payloadLength;
        payload.CopyTo(frame, 2);
        var crc = Crc16X25.Compute(frame.AsSpan(1, 1 + payloadLength));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2 + payloadLength), crc);
        frame[^1] = Flag;
        return frame;
    }

    public static FrameParseResult Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return Failure(FrameErrorKind.Truncated, "Frame is empty.");
        }

        if (data[0] != Flag)
        {
            return Failure(FrameErrorKind.BadStart, "Frame does not start with 0x7E.");
        }

        if (data.Length < 2)
        {
            return Failure(FrameErrorKind.Truncated, "Frame ends before the length byte.");
        }

        var payloadLength = data[1];
        var expected = 1 + 1 + payloadLength + 2 + 1;
        if (data.Length < expected)
        {
            return Failure(
                FrameErrorKind.Truncated,
                $"Frame is truncated; length byte {payloadLength} needs {expected} bytes, got {data.Length}.");
        }

        if (data.Length > expected)
        {
            return Failure(
                FrameErrorKind.LengthMismatch,
                $"Length byte {payloadLength} does not match the {data.Length}-byte buffer.");
        }

        if (data[expected - 1] != Flag)
        {
            return Failure(FrameErrorKind.BadEnd, "Frame does not end with 0x7E.");
        }

        var crcExpected = Crc16X25.Compute(data.Slice(1, 1 + payloadLength));
        var crcActual = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2 + payloadLength, 2));
        if (crcExpected != crcActual)
        {
            return Failure(FrameErrorKind.CrcMismatch, "CRC-16/X-25 does not match the length byte and payload.");
        }

        var payload = data.Slice(2, payloadLength);
        if (payload.Length < 1 + MinDeviceIdLength + FixedPayloadTail)
        {
            return Failure(FrameErrorKind.LengthMismatch, "Payload is shorter than the tutorial layout.");
        }

        var idLength = payload[0];
        if (idLength < MinDeviceIdLength || idLength > MaxDeviceIdLength)
        {
            return Failure(FrameErrorKind.LengthMismatch, "Device id length is outside 1..16.");
        }

        var required = 1 + idLength + FixedPayloadTail;
        if (payload.Length != required)
        {
            return Failure(FrameErrorKind.LengthMismatch, "Payload length does not match the device id and fixed fields.");
        }

        var idBytes = payload.Slice(1, idLength);
        for (var i = 0; i < idBytes.Length; i++)
        {
            if (idBytes[i] is < 0x20 or > 0x7E)
            {
                return Failure(FrameErrorKind.LengthMismatch, "Device id is not printable ASCII.");
            }
        }

        var deviceId = Encoding.ASCII.GetString(idBytes);
        var offset = 1 + idLength;
        var obis = new ObisCode(
            payload[offset],
            payload[offset + 1],
            payload[offset + 2],
            payload[offset + 3],
            payload[offset + 4],
            payload[offset + 5]);
        var scale = unchecked((sbyte)payload[offset + 6]);
        var scaledValue = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(offset + 7, 4));
        return new FrameParseResult.Success(new DlmsLikeFrame(deviceId, obis, scale, scaledValue));
    }

    private static FrameParseResult Failure(FrameErrorKind kind, string message)
    {
        return new FrameParseResult.Failure(new FrameParseError(kind, message));
    }
}
