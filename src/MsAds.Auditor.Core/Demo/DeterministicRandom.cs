namespace MsAds.Auditor.Core.Demo;

/// <summary>
/// A small, self-contained pseudo-random generator (SplitMix64) used to give demo data a
/// realistic texture. Implemented here rather than using <see cref="Random"/> so that the same
/// seed produces the same demo figures on every run and every runtime version, which keeps the
/// demo dashboard and its tests stable.
/// </summary>
internal sealed class DeterministicRandom(ulong seed)
{
    private ulong _state = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        var z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>A value in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Scales a value by ±<paramref name="fraction"/>, e.g. 0.12 for ±12%.</summary>
    public decimal Jitter(decimal value, double fraction)
        => value * (decimal)(1.0 - fraction + NextDouble() * 2.0 * fraction);

    public int Next(int minInclusive, int maxExclusive)
        => minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));

    /// <summary>Builds a stable seed from the parts that identify a dataset.</summary>
    public static ulong Seed(params object[] parts)
    {
        var hash = 0xCBF29CE484222325UL;

        foreach (var part in parts)
        {
            foreach (var ch in part.ToString() ?? string.Empty)
            {
                hash = (hash ^ ch) * 0x100000001B3UL;
            }
        }

        return hash;
    }
}
