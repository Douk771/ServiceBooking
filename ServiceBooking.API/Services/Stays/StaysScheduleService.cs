using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.9, API_CONTRACT_CYCLE37.md §37.31 — the cleaning and check-in schedule. ONE shape for every role, and that shape has NO phone, amounts,
/// files, requisites or journal; the guest's comment is null unless the viewer is not a housekeeper or the owner switched the setting on (ЮР-5).
/// </summary>
public class StaysScheduleService(AppDbContext db, IStaysClock clock)
{
    public const int MaxDays = 15;

    private static readonly string[] Weekdays = ["вс", "пн", "вт", "ср", "чт", "пт", "сб"];
    private static readonly string[] Months = ["янв", "фев", "мар", "апр", "мая", "июн", "июл", "авг", "сен", "окт", "ноя", "дек"];

    public static string Label(DateOnly date, DateOnly today) =>
        date == today ? "Сегодня" : date == today.AddDays(1) ? "Завтра" : $"{Weekdays[(int)date.DayOfWeek]} {date.Day} {Months[date.Month - 1]}";

    public async Task<StaysScheduleDto> BuildAsync(Company company, StaysMyRole role, DateOnly? fromParam, int? daysParam, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var today = StayTime.LocalDate(company.TimeZoneId, now);
        var from = fromParam ?? today;
        var days = Math.Clamp(daysParam ?? MaxDays, 1, MaxDays);
        var to = from.AddDays(days);
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct) ?? new StaysSettings();
        var showComment = role != StaysMyRole.Housekeeper || settings.HousekeeperSeesGuestComment;

        var bookings = await db.StayBookings.AsNoTracking()
            .Where(b => b.CompanyId == company.Id && (b.Status == StayBookingStatus.Confirmed || b.Status == StayBookingStatus.AwaitingPaymentCheck) &&
                        ((b.CheckInDate >= from && b.CheckInDate < to) || (b.CheckOutDate >= from && b.CheckOutDate < to)))
            .Select(b => new
            {
                b.Id, b.HouseId, HouseName = b.House.Name, b.CheckInDate, b.CheckOutDate, b.CheckInTimeSnapshot, b.CheckOutTimeSnapshot, b.ArrivalTime, b.GuestName,
                b.Adults, b.Children, b.ExtraBeds, b.Dogs, b.NeedCot, b.Comment, b.Status, b.PersonalDataErased
            }).ToListAsync(ct);

        var sessionsByDay = await SessionsAsync(company, role, showComment, from, to, ct);
        var result = new List<ScheduleDayDto>();
        for (var d = from; d < to; d = d.AddDays(1))
        {
            var departures = bookings.Where(b => b.CheckOutDate == d).ToList();
            var arrivals = bookings.Where(b => b.CheckInDate == d).ToList();
            var turnoverHouses = departures.Select(b => b.HouseId).Intersect(arrivals.Select(b => b.HouseId)).ToHashSet();
            result.Add(new ScheduleDayDto(d, Label(d, today),
                departures.OrderBy(b => b.HouseName).Select(b => new ScheduleDepartureDto(b.Id, b.HouseId, b.HouseName, StayFormat.Time(b.CheckOutTimeSnapshot), turnoverHouses.Contains(b.HouseId))).ToList(),
                arrivals.OrderBy(b => b.HouseName).Select(b =>
                {
                    var turnover = turnoverHouses.Contains(b.HouseId);
                    var outgoing = turnover ? departures.First(x => x.HouseId == b.HouseId) : null;
                    return new ScheduleArrivalDto(b.Id, b.HouseId, b.HouseName, StayFormat.Time(b.CheckInTimeSnapshot), StayFormat.Time(b.ArrivalTime), b.GuestName,
                        b.Adults, b.Children, b.ExtraBeds, b.Dogs, b.NeedCot, showComment ? b.Comment : null, b.Status == StayBookingStatus.AwaitingPaymentCheck, turnover,
                        outgoing is null ? null : $"Выезд и заезд в один день — уборка {StayFormat.Time(outgoing.CheckOutTimeSnapshot)}–{StayFormat.Time(b.CheckInTimeSnapshot)}");
                }).ToList(), sessionsByDay.GetValueOrDefault(d) ?? []));
        }
        return new StaysScheduleDto(today, result);
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE39.md §39.10, Т39-10 — the sessions of services in the schedule: ONE shape for every role, WITHOUT a phone, amounts, files or a payment status (only the mark that
    /// payment is not confirmed). On the business date of the start; only sessions whose parent is awaiting a check or confirmed.
    /// </summary>
    private async Task<Dictionary<DateOnly, List<ScheduleSessionDto>>> SessionsAsync(Company company, StaysMyRole role, bool showComment, DateOnly from, DateOnly to, CancellationToken ct)
    {
        _ = role;
        var sessions = await db.StayServiceSessions.AsNoTracking()
            .Where(s => s.CompanyId == company.Id && s.ReleasedAtUtc == null && s.BusinessDate >= from && s.BusinessDate < to).OrderBy(s => s.StartUtc).ToListAsync(ct);
        if (sessions.Count == 0) return [];
        var bookingIds = sessions.Where(s => s.StayBookingId != null).Select(s => s.StayBookingId!.Value).ToList();
        var orderIds = sessions.Where(s => s.StayServiceOrderId != null).Select(s => s.StayServiceOrderId!.Value).ToList();
        var bookings = await (from b in db.StayBookings.AsNoTracking() join h in db.Houses.AsNoTracking() on b.HouseId equals h.Id where bookingIds.Contains(b.Id)
                              select new { b.Id, b.Status, b.GuestName, b.Comment, HouseName = h.Name }).ToDictionaryAsync(x => x.Id, ct);
        var orders = await db.StayServiceOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Status, o.GuestName, o.Comment }).ToDictionaryAsync(o => o.Id, ct);
        var result = new Dictionary<DateOnly, List<ScheduleSessionDto>>();
        foreach (var s in sessions)
        {
            StayBookingStatus status;
            string? guest, comment, house = null;
            if (s.StayBookingId is { } bid && bookings.TryGetValue(bid, out var b)) { status = b.Status; guest = b.GuestName; comment = b.Comment; house = b.HouseName; }
            else if (s.StayServiceOrderId is { } oid && orders.TryGetValue(oid, out var o)) { status = o.Status; guest = o.GuestName; comment = o.Comment; }
            else continue;
            if (status is not (StayBookingStatus.AwaitingPaymentCheck or StayBookingStatus.Confirmed)) continue;
            var end = s.StartMinute + 60 * s.Hours;
            var items = ServiceJson.ReadItems(s.ItemsJson).Select(i => new ScheduleItemDto(i.Name, i.Quantity)).ToList();
            if (!result.TryGetValue(s.BusinessDate, out var list)) result[s.BusinessDate] = list = [];
            list.Add(new ScheduleSessionDto(s.Id, s.ServiceNameSnapshot, ServiceTimeFormat.StaffRange(s.BusinessDate, s.StartMinute, end), ServiceDtoMapper.PreparedUntilLabel(s), house, guest, items,
                showComment ? comment : null, status == StayBookingStatus.AwaitingPaymentCheck));
        }
        return result;
    }
}
