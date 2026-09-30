using System.Text;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.2, §575.4 — the generator's own deterministic random source (xorshift64*). Deliberately not <see cref="System.Random"/>: its algorithm is not
/// part of the .NET contract and may change between runtimes, which would silently change what a re-seed produces. The seed is a hash of a KEY (for example
/// <c>"prod:bookings:master-7:2026-10-01"</c>), so adding a company or a master does not shift the numbers of any other entity.
/// </summary>
public sealed class ShowcaseRandom
{
    private ulong _state;

    public ShowcaseRandom(string key)
    {
        // FNV-1a over the key, then one SplitMix64 round so that keys differing in a single character give unrelated states.
        var hash = 14695981039346656037UL;
        foreach (var b in Encoding.UTF8.GetBytes(key))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        hash += 0x9E3779B97F4A7C15UL;
        hash = (hash ^ (hash >> 30)) * 0xBF58476D1CE4E5B9UL;
        hash = (hash ^ (hash >> 27)) * 0x94D049BB133111EBUL;
        hash ^= hash >> 31;
        _state = hash == 0 ? 0x2545F4914F6CDD1DUL : hash;
    }

    public ulong NextUInt64()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 2685821657736338717UL;
    }

    /// <summary>Uniform in [0, <paramref name="maxExclusive"/>). The modulo bias is below 1e-15 for the ranges used here.</summary>
    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return (int)(NextUInt64() % (ulong)maxExclusive);
    }

    /// <summary>Uniform in [<paramref name="min"/>, <paramref name="maxExclusive"/>).</summary>
    public int Next(int min, int maxExclusive)
    {
        if (maxExclusive <= min) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
        return min + Next(maxExclusive - min);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public bool Chance(double probability) => NextDouble() < probability;

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    /// <summary>Fisher–Yates shuffle, in place.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}
