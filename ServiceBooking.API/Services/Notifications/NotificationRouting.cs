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
    /// <summary>One routing target — enough for the caller to build one <see cref="Core.Entities.OutboundNotification"/> row.</summary>
    public readonly record struct Target(Guid ChannelId, NotificationTransport Transport);

    /// <summary>One transport of the account (its first live number), reduced to routability facts.</summary>
    public readonly record struct TransportCandidate(
        NotificationTransport Transport, bool Paid, bool Funded, bool Suspended, ChannelState State)
    {
        /// <summary>"Routable transport": paid ∧ funded ∧ not suspended by admin ∧ the number was bound at least once
        /// (a breakage keeps the messages <c>Pending</c>, it does not drop the transport).</summary>
        public bool IsRoutable => Paid && Funded && !Suspended &&
            State is ChannelState.Connected or ChannelState.Disconnected or ChannelState.NeedsReconnect or ChannelState.Blocked;
    }

    /// <summary>§40.5.2: 0 routable transports → none (the gate gives the reason); 1 → it (mode and priority are not
    /// read); 2 → <c>AllChannels</c>: both, <c>PriorityChannel</c>: the priority one, no silent switch. Result order is
    /// always WhatsApp, MAX.</summary>
    public static IReadOnlyList<NotificationTransport> SelectRoutedTargets(
        NotificationDeliveryMode mode, NotificationTransport priorityTransport, IReadOnlyList<TransportCandidate> candidates) =>
        SelectRoutedTransports(mode, priorityTransport,
            candidates.Where(c => c.IsRoutable).Select(c => c.Transport));

    /// <summary>The same rule over transports already known to be routable.</summary>
    public static IReadOnlyList<NotificationTransport> SelectRoutedTransports(
        NotificationDeliveryMode mode, NotificationTransport priorityTransport, IEnumerable<NotificationTransport> routable)
    {
        var distinct = routable.Distinct().OrderBy(t => (int)t).ToList();
        if (distinct.Count <= 1) return distinct;
        return mode switch
        {
            NotificationDeliveryMode.AllChannels => distinct,
            NotificationDeliveryMode.PriorityChannel => [priorityTransport],
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
