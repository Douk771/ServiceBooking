using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services;

/// <summary>
/// Pure retention-window arithmetic (ARCHITECTURE.md §7.2, §9.1) — no DB, no disk, unit-tested directly.
/// Used both by the retention cleanup task (deciding what is old enough to delete) and, indirectly, by
/// anything that wants to explain "when will this photo be removed".
/// </summary>
public static class PhotoQuota
{
    /// <summary>The cutoff instant for a retention window: photos created before this are expired.
    /// CYCLE5-BREAKING (ARCHITECTURE_CYCLE5.md §44.7): `Forever` no longer exists — every retention value
    /// now has a cutoff, so this can no longer throw.</summary>
    public static DateTime CutoffUtc(PhotoRetention retention, DateTime nowUtc) => retention switch
    {
        PhotoRetention.SixMonths => nowUtc.AddMonths(-6),
        PhotoRetention.TwelveMonths => nowUtc.AddMonths(-12),
        _ => throw new ArgumentOutOfRangeException(nameof(retention))
    };

    /// <summary>Whether a photo created at <paramref name="createdAtUtc"/> is past its retention window.</summary>
    public static bool IsExpired(DateTime createdAtUtc, PhotoRetention retention, DateTime nowUtc) =>
        createdAtUtc < CutoffUtc(retention, nowUtc);
}
