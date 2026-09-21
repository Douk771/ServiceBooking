using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>Result of <see cref="ChannelStateMapper.Map"/> — the state <see cref="ServiceBooking.API.Services.Scheduling.Tasks.ChannelHealthTask"/>
/// should set and, when it actually changes anything, the reason recorded in
/// <see cref="Core.Entities.ChannelStateEvent"/>. <see cref="Reason"/> is null whenever
/// <see cref="State"/> equals the caller's current state — nothing changed, so there is nothing to log.</summary>
public readonly record struct ChannelStateMapping(ChannelState State, ChannelStateReason? Reason);

/// <summary>
/// Provider instance-state vocabulary → our own <see cref="ChannelState"/> (US-55 p.2,
/// ARCHITECTURE_CYCLE4.md §30.1). Used identically by the batched poll (<see cref="ServiceBooking.API.Services.Scheduling.Tasks.ChannelHealthTask"/>)
/// and the webhook's <c>stateInstance</c> event — one mapping, not two copies that could drift.
/// </summary>
public static class ChannelStateMapper
{
    /// <param name="currentState">The channel's current stored state — returned unchanged for
    /// <see cref="ProviderChannelState.Unknown"/>, since an ambiguous provider answer must never regress
    /// a channel that was working a moment ago.</param>
    /// <param name="providerState">What the provider just reported.</param>
    /// <param name="hadBeenConnected">Whether the channel has ever reached <see cref="ChannelState.Connected"/>
    /// (<c>ConnectedAtUtc != null</c>) — the fork that tells "still starting up" (US-53's QR flow, not yet
    /// authorized) apart from "was working, now isn't" for a provider <c>notAuthorized</c> answer, which
    /// otherwise looks identical either way (US-55 p.2).</param>
    public static ChannelStateMapping Map(ChannelState currentState, ProviderChannelState providerState, bool hadBeenConnected) =>
        providerState switch
        {
            ProviderChannelState.Authorized =>
                new ChannelStateMapping(ChannelState.Connected, ChannelStateReason.Authorized),

            ProviderChannelState.Blocked =>
                new ChannelStateMapping(ChannelState.Blocked, ChannelStateReason.ProviderReportsBlocked),

            // Still mid-QR-flow, never authorized yet — not a regression, just "not done setting up".
            ProviderChannelState.NotAuthorized when !hadBeenConnected =>
                new ChannelStateMapping(ChannelState.Connecting, null),

            // Was connected before, provider now says unauthorized — the number was unlinked from the
            // phone, or WhatsApp logged it out. This IS a regression.
            ProviderChannelState.NotAuthorized =>
                new ChannelStateMapping(ChannelState.Disconnected, ChannelStateReason.ProviderReportsUnauthorized),

            // B4: "starting" (and the yellowCard/sleepMode variants folded into it upstream) means
            // "still setting up" ONLY for a channel that has never been authorized. For a channel that
            // had already reached Connected/Disconnected/Blocked, a transient "starting" answer must not
            // regress it to Connecting — same ambiguous-answer reasoning as ProviderChannelState.Unknown
            // below, and the same contract NotAuthorized already follows two cases up.
            ProviderChannelState.Starting when !hadBeenConnected =>
                new ChannelStateMapping(ChannelState.Connecting, null),

            ProviderChannelState.Starting =>
                new ChannelStateMapping(currentState, null),

            // Ambiguous/transient provider answer — never act on it. A channel that was Connected a
            // moment ago must not flip to Disconnected on a single unclear poll response; the next pass
            // (or a clearer answer sooner) resolves it.
            ProviderChannelState.Unknown =>
                new ChannelStateMapping(currentState, null),

            _ => throw new ArgumentOutOfRangeException(nameof(providerState), providerState, null),
        };
}
