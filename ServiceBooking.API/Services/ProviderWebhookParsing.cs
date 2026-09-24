using ServiceBooking.API.Services.Notifications;

namespace ServiceBooking.API.Services;

/// <summary>
/// The DI seam between <c>NotificationsController</c>'s webhook action (this cycle's T4-B13, owned by
/// this developer) and the provider adapter's own parsing (<c>GreenApiWebhookParser</c>, T4-B5, owned by
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

/// <summary>
/// ARCHITECTURE_CYCLE9.md §104.7 (US-120) — resolves the right <see cref="IProviderWebhookParser"/> for
/// the new <c>provider-webhook/{transport}/{token}</c> route's <c>{transport}</c> path segment. Mirrors
/// <see cref="Notifications.INotificationTransportRegistry"/>'s own registry/exception shape for
/// consistency, though the failure mode here is unreachable in practice: unlike the send-side registries
/// (whose completeness genuinely depends on <c>Notifications:Provider</c>), BOTH parsers are registered
/// unconditionally — the ORIGINAL <c>GreenApiWebhookParser</c>'s own doc comment already explains
/// why (a "logging"-provider deployment that receives a stray webhook still parses and safely 200s it
/// rather than throwing on a missing DI registration), and that reasoning applies identically to the MAX
/// parser.
/// </summary>
public interface IProviderWebhookParserRegistry
{
    IProviderWebhookParser For(Core.Enums.NotificationTransport transport);
}

public sealed class ProviderWebhookParserRegistry(IReadOnlyDictionary<Core.Enums.NotificationTransport, IProviderWebhookParser> byTransport)
    : IProviderWebhookParserRegistry
{
    public IProviderWebhookParser For(Core.Enums.NotificationTransport transport) =>
        byTransport.TryGetValue(transport, out var parser)
            ? parser
            : throw new MissingTransportImplementationException(transport);
}
