namespace Meterbench.Core.Simulation;

/// <summary>
/// xorshift32. Seed 0 is remapped to 1 so the generator does not stick at zero.
/// NextUnit returns a multiple of 1/2^24 in [0, 1).
/// </summary>
public sealed class DeterministicRng
{
    private uint _state;

    public DeterministicRng(uint seed)
    {
        _state = seed == 0 ? 1u : seed;
    }

    public double NextUnit()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x == 0 ? 1u : x;
        return (_state & 0x00FF_FFFF) / 16777216d;
    }
}

/// <summary>
/// FNV-1a 32-bit mix of the seed, device id, and quantity code.
/// The same inputs always produce the same generator seed.
/// </summary>
public static class SeedMix
{
    public static uint Mix(int seed, string deviceId, string quantityCode)
    {
        var hash = 2166136261u;
        hash = MixInt(hash, seed);
        hash = MixString(hash, deviceId);
        hash = MixString(hash, quantityCode);
        return hash;
    }

    private static uint MixInt(uint hash, int value)
    {
        hash = MixByte(hash, (byte)value);
        hash = MixByte(hash, (byte)(value >> 8));
        hash = MixByte(hash, (byte)(value >> 16));
        hash = MixByte(hash, (byte)(value >> 24));
        return hash;
    }

    private static uint MixString(uint hash, string value)
    {
        foreach (var ch in value)
        {
            hash = MixByte(hash, (byte)ch);
            hash = MixByte(hash, (byte)(ch >> 8));
        }

        return hash;
    }

    private static uint MixByte(uint hash, byte value)
    {
        hash ^= value;
        return hash * 16777619u;
    }
}
