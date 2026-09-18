namespace ServiceBooking.API.Services.Notifications;

public sealed record ProvisionedInstance(string InstanceId, string Token);

/// <summary><see cref="Base64Png"/> is null while the instance hasn't produced a QR yet (still
/// starting up) or once it's already <see cref="Authorized"/> and there's nothing left to scan.
/// <see cref="PhoneNumber"/> (canonical, <c>PhoneNormalizer</c> form) is set only once
/// <see cref="Authorized"/> is true — this is where the connect flow (§29.1) learns the number to fill
/// into <c>NotificationChannel.PhoneNumber</c> from <c>wid</c>, without a separate provisioning call.</summary>
public sealed record QrSnapshot(string? Base64Png, bool Authorized, int RefreshAfterSeconds, string? PhoneNumber = null);

/// <summary>Provider's own instance-state vocabulary, narrowed to what this cycle acts on — the full
/// mapping from provider strings (including the "just starting up" vs "actually disconnected" split
/// for <c>notAuthorized</c>, US-55 p.2) lives in <c>ChannelStateMapper</c>, not here.</summary>
public enum ProviderChannelState
{
    Authorized,
    NotAuthorized,
    Blocked,
    Starting,
    Unknown,
}

public sealed record InstanceDeletion(bool Success);

/// <summary>
/// Everything that uses the PLATFORM's own partner token rather than a salon's channel token
/// (ARCHITECTURE_CYCLE4.md §28) — kept as a separate interface from <see cref="INotificationTransport"/>
/// specifically so DI can make this one simply not exist outside Production
/// (<c>DeploymentSafetyChecks.ValidateNotificationSecrets</c> rule 3, US-35 p.4): a dev/test process must
/// be structurally unable to create or delete a live salon's WhatsApp instance.
/// </summary>
public interface IChannelProvisioning
{
    Task<ProvisionedInstance> CreateInstanceAsync(CancellationToken ct);
    Task<QrSnapshot> GetQrAsync(ChannelCredentials credentials, CancellationToken ct);
    Task<ProviderChannelState> GetStateAsync(ChannelCredentials credentials, CancellationToken ct);
    Task SetSendDelayAsync(ChannelCredentials credentials, int milliseconds, CancellationToken ct);
    Task LogoutAsync(ChannelCredentials credentials, CancellationToken ct);
    Task<InstanceDeletion> DeleteInstanceAsync(string instanceId, CancellationToken ct);
}
