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

    /// <summary>The provider's own source for the authorized number (<c>getSettings</c>'s <c>wid</c>,
    /// §29.1) — used both by the QR-authorization poll and, for a channel that authorized purely through
    /// <see cref="ChannelStateMapper"/> polling rather than the QR flow, by <c>ChannelHealthTask</c>
    /// (B4). Returns <see langword="null"/> on any failure — never throws — so a channel that is
    /// genuinely Connected is not put at risk by a number lookup that couldn't complete this pass.</summary>
    Task<string?> GetPhoneNumberAsync(ChannelCredentials credentials, CancellationToken ct);
    Task SetSendDelayAsync(ChannelCredentials credentials, int milliseconds, CancellationToken ct);

    /// <summary>I2: without this, the provider never calls back at all — <c>noAccount</c>/delivery-status
    /// events simply don't arrive, no matter how correct §32's endpoint is. Best effort, same as
    /// <see cref="SetSendDelayAsync"/>: a failure here must not undo an otherwise-successful Connect.
    /// <paramref name="webhookUrl"/> already carries this channel's webhook auth token as a PATH segment
    /// (matching the existing <c>/provider-webhook/{token}</c> route) — GREEN-API's own
    /// <c>webhookUrlToken</c> mechanism is deliberately NOT used (see cycle report I2): it arrives as an
    /// <c>Authorization</c> HEADER, which the webhook controller doesn't (and, for a single shared
    /// per-deployment token rather than a per-channel one, doesn't need to) verify.</summary>
    Task ConfigureWebhookAsync(ChannelCredentials credentials, string webhookUrl, CancellationToken ct);
    Task LogoutAsync(ChannelCredentials credentials, CancellationToken ct);
    Task<InstanceDeletion> DeleteInstanceAsync(string instanceId, CancellationToken ct);
}
