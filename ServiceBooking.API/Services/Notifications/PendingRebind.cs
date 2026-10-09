using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

public enum PendingRebindEvent
{
    /// <summary>The owner unbinds the number of transport X.</summary>
    Unbind,
    /// <summary>The owner replaces the number (new row of the SAME transport).</summary>
    Replace,
    /// <summary>A company moves to another account.</summary>
    CompanyTransfer,
}

public readonly record struct RebindTargetChannel(Guid ChannelId, NotificationTransport Transport);

/// <summary><see cref="Cancel"/> → the pending row is cancelled (<c>BookingOrAssignmentCancelled</c>); otherwise it moves
/// to <see cref="ChannelId"/> / <see cref="Transport"/> with <see cref="IdempotencyKey"/>.</summary>
public readonly record struct PendingRebindDecision(bool Cancel, Guid? ChannelId, NotificationTransport? Transport, string? IdempotencyKey)
{
    public static readonly PendingRebindDecision CancelIt = new(true, null, null, null);
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.9 (Р40-Ю3) — one decision for three events about a <c>Pending</c> message. Rebinding =
/// <c>ChannelId := target</c> (+ <c>Transport</c> and the LAST segment of <c>IdempotencyKey</c> when the transport differs);
/// if a row with the resulting key already exists the original is cancelled. Unbinding cancels by default; a move to
/// another routable transport happens only when <c>Notifications:RebindPendingToOtherTransport</c> is on. Pure.
/// </summary>
public static class PendingRebind
{
    /// <summary>Replaces the last <c>:</c>-separated segment (the transport) of an idempotency key.</summary>
    public static string RebindKey(string idempotencyKey, NotificationTransport newTransport)
    {
        var cut = idempotencyKey.LastIndexOf(':');
        return cut < 0 ? $"{idempotencyKey}:{newTransport}" : $"{idempotencyKey[..(cut + 1)]}{newTransport}";
    }

    /// <remarks>Unbind: the account's other ROUTABLE channels; Replace: the new row; CompanyTransfer: the
    /// new account's routable channels (parameter <c>candidates</c>).
    /// Existing keys are those already present (checked for the key after rebinding).</remarks>
    public static PendingRebindDecision Decide(
        PendingRebindEvent evt, NotificationTransport pendingTransport, string idempotencyKey,
        IReadOnlyList<RebindTargetChannel> candidates, bool rebindToOtherTransportAllowed, ISet<string> existingKeys)
    {
        RebindTargetChannel? target = evt switch
        {
            PendingRebindEvent.Unbind => rebindToOtherTransportAllowed
                ? candidates.Where(c => c.Transport != pendingTransport).OrderBy(c => (int)c.Transport).Select(c => (RebindTargetChannel?)c).FirstOrDefault()
                : null,
            PendingRebindEvent.Replace or PendingRebindEvent.CompanyTransfer =>
                candidates.Where(c => c.Transport == pendingTransport).Select(c => (RebindTargetChannel?)c).FirstOrDefault(),
            _ => throw new ArgumentOutOfRangeException(nameof(evt), evt, null),
        };
        if (target is null) return PendingRebindDecision.CancelIt;

        var key = target.Value.Transport == pendingTransport ? idempotencyKey : RebindKey(idempotencyKey, target.Value.Transport);
        if (key != idempotencyKey && existingKeys.Contains(key)) return PendingRebindDecision.CancelIt;

        return new PendingRebindDecision(false, target.Value.ChannelId, target.Value.Transport, key);
    }
}
