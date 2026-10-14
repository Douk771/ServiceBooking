using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.6, API_CONTRACT_CYCLE42.md §42.33 — the schedule of the bath attendant: sessions by the business date of the start, whose booking
/// awaits a payment check or is confirmed (a held one is not shown). ONE closed shape for every role: no phone, amounts, files, requisites, journal or payment
/// status. The guest's comment is null for an attendant unless the owner switched it on (ЮР-5).
/// </summary>
public class BathsScheduleService(AppDbContext db, ServiceSlotService slots)
{
    public const int MaxDays = 31;
    public const int DefaultDays = 7;
    public const string InvalidPeriodText = "Неверный период";

    /// <summary>Parses the query; null error = valid. <paramref name="from"/> is a business date <c>YYYY-MM-DD</c>, <paramref name="days"/> is 1…31.</summary>
    public static string? ParsePeriod(string? from, int? days, out DateOnly? fromDate, out int count)
    {
        fromDate = null;
        count = days ?? DefaultDays;
        if (count is < 1 or > MaxDays) return InvalidPeriodText;
        if (string.IsNullOrWhiteSpace(from)) return null;
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
            return InvalidPeriodText;
        fromDate = parsed;
        return null;
    }

    public async Task<BathScheduleDto> BuildAsync(Company company, StaysMyRole role, DateOnly? fromParam, int days, CancellationToken ct)
    {
        var today = slots.TodayOf(company);
        var start = fromParam ?? today;
        var end = start.AddDays(days);
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct) ?? new StaysSettings();
        var showComment = role != StaysMyRole.Housekeeper || settings.HousekeeperSeesGuestComment;

        var rows = await (from s in db.StayServiceSessions.AsNoTracking()
                          join o in db.StayServiceOrders.AsNoTracking() on s.StayServiceOrderId equals o.Id
                          where s.CompanyId == company.Id && s.ReleasedAtUtc == null && s.BusinessDate >= start && s.BusinessDate < end &&
                                (o.Status == StayBookingStatus.AwaitingPaymentCheck || o.Status == StayBookingStatus.Confirmed)
                          orderby s.StartUtc, s.Id
                          select new { Session = s, o.Status, o.GuestName, o.Comment, o.GuestsCount }).ToListAsync(ct);

        var byDay = rows.ToLookup(r => r.Session.BusinessDate);
        var result = new List<BathScheduleDayDto>();
        for (var d = start; d < end; d = d.AddDays(1))
        {
            var sessions = byDay[d].Select(r =>
            {
                var s = r.Session;
                var items = ServiceJson.ReadItems(s.ItemsJson).Select(i => new ScheduleItemDto(i.Name, i.Quantity)).ToList();
                return new BathScheduleSessionDto(
                    s.Id, s.ServiceNameSnapshot, ServiceTimeFormat.Guest(s.BusinessDate, s.StartMinute, s.Hours), ServiceDtoMapper.PreparedUntilLabel(s),
                    r.GuestName, r.GuestsCount, items, showComment ? r.Comment : null, r.Status == StayBookingStatus.AwaitingPaymentCheck);
            }).ToList();
            result.Add(new BathScheduleDayDto(d, ServiceTimeFormat.DateLabel(d), sessions));
        }
        return new BathScheduleDto(today, result);
    }
}
