using ServiceBooking.API.Services;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Default transport (<c>Notifications:Provider=logging</c>) — the app runs and every notification-
/// related endpoint works with no provider configured at all (US-27 p.9, matching
/// <c>CaptchaService.IsEnforced</c>'s pattern). Writes one structured line and returns a synthetic
/// success; never makes a network call, so it is also what backs every dispatch test
/// (ARCHITECTURE_CYCLE4.md §27) and guarantees Development/Testing can never reach a real WhatsApp
/// account by accident.
/// </summary>
public sealed class LoggingNotificationTransport(ILogger<LoggingNotificationTransport> logger) : INotificationTransport
{
    public Task<SendOutcome> SendAsync(ChannelCredentials credentials, string canonicalPhone, string text, CancellationToken ct)
    {
        logger.LogInformation(
            "Notification stub send: instance={InstanceId} phone={MaskedPhone} length={Length}",
            credentials.InstanceId, LogMasking.Phone(canonicalPhone), text.Length);

        return Task.FromResult<SendOutcome>(new SendOutcome.Sent($"stub-{Guid.NewGuid():N}"));
    }
}

/// <summary>Default provisioning stub (US-35 p.1) — returns a fake instance/QR so the connect flow is
/// exercisable end-to-end without a partner account. Registered together with
/// <see cref="LoggingNotificationTransport"/> under <c>Notifications:Provider=logging</c>.</summary>
public sealed class NoopChannelProvisioning : IChannelProvisioning
{
    // A recognizable 1x1 PNG, not a real QR — the point is that the connect modal has something to
    // render, not that scanning it does anything.
    private const string PlaceholderPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    public Task<ProvisionedInstance> CreateInstanceAsync(CancellationToken ct) =>
        Task.FromResult(new ProvisionedInstance($"stub-instance-{Guid.NewGuid():N}", $"stub-token-{Guid.NewGuid():N}"));

    public Task<QrSnapshot> GetQrAsync(ChannelCredentials credentials, CancellationToken ct) =>
        Task.FromResult(new QrSnapshot(PlaceholderPngBase64, Authorized: false, RefreshAfterSeconds: 3));

    public Task<ProviderChannelState> GetStateAsync(ChannelCredentials credentials, CancellationToken ct) =>
        Task.FromResult(ProviderChannelState.Authorized);

    public Task<string?> GetPhoneNumberAsync(ChannelCredentials credentials, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    public Task SetSendDelayAsync(ChannelCredentials credentials, int milliseconds, CancellationToken ct) =>
        Task.CompletedTask;

    public Task ConfigureWebhookAsync(ChannelCredentials credentials, string webhookUrl, CancellationToken ct) =>
        Task.CompletedTask;

    public Task LogoutAsync(ChannelCredentials credentials, CancellationToken ct) => Task.CompletedTask;

    public Task<InstanceDeletion> DeleteInstanceAsync(string instanceId, CancellationToken ct) =>
        Task.FromResult(new InstanceDeletion(Success: true));
}
