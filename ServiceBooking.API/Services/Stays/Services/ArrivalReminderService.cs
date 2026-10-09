using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.11, API_CONTRACT_CYCLE39.md §39.33 — the setting of the reminder the day before the check-in: read, save (with the checks of §39.11.5 and a row of the history on
/// every save), preview without saving and the history. The texts are assembled by the pure <see cref="ArrivalReminderTemplate"/>; the frontend prints what it gets.
/// </summary>
public class ArrivalReminderService(AppDbContext db, LegalDocumentProvider legalProvider, PublicSiteLinks links, IStaysClock clock)
{
    public const string TimeText = "Время — с 08:00 до 22:00 с шагом 30 минут";
    public const string PushNoticeText = "Подтвердите, что прочитали предупреждение о push";
    public const string CodeNoticeFallback =
        "Коды доступа лучше не отправлять в сообщении: в мессенджер по умолчанию уходит только ссылка на бронь, а информация к заселению открывается на странице брони.";

    public sealed record SaveResult(string? BadRequest, StaysServiceConflictDto? Conflict, ArrivalReminderSettingsDto? Settings);

    public NoticeRefDto Notice(string key) => new(key, legalProvider.Current?.GetText(key)?.Version
        ?? "fallback:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant());

    public ArrivalReminderSettingsDto ToDto(StaysSettings s)
    {
        var validation = ArrivalReminderTemplate.Validate(s.ArrivalReminderTemplate);
        return new ArrivalReminderSettingsDto(
            s.ArrivalReminderEnabled, StayFormat.Time(s.ArrivalReminderTime), s.ArrivalReminderTemplate, s.ArrivalReminderTemplate is null,
            s.ArrivalReminderTemplate ?? ArrivalReminderTemplate.Default, ArrivalReminderTemplate.Default, s.ArrivalReminderPushText,
            ArrivalReminderTemplate.Placeholders.Select(p => new ReminderPlaceholderDto(p.Token, p.Description, p.InMessenger, p.OnPage, p.InPush)).ToList(),
            new ReminderLimitsDto(ArrivalReminderTemplate.TemplateMaxLength, ArrivalReminderTemplate.MessengerMaxLength, ArrivalReminderTemplate.PageMaxLength, ArrivalReminderTemplate.PushMaxLength),
            Notice(LegalTextKey.StayReminderTemplateOwnerNotice), Notice(LegalTextKey.StayReminderPushOwnerNotice), validation.Warnings.ToList());
    }

    public static bool TimeValid(string? raw, out TimeOnly time) =>
        StayFormat.TryParseTime(raw, out time) && StayFormat.IsHalfHour(time) && time >= new TimeOnly(8, 0) && time <= new TimeOnly(22, 0);

    public async Task<SaveResult> SaveAsync(Company company, StaysSettings settings, ArrivalReminderInput input, StayActor actor, CancellationToken ct)
    {
        if (!TimeValid(input.Time, out var time)) return new(TimeText, null, null);
        var template = string.IsNullOrWhiteSpace(input.Template) ? null : ArrivalReminderTemplate.Normalize(input.Template);
        if (template is not null && ArrivalReminderTemplate.IsDefault(template)) template = null;
        var validation = ArrivalReminderTemplate.Validate(template);
        if (validation.Errors.Count > 0) return new(validation.Errors[0].Message, null, null);
        var enablesPush = input.PushTextEnabled && !settings.ArrivalReminderPushText;
        if (enablesPush && string.IsNullOrWhiteSpace(input.PushNoticeVersion)) return new(PushNoticeText, null, null);
        if (validation.ConfirmationRequired && !input.ConfirmCodeMarkers && !string.Equals(template, settings.ArrivalReminderTemplate, StringComparison.Ordinal))
            return new(null, new StaysServiceConflictDto(nameof(StaysServiceConflictCode.ReminderConfirmationRequired),
                $"В тексте есть похожее на код доступа: {string.Join(", ", validation.Markers)}. Коды по умолчанию отправляются только ссылкой. Сохранить всё равно?",
                null, validation.Markers.ToList(), legalProvider.Current?.GetText(LegalTextKey.StayCheckInInfoOwnerNotice)?.ContentHtml ?? CodeNoticeFallback), null);

        var tracked = await db.StaysSettings.FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct);
        if (tracked is null)
        {
            tracked = new StaysSettings { CompanyId = company.Id };
            db.StaysSettings.Add(tracked);
        }
        var ownerVersion = string.IsNullOrWhiteSpace(input.OwnerNoticeVersion) ? Notice(LegalTextKey.StayReminderTemplateOwnerNotice).Version : input.OwnerNoticeVersion.Trim();
        db.StaysReminderTemplateChanges.Add(new StaysReminderTemplateChange
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, ChangedAtUtc = clock.UtcNow, ChangedByUserId = actor.UserId, ChangedByNameSnapshot = actor.NameSnapshot ?? "Сотрудник",
            PreviousTime = tracked.ArrivalReminderTime, NewTime = time, PreviousTemplate = tracked.ArrivalReminderTemplate, NewTemplate = template,
            PreviousPushText = tracked.ArrivalReminderPushText, NewPushText = input.PushTextEnabled, OwnerNoticeVersion = Cut(ownerVersion, 80),
            PushNoticeVersion = enablesPush ? Cut(input.PushNoticeVersion!.Trim(), 80) : null,
            CodeMarkersConfirmed = validation.ConfirmationRequired && input.ConfirmCodeMarkers,
            CodeMarkersHit = validation.ConfirmationRequired ? Cut(string.Join(", ", validation.Markers), 200) : null,
        });
        tracked.ArrivalReminderTime = time;
        tracked.ArrivalReminderTemplate = template;
        tracked.ArrivalReminderPushText = input.PushTextEnabled;
        tracked.UpdatedAtUtc = clock.UtcNow;
        tracked.UpdatedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);
        return new(null, null, ToDto(tracked));
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];

    public async Task<ArrivalReminderPreviewDto?> PreviewAsync(Company company, ArrivalReminderPreviewInput input, CancellationToken ct)
    {
        var template = string.IsNullOrWhiteSpace(input.Template) ? null : ArrivalReminderTemplate.Normalize(input.Template);
        if (template is not null && ArrivalReminderTemplate.IsDefault(template)) template = null;
        ReminderFacts facts;
        if (input.BookingId is { } bookingId)
        {
            var booking = await db.StayBookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId && b.CompanyId == company.Id, ct);
            if (booking is null) return null;
            facts = await FactsAsync(booking, company, unsubscribeUrl: null, ct);
        }
        else
        {
            facts = ArrivalReminderTemplate.Sample(StayTime.LocalDate(company.TimeZoneId, clock.UtcNow), company.Name);
        }
        var validation = ArrivalReminderTemplate.Validate(template);
        var messenger = ArrivalReminderTemplate.Render(template, facts, ReminderMode.Messenger).Text;
        var page = ArrivalReminderTemplate.Render(template, facts, ReminderMode.Page).Text;
        var push = ArrivalReminderTemplate.Render(template, facts, ReminderMode.Push, input.PushTextEnabled);
        return new ArrivalReminderPreviewDto(
            new PreviewTextDto(messenger, messenger.Length), new PreviewTextDto(page, page.Length),
            new PushPreviewDto(push.Text, push.Text.Length, push.UsesFixedText, push.Dropped.Select(d => new DroppedLineDto(d.Line, d.Text, d.Reason)).ToList()),
            validation.Warnings.ToList(), validation.Errors.Select(e => new ReminderErrorDto(e.Code, e.Message)).ToList(), validation.ConfirmationRequired, validation.Markers.ToList());
    }

    public async Task<List<ArrivalReminderChangeDto>> HistoryAsync(Guid companyId, int rows, CancellationToken ct) =>
        (await db.StaysReminderTemplateChanges.AsNoTracking().Where(c => c.CompanyId == companyId).OrderByDescending(c => c.ChangedAtUtc).Take(rows).ToListAsync(ct))
            .Select(c => new ArrivalReminderChangeDto(c.ChangedAtUtc, c.ChangedByNameSnapshot, StayFormat.Time(c.PreviousTime), StayFormat.Time(c.NewTime), c.PreviousTemplate, c.NewTemplate,
                c.PreviousPushText, c.NewPushText, c.CodeMarkersConfirmed, c.CodeMarkersHit)).ToList();

    /// <summary>The facts of a reminder, taken from the booking (its snapshot times and the active sessions) at the moment of sending.</summary>
    public async Task<ReminderFacts> FactsAsync(StayBooking b, Company company, string? unsubscribeUrl, CancellationToken ct)
    {
        var house = await db.Houses.AsNoTracking().FirstAsync(h => h.Id == b.HouseId, ct);
        var sessions = (await db.StayServiceSessions.AsNoTracking().Where(s => s.StayBookingId == b.Id && s.State == StayServiceSessionState.Active).OrderBy(s => s.StartUtc).ToListAsync(ct))
            .Select(s => new ReminderSessionFact(s.ServiceNameSnapshot, s.BusinessDate, s.StartMinute, s.Hours)).ToList();
        return new ReminderFacts(company.Name, house.Name, b.CheckInDate, b.CheckOutDate, b.Nights, StayFormat.Time(b.CheckInTimeSnapshot), StayFormat.Time(b.CheckOutTimeSnapshot),
            b.PersonalDataErased ? null : b.GuestName, house.Address ?? company.Address, company.Phone, b.DueAtCheckInRub, links.StayBookingPageUrl(b.PublicToken), unsubscribeUrl, sessions);
    }
}
