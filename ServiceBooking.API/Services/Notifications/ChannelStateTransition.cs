using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// The one place a <see cref="NotificationChannel.State"/> change is written together with its
/// <see cref="ChannelStateEvent"/> history row (US-55 p.4) — shared by <c>NotificationDispatchTask</c>
/// (send-time <c>ChannelInvalid</c>/consecutive-failure transitions, §26.4) and <c>ChannelHealthTask</c>
/// (polled transitions, idle deletion, unauthorized-instance timeout, §30) so both write the exact same
/// three fields in the exact same way instead of two independently-maintained copies of this pattern.
/// </summary>
public static class ChannelStateTransition
{
    /// <summary>No-op when <paramref name="targetState"/> already equals the channel's current state —
    /// callers can call this unconditionally without first checking whether anything actually changed.</summary>
    public static void Apply(
        AppDbContext db, NotificationChannel channel, ChannelState targetState, ChannelStateReason reason,
        string? detail, DateTime nowUtc)
    {
        if (channel.State == targetState) return;

        db.ChannelStateEvents.Add(new ChannelStateEvent
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            FromState = channel.State,
            ToState = targetState,
            Reason = reason,
            Detail = Truncate(detail),
            OccurredAtUtc = nowUtc,
        });
        channel.State = targetState;
        channel.LastStateReason = reason;

        // B4: ConnectedAtUtc is what ChannelStateMapper's hadBeenConnected fork keys on (§30.1) — it has
        // to be set on EVERY path into Connected, not just the QR-scan controller's, or a channel that
        // authorized purely through polling can never be told apart from one still mid-QR-flow.
        // I8: ConsecutiveSendFailures was previously reset only on a successful Sent outcome — a channel
        // that just got disconnected by the threshold and then reconnected would still carry the OLD
        // count, so a single transient error right after reconnecting immediately disconnected it again.
        if (targetState == ChannelState.Connected)
        {
            channel.ConnectedAtUtc ??= nowUtc;
            channel.ConsecutiveSendFailures = 0;
        }
    }

    // ChannelStateEvent.Detail is string(500) — never secret material (§23.1), just a technical note.
    private static string? Truncate(string? text) =>
        string.IsNullOrEmpty(text) || text.Length <= 500 ? text : text[..500];
}
