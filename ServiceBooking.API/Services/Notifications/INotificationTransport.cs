using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// A channel's decrypted credentials, alive only for the duration of one call (ARCHITECTURE_CYCLE4.md
/// §28). <see cref="ToString"/> is overridden so a structural logger that naively interpolates this into
/// a log template never serializes the real token — the three barriers in §24.3 are about the URL/log
/// category, this is the equivalent guard for the value itself.
/// </summary>
public sealed record ChannelCredentials(string InstanceId, string Token)
{
    public override string ToString() => "ChannelCredentials(***)";
}

/// <summary>Outcome of one send attempt (§26.4) — classification decides both what happens to the queue
/// row and what happens to the channel's health counters. See <c>GreenApiResultClassifier</c> for how a
/// provider's raw HTTP response becomes one of these.</summary>
public abstract record SendOutcome
{
    /// <summary>Accepted by the provider. <see cref="ProviderMessageId"/> is <c>idMessage</c> — what a
    /// later delivery-status webhook correlates back to this row.</summary>
    public sealed record Sent(string ProviderMessageId) : SendOutcome;

    /// <summary>Network failure, timeout, 5xx, or 429 (rate limited) — retry later with backoff.</summary>
    public sealed record TransientFailure(string? Detail) : SendOutcome;

    /// <summary>Provider rejected the message for a reason that will never succeed on retry (e.g. the
    /// recipient has no WhatsApp account) — terminal, no retry.</summary>
    public sealed record PermanentlyRejected(NotificationReason Reason, string? Detail) : SendOutcome;

    /// <summary>401/403 or another sign the CHANNEL itself (not this one message) is the problem — the
    /// row stays <c>Pending</c> (§26.4) and the channel's state is what needs to change, not the queue.</summary>
    public sealed record ChannelInvalid(string? Detail) : SendOutcome;
}

/// <summary>
/// Sends one message through a salon's own channel. Stateless and takes credentials as an explicit
/// parameter (never DI/ambient context, §28) — multi-tenancy here means the same transport instance
/// speaks for a different salon on every call, and any state cached on the transport itself would be a
/// future message sent under the wrong number.
/// </summary>
public interface INotificationTransport
{
    Task<SendOutcome> SendAsync(ChannelCredentials credentials, string canonicalPhone, string text, CancellationToken ct);
}
