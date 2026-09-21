namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// The single place <see cref="Core.Entities.NotificationChannel.IdleSinceUtc"/> is computed
/// (ARCHITECTURE_CYCLE4.md §30.3). Four events can end a channel's idle period (company unblocked,
/// company reactivated, a company assigned to the channel, the channel's period paid) and none of them
/// touch this field directly — they change only their own data, and <see cref="ServiceBooking.API.Services.Scheduling.Tasks.ChannelHealthTask"/> is
/// the only caller of <see cref="Recompute"/>, once per pass, from a single batched query over all
/// channels. That is the whole reason this exists as a separate pure function rather than four separate
/// "clear IdleSinceUtc" statements scattered across four handlers: with four call sites the rule would
/// inevitably drift the first time a fifth cause is added.
/// </summary>
public static class ChannelIdleCalculator
{
    /// <summary>
    /// Recomputes <c>IdleSinceUtc</c> for one channel from the two things §30.3 reduces "is this channel
    /// idle" to — whether it has at least one active (not blocked/deactivated) assigned company, and
    /// whether its paid period is currently live.
    /// </summary>
    /// <param name="existingIdleSinceUtc">The channel's current <c>IdleSinceUtc</c> value.</param>
    /// <param name="activeCompanyCount">Count of companies assigned to the channel with
    /// <c>Company.IsActive == true</c> — both "blocked by superadmin" and "deactivated by owner" are the
    /// same field (§30.3), so this single count captures both causes.</param>
    /// <param name="paidUntilUtc">The channel's paid-until date, or null if never paid.</param>
    /// <param name="isSuspendedByAdmin">Whether the channel itself was suspended by a superadmin.</param>
    /// <param name="nowUtc">Current time.</param>
    /// <returns><see langword="null"/> when the channel has both an active company and a live paid
    /// period — idle ends, regardless of how long it had been running. Otherwise the idle period
    /// continues: the existing start date if there was one, or <paramref name="nowUtc"/> if idle just
    /// started — a date is set ONCE and never "restarted" while it stays unset.</returns>
    public static DateTime? Recompute(
        DateTime? existingIdleSinceUtc,
        int activeCompanyCount,
        DateTime? paidUntilUtc,
        bool isSuspendedByAdmin,
        DateTime nowUtc)
    {
        var periodIsLive = paidUntilUtc is { } paidUntil && paidUntil >= nowUtc && !isSuspendedByAdmin;
        var hasActiveCompany = activeCompanyCount > 0;

        if (hasActiveCompany && periodIsLive) return null;

        return existingIdleSinceUtc ?? nowUtc;
    }
}
