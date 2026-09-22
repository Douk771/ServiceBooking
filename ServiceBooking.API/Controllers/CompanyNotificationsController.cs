using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
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
    PlatformSettings platformSettings) : ControllerBase
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
        var assignment = await db.ChannelCompanyAssignments.AsNoTracking()
            .Include(a => a.Channel).FirstOrDefaultAsync(a => a.CompanyId == companyId);

        return Ok(await BuildSettingsDtoAsync(plan, settings, assignment?.Channel));
    }

    [HttpPut("notification-settings")]
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
        var assignment = await db.ChannelCompanyAssignments
            .Include(a => a.Channel).FirstOrDefaultAsync(a => a.CompanyId == companyId);

        if (!plan.AllowNotificationChannel)
            return StatusCode(402, "Недоступно на вашем тарифе");
        if (assignment is null || ChannelPaymentState.Of(assignment.Channel, DateTime.UtcNow) != ChannelPaymentStatus.Paid)
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
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // API_CONTRACT_CYCLE4.md §28.2: a lowered threshold takes effect immediately on already-queued
        // rows — the next dispatcher pass re-evaluates them against the new MinLeadMinutes anyway
        // (NotificationGate.Evaluate is re-run per pass, not cached on the row), so nothing further is
        // needed here beyond persisting the new threshold; no queued row is touched by this request.
        await db.SaveChangesAsync();

        return Ok(await BuildSettingsDtoAsync(plan, settings, assignment.Channel));
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

        return Ok(new TemplatesResponseDto(placeholders, "Отказаться от уведомлений: https://ezbook.ru/u/…", templates));
    }

    [HttpPut("notification-templates/{type}")]
    public async Task<ActionResult<TemplateItemDto>> UpdateTemplate(Guid companyId, string type, [FromBody] TemplateBodyDto dto)
    {
        if (!await CanManageCompanyAsync(companyId)) return Forbid();
        if (!TryParseCustomizableType(type, out var parsedType)) return NotFound();

        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null) return NotFound();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        var assignment = await db.ChannelCompanyAssignments
            .Include(a => a.Channel).FirstOrDefaultAsync(a => a.CompanyId == companyId);
        if (!plan.AllowNotificationChannel ||
            assignment is null || ChannelPaymentState.Of(assignment.Channel, DateTime.UtcNow) != ChannelPaymentStatus.Paid)
            return StatusCode(402, "Канал не оплачен");

        // Empty body ("вернуть текст платформы", API_CONTRACT_CYCLE4.md §29.1) bypasses length/placeholder
        // validation entirely — it's a request to delete the override, not a 1000-character message.
        var resetToDefault = dto.Body.Length == 0;
        if (!resetToDefault)
        {
            var validation = NotificationTemplateValidator.Validate(dto.Body, parsedType);
            if (!validation.IsValid) return BadRequest(validation.Error);
        }

        var existing = await db.NotificationTemplates.FirstOrDefaultAsync(t => t.CompanyId == companyId && t.Type == parsedType);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var nowUtc = DateTime.UtcNow;

        if (existing is not null)
        {
            db.NotificationTemplateHistories.Add(new NotificationTemplateHistory
            {
                Id = Guid.NewGuid(), CompanyId = companyId, Type = parsedType,
                PreviousBody = existing.Body, ChangedByUserId = userId, ChangedAtUtc = nowUtc,
            });
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
        [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!await IsStaffAsync(companyId)) return Forbid();

        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.OutboundNotifications.AsNoTracking().Where(n => n.CompanyId == companyId);
        if (status.HasValue) query = query.Where(n => n.Status == status);
        if (type.HasValue) query = query.Where(n => n.Type == type);
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
            n.BookingId, n.VisitStartUtc, n.SentAtUtc, n.ChannelId)).ToList();

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
        EffectivePlan plan, CompanyNotificationSettings? settings, NotificationChannel? channel)
    {
        var enabledMask = settings?.EnabledTypeMask ?? CompanyNotificationSettings.DefaultEnabledTypeMask;
        var enabledTypes = Enum.GetValues<NotificationType>().Where(t => (enabledMask & (1 << (int)t)) != 0).ToList();

        ChannelPaymentStatus? paymentState = channel is null ? null : ChannelPaymentState.Of(channel, DateTime.UtcNow);
        var idleDays = await platformSettings.GetChannelIdleDaysAsync();
        var stateText = channel is null ? null : ChannelPresentation.StateText(
            channel.State, channel.PhoneNumber is null ? null : PhoneDisplayMask.Mask(channel.PhoneNumber),
            idleDays, channel.PaidUntilUtc, channel.LastStateReason);

        var channelDto = new SettingsChannelDto(channel is not null, channel?.Id, channel?.State, stateText, paymentState, channel?.PaidUntilUtc);

        var blockedReason = ChannelPresentation.SettingsBlockedReason(
            plan.AllowNotificationChannel, channel is not null, paymentState, channel?.State);

        return new NotificationSettingsDto(
            enabledTypes, settings?.ReminderLeadMinutes ?? new CompanyNotificationSettings().ReminderLeadMinutes,
            settings?.MinLeadMinutes ?? new CompanyNotificationSettings().MinLeadMinutes,
            plan.AllowNotificationChannel, channelDto, blockedReason is null, blockedReason);
    }

    private static int BuildMask(IReadOnlyList<NotificationType> types)
    {
        var mask = 0;
        foreach (var type in types) mask |= 1 << (int)type;
        return mask;
    }

    private static bool TryParseCustomizableType(string raw, out NotificationType type) =>
        Enum.TryParse(raw, ignoreCase: false, out type) && DefaultTemplates.CustomizableTypes.Contains(type);

    private async Task<bool> CanManageCompanyAsync(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        return await CompanyMembership.IsOwnerAsync(db, companyId, userId);
    }

    private async Task<bool> IsStaffAsync(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        return await CompanyMembership.IsStaffAsync(db, companyId, userId);
    }
}
