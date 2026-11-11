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
/// A CRC-valid frame may carry any int8 scale, so the product can fall outside the
/// decimal range. Use <see cref="TryGetEngineeringValue"/> on untrusted bytes.
/// </summary>
public sealed record DlmsLikeFrame(string DeviceId, ObisCode Obis, sbyte Scale, int ScaledValue)
{
    /// <summary>Largest power of ten that System.Decimal can hold exactly (10^28).</summary>
    public const int MaxDecimalExponent = 28;

    /// <summary>
    /// The engineering value. Throws <see cref="OverflowException"/> when the scale puts the
    /// value outside what decimal can represent; prefer <see cref="TryGetEngineeringValue"/>.
    /// </summary>
    public decimal EngineeringValue => TryGetEngineeringValue(out var value)
        ? value
        : throw new OverflowException(
            $"Scaled value {ScaledValue} with scale {Scale} is outside the decimal range.");

    /// <summary>
    /// Computes ScaledValue * 10^Scale without throwing. Returns false when the result
    /// does not fit in a decimal (|scale| above 28, or a product above decimal.MaxValue).
    /// </summary>
    public bool TryGetEngineeringValue(out decimal value)
    {
        value = 0m;
        if (ScaledValue == 0)
        {
            return true;
        }

        var exponent = Math.Abs((int)Scale);
        if (exponent > MaxDecimalExponent)
        {
            return false;
        }

        var factor = 1m;
        for (var i = 0; i < exponent; i++)
        {
            factor *= 10m;
        }

        if (Scale >= 0)
        {
            if (Math.Abs((decimal)ScaledValue) > decimal.MaxValue / factor)
            {
                return false;
            }

            value = ScaledValue * factor;
            return true;
        }

        // exponent <= 28, so a non-zero int32 divided by 10^exponent is at least 10^-28 and stays exact.
        value = ScaledValue / factor;
        return true;
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
