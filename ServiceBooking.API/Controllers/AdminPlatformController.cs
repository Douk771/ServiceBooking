using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): the platform half of the former <c>AdminController</c>
/// (scheduled tasks, platform settings, retention policy) — same <c>api/admin</c> prefix, same SuperAdmin
/// gate, same per-action routes. The logger keeps the <c>AdminController</c> category the platform-settings
/// log line was always written under (a mechanical move must not rename a log source).
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminPlatformController(
    AppDbContext db, PricingCatalogCache pricingCatalogCache, ILogger<AdminController> logger) : ControllerBase
{
    // ── Scheduled tasks ────────────────────────────────────────────────────────

    // US-21 p.6: the only visibility this cycle gives the background task component — no admin screen,
    // just an endpoint a human (or a monitor) can curl. SuperAdmin-only like the rest of this controller.
    //
    // scheduledTasks/config are [FromServices] action parameters, not primary-constructor fields (code
    // review finding): a primary-constructor dependency is resolved for EVERY action on this controller,
    // even ones that never touch it — GetStats, GetUsers, etc. would all pay for constructing
    // IEnumerable<IScheduledTask> (which resolves PhotoRetentionCleanupTask and everything IT depends on
    // — AppDbContext, SubscriptionResolver, FileStorage) on every admin request. Scoping it to just this
    // action means that cost is only ever paid here.
    [HttpGet("scheduled-tasks")]
    public async Task<ActionResult<List<ScheduledTaskStatusDto>>> GetScheduledTasks(
        [FromServices] IEnumerable<IScheduledTask> scheduledTasks, [FromServices] IConfiguration config,
        CancellationToken ct)
    {
        var states = await db.ScheduledTaskStates.ToDictionaryAsync(s => s.Name, ct);
        var nowUtc = DateTime.UtcNow;

        var result = scheduledTasks.Select(task =>
        {
            var options = ScheduledTaskOptions.For(config, task);
            states.TryGetValue(task.Name, out var state);

            var isOverdue = ScheduledTaskSchedule.IsOverdue(state?.LastFinishedAtUtc, options.Period, nowUtc);

            return new ScheduledTaskStatusDto(
                task.Name, options.Enabled, (int)options.Period.TotalMinutes,
                state?.LastStartedAtUtc, state?.LastFinishedAtUtc, state?.LastDurationMs ?? 0,
                state?.LastSucceeded ?? false, state?.LastSummary, state?.LastError, isOverdue);
        }).ToList();

        return Ok(result);
    }

    [HttpGet("platform-settings")]
    public async Task<ActionResult<AdminPlatformSettingsDto>> GetPlatformSettings(
        [FromServices] Services.Notifications.PlatformSettings platformSettings,
        [FromServices] Services.Legal.LegalDocumentProvider legalDocuments,
        CancellationToken ct)
    {
        var price = await platformSettings.GetChannelPricePerMonthAsync(ct);
        var idleDays = await platformSettings.GetChannelIdleDaysAsync(ct);
        var pricingPublicEnabled = await pricingCatalogCache.IsPublicEnabledAsync(ct);
        var blockedReason = PricingCatalogCache.GetPublicationBlockReason(legalDocuments.Current);
        var trialDurationDays = await platformSettings.GetTrialDurationDaysAsync(ct);
        var trialMailingWindowDays = await platformSettings.GetTrialMailingWindowDaysAsync(ct);
        var trialWarningThresholdsDays = (await platformSettings.GetTrialWarningThresholdsDaysAsync(ct)).ToList();
        return Ok(new AdminPlatformSettingsDto(
            price, idleDays, pricingPublicEnabled, blockedReason,
            trialDurationDays, trialMailingWindowDays, trialWarningThresholdsDays));
    }

    [HttpPut("platform-settings")]
    public async Task<ActionResult<AdminPlatformSettingsDto>> UpdatePlatformSettings(
        [FromBody] AdminPlatformSettingsDto dto, [FromServices] Services.Notifications.PlatformSettings platformSettings,
        [FromServices] Services.Legal.LegalDocumentProvider legalDocuments)
    {
        if (dto.ChannelIdleDays is < 0 or > 60) return BadRequest("channelIdleDays must be between 0 and 60");
        if (dto.ChannelPricePerMonth is < 0) return BadRequest("channelPricePerMonth must not be negative");

        // Cycle 18 (§367): trialDurationDays/trialMailingWindowDays out of 1..365, or the window bigger
        // than the duration, or the thresholds not 1..5 distinct positive values not exceeding the
        // duration, or the thresholds disagreeing with what the CURRENT activation-terms edition
        // literally promises (§367.1 — "текст называет числа буквально") are all 400, not silently
        // clamped or ignored.
        if (dto.TrialDurationDays is { } trialDurationDays && trialDurationDays is < 1 or > 365)
            return BadRequest("trialDurationDays должен быть от 1 до 365.");
        if (dto.TrialMailingWindowDays is { } trialMailingWindowDaysInput)
        {
            if (trialMailingWindowDaysInput is < 1 or > 365)
                return BadRequest("trialMailingWindowDays должен быть от 1 до 365.");
            var effectiveDuration = dto.TrialDurationDays ?? await platformSettings.GetTrialDurationDaysAsync();
            if (effectiveDuration is { } d && trialMailingWindowDaysInput > d)
                return BadRequest("trialMailingWindowDays не может быть больше trialDurationDays.");
        }
        if (dto.TrialWarningThresholdsDays is { } thresholdsInput)
        {
            if (thresholdsInput.Count is 0 or > 5 || thresholdsInput.Any(t => t <= 0) || thresholdsInput.Distinct().Count() != thresholdsInput.Count)
                return BadRequest("trialWarningThresholdsDays должен содержать от 1 до 5 различных положительных значений.");
            var effectiveDuration = dto.TrialDurationDays ?? await platformSettings.GetTrialDurationDaysAsync();
            if (effectiveDuration is { } d && thresholdsInput.Any(t => t > d))
                return BadRequest("trialWarningThresholdsDays не может превышать trialDurationDays.");
            var promised = Services.Billing.TrialTermsRegistry.CurrentPromisedThresholds;
            if (!thresholdsInput.OrderByDescending(t => t).SequenceEqual(promised.OrderByDescending(t => t)))
                return BadRequest(
                    $"Текущая редакция текста активации обещает пороги {string.Join(", ", promised)}; " +
                    "другие значения возможны только с новой редакцией текста от legal-counsel.");
        }

        var oldPrice = await platformSettings.GetChannelPricePerMonthAsync();
        var oldIdleDays = await platformSettings.GetChannelIdleDaysAsync();
        var oldPricingPublicEnabled = await pricingCatalogCache.IsPublicEnabledAsync();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // ARCHITECTURE_CYCLE11.md §102.7/§114.2: turning the switch ON while the channel offer is a
        // draft (or unreadable) is rejected wholesale — the switch doesn't move, nothing else in this
        // request is applied either, and no change-log row is written. Turning it OFF is always allowed.
        if (dto.PricingPublicEnabled && !oldPricingPublicEnabled)
        {
            var blockReason = PricingCatalogCache.GetPublicationBlockReason(legalDocuments.Current);
            if (blockReason is not null)
            {
                var offer = legalDocuments.Current?.Get(Core.Enums.LegalDocumentType.TermsOwner);
                return Conflict(new PricingPublicationBlockedDto(
                    blockReason,
                    blockReason == "OfferIsDraft"
                        ? "Публичные цены нельзя включить: оферта на подключение канала (Приложение № 1 к Соглашению с компанией) — черновая редакция."
                        : "Публичные цены нельзя включить: снимок правовых документов недоступен.",
                    "TermsOwner",
                    offer?.Version));
            }
        }

        // Reviewer note: previously wrote (and journaled) both keys unconditionally, even when the
        // request left one of them unchanged — a no-op "save" produced a change-log row that recorded no
        // actual change, and every PUT (again, even a no-op one) went stale-for-60s on the read side.
        // Guarded per key now; cache invalidated only for the key(s) that actually moved. Invalidation
        // itself is deferred until after SaveChangesAsync below — invalidating first opens a window where
        // a concurrent reader repopulates the cache from the not-yet-committed old row and pins the stale
        // value for the cache's full TTL even though the write already succeeded.
        var priceChanged = oldPrice != dto.ChannelPricePerMonth;
        if (priceChanged)
        {
            await PlatformSettingsWriter.WriteAsync(
                db, PlatformSettingsWriter.PriceKey, oldPrice?.ToString(CultureInfo.InvariantCulture),
                dto.ChannelPricePerMonth?.ToString(CultureInfo.InvariantCulture) ?? "", userId);
        }

        var idleDaysChanged = oldIdleDays != dto.ChannelIdleDays;
        if (idleDaysChanged)
        {
            await PlatformSettingsWriter.WriteAsync(
                db, PlatformSettingsWriter.IdleDaysKey, oldIdleDays.ToString(CultureInfo.InvariantCulture),
                dto.ChannelIdleDays.ToString(CultureInfo.InvariantCulture), userId);
        }

        // ARCHITECTURE_CYCLE7.md §48: the "рубильник" for the public price list (GET /api/pricing). Was
        // previously settable only by hand-editing the PlatformSettings row directly in the database —
        // see the standing comment on PricingCatalogCache.Invalidate — this is the admin lever for it.
        var pricingPublicEnabledChanged = oldPricingPublicEnabled != dto.PricingPublicEnabled;
        if (pricingPublicEnabledChanged)
        {
            await PlatformSettingsWriter.WriteAsync(
                db, PricingCatalogCache.PublicEnabledSettingKey,
                oldPricingPublicEnabled ? "true" : "false", dto.PricingPublicEnabled ? "true" : "false", userId);

            // §59: Information-level log for the price-list publication toggle, by whom.
            logger.LogInformation(
                "Public pricing catalog {State} by {UserId}",
                dto.PricingPublicEnabled ? "enabled" : "disabled", userId);
        }

        // Cycle 18 (§367): null/absent means "не менять" — only a present value ever gets written.
        bool trialDurationChanged = false, trialWindowChanged = false, trialThresholdsChanged = false;
        if (dto.TrialDurationDays.HasValue)
        {
            var oldTrialDuration = await platformSettings.GetTrialDurationDaysAsync();
            trialDurationChanged = oldTrialDuration != dto.TrialDurationDays;
            if (trialDurationChanged)
                await PlatformSettingsWriter.WriteAsync(
                    db, Services.Notifications.PlatformSettings.TrialDurationDaysKey,
                    oldTrialDuration?.ToString(CultureInfo.InvariantCulture),
                    dto.TrialDurationDays.Value.ToString(CultureInfo.InvariantCulture), userId);
        }
        if (dto.TrialMailingWindowDays.HasValue)
        {
            var oldTrialWindow = await platformSettings.GetTrialMailingWindowDaysAsync();
            trialWindowChanged = oldTrialWindow != dto.TrialMailingWindowDays;
            if (trialWindowChanged)
                await PlatformSettingsWriter.WriteAsync(
                    db, Services.Notifications.PlatformSettings.TrialMailingWindowDaysKey,
                    oldTrialWindow?.ToString(CultureInfo.InvariantCulture),
                    dto.TrialMailingWindowDays.Value.ToString(CultureInfo.InvariantCulture), userId);
        }
        if (dto.TrialWarningThresholdsDays is { } newThresholds)
        {
            var oldThresholds = await platformSettings.GetTrialWarningThresholdsDaysAsync();
            var oldThresholdsRaw = string.Join(",", oldThresholds);
            var newThresholdsRaw = string.Join(",", newThresholds);
            trialThresholdsChanged = oldThresholdsRaw != newThresholdsRaw;
            if (trialThresholdsChanged)
                await PlatformSettingsWriter.WriteAsync(
                    db, Services.Notifications.PlatformSettings.TrialWarningThresholdsDaysKey,
                    oldThresholdsRaw, newThresholdsRaw, userId);
        }

        await db.SaveChangesAsync();

        if (priceChanged) platformSettings.InvalidateCache(PlatformSettingsWriter.PriceKey);
        if (idleDaysChanged) platformSettings.InvalidateCache(PlatformSettingsWriter.IdleDaysKey);
        if (pricingPublicEnabledChanged) pricingCatalogCache.Invalidate();
        if (trialDurationChanged) platformSettings.InvalidateCache(Services.Notifications.PlatformSettings.TrialDurationDaysKey);
        if (trialWindowChanged) platformSettings.InvalidateCache(Services.Notifications.PlatformSettings.TrialMailingWindowDaysKey);
        if (trialThresholdsChanged) platformSettings.InvalidateCache(Services.Notifications.PlatformSettings.TrialWarningThresholdsDaysKey);

        // Reviewer note: echoing `dto` back here would leak client-supplied fields the server never
        // validated or stored as-is (e.g. `pricingPublicBlockedReason`, which GET always recomputes from
        // the live legal snapshot). Recompute and return the same shape GET produces: re-read price/idle
        // days back from the writer (post-invalidation, so this reflects exactly what was persisted rather
        // than trusting the client-supplied `dto` values verbatim) and recompute the blocked reason.
        var freshPrice = await platformSettings.GetChannelPricePerMonthAsync();
        var freshIdleDays = await platformSettings.GetChannelIdleDaysAsync();
        var freshBlockedReason = PricingCatalogCache.GetPublicationBlockReason(legalDocuments.Current);
        var freshTrialDuration = await platformSettings.GetTrialDurationDaysAsync();
        var freshTrialWindow = await platformSettings.GetTrialMailingWindowDaysAsync();
        var freshTrialThresholds = (await platformSettings.GetTrialWarningThresholdsDaysAsync()).ToList();
        return Ok(new AdminPlatformSettingsDto(
            freshPrice, freshIdleDays, dto.PricingPublicEnabled, freshBlockedReason,
            freshTrialDuration, freshTrialWindow, freshTrialThresholds));
    }

    // ── Retention policy (T5-B8/B9, ARCHITECTURE_CYCLE5.md §49.5) ────────────────

    // §49.5: "сроки не переписываются руками в документ, а выгружаются из работающей конфигурации" —
    // this endpoint reads the SAME IOptions<RetentionPeriods> every rule reads, so a declared-vs-actual
    // mismatch is structurally impossible. [FromServices], same reasoning as GetScheduledTasks above:
    // only this one action pays for resolving it.
    [HttpGet("retention/policy")]
    public ActionResult<RetentionPolicyDto> GetRetentionPolicy(
        [FromServices] Microsoft.Extensions.Options.IOptions<Services.Retention.RetentionPeriods> periods,
        [FromServices] IConfiguration config)
    {
        var p = periods.Value;
        var dryRun = config.GetSection("ScheduledTasks:data-retention").GetValue("DryRun", true);

        return Ok(new RetentionPolicyDto(
            p.NotificationBodyDays, p.NotificationMetadataDays, p.TemplateHistoryDays,
            p.InactiveAccountDays, p.BookingPersonalizationDays, p.ClientNoteDays, p.ClientNotePhotoDays,
            p.ClientHealthNoteDays, p.ConsentRecordDays, p.ChannelStateEventDays, p.PaymentLogDays,
            p.MailLogDays, p.AppLogDays, dryRun,
            p.PhoneVerificationSessionDays, p.VerifiedPhoneOrphanDays, p.TrialPhoneRegistrationDays,
            p.BookingEventDays, p.GuestDataGateEventDays, p.PlatformNoticeDays));
    }

    // ARCHITECTURE_CYCLE20.md §406.2, API_CONTRACT_CYCLE20.md §436.2 (US-20-05, Т20-06) — the journal's
    // only reader. No screen reads this in cycle 20 (out of scope); SuperAdmin-only, for incident review.
    // Written in cycle 20 inside AdminController; placed here on the merge with cycle 22 (§378), next to
    // the retention policy it belongs to — same api/admin prefix, same SuperAdmin gate, same route.
    [HttpGet("guest-data-gate-events")]
    public async Task<ActionResult<PagedResult<GuestDataGateEventDto>>> GetGuestDataGateEvents(
        [FromQuery] string? userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct)
    {
        if (from is not null && to is not null && from > to)
            return BadRequest("Дата начала не может быть позже даты окончания.");

        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.GuestDataGateEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(e => e.UserId == userId);
        if (from is not null) query = query.Where(e => e.OccurredAtUtc >= from);
        if (to is not null) query = query.Where(e => e.OccurredAtUtc <= to);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.OccurredAtUtc)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToListAsync(ct);

        var items = rows.Select(e => new GuestDataGateEventDto(
            e.Id, e.OccurredAtUtc, "guest-data-gate.applied", e.UserId,
            e.Operation.ToString(), e.Outcome.ToString(), e.TraceId)).ToList();

        return Ok(Pagination.Create(items, currentPage, currentPageSize, total));
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

public record ScheduledTaskStatusDto(
    string Name, bool Enabled, int PeriodMinutes,
    DateTime? LastStartedAt, DateTime? LastFinishedAt, int LastDurationMs,
    bool LastSucceeded, string? LastSummary, string? LastError, bool IsOverdue);

// T5-B8/B9 (ARCHITECTURE_CYCLE5.md §49.5) — the actual configured retention values, for publication in
// the platform's privacy policy and for the lawyer's own periodic check (US-73 п. 7, US-80 п. 5).
// PhoneVerificationSessionDays/VerifiedPhoneOrphanDays (cycle 14) and TrialPhoneRegistrationDays
// (cycle 18, §343.2) appended additively — §49.5's own rule ("сроки не переписываются руками")
// applied retroactively to the two cycle-14 periods that were never wired into this DTO (code review,
// cycle 18 late delta): closing that gap here, since this endpoint is the one place it's checked.
// ARCHITECTURE_CYCLE20.md §406.1 (US-20-05, П-5/П-10 §413) — three more fields, appended at the end.
// BookingEventDays wasn't in this DTO at all before this cycle, despite SPEC already requiring it be
// shown (a pre-existing gap this cycle closes together with giving the period an actual legal number).
public record RetentionPolicyDto(
    int NotificationBodyDays, int NotificationMetadataDays, int TemplateHistoryDays,
    int InactiveAccountDays, int BookingPersonalizationDays, int ClientNoteDays, int ClientNotePhotoDays,
    int ClientHealthNoteDays, int ConsentRecordDays, int ChannelStateEventDays, int PaymentLogDays,
    int MailLogDays, int AppLogDays, bool DryRun,
    int PhoneVerificationSessionDays = 0, int VerifiedPhoneOrphanDays = 0, int TrialPhoneRegistrationDays = 0,
    int BookingEventDays = 0, int GuestDataGateEventDays = 0, int PlatformNoticeDays = 0);

// ARCHITECTURE_CYCLE20.md §406.2, API_CONTRACT_CYCLE20.md §436.2 (US-20-05, Т20-06). 🔴 No IP, no
// User-Agent, no phone in any form, no counts — the schema behind this DTO has no such columns
// (LEGAL_REVIEW_CYCLE16.md §6.4), so there is nothing here for a future field to accidentally leak.
public record GuestDataGateEventDto(
    long Id, DateTime OccurredAt, string EventCode, string UserId, string Operation, string Outcome, string? TraceId);

// pricingPublicBlockedReason: ARCHITECTURE_CYCLE11.md §114.1 — nullable, added by cycle 11. Absent/null
// means no obstacle to turning the switch on; a non-null value is one of "OfferIsDraft"/"LegalUnavailable"
// and the request body never needs to set it (round-tripped by GetPlatformSettings/UpdatePlatformSettings
// sharing this one DTO, its value on write is ignored).
// Cycle 18 (API_CONTRACT_CYCLE18.md §367) — three trailing trial fields, all nullable on input (PUT):
// null/absent = "не менять" (§367), same convention as AdminPlanInput.IsPublic/SortOrder above.
public record AdminPlatformSettingsDto(
    decimal? ChannelPricePerMonth, int ChannelIdleDays, bool PricingPublicEnabled, string? PricingPublicBlockedReason = null,
    int? TrialDurationDays = null, int? TrialMailingWindowDays = null, List<int>? TrialWarningThresholdsDays = null);

// ARCHITECTURE_CYCLE11.md §114.2 — 409 body for PUT /api/admin/platform-settings when
// pricingPublicEnabled: true is rejected because the channel offer isn't published.
public record PricingPublicationBlockedDto(string Reason, string Message, string DocumentType, string? Version);
