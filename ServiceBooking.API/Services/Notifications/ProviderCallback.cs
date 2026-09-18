namespace ServiceBooking.API.Services.Notifications;

public enum ProviderCallbackKind
{
    DeliveryStatus,
    ChannelState,
}

/// <summary>Neutral (non-provider-specific) delivery-status vocabulary a webhook's
/// <see cref="ProviderCallbackKind.DeliveryStatus"/> event carries. Monotonic ordering
/// (Sent &lt; Delivered &lt; Read) is <c>NotificationsController</c>'s own rule to apply (§32) — this
/// type only carries the value, it doesn't order it.</summary>
public enum ProviderMessageStatus
{
    Sent,
    Delivered,
    Read,
    Failed,
}

/// <summary>
/// A provider webhook event, reduced to what the webhook controller needs — deliberately declared
/// OUTSIDE the <c>GreenApi</c> subfolder (ARCHITECTURE_CYCLE4.md §21 p.4, US-27 p.4) so the controller
/// only ever depends on this neutral shape, never on a type living in the one folder that is allowed to
/// know the provider's name. Both <see cref="MessageStatus"/> and <see cref="ChannelState"/> are already
/// translated out of the provider's own raw strings by <see cref="IProviderWebhookParser"/>'s
/// implementation — the controller never sees <c>"noAccount"</c>/<c>"notAuthorized"</c> literals.
/// </summary>
/// <param name="Kind">Which of the two events this is.</param>
/// <param name="ProviderMessageId"><c>idMessage</c> — set only for <see cref="ProviderCallbackKind.DeliveryStatus"/>.</param>
/// <param name="InstanceId">The instance this event is about, when present.</param>
/// <param name="MessageStatus">Set only for <see cref="ProviderCallbackKind.DeliveryStatus"/>.</param>
/// <param name="ChannelState">Set only for <see cref="ProviderCallbackKind.ChannelState"/> — already
/// narrowed to <see cref="ProviderChannelState"/>, the same vocabulary <c>GetStateAsync</c> polling
/// returns, so <c>ChannelStateMapper</c> is the one place either path is turned into our own
/// <see cref="Core.Enums.ChannelState"/>.</param>
/// <param name="OccurredAtUtc">Provider's own event timestamp, or "now" when the payload carries none.</param>
public sealed record ProviderCallback(
    ProviderCallbackKind Kind,
    string? ProviderMessageId,
    string? InstanceId,
    ProviderMessageStatus? MessageStatus,
    ProviderChannelState? ChannelState,
    DateTime OccurredAtUtc);

// The DI seam (IProviderWebhookParser) that resolves THIS record lives in
// ServiceBooking.API.Services.ProviderWebhookParsing.cs, not here — it was written by the other backend
// developer working NotificationsController.cs in parallel (T4-B13) before this file's
// GreenApiWebhookParser implementation landed. GreenApiWebhookParser (Services/Notifications/GreenApi/)
// implements that interface directly rather than duplicating a second one here.
