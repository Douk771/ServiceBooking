using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.API.Services;

/// <summary>
/// The DI seam between <c>NotificationsController</c>'s webhook action (this cycle's T4-B13, owned by
/// this developer) and the GREEN-API adapter's own parsing (<c>GreenApiWebhookParser</c>, T4-B5, owned by
/// the other backend developer working this cycle in parallel). Both sides share the neutral
/// <see cref="ProviderCallback"/>/<see cref="ProviderCallbackKind"/> shape, which lives in
/// <c>Services/Notifications/</c> — not the <c>GreenApi/</c> subfolder — precisely so it can be
/// referenced from here without either side depending on provider vocabulary. This interface is the one
/// extra piece that keeps the controller depending on an abstraction (swappable, mockable) instead of a
/// concrete class, the same pattern every other adapter seam in this cycle already uses
/// (<see cref="INotificationTransport"/>, <see cref="IChannelProvisioning"/>). Registered in
/// <c>Program.cs</c> (by the other developer) against <c>GreenApiWebhookParser</c>.
/// </summary>
public interface IProviderWebhookParser
{
    /// <summary>Returns <see langword="null"/> for a body this parser doesn't recognize at all (not the
    /// same as "recognized event, unknown idMessage" — that case still parses fine and is handled by the
    /// controller returning 200 without a matching row, per ARCHITECTURE_CYCLE4.md §32).</summary>
    ProviderCallback? Parse(string rawBody);
}
