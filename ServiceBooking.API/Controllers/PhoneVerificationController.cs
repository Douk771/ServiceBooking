using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.PhoneVerification;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.PhoneVerification.Max;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE12.md §148, API_CONTRACT_CYCLE12.md §162-§166. Every route here is either fully
/// anonymous or "anonymous OR bearer" (§163) — none of them go through the regular
/// <c>[Authorize]</c>-everything-by-default shape this codebase otherwise uses, since the whole point of
/// the registration entry point is that no account exists yet.
/// </summary>
[ApiController]
[Route("api/phone-verification")]
[AllowAnonymous]
public class PhoneVerificationController(
    PhoneVerificationSessionService sessionService,
    IPhoneVerificationMethodRegistry registry,
    PhoneVerificationDiagnostics diagnostics,
    IOptions<PhoneVerificationOptions> options,
    UserManager<AppUser> userManager,
    MaxWebhookHandler webhookHandler,
    ILogger<PhoneVerificationController> logger) : ControllerBase
{
    /// <summary>§150.3, §162 — one answer for the register form, the profile screen and the change-phone
    /// screen; each avoids a second round trip by caching this for 5 minutes (react-query, frontend-side).</summary>
    [HttpGet("config")]
    public ActionResult<PhoneVerificationConfigDto> GetConfig()
    {
        var adapter = registry.Get(PhoneVerificationMethod.MaxBot);
        var enabled = adapter.Enabled;
        var healthy = enabled && diagnostics.WebhookSubscribed;
        var methods = enabled ? new[] { adapter.Method } : [];

        return Ok(new PhoneVerificationConfigDto(
            enabled, healthy, methods, options.Value.SessionTtlMinutes * 60, options.Value.PollIntervalSeconds));
    }

    /// <summary>§163. Accepts a request with no bearer token (registration) or with one (profile,
    /// US-12-16) — <c>[AllowAnonymous]</c> on the controller means the framework never rejects a missing
    /// token on its own, so a PRESENT-but-invalid token is checked by hand below (the one case §163's
    /// 401 actually covers).</summary>
    [HttpPost("sessions")]
    [EnableRateLimiting("phone-verify-start")]
    public async Task<IActionResult> StartSession([FromBody] StartPhoneVerificationRequestDto? dto, CancellationToken ct)
    {
        if (Request.Headers.ContainsKey("Authorization") && User.Identity?.IsAuthenticated != true)
            return Unauthorized();

        AppUser? user = null;
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            user = userId is null ? null : await userManager.FindByIdAsync(userId);
            if (user is null) return Unauthorized();
        }

        string canonicalPhone;
        if (user is not null && string.IsNullOrWhiteSpace(dto?.Phone))
        {
            // §163: an authenticated caller may omit `phone` — falls back to the account's current number.
            if (string.IsNullOrEmpty(user.PhoneNumber) || !PhoneNormalizer.TryNormalizeRussian(user.PhoneNumber, out canonicalPhone))
                return BadRequest(PhoneVerificationTexts.InvalidPhoneFormat);
        }
        else if (!PhoneNormalizer.TryNormalizeRussian(dto?.Phone, out canonicalPhone))
        {
            return BadRequest(PhoneVerificationTexts.InvalidPhoneFormat);
        }

        // §163's error table requires the same 409 both when the subsystem is switched off AND when the
        // webhook isn't currently subscribed (enabled && !healthy) — a session started while the
        // subscription is lost can never receive the update that would resolve it, and GetConfig's own
        // `healthy` is exactly this same diagnostics read (review finding, blocker 4).
        var adapterForHealthCheck = registry.Get(PhoneVerificationMethod.MaxBot);
        if (adapterForHealthCheck.Enabled && !diagnostics.WebhookSubscribed)
            return Conflict(PhoneVerificationTexts.SubsystemUnavailable);

        var purpose = user is null ? PhoneVerificationPurpose.Registration : PhoneVerificationPurpose.Profile;
        var result = await sessionService.StartAsync(canonicalPhone, user?.Id, purpose, ct);

        switch (result.Outcome)
        {
            case PhoneVerificationSessionService.StartOutcome.SubsystemDisabled:
                return Conflict(PhoneVerificationTexts.SubsystemUnavailable);
            case PhoneVerificationSessionService.StartOutcome.TooManyOpenSessions:
                return StatusCode(StatusCodes.Status429TooManyRequests, PhoneVerificationTexts.TooManyStartAttempts);
        }

        var session = result.Session!;
        var created = new PhoneVerificationSessionCreatedDto(
            session.Id, result.StatusToken!, session.Method, result.DeepLink!,
            result.QrPng is null ? null : Convert.ToBase64String(result.QrPng),
            PhoneDisplayMask.Mask(canonicalPhone), session.ExpiresAtUtc,
            (int)Math.Round((session.ExpiresAtUtc - session.CreatedAtUtc).TotalSeconds));

        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>§164 — polled by the frontend every <c>pollIntervalSeconds</c>. 404 covers BOTH an
    /// unknown id and a mismatched token, by design (never lets the caller distinguish "wrong token" from
    /// "no such session").</summary>
    [HttpGet("sessions/{sessionId:guid}")]
    public async Task<IActionResult> GetSession(Guid sessionId, [FromQuery] string? statusToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(statusToken)) return NotFound();

        var session = await sessionService.FindByTokenAsync(sessionId, statusToken, ct);
        if (session is null) return NotFound();

        return Ok(MapStatus(session));
    }

    /// <summary>§166 — ALWAYS 204, including when there was nothing to cancel.</summary>
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> CancelSession(Guid sessionId, [FromQuery] string? statusToken, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(statusToken))
            await sessionService.CancelAsync(sessionId, statusToken, ct);

        return NoContent();
    }

    /// <summary>§146, §167 — anonymous, secret in the path's last segment. Returns 404 for both an
    /// unrecognized token AND a disabled subsystem (§146.2's own table: "существование маршрута не
    /// подтверждается"). Always 200 for a token that DOES match, regardless of what's inside the body —
    /// see <see cref="MaxWebhookHandler"/> for why (О4: the platform drops the subscription after 8h
    /// without a single successful response).</summary>
    [HttpPost("max/webhook/{token}")]
    [EnableRateLimiting("phone-verify-webhook")]
    public async Task<IActionResult> ReceiveWebhook(string token, CancellationToken ct)
    {
        var adapter = registry.Get(PhoneVerificationMethod.MaxBot);
        if (!adapter.Enabled) return NotFound();

        var configuredToken = options.Value.Max.WebhookToken;
        if (string.IsNullOrEmpty(configuredToken) || !TokenMatches(token, configuredToken))
            return NotFound();

        JsonElement root;
        try
        {
            root = await JsonSerializer.DeserializeAsync<JsonElement>(Request.Body, cancellationToken: ct);
        }
        catch (JsonException)
        {
            // §146.2: an unparsable body is still "an update we don't recognize", not a 4xx — the
            // platform must never see anything but 2xx from this route while it's enabled.
            logger.LogInformation("phone-verification webhook: request body was not valid JSON");
            return Ok();
        }

        try
        {
            await webhookHandler.HandleAsync(root, ct);
        }
        catch (Exception ex)
        {
            // §146.2: an exception anywhere in processing still answers 200 — redelivery of the same
            // update is safe (§145.2's idempotency table), a non-2xx response is not.
            logger.LogError(ex, "phone-verification webhook: unhandled exception while processing an update");
        }

        return Ok();
    }

    // Constant-time comparison — the webhook token is a bearer secret living in the URL path (§146.1).
    private static bool TokenMatches(string presented, string configured) =>
        presented.Length == configured.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(configured));

    private static PhoneVerificationSessionStatusDto MapStatus(PhoneVerificationSession session)
    {
        var now = DateTime.UtcNow;
        var expired = PhoneVerificationStateMachine.IsExpired(session.Status, session.ExpiresAtUtc, now);

        // §164's status vocabulary has no "Consumed" member — a session already redeemed by /auth/register
        // still reads as Verified from the outside (its CompletedAtUtc/verifiedAtUtc are unchanged); the
        // frontend's own poller never observes this in practice since it stops on the FIRST terminal
        // status it sees (§148.4), before a redemption could happen.
        var displayStatus = expired ? PhoneVerificationDisplayStatus.Expired : session.Status switch
        {
            PhoneVerificationStatus.Pending => PhoneVerificationDisplayStatus.Pending,
            PhoneVerificationStatus.Linked => PhoneVerificationDisplayStatus.Linked,
            PhoneVerificationStatus.Verified => PhoneVerificationDisplayStatus.Verified,
            PhoneVerificationStatus.Consumed => PhoneVerificationDisplayStatus.Verified,
            PhoneVerificationStatus.Rejected => PhoneVerificationDisplayStatus.Rejected,
            PhoneVerificationStatus.Cancelled => PhoneVerificationDisplayStatus.Cancelled,
            _ => PhoneVerificationDisplayStatus.Rejected,
        };

        // §164: failureReason is documented as a code that accompanies status == Rejected — Expired is a
        // synthesized display status (TTL passed, independent of what session.Status actually is), not
        // Rejected, so it must not carry one (review finding, non-blocking 11). The human-readable
        // `message` below is the contract-sanctioned way to explain an Expired session either way.
        var failureReason = displayStatus == PhoneVerificationDisplayStatus.Rejected ? session.FailureReason : null;

        string? message = displayStatus switch
        {
            PhoneVerificationDisplayStatus.Expired => PhoneVerificationTexts.ForFailureReason(PhoneVerificationFailureReason.PayloadExpired),
            PhoneVerificationDisplayStatus.Rejected when session.FailureReason == PhoneVerificationFailureReason.PhoneMismatch =>
                PhoneVerificationTexts.PhoneMismatch(session.MismatchedPhoneMasked ?? "?", PhoneDisplayMask.Mask(session.CanonicalPhone)),
            PhoneVerificationDisplayStatus.Rejected => PhoneVerificationTexts.ForFailureReason(session.FailureReason),
            _ => null,
        };

        // Consumed also displays as Verified (see the class doc above) — its own verifiedAtUtc must ride
        // along too, or a poll observed after redemption would show "Verified" with no date (review
        // finding, non-blocking 11).
        DateTime? verifiedAtUtc = session.Status is PhoneVerificationStatus.Verified or PhoneVerificationStatus.Consumed
            ? session.CompletedAtUtc
            : null;

        return new PhoneVerificationSessionStatusDto(
            session.Id, displayStatus, failureReason, message, PhoneDisplayMask.Mask(session.CanonicalPhone),
            session.ExpiresAtUtc, verifiedAtUtc);
    }
}
