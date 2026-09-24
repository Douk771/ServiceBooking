using Microsoft.Extensions.Options;

namespace ServiceBooking.API.Services.PhoneVerification.Max;

/// <summary>
/// Shared "subscribe (or re-subscribe) the webhook, record the outcome" logic (ARCHITECTURE_CYCLE14.md
/// §146.3, Q4) — used by BOTH <c>MaxWebhookStartupSubscriber</c> (at process start, registered only when
/// <c>PhoneVerification:Provider = "max-bot"</c>) and <c>MaxWebhookRenewTask</c> (every 4 hours,
/// registered UNCONDITIONALLY like every other <c>IScheduledTask</c>). Neither caller has to know how to
/// talk to <see cref="PhoneVerificationDiagnostics"/> itself; this class is the one place that does.
/// </summary>
public sealed class MaxWebhookSubscriber(
    IMaxBotClient botClient, IOptions<PhoneVerificationOptions> options,
    PhoneVerificationDiagnostics diagnostics, ILogger<MaxWebhookSubscriber> logger)
{
    /// <summary>Never throws — a subscribe failure (network, misconfiguration, platform outage) is
    /// recorded and logged, never allowed to crash the caller (§146.3: "неудачная подписка старт не
    /// роняет"). While <c>PhoneVerification:Provider = "stub"</c> (the shipped default — SPEC §0.5's
    /// "невыпущенность"), this is a TRUE no-op: it does not call <see cref="IMaxBotClient.SubscribeAsync"/>
    /// at all and logs nothing, rather than an Error-level "subscribe failed" every single renewal pass
    /// for as long as the subsystem stays off, which would be a false alarm reaching GlitchTip for the
    /// documented default state, not a real incident.</summary>
    public async Task<bool> SubscribeAsync(CancellationToken ct)
    {
        if (!string.Equals(options.Value.Provider, "max-bot", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var success = await botClient.SubscribeAsync(ct);
            diagnostics.RecordSubscriptionAttempt(success, success ? null : "MAX platform did not confirm the subscription");

            if (success)
                logger.LogInformation("phone-verification: webhook subscription confirmed");
            else
                logger.LogError("phone-verification: webhook subscribe request did not succeed");

            return success;
        }
        catch (Exception ex)
        {
            diagnostics.RecordSubscriptionAttempt(false, ex.Message);
            logger.LogError(ex, "phone-verification: webhook subscribe threw");
            return false;
        }
    }
}
