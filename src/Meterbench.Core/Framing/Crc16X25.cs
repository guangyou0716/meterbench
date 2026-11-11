namespace Meterbench.Core.Framing;

/// <summary>
/// CRC-16/X-25 (ISO-HDLC FCS): reflected poly 0x8408, init 0xFFFF, xorout 0xFFFF.
/// Check value for ASCII "123456789" is 0x906E. This is the public HDLC polynomial,
/// used here only by the unofficial tutorial frame.
/// </summary>
public static class Crc16X25
{
    public const ushort CheckValue = 0x906E;

    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ 0x8408);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        return (ushort)(crc ^ 0xFFFF);
    }
}
