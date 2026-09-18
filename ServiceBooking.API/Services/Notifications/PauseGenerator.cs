namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Picks the next 5-15s (configurable) pause between two sends on the same channel
/// (ARCHITECTURE_CYCLE4.md §26.1). Abstracted purely so <c>NotificationDispatchTestFactory</c> can
/// assert "every interval fell in [min;max] and they are not all equal" without a test depending on
/// actual randomness (§27.3) — production uses real jitter, nothing here is deterministic by design
/// (unlike <c>NotificationTiming.JitterMinutes</c>, which deliberately IS deterministic for a different
/// reason: reminder due-time must recompute identically on retry).
/// </summary>
public interface IPauseGenerator
{
    /// <summary>Next pause, uniformly distributed in [minMs; maxMs].</summary>
    TimeSpan Next(int minMs, int maxMs);
}

/// <summary>Production implementation. Not cryptographically secure and does not need to be — this is
/// an anti-throttling spacing value, not a secret.</summary>
public sealed class PauseGenerator : IPauseGenerator
{
    public TimeSpan Next(int minMs, int maxMs)
    {
        if (maxMs < minMs)
            throw new ArgumentException($"maxMs ({maxMs}) must be >= minMs ({minMs}).", nameof(maxMs));

        return TimeSpan.FromMilliseconds(Random.Shared.Next(minMs, maxMs + 1));
    }
}
