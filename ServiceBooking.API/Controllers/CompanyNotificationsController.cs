using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Companies;
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
    PlatformSettings platformSettings,
    LegalDocumentProvider legalProvider,
    ChannelFundingReader fundingReader,
    AccountMessagingReader messagingReader) : ControllerBase
{
    // ── Settings ─────────────────────────────────────────────────────────────────────────────────

    [HttpGet("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> GetSettings(Guid companyId, CancellationToken ct)
    {
        if (!await CanManageCompanyAsync(companyId)) return Forbid();
        // §389.2: notifications belong to booking events — shops get them in cycle 2 (rights first, kind second).
        if (await CompanyKindGuard.RejectNonSalonAsync(db, companyId) is { } shopRefusal) return shopRefusal;

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId, ct);
        if (company is null) return NotFound();

        var settings = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);
        // ARCHITECTURE_CYCLE40.md §40.4: the company's channels are those of its billing account (one number serves every company of the account).
        var messaging = await messagingReader.ForCompanyAsync(companyId, ct: ct);

        return Ok(await BuildSettingsDtoAsync(settings, messaging));
    }

    [HttpPut("notification-settings")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<NotificationSettingsDto>> UpdateSettings(Guid companyId, [FromBody] UpdateNotificationSettingsDto dto)
    {
        if (!await CanManageCompanyAsync(companyId)) return Forbid();
        // §389.2: notifications belong to booking events — shops get them in cycle 2 (rights first, kind second).
        if (await CompanyKindGuard.RejectNonSalonAsync(db, companyId) is { } shopRefusal) return shopRefusal;

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null) return NotFound();

        if (dto.ReminderLeadMinutes is < 60 or > 4320)
            return BadRequest("Напоминание можно отправлять за 1–72 часа до визита");
        if (dto.MinLeadMinutes is < 0 or > 720)
            return BadRequest("Порог может быть от 0 до 12 часов");
        if (dto.MinLeadMinutes >= dto.ReminderLeadMinutes)
            return BadRequest("Напоминание за 1 час при пороге 2 часа не уйдёт никогда");

        // Cycle 40 (ARCHITECTURE_CYCLE40.md §40.3.4, §40.4): neither the tariff flag nor "an assigned, paid number" gates the settings any more —
        // no 402 "Канал не оплачен". The company's channels are those of its billing account.
        var messaging = await messagingReader.ForCompanyAsync(companyId);

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
            // §114.4/B4 + §40.5.1: priorityTransport must be a ROUTABLE transport of the account (paid, first number, not suspended,
            // bound at least once) — the same definition routing uses at queue time. ChannelState is deliberately not required to be
            // Connected: gating this WRITE on it would reject saving unrelated settings while a number is briefly reconnecting.
            if (!messaging.For(dto.PriorityTransport.Value).Routable)
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

        return Ok(await BuildSettingsDtoAsync(settings, messaging));
    }

    // ── Templates ────────────────────────────────────────────────────────────────────────────────

    [HttpGet("notification-templates")]
    public async Task<ActionResult<TemplatesResponseDto>> GetTemplates(Guid companyId, CancellationToken ct)
    {
        if (!await CanManageCompanyAsync(companyId)) return Forbid();
        // §389.2: notifications belong to booking events — shops get them in cycle 2 (rights first, kind second).
        if (await CompanyKindGuard.RejectNonSalonAsync(db, companyId) is { } shopRefusal) return shopRefusal;

        var rows = await db.NotificationTemplates.AsNoTracking().Where(t => t.CompanyId == companyId).ToListAsync(ct);
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
        // §389.2: notifications belong to booking events — shops get them in cycle 2 (rights first, kind second).
        if (await CompanyKindGuard.RejectNonSalonAsync(db, companyId) is { } shopRefusal) return shopRefusal;
        if (!TryParseCustomizableType(type, out var parsedType)) return NotFound();

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null) return NotFound();

        // Cycle 40 (§40.3.4): templates stay editable regardless of the payment — the 402 "Канал не оплачен" is gone.

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
        // §389.2: notifications belong to booking events — shops get them in cycle 2 (rights first, kind second).
        if (await CompanyKindGuard.RejectNonSalonAsync(db, companyId) is { } shopRefusal) return shopRefusal;
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
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        if (!await IsStaffAsync(companyId)) return Forbid();
        // §389.2: notifications belong to booking events — shops get them in cycle 2 (rights first, kind second).
        if (await CompanyKindGuard.RejectNonSalonAsync(db, companyId) is { } shopRefusal) return shopRefusal;

        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.OutboundNotifications.AsNoTracking().Where(n => n.CompanyId == companyId);
        if (status.HasValue) query = query.Where(n => n.Status == status);
        if (type.HasValue) query = query.Where(n => n.Type == type);
        // ARCHITECTURE_CYCLE9.md §114.3 (US-120) — additive ?transport= filter.
        if (transport.HasValue) query = query.Where(n => n.Transport == transport);
        from = QueryDateTime.ToUtc(from);
        to = QueryDateTime.ToUtc(to);
        if (from.HasValue) query = query.Where(n => n.CreatedAt >= from);
        if (to.HasValue) query = query.Where(n => n.CreatedAt <= to);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToListAsync(ct);

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
    public async Task<ActionResult<NotificationSummaryDto>> GetSummary(Guid companyId, [FromQuery] int? days, CancellationToken ct)
    {
        if (!await IsStaffAsync(companyId)) return Forbid();
        // §389.2: notifications belong to booking events — shops get them in cycle 2 (rights first, kind second).
        if (await CompanyKindGuard.RejectNonSalonAsync(db, companyId) is { } shopRefusal) return shopRefusal;

        var window = days is > 0 ? days.Value : 30;
        var sinceUtc = DateTime.UtcNow.AddDays(-window);

        var rows = await db.OutboundNotifications.AsNoTracking()
            .Where(n => n.CompanyId == companyId && n.CreatedAt >= sinceUtc)
            .Select(n => new NotificationCounterRow(n.CompanyId, n.Status, n.ReadAtUtc))
            .ToListAsync(ct);

        // Cycle 40 (§40.4): the channels of the company are those of its billing account; "several companies on the channel" = several
        // companies on the account. The paid-until date is the latest payment date of the account's transports that have a number.
        var messaging = await messagingReader.ForCompanyAsync(companyId, ct: ct);
        var liveTransports = messaging.Transports.Where(t => t.Primary is not null).ToList();
        DateTime? channelPaidUntil = liveTransports.Select(t => t.Payment.PaidUntil).Max();

        IReadOnlyList<NotificationSummaryByCompanyDto>? byCompany = null;
        var siblingCompanies = messaging.Channels.Count == 0 || messaging.AccountId == Guid.Empty
            ? []
            : await db.Companies.AsNoTracking().Where(c => c.BillingAccountId == messaging.AccountId)
                .Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        if (siblingCompanies.Count > 1)
        {
            var siblingCompanyIds = siblingCompanies.Select(c => c.Id).ToList();
            var siblingRows = await db.OutboundNotifications.AsNoTracking()
                .Where(n => siblingCompanyIds.Contains(n.CompanyId) && n.CreatedAt >= sinceUtc)
                .Select(n => new NotificationCounterRow(n.CompanyId, n.Status, n.ReadAtUtc))
                .ToListAsync(ct);
            var companyNames = siblingCompanies.ToDictionary(c => c.Id, c => c.Name);

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
        CompanyNotificationSettings? settings, AccountMessagingState messaging)
    {
        var enabledMask = settings?.EnabledTypeMask ?? CompanyNotificationSettings.DefaultEnabledTypeMask;
        // Cycle 24 (§458): the salon screen speaks ONLY about booking types — the order types share the enum but are not salon settings.
        var enabledTypes = NotificationTypeCatalog.BookingTypes.Where(t => (enabledMask & (1 << (int)t)) != 0).ToList();

        var deliveryMode = settings?.DeliveryMode ?? new CompanyNotificationSettings().DeliveryMode;
        var priorityTransport = settings?.PriorityTransport ?? new CompanyNotificationSettings().PriorityTransport;

        // ARCHITECTURE_CYCLE9.md §104.5/§114.4: `channel` legacy field describes the PRIORITY transport's
        // own assignment specifically — with more than one transport possibly connected, that is the one
        // channel the rest of this screen's (pre-cycle-9) fields still meaningfully describe.
        var channel = messaging.For(priorityTransport).Primary;

        // §47.3: ChannelDto/SettingsChannelDto's paymentState keeps its FORM but its SOURCE is the
        // account's funding ranking. Cycle 22 (§379, Р2): paidUntil (and the NeedsReconnect state text's
        // "оплаченный период до") come from the same funding — the WhatsApp option's PaidUntilUtc, else
        // the subscription period — instead of the channel's dropped PaidUntilUtc column.
        ChannelPaymentStatus? paymentState = null;
        DateTime? paidUntil = null;
        if (channel is not null)
        {
            var funding = (await fundingReader.LoadAsync([channel])).GetValueOrDefault(channel.Id);
            paymentState = ChannelPaymentState.Of(channel, funding);
            paidUntil = funding?.PaidUntil;
        }
        var idleDays = await platformSettings.GetChannelIdleDaysAsync();
        var stateText = channel is null ? null : ChannelPresentation.StateText(
            channel.State, channel.PhoneNumber is null ? null : PhoneDisplayMask.Mask(channel.PhoneNumber),
            idleDays, paidUntil, channel.LastStateReason);

        var channelDto = new SettingsChannelDto(channel is not null, channel?.Id, channel?.State, stateText, paymentState, paidUntil);

        var blockedReason = ChannelPresentation.SettingsBlockedReason(channel is not null, paymentState, channel?.State);

        // ARCHITECTURE_CYCLE9.md §114.4: connectedTransports/priorityChannelHealthy read-only fields —
        // "usable" means the SAME thing NotificationRouting.SelectTargets means by it (funded AND
        // Connected), so the settings screen never shows a transport as pickable that routing would then
        // immediately treat as unavailable.
        var usableTransports = UsableTransports(messaging);
        var priorityChannelHealthy = usableTransports.Contains(priorityTransport);

        return new NotificationSettingsDto(
            enabledTypes, settings?.ReminderLeadMinutes ?? new CompanyNotificationSettings().ReminderLeadMinutes,
            settings?.MinLeadMinutes ?? new CompanyNotificationSettings().MinLeadMinutes,
            PlanAllowsChannel: true, channelDto, blockedReason is null, blockedReason,
            deliveryMode, priorityTransport, usableTransports, priorityChannelHealthy);
    }

    /// <summary>ARCHITECTURE_CYCLE9.md §104.5/§114.4 — the transports the READ-ONLY settings screen shows as currently pickable/healthy:
    /// routable (§40.5.1) AND <see cref="ChannelState.Connected"/>. Deliberately stricter than what queueing requires (a live status signal
    /// for the owner, not a gate on what the PUT endpoint accepts).</summary>
    private static IReadOnlyList<NotificationTransport> UsableTransports(AccountMessagingState messaging) =>
        messaging.Transports.Where(t => t.Routable && t.Primary!.State == ChannelState.Connected).Select(t => t.Transport).ToList();

    internal static int BuildMask(IReadOnlyList<NotificationType> types)
    {
        var mask = 0;
        // ARCHITECTURE_CYCLE39.md §39.9.1: only the salon types have a bit. A type from the body outside BookingTypes is ignored — in C# `1 << 32 == 1`,
        // so a value ≥ 32 would otherwise flip the bit of BookingConfirmed.
        foreach (var type in types.Where(NotificationTypeCatalog.IsBookingType)) mask |= 1 << (int)type;
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
