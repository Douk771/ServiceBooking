namespace ServiceBooking.TestKit;

/// <summary>
/// Process-stable string hash (FNV-1a, 32-bit).
/// <para>
/// Deliberately NOT <c>string.GetHashCode</c>: .NET randomizes string hashing per process and that
/// cannot be turned off. The random-order test orderer prints "replay this exact order with
/// SEED=N" on every run, and derives a per-class offset from the class name — with GetHashCode that
/// offset differed in every process, so the replay promise was silently false and a green replay
/// would have been mistaken for "the race is gone". Measured: one class name hashed to 1807108974,
/// -184541775 and 894336771 in three consecutive processes.
/// </para>
/// <para>Lives in TestKit rather than in the functional test project so it stays unit-testable:
/// ServiceBooking.UnitTests references TestKit and must never reference the Docker-bound suite.</para>
/// </summary>
public static class StableHash
{
    public static int OfString(string value)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            var hash = offsetBasis;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= prime;
            }

            return (int)hash;
        }
    }
}
