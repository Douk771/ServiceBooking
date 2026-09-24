using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.PhoneVerification.Max;

namespace ServiceBooking.API.Services.Scheduling.Tasks;

/// <summary>
/// The SIXTH scheduled task (ARCHITECTURE_CYCLE14.md §146.3, Q4, R3) — period 4 hours, comfortably under
/// the platform's own 8-hour "no successful response → subscription silently dropped" window (О4), so a
/// long-lived process never goes more than half that window without re-confirming. Registered
/// unconditionally in <c>Program.cs</c> (like every other <see cref="IScheduledTask"/>), but a TRUE no-op
/// while <c>PhoneVerification:Provider = "stub"</c> — <c>MaxWebhookSubscriber.SubscribeAsync</c> itself
/// skips the network call and logs nothing in that state, so the shipped default configuration never
/// produces a false "subscribe failed" every 4 hours forever.
/// </summary>
public sealed class MaxWebhookRenewTask(MaxWebhookSubscriber subscriber, IOptions<PhoneVerificationOptions> options) : IScheduledTask
{
    public string Name => "max-webhook-renew";
    public TimeSpan DefaultPeriod => TimeSpan.FromHours(4);

    public async Task<ScheduledTaskOutcome> ExecuteAsync(CancellationToken ct)
    {
        if (!string.Equals(options.Value.Provider, "max-bot", StringComparison.OrdinalIgnoreCase))
            return new ScheduledTaskOutcome(0, 0, 0, "phone-verification webhook renewal skipped (subsystem disabled)");

        var success = await subscriber.SubscribeAsync(ct);
        var summary = success ? "phone-verification webhook re-subscribed" : "phone-verification webhook subscribe failed";
        return new ScheduledTaskOutcome(1, success ? 1 : 0, 0, summary);
    }
}
