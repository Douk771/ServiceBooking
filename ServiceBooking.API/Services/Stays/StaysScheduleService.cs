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
                }).ToList()));
        }
        return new StaysScheduleDto(today, result);
    }
}
