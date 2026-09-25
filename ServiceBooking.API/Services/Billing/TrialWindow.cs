namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 18 (ARCHITECTURE_CYCLE18.md §336.2) — pure arithmetic for the trial's mailing window and its
/// warning thresholds. No DB access; "now" and every other timestamp is passed in so this is fully
/// unit-testable.
/// </summary>
public static class TrialWindow
{
    /// <summary>
    /// Т3 — legal guarantee (пункт 6.16.11 Соглашения): the "trial ended" message must stay visible
    /// for at least this many calendar days after the transition materialized. A code constant, not an
    /// admin setting — it must not be shortened by configuration.
    /// </summary>
    public const int ExpiredNoticeMinDays = 30;

    /// <summary>What the current activation-terms edition promises when the platform setting is
    /// missing or unparsable (§337.1 фаза 3) — "то, что обещает текущая редакция текста".</summary>
    public static readonly int[] DefaultWarningThresholds = [7, 3, 1];

    /// <summary>
    /// Window end = max(first channel authorization, trial start) + windowDays, capped at trial end.
    /// <c>max</c> is deliberate: a channel authorized long BEFORE the trial started (the account lived
    /// on a paid plan, dropped to Free, then got a trial) must not burn the promised days against
    /// history that didn't exist yet at the time of the promise. Returns null when no channel has ever
    /// been authorized — the window simply hasn't started (Д5), which is not the same as "unlimited":
    /// the ceiling is still the trial's own end.
    /// </summary>
    public static DateTime? WindowEnd(DateTime? firstAuthorizedUtc, DateTime trialStartUtc, DateTime trialEndUtc, int windowDays)
    {
        if (firstAuthorizedUtc is null) return null;
        var start = firstAuthorizedUtc.Value > trialStartUtc ? firstAuthorizedUtc.Value : trialStartUtc;
        var end = start.AddDays(windowDays);
        return end < trialEndUtc ? end : trialEndUtc;
    }

    /// <summary>
    /// The nearest warning threshold NOT yet crossed, given how many whole days remain until
    /// "trialEndUtc" and which threshold (if any) was already warned about last time
    /// (Д19/US-18-10: pörogi are read from the account's own snapshot, not the live platform setting —
    /// callers are responsible for passing the snapshot in via <paramref name="thresholdsDays"/>).
    /// Returns null when no unwarned threshold applies yet (too early) or all have already been
    /// crossed and reported. A missed background pass does not "catch up" with a burst of stale
    /// warnings: only the closest not-yet-crossed threshold is ever returned.
    /// </summary>
    public static int? ApplicableThreshold(
        IReadOnlyCollection<int> thresholdsDays, int? alreadyWarnedAtThresholdDays, int daysLeft)
    {
        var candidates = thresholdsDays
            .Where(t => t >= daysLeft)
            .Where(t => alreadyWarnedAtThresholdDays is null || t < alreadyWarnedAtThresholdDays)
            .OrderBy(t => t)
            .ToList();
        return candidates.Count == 0 ? null : candidates[0];
    }

    /// <summary>Parses the snapshot string ("7,3,1") stored on <c>BillingAccount.TrialWarningThresholdsDays</c>
    /// / <c>TrialGrant.WarningThresholdsDays</c>; falls back to <see cref="DefaultWarningThresholds"/> on
    /// a missing/corrupt value (§337.1: "порча одной строки не роняет проход").</summary>
    public static int[] ParseThresholds(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return DefaultWarningThresholds;
        var parts = snapshot.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var values = new List<int>();
        foreach (var part in parts)
        {
            if (!int.TryParse(part, out var value) || value <= 0) return DefaultWarningThresholds;
            values.Add(value);
        }
        return values.Count == 0 ? DefaultWarningThresholds : values.ToArray();
    }

    public static string FormatThresholds(IReadOnlyCollection<int> thresholdsDays) =>
        string.Join(',', thresholdsDays);
}
