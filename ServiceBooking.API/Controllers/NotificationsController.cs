using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Client-facing opt-out (cabinet + public link, US-33, T4-B10) and the provider's delivery-status
/// webhook (US-32 p.7, T4-B13 — cut second). Two very different trust levels share this controller only
/// because API_CONTRACT_CYCLE4.md groups them under <c>/api/notifications/*</c>.
/// </summary>
[ApiController]
[Route("api/notifications")]
public class NotificationsController(AppDbContext db, IOptions<NotificationOptions> options, ILogger<NotificationsController> logger) : ControllerBase
{
    // ── Cabinet preferences ──────────────────────────────────────────────────────────────────────

    [HttpGet("preferences")]
    [Authorize]
    public async Task<ActionResult<NotificationPreferencesDto>> GetPreferences()
    {
        var phone = await CallerCanonicalPhoneAsync();
        if (phone is null) return Ok(new NotificationPreferencesDto(true));

        var optedOut = await db.NotificationOptOuts.AsNoTracking().AnyAsync(o => o.Phone == phone);  // SUBJECT-PHONE-GATE: not-account-scoped — reads only the CALLER's own opt-out status for their own phone (CallerCanonicalPhoneAsync), never another subject's data (ARCHITECTURE_CYCLE16.md §245.3)
        return Ok(new NotificationPreferencesDto(!optedOut));
    }

    [HttpPut("preferences")]
    [Authorize]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferencesDto dto)
    {
        var phone = await CallerCanonicalPhoneAsync();
        if (phone is null) return NoContent();

        await SetOptOutAsync(phone, optedOut: !dto.Enabled, OptOutSource.Cabinet, User.FindFirstValue(ClaimTypes.NameIdentifier));
        return NoContent();
    }

    // ── Public unsubscribe link ──────────────────────────────────────────────────────────────────

    [HttpGet("unsubscribe/{token}")]
    public async Task<ActionResult<UnsubscribePageDto>> GetUnsubscribePage(string token)
    {
        if (!TryReadToken(token, out var phone)) return NotFound();

        var alreadyOptedOut = await db.NotificationOptOuts.AsNoTracking().AnyAsync(o => o.Phone == phone);  // SUBJECT-PHONE-GATE: not-account-scoped — anonymous unsubscribe link, phone comes from a signed one-time token, not from any caller account (ARCHITECTURE_CYCLE16.md §245.3)
        return Ok(new UnsubscribePageDto(PhoneDisplayMask.Mask(phone), alreadyOptedOut));
    }

    [HttpPost("unsubscribe/{token}")]
    public async Task<IActionResult> Unsubscribe(string token)
    {
        if (!TryReadToken(token, out var phone)) return NotFound();

        await SetOptOutAsync(phone, optedOut: true, OptOutSource.Link, userId: null);
        return NoContent();
    }

    // ── Provider webhook ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// API_CONTRACT_CYCLE4.md §33 / ARCHITECTURE_CYCLE4.md §32. <paramref name="parser"/> is resolved
    /// per-action (not a primary-constructor dependency) so an environment where
    /// <c>IProviderWebhookParser</c> isn't registered yet doesn't break every OTHER action on this
    /// controller (preferences, unsubscribe) — same reasoning as <c>AdminController.GetScheduledTasks</c>'s
    /// <c>[FromServices]</c> use.
    /// </summary>
    [HttpPost("provider-webhook/{token}")]
    [EnableRateLimiting("notifications-webhook")]
    public async Task<IActionResult> ProviderWebhook(string token, [FromServices] IProviderWebhookParser parser) =>
        await HandleWebhookAsync(token, parser);

    /// <summary>
    /// ARCHITECTURE_CYCLE9.md §104.7 / API_CONTRACT_CYCLE9.md §114.5 (US-120). The ORIGINAL
    /// <c>provider-webhook/{token}</c> route above is untouched and still means WhatsApp — it may already
    /// be configured at the provider's end (§104.7). This one adds the transport as an explicit path
    /// segment, resolved through <see cref="IProviderWebhookParserRegistry"/> instead of a single
    /// injected <see cref="IProviderWebhookParser"/>.
    ///
    /// A <paramref name="transport"/> segment that doesn't parse as <see cref="NotificationTransport"/>
    /// is a 404, not a 400 — this route must never tell an unauthenticated caller which transports exist
    /// (the SAME reasoning §19.2 already applies to a wrong/missing token below).
    /// </summary>
    [HttpPost("provider-webhook/{transport}/{token}")]
    [EnableRateLimiting("notifications-webhook")]
    public async Task<IActionResult> ProviderWebhookByTransport(
        string transport, string token, [FromServices] IProviderWebhookParserRegistry parserRegistry)
    {
        if (!Enum.TryParse<NotificationTransport>(transport, ignoreCase: true, out var parsedTransport))
            return NotFound();

        IProviderWebhookParser parser;
        try
        {
            parser = parserRegistry.For(parsedTransport);
        }
        catch (MissingTransportImplementationException)
        {
            // Recognized enum member, genuinely no parser wired up for it — same "don't reveal what
            // exists" treatment as an unparsable segment, not a 500 a provider would retry forever on.
            return NotFound();
        }

        return await HandleWebhookAsync(token, parser);
    }

    /// <summary>Shared by both webhook actions above (ARCHITECTURE_CYCLE4.md §32, extended by cycle 9 to
    /// be transport-agnostic) — token check, body read, parse, and the delivery-status/channel-state
    /// dispatch are identical regardless of which route or which transport's parser produced the
    /// <see cref="ProviderCallback"/>.</summary>
    private async Task<IActionResult> HandleWebhookAsync(string token, IProviderWebhookParser parser)
    {
        var expectedToken = options.Value.WebhookToken;
        if (string.IsNullOrEmpty(expectedToken) || !ConstantTimeEquals(token, expectedToken))
        {
            // API_CONTRACT_CYCLE4.md §19.2: 401 here must be an EMPTY body — the caller is a provider,
            // not a browser, and ProblemDetails tells an unauthenticated caller more than it needs.
            // Neither Unauthorized() nor StatusCode(401) does that: both return StatusCodeResult, which
            // DOES implement IClientErrorActionResult, so [ApiController]'s ClientErrorResultFilter
            // rewrites them into ProblemDetails (an earlier comment here claimed otherwise; NTF-W002
            // caught it). EmptyResult is outside that interface, so the filter leaves it alone.
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return new EmptyResult();
        }

        string rawBody;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            rawBody = await reader.ReadToEndAsync();

        ProviderCallback? callback;
        try
        {
            callback = parser.Parse(rawBody);
        }
        catch (Exception ex)
        {
            // A malformed/unexpected body must never fail the webhook with anything but 200 (§32: the
            // provider retries forever on anything else) — log for our own visibility and move on.
            logger.LogWarning(ex, "Failed to parse provider webhook body");
            return Ok();
        }

        if (callback is null) return Ok();

        switch (callback.Kind)
        {
            case ProviderCallbackKind.DeliveryStatus:
                await ApplyDeliveryStatusAsync(callback);
                break;
            case ProviderCallbackKind.ChannelState:
                await ApplyChannelStateAsync(callback);
                break;
        }

        return Ok();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task ApplyDeliveryStatusAsync(ProviderCallback callback)
    {
        if (callback.ProviderMessageId is null || callback.MessageStatus is null) return;

        // Unknown idMessage is NOT an error (§32) — the row may belong to a different environment/run,
        // or the provider may be replaying an event for a message we never actually queued.
        var row = await db.OutboundNotifications
            .FirstOrDefaultAsync(n => n.ProviderMessageId == callback.ProviderMessageId);
        if (row is null)
        {
            logger.LogDebug("Provider webhook referenced unknown idMessage {ProviderMessageId}", callback.ProviderMessageId);
            return;
        }

        // Monotonicity (§32): a status only ever moves forward. Failed is handled as its own
        // unconditional terminal case — a terminal rejection can arrive instead of a delivery update at
        // any point, so it isn't part of the Sent<Delivered<Read progression.
        switch (callback.MessageStatus.Value)
        {
            case ProviderMessageStatus.Failed:
                if (row.Status is NotificationStatus.Sent or NotificationStatus.Pending)
                {
                    row.Status = NotificationStatus.Failed;
                    // ARCHITECTURE_CYCLE9.md §104.9 (US-120): the parser that produced this callback
                    // knows which transport it's for and, for MAX, tells us explicitly which reason
                    // applies (RecipientNotInMax/RejectedByProvider) via TerminalReason — the fallback to
                    // RecipientHasNoWhatsApp is EXACTLY today's behavior for every event
                    // GreenApiWebhookParser produces (it never sets TerminalReason), so WhatsApp's own
                    // delivery log wording is unchanged by this cycle.
                    row.Reason = callback.TerminalReason ?? NotificationReason.RecipientHasNoWhatsApp;
                }
                break;

            case ProviderMessageStatus.Sent:
                if (row.Status == NotificationStatus.Pending) { row.Status = NotificationStatus.Sent; row.SentAtUtc ??= callback.OccurredAtUtc; }
                break;

            case ProviderMessageStatus.Delivered:
                if (row.Status is NotificationStatus.Pending or NotificationStatus.Sent)
                {
                    row.Status = NotificationStatus.Delivered;
                    row.DeliveredAtUtc ??= callback.OccurredAtUtc;
                }
                break;

            case ProviderMessageStatus.Read:
                // Reviewer note: ReadAtUtc used to be stamped unconditionally, even on a row that had
                // already gone Failed/Cancelled/Expired by the time a late/out-of-order "read" event
                // arrived — a terminal row picking up a later timestamp field looks like it was still
                // live after the terminal status was recorded. Moved inside the same monotonicity guard
                // as the Status/DeliveredAtUtc update above it.
                if (row.Status is NotificationStatus.Pending or NotificationStatus.Sent or NotificationStatus.Delivered)
                {
                    row.Status = NotificationStatus.Delivered; // "Read" is a timestamp, not its own Status member (§23.4: exactly seven)
                    row.DeliveredAtUtc ??= callback.OccurredAtUtc;
                    row.ReadAtUtc ??= callback.OccurredAtUtc;
                }
                break;
        }

        await db.SaveChangesAsync();
    }

    private async Task ApplyChannelStateAsync(ProviderCallback callback)
    {
        if (callback.InstanceId is null || callback.ChannelState is null) return;

        var channel = await db.NotificationChannels.FirstOrDefaultAsync(c => c.ProviderInstanceId == callback.InstanceId);
        if (channel is null) return;

        var mapping = ChannelStateMapper.Map(channel.State, callback.ChannelState.Value, channel.ConnectedAtUtc is not null);
        channel.LastStateCheckAtUtc = callback.OccurredAtUtc;

        if (mapping.State != channel.State && mapping.Reason is { } reason)
        {
            db.ChannelStateEvents.Add(new ChannelStateEvent
            {
                Id = Guid.NewGuid(), ChannelId = channel.Id,
                FromState = channel.State, ToState = mapping.State, Reason = reason, OccurredAtUtc = callback.OccurredAtUtc,
            });
            channel.State = mapping.State;
            channel.LastStateReason = reason;
            if (mapping.State == ChannelState.Connected)
            {
                channel.ConnectedAtUtc ??= callback.OccurredAtUtc;
                channel.ConsecutiveSendFailures = 0; // I8: same reset NotificationChannelsController's QR path and ChannelStateTransition.Apply do
            }

            // B8 / SPEC US-56 п. 6, US-63 п. 1: a ban must not wait for the N-day idle grace period —
            // §30.4 database-first step only (orphan the id, blank the channel's own credentials); the
            // actual provider delete is left to ChannelHealthTask's orphan-retry sweep (which runs first
            // thing every pass, at most a few minutes away) rather than a synchronous provider call from
            // inside a webhook handler, which must stay fast and always answer 200 (§32).
            if (mapping.State == ChannelState.Blocked && channel.ProviderInstanceId is { } bannedInstanceId)
            {
                channel.OrphanedInstanceId = bannedInstanceId;
                channel.ProviderInstanceId = null;
                channel.ProviderSecretCiphertext = null;
                channel.ProviderSecretKeyId = null;
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task<string?> CallerCanonicalPhoneAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return null;
        return await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
    }

    private bool TryReadToken(string token, out string phone)
    {
        phone = string.Empty;
        var key = options.Value.UnsubscribeKey;
        if (string.IsNullOrEmpty(key)) return false;
        return UnsubscribeTokens.TryRead(token, Encoding.UTF8.GetBytes(key), out phone);
    }

    private async Task SetOptOutAsync(string phone, bool optedOut, OptOutSource source, string? userId)
    {
        var existing = await db.NotificationOptOuts.FirstOrDefaultAsync(o => o.Phone == phone);  // SUBJECT-PHONE-GATE: not-account-scoped — writes the opt-out FOR the phone the caller explicitly supplied (own preferences endpoint or signed unsubscribe token), not a guest-data lookup by an unrelated account (ARCHITECTURE_CYCLE16.md §245.3)
        if (optedOut)
        {
            if (existing is not null) return; // idempotent
            db.NotificationOptOuts.Add(new NotificationOptOut { Id = Guid.NewGuid(), Phone = phone, Source = source, UserId = userId });
        }
        else if (existing is not null)
        {
            db.NotificationOptOuts.Remove(existing);
        }
        await db.SaveChangesAsync();
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var bytesA = Encoding.UTF8.GetBytes(a);
        var bytesB = Encoding.UTF8.GetBytes(b);
        // CryptographicOperations.FixedTimeEquals requires equal-length spans to run in constant time;
        // a length mismatch is itself not sensitive information here (token length isn't secret), so
        // returning false immediately for it is the standard, accepted use of this API.
        return bytesA.Length == bytesB.Length && CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
