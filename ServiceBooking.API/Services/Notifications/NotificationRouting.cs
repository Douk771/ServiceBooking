using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Picks which channel(s) a queued event targets, given a company's <see cref="NotificationDeliveryMode"/>
/// (ARCHITECTURE_CYCLE9.md §104.5, US-125). Pure — no DB, no HTTP, no clock — deliberately kept separate
/// from <see cref="NotificationGate"/> (which decides "queue at all, or skip for a reason that has
/// nothing to do with which channel"): this class only answers "of the channels this company has, which
/// one(s) does THIS event go to".
/// </summary>
public static class NotificationRouting
{
    /// <summary>One of the company's live channel assignments, reduced to what routing needs to decide
    /// with. <see cref="IsUsable"/> is the caller's own "funded AND Connected" verdict — this class does
    /// not re-derive it, the same "caller says out loud where it got this from" convention
    /// <see cref="NotificationGate"/>'s own <c>channelIsFunded</c> parameter already established.</summary>
    public readonly record struct Candidate(Guid ChannelId, NotificationTransport Transport, bool IsUsable);

    /// <summary>One routing target — enough for the caller to build one <see cref="Core.Entities.OutboundNotification"/> row.</summary>
    public readonly record struct Target(Guid ChannelId, NotificationTransport Transport);

    /// <param name="Targets">Zero, one, or (in <see cref="NotificationDeliveryMode.AllChannels"/>) several
    /// targets — one <see cref="Core.Entities.OutboundNotification"/> row per target.</param>
    /// <param name="UnavailableChannelId">Set only when <see cref="NotificationReason.PriorityChannelUnavailable"/>
    /// is the reason AND a channel of the priority transport actually exists (just isn't usable) — lets
    /// the caller's Skipped row point at the specific channel that's the problem, the same way an
    /// ordinary <c>NoUsableChannel</c> Skipped row already carries a <c>ChannelId</c> when one exists.</param>
    /// <param name="SkipReason">Null when <see cref="Targets"/> is non-empty, or when the candidate list
    /// passed to <see cref="SelectTargets"/> was empty to begin with (the caller's own "no assignment at all"
    /// path already has a reason for that — <see cref="NotificationReason.NoUsableChannel"/> — this class
    /// doesn't repeat it). Otherwise <see cref="NotificationReason.PriorityChannelUnavailable"/> for
    /// <see cref="NotificationDeliveryMode.PriorityChannel"/>'s two zero-target cases (§104.5); null for
    /// <see cref="NotificationDeliveryMode.AllChannels"/> with zero usable candidates — the caller falls
    /// back to its own generic "nothing usable" reason for that case, same as it always has.</param>
    public readonly record struct RoutingResult(
        IReadOnlyList<Target> Targets, Guid? UnavailableChannelId, NotificationReason? SkipReason);

    public static RoutingResult SelectTargets(
        NotificationDeliveryMode mode, NotificationTransport priorityTransport, IReadOnlyList<Candidate> candidates)
    {
        // No assignment of any transport — not this class's reason to give; every caller already has its
        // own "no channel at all" path (NotificationGate.NoUsableChannel) for the zero-candidate case.
        if (candidates.Count == 0) return new RoutingResult([], null, null);

        return mode switch
        {
            NotificationDeliveryMode.AllChannels => SelectAllChannels(candidates),
            NotificationDeliveryMode.PriorityChannel => SelectPriorityChannel(priorityTransport, candidates),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    /// <summary>§104.5: "все годные кандидаты, по одной цели на транспорт." Zero usable candidates here
    /// is deliberately NOT tagged <see cref="NotificationReason.PriorityChannelUnavailable"/> — that
    /// reason is specifically about a NAMED priority transport being unavailable, a concept AllChannels
    /// mode doesn't have; the caller falls back to its own existing "nothing usable" reason.</summary>
    private static RoutingResult SelectAllChannels(IReadOnlyList<Candidate> candidates)
    {
        var usable = candidates.Where(c => c.IsUsable).Select(c => new Target(c.ChannelId, c.Transport)).ToList();
        return new RoutingResult(usable, null, null);
    }

    /// <summary>§104.5's three PriorityChannel cases, in order:
    /// (1) exists and usable → exactly one target;
    /// (2) exists but not usable (unlinked/banned/unfunded/not Connected) → zero targets, PriorityChannelUnavailable, carrying the unusable channel's id;
    /// (3) doesn't exist among this company's candidates at all, but a DIFFERENT transport does → zero targets, same reason, no channel id.
    /// No silent fallback to a different transport in any of these — §3 SPEC, Q7в.</summary>
    private static RoutingResult SelectPriorityChannel(NotificationTransport priorityTransport, IReadOnlyList<Candidate> candidates)
    {
        Candidate? match = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Transport == priorityTransport) { match = candidate; break; }
        }

        if (match is null)
            return new RoutingResult([], null, NotificationReason.PriorityChannelUnavailable);

        if (!match.Value.IsUsable)
            return new RoutingResult([], match.Value.ChannelId, NotificationReason.PriorityChannelUnavailable);

        return new RoutingResult([new Target(match.Value.ChannelId, match.Value.Transport)], null, null);
    }
}
