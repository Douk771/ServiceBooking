using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.8 — the automatic check message of a freshly bound number, sent to the OWNER's phone. The mark
/// (<see cref="NotificationChannel.LastTestResult"/> = Pending for <see cref="NotificationChannel.AutoTestInstanceId"/>) is written by
/// <see cref="ChannelStateTransition.Apply"/>; this service takes the mark with a conditional UPDATE (the row that flips Pending to
/// Sending owns the send — a second server or a second pass gets zero rows and does nothing), decides by
/// <see cref="ChannelTestMessageRule"/>, sends once and records the outcome on the channel and in the channel's journal. A send is never
/// repeated: a stuck "Sending" ends as Failed after <see cref="SendingTimeout"/>, an unbound "Pending" after <see cref="PendingTimeout"/>.
/// It does not go through <c>OutboundNotifications</c> (that table needs a company and the company gate; the check belongs to the account).
/// </summary>
public sealed class ChannelTestMessageService(
    AppDbContext db,
    INotificationTransportRegistry transports,
    PlatformSettings platformSettings,
    IOptions<NotificationOptions> options,
    IOptions<DemoModeOptions> demoOptions,
    INotificationClock clock,
    ILogger<ChannelTestMessageService> logger)
{
    public const int BatchSize = 20;
    public static readonly TimeSpan SendingTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PendingTimeout = TimeSpan.FromHours(1);

    public async Task<(int Scanned, int Sent)> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        await ExpireStuckAsync(now, ct);

        var candidates = await db.NotificationChannels.AsNoTracking()
            .Where(c => c.LastTestResult == ChannelTestResult.Pending && c.State == ChannelState.Connected &&
                        c.ProviderInstanceId != null && c.AutoTestInstanceId == c.ProviderInstanceId)
            .OrderBy(c => c.LastTestResultAtUtc)
            .Select(c => new { c.Id, Instance = c.ProviderInstanceId! })
            .Take(BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            var owned = await db.NotificationChannels
                .Where(c => c.Id == candidate.Id && c.LastTestResult == ChannelTestResult.Pending &&
                            c.AutoTestInstanceId == candidate.Instance && c.ProviderInstanceId == candidate.Instance)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.LastTestResult, ChannelTestResult.Sending)
                    .SetProperty(c => c.LastTestResultAtUtc, now), ct);
            if (owned != 1) continue;

            try
            {
                if (await ProcessAsync(candidate.Id, candidate.Instance, now, ct)) sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The mark is already Sending: the send is never retried; the timeout turns it into Failed.
                logger.LogError(ex, "Automatic check message failed for channel {ChannelId}", candidate.Id);
            }
        }
        return (candidates.Count, sent);
    }

    /// <summary>Sending longer than five minutes, or Pending for an hour on a number that is not connected, ends as Failed.</summary>
    private async Task ExpireStuckAsync(DateTime now, CancellationToken ct)
    {
        var sendingCutoff = now - SendingTimeout;
        var stuckSending = await db.NotificationChannels
            .Where(c => c.LastTestResult == ChannelTestResult.Sending && c.LastTestResultAtUtc < sendingCutoff)
            .ToListAsync(ct);
        foreach (var channel in stuckSending)
            Finish(channel, ChannelTestResult.Failed, ChannelStateReason.TestMessageFailed, ChannelTestFailure.Unconfirmed, now);

        var pendingCutoff = now - PendingTimeout;
        var stuckPending = await db.NotificationChannels
            .Where(c => c.LastTestResult == ChannelTestResult.Pending && c.State != ChannelState.Connected &&
                        c.LastTestResultAtUtc < pendingCutoff)
            .ToListAsync(ct);
        foreach (var channel in stuckPending)
            Finish(channel, ChannelTestResult.Failed, ChannelStateReason.TestMessageFailed, ChannelTestFailure.NotConnected, now);

        if (stuckSending.Count + stuckPending.Count > 0) await db.SaveChangesAsync(ct);
    }

    private async Task<bool> ProcessAsync(Guid channelId, string instance, DateTime now, CancellationToken ct)
    {
        var channel = await db.NotificationChannels.FirstAsync(c => c.Id == channelId, ct);
        var ownerPhone = await db.Users.AsNoTracking().Where(u => u.Id == channel.OwnerUserId)
            .Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct);
        var decision = ChannelTestMessageRule.Decide(
            await platformSettings.IsCustomerMessagingEnabledAsync(ct), demoOptions.Value.Enabled, ownerPhone, channel.PhoneNumber,
            options.Value.TestMessage.AllowSameNumber);

        if (decision != ChannelTestDecision.Send)
        {
            Finish(channel, ChannelTestMessageRule.ResultOf(decision), ChannelStateReason.TestMessageSkipped, decision.ToString(), now);
            await db.SaveChangesAsync(ct);
            return false;
        }

        if (channel.ProviderInstanceId != instance || channel.ProviderSecretCiphertext is null || string.IsNullOrEmpty(options.Value.EncryptionKey))
        {
            Finish(channel, ChannelTestResult.Failed, ChannelStateReason.TestMessageFailed, ChannelTestFailure.NotConnected, now);
            await db.SaveChangesAsync(ct);
            return false;
        }

        ChannelCredentials credentials;
        try
        {
            credentials = new ChannelCredentials(instance, SecretProtector.Decrypt(channel.ProviderSecretCiphertext, options.Value.EncryptionKey, channel.Id));
        }
        catch (Exception ex) when (ex is ChannelSecretUnavailableException or ArgumentException)
        {
            Finish(channel, ChannelTestResult.Failed, ChannelStateReason.TestMessageFailed, ChannelTestFailure.NotConnected, now);
            await db.SaveChangesAsync(ct);
            return false;
        }

        var outcome = await transports.For(channel.Transport)
            .SendAsync(credentials, ownerPhone!, MessengerTexts.TestMessageBody(channel.Transport), ct);
        var ok = outcome is SendOutcome.Sent;
        channel.LastTestMessageAtUtc = clock.UtcNow;
        Finish(channel, ok ? ChannelTestResult.Sent : ChannelTestResult.Failed,
            ok ? ChannelStateReason.TestMessageSent : ChannelStateReason.TestMessageFailed,
            ok ? null : ChannelTestFailure.SendFailed, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return ok;
    }

    private void Finish(NotificationChannel channel, ChannelTestResult result, ChannelStateReason reason, string? detail, DateTime now)
    {
        channel.LastTestResult = result;
        channel.LastTestResultAtUtc = now;
        db.ChannelStateEvents.Add(new ChannelStateEvent
        {
            Id = Guid.NewGuid(), ChannelId = channel.Id, FromState = channel.State, ToState = channel.State,
            Reason = reason, Detail = detail, OccurredAtUtc = now,
        });
    }
}
