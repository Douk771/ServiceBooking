using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Resolves the right <see cref="INotificationTransport"/> for a channel's own
/// <see cref="NotificationTransport"/> (ARCHITECTURE_CYCLE9.md §104.2, US-122). Before this cycle,
/// <see cref="INotificationTransport"/> was resolved from DI as the single active implementation —
/// there was only ever one messenger, so "which provider" (logging vs green-api) was the only axis. With
/// a second transport the choice becomes two-dimensional: provider × messenger. The registry is that
/// second axis; <see cref="INotificationTransport"/>/<see cref="IChannelProvisioning"/> themselves are
/// UNCHANGED — only how a caller obtains one changes.
///
/// <see cref="For"/> throws <see cref="MissingTransportImplementationException"/> rather than returning
/// null for a member of <see cref="NotificationTransport"/> the registry has no mapping for — a third
/// transport added to the enum without a matching adapter registration must fail LOUD (caught at startup
/// by <c>DeploymentSafetyChecks</c>, §104.2), never silently "just not send" for that transport.
/// </summary>
public interface INotificationTransportRegistry
{
    INotificationTransport For(NotificationTransport transport);

    /// <summary>Every transport this registry can currently resolve — used by
    /// <c>DeploymentSafetyChecks</c> to verify every member of <see cref="NotificationTransport"/> has an
    /// implementation before the app finishes starting, rather than waiting to discover a gap the first
    /// time a message for that transport is due.</summary>
    IReadOnlyCollection<NotificationTransport> RegisteredTransports { get; }
}

/// <summary>Mirrors <see cref="INotificationTransportRegistry"/> for the platform-token side
/// (ARCHITECTURE_CYCLE9.md §104.2) — kept as a SEPARATE registry, not a second method on the same
/// interface, for the same reason <see cref="IChannelProvisioning"/> is its own interface rather than a
/// second method on <see cref="INotificationTransport"/> (ARCHITECTURE_CYCLE4.md §28): DI can make an
/// entire registration not exist outside Production, and that guarantee is per-interface.</summary>
public interface IChannelProvisioningRegistry
{
    IChannelProvisioning For(NotificationTransport transport);

    IReadOnlyCollection<NotificationTransport> RegisteredTransports { get; }
}

/// <summary>Thrown by <see cref="INotificationTransportRegistry.For"/>/<see cref="IChannelProvisioningRegistry.For"/>
/// when no adapter is registered for the requested transport — a configuration/deployment defect
/// (a transport enum member exists with no matching adapter wiring in <c>Program.cs</c>), not a runtime
/// condition any caller should catch and route around silently.</summary>
public sealed class MissingTransportImplementationException(NotificationTransport transport)
    : InvalidOperationException($"No implementation is registered for transport '{transport}'.")
{
    public NotificationTransport Transport { get; } = transport;
}

/// <summary>
/// Dictionary-backed implementation shared by both registries above (same shape, different service type)
/// — built once in <c>Program.cs</c> from the map <c>Notifications:Provider</c> selects
/// (ARCHITECTURE_CYCLE9.md §104.2):
/// <c>Notifications:Provider=logging</c> → every transport maps to the SAME logging stub instance
/// ("tests never hit the network", "nothing sends after deploy until configured"); <c>=green-api</c> →
/// <c>WhatsApp</c> maps to the existing GREEN-API adapter, <c>Max</c> to the new
/// <c>Services/Notifications/GreenApiMax/*</c> adapter.
/// </summary>
public sealed class NotificationTransportRegistry(IReadOnlyDictionary<NotificationTransport, INotificationTransport> byTransport)
    : INotificationTransportRegistry
{
    public INotificationTransport For(NotificationTransport transport) =>
        byTransport.TryGetValue(transport, out var transportImpl)
            ? transportImpl
            : throw new MissingTransportImplementationException(transport);

    public IReadOnlyCollection<NotificationTransport> RegisteredTransports => byTransport.Keys.ToArray();
}

public sealed class ChannelProvisioningRegistry(IReadOnlyDictionary<NotificationTransport, IChannelProvisioning> byTransport)
    : IChannelProvisioningRegistry
{
    public IChannelProvisioning For(NotificationTransport transport) =>
        byTransport.TryGetValue(transport, out var provisioningImpl)
            ? provisioningImpl
            : throw new MissingTransportImplementationException(transport);

    public IReadOnlyCollection<NotificationTransport> RegisteredTransports => byTransport.Keys.ToArray();
}
