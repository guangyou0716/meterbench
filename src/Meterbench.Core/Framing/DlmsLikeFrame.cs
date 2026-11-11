using System.Globalization;

namespace Meterbench.Core.Framing;

/// <summary>
/// Six-group code shaped like OBIS. The values used by this repo are invented
/// (medium 9). They are not a certified OBIS mapping.
/// </summary>
public sealed record ObisCode(byte A, byte B, byte C, byte D, byte E, byte F)
{
    public static ObisCode Parse(string text)
    {
        if (!TryParse(text, out var code))
        {
            throw new FormatException($"'{text}' is not six dot-separated groups in 0..255.");
        }

        return code;
    }

    public static bool TryParse(string text, out ObisCode code)
    {
        code = new ObisCode(0, 0, 0, 0, 0, 0);
        var parts = text.Split('.');
        if (parts.Length != 6)
        {
            return false;
        }

        var values = new byte[6];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
            {
                return false;
            }
        }

        code = new ObisCode(values[0], values[1], values[2], values[3], values[4], values[5]);
        return true;
    }

    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{A}.{B}.{C}.{D}.{E}.{F}");
    }
}

/// <summary>
/// One decoded tutorial frame. EngineeringValue = ScaledValue * 10^Scale.
/// </summary>
public sealed record DlmsLikeFrame(string DeviceId, ObisCode Obis, sbyte Scale, int ScaledValue)
{
    public decimal EngineeringValue
    {
        get
        {
            var factor = 1m;
            if (Scale >= 0)
            {
                for (var i = 0; i < Scale; i++)
                {
                    factor *= 10m;
                }

                return ScaledValue * factor;
            }

            for (var i = 0; i < -Scale; i++)
            {
                factor *= 10m;
            }

            return ScaledValue / factor;
        }
    }
}

public enum FrameErrorKind
{
    BadStart,
    BadEnd,
    LengthMismatch,
    CrcMismatch,
    Truncated
}

public sealed record FrameParseError(FrameErrorKind Kind, string Message);

public abstract record FrameParseResult
{
    private FrameParseResult()
    {
    }

    public sealed record Success(DlmsLikeFrame Frame) : FrameParseResult;

    public sealed record Failure(FrameParseError Error) : FrameParseResult;
}
