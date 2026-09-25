using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// A company's notification settings, templates, and delivery log (API_CONTRACT_CYCLE4.md §28–§30,
/// T4-B7/T4-B10). Settings and templates are owner-only; the log and its summary are staff-visible
/// (US-32 p.5).
/// </summary>
[ApiController]
[Route("api/companies/{companyId:guid}")]
[Authorize]
public class CompanyNotificationsController(
    AppDbContext db,
    SubscriptionResolver subscriptionResolver,
    PlatformSettings platformSettings,
    LegalDocumentProvider legalProvider) : ControllerBase
{
    // ── Settings ─────────────────────────────────────────────────────────────────────────────────

    [HttpGet("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> GetSettings(Guid companyId)
    {
        if (!await CanManageCompanyAsync(companyId)) return Forbid();

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null) return NotFound();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        var settings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId);
        // ARCHITECTURE_CYCLE9.md §104.3/§104.5: a company may now hold one assignment PER TRANSPORT, not
        // one ever — every live assignment is loaded so connectedTransports/priorityChannelHealthy can be
        // computed across all of them, not just an arbitrary FirstOrDefault.
        var assignments = await db.ChannelCompanyAssignments.AsNoTracking()
            .Include(a => a.Channel).Where(a => a.CompanyId == companyId).ToListAsync();

        return Ok(await BuildSettingsDtoAsync(plan, settings, assignments));
    }

    [HttpPut("notification-settings")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<NotificationSettingsDto>> UpdateSettings(Guid companyId, [FromBody] UpdateNotificationSettingsDto dto)
    {
        if (!await CanManageCompanyAsync(companyId)) return Forbid();

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null) return NotFound();

        if (dto.ReminderLeadMinutes is < 60 or > 4320)
            return BadRequest("Напоминание можно отправлять за 1–72 часа до визита");
        if (dto.MinLeadMinutes is < 0 or > 720)
            return BadRequest("Порог может быть от 0 до 12 часов");
        if (dto.MinLeadMinutes >= dto.ReminderLeadMinutes)
            return BadRequest("Напоминание за 1 час при пороге 2 часа не уйдёт никогда");

        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        var assignments = await db.ChannelCompanyAssignments
            .Include(a => a.Channel).Where(a => a.CompanyId == companyId).ToListAsync();

        if (!plan.AllowNotificationChannel)
            return StatusCode(402, "Недоступно на вашем тарифе");
        // ARCHITECTURE_CYCLE9.md §104.3: "канал не оплачен" now asks "does this company have AT LEAST
        // ONE funded channel", not "is THE (arbitrary) assignment funded" — a company can have a funded
        // MAX channel and an unfunded WhatsApp one (or vice versa); settings must stay saveable through
        // whichever channel is actually usable, not gated on which one FirstOrDefault happened to pick.
        var hasFundedAssignment = false;
        foreach (var a in assignments)
        {
            if (await IsChannelFundedAsync(a.Channel, plan)) { hasFundedAssignment = true; break; }
        }
        if (!hasFundedAssignment)
            return StatusCode(402, "Канал не оплачен");

        var settings = await db.CompanyNotificationSettings.FirstOrDefaultAsync(s => s.CompanyId == companyId);
        if (settings is null)
        {
            settings = new CompanyNotificationSettings { CompanyId = companyId };
            db.CompanyNotificationSettings.Add(settings);
        }

        settings.EnabledTypeMask = BuildMask(dto.EnabledTypes);
        settings.ReminderLeadMinutes = dto.ReminderLeadMinutes;
        settings.MinLeadMinutes = dto.MinLeadMinutes;

        // ARCHITECTURE_CYCLE9.md §104.5/§114.4 (US-125): both optional — "не прислали — не меняем".
        if (dto.DeliveryMode.HasValue) settings.DeliveryMode = dto.DeliveryMode.Value;
        if (dto.PriorityTransport.HasValue)
        {
            // §114.4/B4: priorityTransport must be ASSIGNED AND FUNDED to be accepted — deliberately the
            // SAME "funded, ChannelState not checked" definition NotificationScheduler.SelectTargets uses
            // at queue time (§104.5's own explicit rejection of gating on Connected), NOT the stricter
            // "funded AND Connected" UsableTransportsAsync computes for the read-only screen fields below.
            // Gating this WRITE on Connected would regress a paid-but-momentarily-disconnected/reconnecting
            // company's ability to save ANY setting on this endpoint (e.g. just reminderLeadMinutes) back
            // to a 400 — the same class of over-eager coupling §104.5 already rejected once (см. Н3).
            var fundedTransports = await FundedTransportsAsync(assignments, plan);
            if (!fundedTransports.Contains(dto.PriorityTransport.Value))
                return BadRequest("Приоритетный канал должен быть среди оплаченных транспортов компании");
            settings.PriorityTransport = dto.PriorityTransport.Value;
        }

        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // API_CONTRACT_CYCLE4.md §28.2: a lowered threshold takes effect immediately on already-queued
        // rows — the next dispatcher pass re-evaluates them against the new MinLeadMinutes anyway
        // (NotificationGate.Evaluate is re-run per pass, not cached on the row), so nothing further is
        // needed here beyond persisting the new threshold; no queued row is touched by this request.
        // ARCHITECTURE_CYCLE9.md §104.5/§114.4: same rule for deliveryMode/priorityTransport — they apply
        // to events queued AFTER this save, never retroactively to rows already in the queue.
        await db.SaveChangesAsync();

        return Ok(await BuildSettingsDtoAsync(plan, settings, assignments));
    }

    // ── Templates ────────────────────────────────────────────────────────────────────────────────

    [HttpGet("notification-templates")]
    public async Task<ActionResult<TemplatesResponseDto>> GetTemplates(Guid companyId)
    {
        if (!await CanManageCompanyAsync(companyId)) return Forbid();

        var rows = await db.NotificationTemplates.AsNoTracking().Where(t => t.CompanyId == companyId).ToListAsync();
        var placeholders = TemplatePlaceholders.All
            .Select(p => new TemplatePlaceholderDto(p.Token, p.Description, p.Types)).ToList();

        var templates = DefaultTemplates.CustomizableTypes.Select(type =>
        {
            var row = rows.FirstOrDefault(r => r.Type == type);
            var isDefault = string.IsNullOrEmpty(row?.Body);
            return new TemplateItemDto(
                type.ToString(), isDefault ? "" : row!.Body, isDefault, DefaultTemplates.For(type), row?.UpdatedAt);
        }).ToList();

        // T5-B12 (ARCHITECTURE_CYCLE5.md §51.2): the word list travels with the response, not baked into
        // the frontend — see TemplatesResponseDto's own doc comment. warningVersion is 0-length when the
        // manifest isn't loaded (only possible outside Production); the frontend simply can't submit an
        // acknowledgement that matches an empty string, so PUT falls through to its own 503 there.
        var adMarkers = await platformSettings.GetAdMarkersAsync();
        var warningVersion = legalProvider.Current?.GetText(LegalTextKey.TemplateAdWarning)?.Version ?? "";

        return Ok(new TemplatesResponseDto(
            placeholders, "Отказаться от уведомлений: https://ezbook.ru/u/…", templates,
            adMarkers, LegalTextKey.TemplateAdWarning, warningVersion));
    }

    [HttpPut("notification-templates/{type}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<TemplateItemDto>> UpdateTemplate(Guid companyId, string type, [FromBody] TemplateBodyDto dto)
    {
        if (!await CanManageCompanyAsync(companyId)) return Forbid();
        if (!TryParseCustomizableType(type, out var parsedType)) return NotFound();

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null) return NotFound();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        // ARCHITECTURE_CYCLE9.md §104.3: same "at least one funded assignment" generalization as
        // UpdateSettings above — a company's templates stay editable through whichever channel is
        // actually funded, not gated on an arbitrary single assignment.
        var templateAssignments = await db.ChannelCompanyAssignments
            .Include(a => a.Channel).Where(a => a.CompanyId == companyId).ToListAsync();
        var hasFundedAssignmentForTemplate = false;
        foreach (var a in templateAssignments)
        {
            if (await IsChannelFundedAsync(a.Channel, plan)) { hasFundedAssignmentForTemplate = true; break; }
        }
        if (!plan.AllowNotificationChannel || !hasFundedAssignmentForTemplate)
            return StatusCode(402, "Канал не оплачен");

        // Empty body ("вернуть текст платформы", API_CONTRACT_CYCLE4.md §29.1) bypasses length/placeholder
        // validation entirely — it's a request to delete the override, not a 1000-character message.
        var resetToDefault = dto.Body.Length == 0;
        IReadOnlyList<string> markersHit = [];
        if (!resetToDefault)
        {
            var validation = NotificationTemplateValidator.Validate(dto.Body, parsedType);
            if (!validation.IsValid) return BadRequest(validation.Error);

            // T5-B12 (ARCHITECTURE_CYCLE7.md §51.1, US-69 п. 1/4): required on every save that keeps
            // custom text, never cached/inherited from a previous save of the SAME text. Checked before
            // the marker scan — an owner who never acknowledged at all gets that specific message, not a
            // marker warning that implies they just need to tick a different box.
            var warningText = legalProvider.Current?.GetText(LegalTextKey.TemplateAdWarning);
            if (warningText is null)
                return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");
            if (dto.Acknowledgement is not { Accepted: true })
                return BadRequest("Подтвердите, что текст сервисный и вы принимаете ответственность за его содержание.");
            if (dto.Acknowledgement.WarningVersion != warningText.Version)
                return Conflict("Текст предупреждения был обновлён — перечитайте и подтвердите заново.");

            var adMarkers = await platformSettings.GetAdMarkersAsync();
            markersHit = TemplateAdHeuristics.Scan(dto.Body, adMarkers);
            if (markersHit.Count > 0 && !dto.Acknowledgement.ConfirmedDespiteMarkers)
                return BadRequest(new TemplateMarkersHitDto(markersHit,
                    "Такой текст с высокой вероятностью является рекламой. Подтвердите, что это сервисное уведомление."));
        }

        var existing = await db.NotificationTemplates.FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Type == parsedType);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var nowUtc = DateTime.UtcNow;
        var newBody = resetToDefault ? "" : dto.Body;

        // A history row is written on every ACTUAL save (existing row updated, or a fresh row created) —
        // ARCHITECTURE_CYCLE5.md §44.6: the newest revision needs a row too, or there is nothing to point
        // the acknowledgement evidence at for the very first save. Reset-to-default writes a history row
        // with no acknowledgement fields (nothing was confirmed — there is no custom text to be
        // responsible for), matching every other "no custom text" branch in this method.
        if (existing is not null || !resetToDefault)
        {
            db.NotificationTemplateHistories.Add(new NotificationTemplateHistory
            {
                Id = Guid.NewGuid(), CompanyId = companyId, Type = parsedType,
                PreviousBody = existing?.Body ?? "", ChangedByUserId = userId, ChangedAtUtc = nowUtc,
                NewBody = newBody,
                AcknowledgedByUserId = resetToDefault ? null : userId,
                AcknowledgedAtUtc = resetToDefault ? null : nowUtc,
                WarningVersion = resetToDefault ? null : dto.Acknowledgement!.WarningVersion,
                AdMarkersHit = markersHit.Count > 0 ? string.Join(",", markersHit) : null,
            });
        }

        if (existing is not null)
        {
            existing.Body = resetToDefault ? "" : dto.Body;
            existing.UpdatedAt = nowUtc;
            existing.UpdatedByUserId = userId;
        }
        else if (!resetToDefault)
        {
            existing = new NotificationTemplate
            {
                Id = Guid.NewGuid(), CompanyId = companyId, Type = parsedType,
                Body = dto.Body, UpdatedAt = nowUtc, UpdatedByUserId = userId,
            };
            db.NotificationTemplates.Add(existing);
        }
        // resetToDefault with no existing row: nothing to do, it's already the platform default.

        await db.SaveChangesAsync();

        var isDefault = existing is null || string.IsNullOrEmpty(existing.Body);
        return Ok(new TemplateItemDto(
            parsedType.ToString(), isDefault ? "" : existing!.Body, isDefault, DefaultTemplates.For(parsedType), existing?.UpdatedAt));
    }

    [HttpPost("notification-templates/{type}/preview")]
    public async Task<ActionResult<TemplatePreviewDto>> PreviewTemplate(Guid companyId, string type, [FromBody] TemplateBodyDto dto)
    {
        if (!await CanManageCompanyAsync(companyId)) return Forbid();
        if (!TryParseCustomizableType(type, out var parsedType)) return NotFound();

        var validation = NotificationTemplateValidator.Validate(dto.Body, parsedType);
        if (!validation.IsValid) return BadRequest(validation.Error);

        var sampleContext = new TemplateContext(
            ClientName: "Анна", ServiceName: "Стрижка", MasterName: "Мария", Date: "19.09.2026", Time: "10:00",
            CompanyName: "Ваш салон", Address: "ул. Примерная, 1", CompanyPhone: "+7 900 000-00-00",
            CancellationReason: "по просьбе клиента", NewDate: "20.09.2026", NewTime: "12:00");

        var rendered = NotificationTemplateRenderer.Render(dto.Body, sampleContext);
        var withUnsubscribe = NotificationTemplateRenderer.AppendUnsubscribeLine(
            rendered, "Отказаться от уведомлений: https://ezbook.ru/u/…");

        return Ok(new TemplatePreviewDto(withUnsubscribe));
    }

    // ── Delivery log ─────────────────────────────────────────────────────────────────────────────

    [HttpGet("notifications")]
    public async Task<ActionResult<PagedResult<NotificationLogItemDto>>> GetLog(
        Guid companyId, [FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] NotificationStatus? status, [FromQuery] NotificationType? type,
        [FromQuery] NotificationTransport? transport,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!await IsStaffAsync(companyId)) return Forbid();

        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.OutboundNotifications.AsNoTracking().Where(n => n.CompanyId == companyId);
        if (status.HasValue) query = query.Where(n => n.Status == status);
        if (type.HasValue) query = query.Where(n => n.Type == type);
        // ARCHITECTURE_CYCLE9.md §114.3 (US-120) — additive ?transport= filter.
        if (transport.HasValue) query = query.Where(n => n.Transport == transport);
        if (from.HasValue) query = query.Where(n => n.CreatedAt >= from);
        if (to.HasValue) query = query.Where(n => n.CreatedAt <= to);

        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToListAsync();

        // N8/N9: ProfileController.DeleteAccount scrubs a Cancelled row's RecipientPhone to an empty
        // string (never a fake sentinel like "deleted", which PhoneDisplayMask.Mask would garble into
        // something that LOOKS like a real masked phone, e.g. "+de***ed") — an empty RecipientPhone
        // never happens on an ordinary row (every row is queued knowing who to send to), so it uniquely
        // identifies a scrubbed one and gets its own, honest label instead of running through the masker.
        var items = rows.Select(n => new NotificationLogItemDto(
            n.Id, n.CreatedAt, n.Type, NotificationTexts.TypeText(n.Type),
            n.RecipientName, string.IsNullOrEmpty(n.RecipientPhone) ? "получатель удалён" : PhoneDisplayMask.Mask(n.RecipientPhone),
            n.Status, NotificationTexts.StatusText(n.Status, n.Reason, n.ChannelId, n.ReadAtUtc, n.AttemptCount),
            n.BookingId, n.VisitStartUtc, n.SentAtUtc, n.ChannelId, n.Transport, n.ContentRedactedAtUtc != null)).ToList();

        return Ok(Pagination.Create(items, currentPage, currentPageSize, total));
    }

    [HttpGet("notifications/summary")]
    public async Task<ActionResult<NotificationSummaryDto>> GetSummary(Guid companyId, [FromQuery] int? days)
    {
        if (!await IsStaffAsync(companyId)) return Forbid();

        var window = days is > 0 ? days.Value : 30;
        var sinceUtc = DateTime.UtcNow.AddDays(-window);

        var rows = await db.OutboundNotifications.AsNoTracking()
            .Where(n => n.CompanyId == companyId && n.CreatedAt >= sinceUtc)
            .Select(n => new NotificationCounterRow(n.CompanyId, n.Status, n.ReadAtUtc))
            .ToListAsync();

        var assignedChannelIds = await db.ChannelCompanyAssignments.AsNoTracking()
            .Where(a => a.CompanyId == companyId).Select(a => a.ChannelId).ToListAsync();
        var channelPaidUntil = assignedChannelIds.Count > 0
            ? await db.NotificationChannels.AsNoTracking()
                .Where(c => assignedChannelIds.Contains(c.Id)).MaxAsync(c => (DateTime?)c.PaidUntilUtc)
            : null;

        var companyAssignments = await db.ChannelCompanyAssignments.AsNoTracking()
            .CountAsync(a => assignedChannelIds.Contains(a.ChannelId));
        var multiCompanyChannel = companyAssignments > 1;

        IReadOnlyList<NotificationSummaryByCompanyDto>? byCompany = null;
        if (multiCompanyChannel)
        {
            var siblingCompanyIds = await db.ChannelCompanyAssignments.AsNoTracking()
                .Where(a => assignedChannelIds.Contains(a.ChannelId)).Select(a => a.CompanyId).ToListAsync();

            var siblingRows = await db.OutboundNotifications.AsNoTracking()
                .Where(n => siblingCompanyIds.Contains(n.CompanyId) && n.CreatedAt >= sinceUtc)
                .Select(n => new NotificationCounterRow(n.CompanyId, n.Status, n.ReadAtUtc))
                .ToListAsync();
            var companyNames = await db.Companies.AsNoTracking()
                .Where(c => siblingCompanyIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);

            byCompany = siblingRows.GroupBy(r => r.CompanyId)
                .Select(g => Summarize(g.Key, companyNames.GetValueOrDefault(g.Key, ""), g)).ToList();
        }

        var summary = Summarize(null, "", rows);
        return Ok(new NotificationSummaryDto(
            window, summary.Sent, summary.Delivered, summary.Read, summary.Failed, summary.Skipped, summary.Expired,
            channelPaidUntil, byCompany));
    }

    private readonly record struct NotificationCounterRow(Guid CompanyId, NotificationStatus Status, DateTime? ReadAtUtc);

    private static NotificationSummaryByCompanyDto Summarize(Guid? companyId, string companyName, IEnumerable<NotificationCounterRow> rows)
    {
        int sent = 0, delivered = 0, read = 0, failed = 0, skipped = 0, expired = 0;
        foreach (var r in rows)
        {
            switch (r.Status)
            {
                case NotificationStatus.Sent: sent++; break;
                case NotificationStatus.Delivered:
                    delivered++;
                    if (r.ReadAtUtc is not null) read++;
                    break;
                case NotificationStatus.Failed: failed++; break;
                case NotificationStatus.Skipped: skipped++; break;
                case NotificationStatus.Expired: expired++; break;
            }
        }
        return new NotificationSummaryByCompanyDto(companyId ?? Guid.Empty, companyName, sent, delivered, read, failed, skipped, expired);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task<NotificationSettingsDto> BuildSettingsDtoAsync(
        EffectivePlan plan, CompanyNotificationSettings? settings, IReadOnlyList<ChannelCompanyAssignment> assignments)
    {
        var enabledMask = settings?.EnabledTypeMask ?? CompanyNotificationSettings.DefaultEnabledTypeMask;
        var enabledTypes = Enum.GetValues<NotificationType>().Where(t => (enabledMask & (1 << (int)t)) != 0).ToList();

        var deliveryMode = settings?.DeliveryMode ?? new CompanyNotificationSettings().DeliveryMode;
        var priorityTransport = settings?.PriorityTransport ?? new CompanyNotificationSettings().PriorityTransport;

        // ARCHITECTURE_CYCLE9.md §104.5/§114.4: `channel` legacy field describes the PRIORITY transport's
        // own assignment specifically — with more than one transport possibly connected, that is the one
        // channel the rest of this screen's (pre-cycle-9) fields still meaningfully describe.
        var priorityAssignment = assignments.FirstOrDefault(a => a.Transport == priorityTransport);
        var channel = priorityAssignment?.Channel;

        // §47.3: ChannelDto/SettingsChannelDto's paymentState keeps its FORM but its SOURCE is now the
        // account's funding ranking, not the channel's own (historical, unread-by-business-logic)
        // PaidFromUtc/PaidUntilUtc columns.
        ChannelPaymentStatus? paymentState = channel is null
            ? null
            : channel.IsSuspendedByAdmin
                ? ChannelPaymentStatus.Suspended
                : await IsChannelFundedAsync(channel, plan) ? ChannelPaymentStatus.Paid : ChannelPaymentStatus.NotPaid;
        var idleDays = await platformSettings.GetChannelIdleDaysAsync();
        var stateText = channel is null ? null : ChannelPresentation.StateText(
            channel.State, channel.PhoneNumber is null ? null : PhoneDisplayMask.Mask(channel.PhoneNumber),
            idleDays, channel.PaidUntilUtc, channel.LastStateReason);

        var channelDto = new SettingsChannelDto(channel is not null, channel?.Id, channel?.State, stateText, paymentState, channel?.PaidUntilUtc);

        var blockedReason = ChannelPresentation.SettingsBlockedReason(
            plan.AllowNotificationChannel, channel is not null, paymentState, channel?.State);

        // ARCHITECTURE_CYCLE9.md §114.4: connectedTransports/priorityChannelHealthy read-only fields —
        // "usable" means the SAME thing NotificationRouting.SelectTargets means by it (funded AND
        // Connected), so the settings screen never shows a transport as pickable that routing would then
        // immediately treat as unavailable.
        var usableTransports = await UsableTransportsAsync(assignments, plan);
        var priorityChannelHealthy = usableTransports.Contains(priorityTransport);

        return new NotificationSettingsDto(
            enabledTypes, settings?.ReminderLeadMinutes ?? new CompanyNotificationSettings().ReminderLeadMinutes,
            settings?.MinLeadMinutes ?? new CompanyNotificationSettings().MinLeadMinutes,
            plan.AllowNotificationChannel, channelDto, blockedReason is null, blockedReason,
            deliveryMode, priorityTransport, usableTransports, priorityChannelHealthy);
    }

    /// <summary>ARCHITECTURE_CYCLE9.md §104.5/§114.4 — the transports the READ-ONLY settings screen shows
    /// as currently pickable/healthy: assigned, funded, AND <see cref="ChannelState.Connected"/>. This is
    /// deliberately STRICTER than what queueing itself requires (<see cref="FundedTransportsAsync"/>) —
    /// it exists to give the owner a live status signal ("this channel needs reconnecting"), not to gate
    /// what values the PUT endpoint accepts (см. Н3/B4: those are two different questions, and conflating
    /// them once already caused a spurious 400 on saving unrelated settings while a channel briefly
    /// disconnected).</summary>
    private async Task<IReadOnlyList<NotificationTransport>> UsableTransportsAsync(
        IReadOnlyList<ChannelCompanyAssignment> assignments, EffectivePlan plan)
    {
        var usable = new List<NotificationTransport>();
        foreach (var a in assignments)
        {
            if (a.Channel.State == ChannelState.Connected && await IsChannelFundedAsync(a.Channel, plan))
                usable.Add(a.Transport);
        }
        return usable;
    }

    /// <summary>The transports actually accepted as <c>priorityTransport</c> on the PUT endpoint (B4):
    /// assigned and funded, <see cref="ChannelState"/> deliberately NOT checked — the same "funding only"
    /// definition <c>NotificationScheduler.SelectTargets</c> uses at queue time (§104.5 explicitly rejected
    /// gating routing on Connected; see that method's own comment). Keeping this identical to routing's own
    /// definition means the PUT never rejects a value routing would itself have accepted.</summary>
    private async Task<IReadOnlyList<NotificationTransport>> FundedTransportsAsync(
        IReadOnlyList<ChannelCompanyAssignment> assignments, EffectivePlan plan)
    {
        var funded = new List<NotificationTransport>();
        foreach (var a in assignments)
        {
            if (await IsChannelFundedAsync(a.Channel, plan))
                funded.Add(a.Transport);
        }
        return funded;
    }

    // ARCHITECTURE_CYCLE7.md §47.1/§47.2: funded/unfunded, ranked across every live channel on the
    // SAME billing account as `channel` — not a channel-level payment read.
    private async Task<bool> IsChannelFundedAsync(NotificationChannel? channel, EffectivePlan plan)
    {
        if (channel?.BillingAccountId is not { } accountId) return false;
        var siblings = await db.NotificationChannels.AsNoTracking().Where(c => c.BillingAccountId == accountId).ToListAsync();
        var ranking = ChannelFunding.Rank(siblings, plan.PaidNotificationNumbers);
        return ranking.TryGetValue(channel.Id, out var state) && state == ChannelFundingState.Funded;
    }

    private static int BuildMask(IReadOnlyList<NotificationType> types)
    {
        var mask = 0;
        foreach (var type in types) mask |= 1 << (int)type;
        return mask;
    }

    private static bool TryParseCustomizableType(string raw, out NotificationType type) =>
        Enum.TryParse(raw, ignoreCase: false, out type) && DefaultTemplates.CustomizableTypes.Contains(type);

    // TD-11 (ARCHITECTURE_CYCLE16.md §254): delegates to the single shared implementation.
    // superAdminBypass: false — this controller's existing behavior is that SuperAdmin does NOT
    // automatically manage a company's notification settings; that is preserved deliberately, not an
    // oversight (naive unification here would have widened SuperAdmin's access, which cycle 16's NFT
    // §7.1 forbids).
    private Task<bool> CanManageCompanyAsync(Guid companyId) =>
        CompanyAccess.CanManageCompanyAsync(db, User, companyId, superAdminBypass: false);

    private async Task<bool> IsStaffAsync(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        return await CompanyMembership.IsStaffAsync(db, companyId, userId);
    }
}
