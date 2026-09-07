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
    /// <c>Forever</c> has no cutoff — see <see cref="IsExpired"/>, which always returns false for it
    /// instead of calling this.</summary>
    public static DateTime CutoffUtc(PhotoRetention retention, DateTime nowUtc) => retention switch
    {
        PhotoRetention.SixMonths => nowUtc.AddMonths(-6),
        PhotoRetention.TwelveMonths => nowUtc.AddMonths(-12),
        PhotoRetention.Forever => throw new InvalidOperationException(
            "Forever retention has no cutoff — check for it before calling CutoffUtc."),
        _ => throw new ArgumentOutOfRangeException(nameof(retention))
    };

    /// <summary>Whether a photo created at <paramref name="createdAtUtc"/> is past its retention window.
    /// <c>Forever</c> photos are never expired (US-21 p.8).</summary>
    public static bool IsExpired(DateTime createdAtUtc, PhotoRetention retention, DateTime nowUtc) =>
        retention != PhotoRetention.Forever && createdAtUtc < CutoffUtc(retention, nowUtc);
}
