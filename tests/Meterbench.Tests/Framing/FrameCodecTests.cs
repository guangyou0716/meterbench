using System.Buffers.Binary;
using System.Text;
using Meterbench.Core.Framing;

namespace Meterbench.Tests.Framing;

public class FrameCodecTests
{
    private static readonly ObisCode VoltageObis = ObisCode.Parse("9.1.1.1.0.255");

    [Fact]
    public void Crc16X25_MatchesTheHdlcCheckValue()
    {
        var crc = Crc16X25.Compute(Encoding.ASCII.GetBytes("123456789"));
        Assert.Equal((ushort)0x906E, crc);
        Assert.Equal(Crc16X25.CheckValue, crc);
    }

    [Fact]
    public void ValidFrame_ExposesDeviceObisScaleAndScaledValue()
    {
        var bytes = FrameCodec.Encode("MTR-A1", VoltageObis, -1, 2301);

        Assert.Equal(0x7E, bytes[0]);
        Assert.Equal(0x7E, bytes[^1]);
        Assert.Equal(18, bytes[1]);
        Assert.Equal(23, bytes.Length);

        var crc = Crc16X25.Compute(bytes.AsSpan(1, 1 + bytes[1]));
        Assert.Equal((byte)(crc & 0xFF), bytes[2 + bytes[1]]);
        Assert.Equal((byte)(crc >> 8), bytes[3 + bytes[1]]);

        var parsed = Assert.IsType<FrameParseResult.Success>(FrameCodec.Parse(bytes));
        Assert.Equal("MTR-A1", parsed.Frame.DeviceId);
        Assert.Equal("9.1.1.1.0.255", parsed.Frame.Obis.ToString());
        Assert.Equal((sbyte)-1, parsed.Frame.Scale);
        Assert.Equal(2301, parsed.Frame.ScaledValue);
        Assert.Equal(230.1m, parsed.Frame.EngineeringValue);
    }

    [Fact]
    public void RoundTrip_EncodeThenParse_PreservesFieldsAndBytes()
    {
        var obis = ObisCode.Parse("9.2.1.3.0.255");
        var once = FrameCodec.Encode("LC-B1", obis, -1, -55);

        var parsed = Assert.IsType<FrameParseResult.Success>(FrameCodec.Parse(once));
        Assert.Equal("LC-B1", parsed.Frame.DeviceId);
        Assert.Equal(obis, parsed.Frame.Obis);
        Assert.Equal((sbyte)-1, parsed.Frame.Scale);
        Assert.Equal(-55, parsed.Frame.ScaledValue);
        Assert.Equal(-5.5m, parsed.Frame.EngineeringValue);

        var twice = FrameCodec.Encode(
            parsed.Frame.DeviceId,
            parsed.Frame.Obis,
            parsed.Frame.Scale,
            parsed.Frame.ScaledValue);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void RoundTrip_AcceptsA16CharacterDeviceId()
    {
        var deviceId = new string('B', 16);
        var bytes = FrameCodec.Encode(deviceId, VoltageObis, 0, 42);
        var parsed = Assert.IsType<FrameParseResult.Success>(FrameCodec.Parse(bytes));
        Assert.Equal(deviceId, parsed.Frame.DeviceId);
        Assert.Equal(42m, parsed.Frame.EngineeringValue);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABCDEFGHIJKLMNOPQ")]
    public void Encode_RejectsDeviceIdsOutside1To16(string deviceId)
    {
        Assert.Throws<ArgumentException>(() => FrameCodec.Encode(deviceId, VoltageObis, -1, 1));
    }

    [Fact]
    public void BadStart_IsATypedError()
    {
        var bytes = Sample();
        bytes[0] = 0x00;
        Expect(bytes, FrameErrorKind.BadStart);
        Expect(new byte[] { 0x00 }, FrameErrorKind.BadStart);
    }

    [Fact]
    public void BadEnd_IsATypedError()
    {
        var bytes = Sample();
        bytes[^1] = 0x00;
        Expect(bytes, FrameErrorKind.BadEnd);
    }

    [Fact]
    public void LengthMismatch_WhenBufferIsLongerThanTheLengthByte()
    {
        var bytes = Sample();
        var longer = new byte[bytes.Length + 1];
        bytes.CopyTo(longer, 0);
        longer[^1] = 0x11;
        Expect(longer, FrameErrorKind.LengthMismatch);
    }

    [Fact]
    public void LengthMismatch_WhenPayloadLayoutDoesNotMatchIdLength()
    {
        var frame = Sample();
        frame[2] = 5;
        var payloadLength = frame[1];
        var crc = Crc16X25.Compute(frame.AsSpan(1, 1 + payloadLength));
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2 + payloadLength), crc);
        Expect(frame, FrameErrorKind.LengthMismatch);
    }

    [Fact]
    public void CrcMismatch_IsATypedError()
    {
        var bytes = Sample();
        bytes[4] ^= 0x01;
        Expect(bytes, FrameErrorKind.CrcMismatch);
    }

    [Theory]
    [MemberData(nameof(TruncatedFrames))]
    public void Truncated_IsATypedError(byte[] frame)
    {
        Expect(frame, FrameErrorKind.Truncated);
    }

    public static IEnumerable<object[]> TruncatedFrames()
    {
        yield return new object[] { Array.Empty<byte>() };
        yield return new object[] { new byte[] { FrameCodec.Flag } };
        yield return new object[] { Sample()[..^1] };
        yield return new object[] { new byte[] { FrameCodec.Flag, 50, 0x01, 0x02 } };
    }

    private static byte[] Sample()
    {
        return FrameCodec.Encode("MTR-A1", VoltageObis, -1, 2301);
    }

    private static void Expect(byte[] frame, FrameErrorKind kind)
    {
        var result = FrameCodec.Parse(frame);
        var failure = Assert.IsType<FrameParseResult.Failure>(result);
        Assert.Equal(kind, failure.Error.Kind);
        Assert.False(string.IsNullOrWhiteSpace(failure.Error.Message));
    }
}
