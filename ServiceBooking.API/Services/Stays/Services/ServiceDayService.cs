using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.10, API_CONTRACT_CYCLE39.md §39.30.2 — «День услуг»: the sessions of every service on one business date as bars on a minute axis (a session after midnight sits at
/// minute ≥ 1440 of the SAME date). A buffer that runs past the border of the business day is cut at the border and arrives on the next day as a carry-over bar.
/// </summary>
public class ServiceDayService(AppDbContext db, ServiceSlotService slots, IStaysClock clock)
{
    private sealed record Row(StayServiceSession Session, string? HouseName, StayBookingStatus? Status, DateTime? HoldExpiresAtUtc, string? GuestName = null, int? GuestsCount = null, bool Erased = false);

    public static string BarStateText(StayBookingStatus status) => status switch
    {
        StayBookingStatus.Held => "Ждёт оплаты",
        StayBookingStatus.AwaitingPaymentCheck => "Проверка оплаты",
        _ => "Подтверждён"
    };

    /// <summary>API_CONTRACT_CYCLE42.md §42.32: the bar of a «Бани» booking — «{имя гостя}, {N} чел.», or «Бронь» when the name is gone (erased / not given). The number of guests is shown only with a name.</summary>
    public static string BathsBarLabel(string? guestName, int? guestsCount, bool erased)
    {
        var name = erased ? null : guestName?.Trim();
        if (string.IsNullOrEmpty(name)) return "Бронь";
        return guestsCount is { } n ? $"{name}, {n} чел." : name;
    }

    public async Task<ServiceDayDto> BuildAsync(Company company, DateOnly date, CancellationToken ct)
    {
        var b = slots.BusinessDayStart;
        var end = b + BusinessClock.MinutesPerDay;
        var now = clock.UtcNow;
        var sessions = await RowsAsync(company.Id, date, now, ct);
        var previous = (await RowsAsync(company.Id, date.AddDays(-1), now, ct)).Where(r => r.Session.StartMinute + 60 * r.Session.Hours + r.Session.BufferMinutesSnapshot > end).ToList();

        var serviceIds = sessions.Select(r => r.Session.ServiceId).Concat(previous.Select(r => r.Session.ServiceId)).ToHashSet();
        var services = await db.StayServices.AsNoTracking().Where(s => s.CompanyId == company.Id && (s.ArchivedAtUtc == null || serviceIds.Contains(s.Id)))
            .OrderBy(s => s.ArchivedAtUtc != null).ThenBy(s => s.Position).ThenBy(s => s.Name).ToListAsync(ct);
        var isBaths = company.Kind == CompanyKind.Baths;
        var result = new List<ServiceDayServiceDto>();
        var from = int.MaxValue;
        var to = 0;
        foreach (var s in services)
        {
            var windows = (await slots.WindowsAsync(s.Id, date, date, ct))[date];
            var bars = new List<ServiceDayBarDto>();
            foreach (var r in previous.Where(x => x.Session.ServiceId == s.Id))
            {
                var until = r.Session.StartMinute + 60 * r.Session.Hours + r.Session.BufferMinutesSnapshot - BusinessClock.MinutesPerDay;
                bars.Add(new ServiceDayBarDto(ServiceDayBarKind.CarryOverBuffer, r.Session.Id, b, until, "подготовка после вчерашнего сеанса", null, null, false));
            }
            foreach (var r in sessions.Where(x => x.Session.ServiceId == s.Id))
            {
                var ses = r.Session;
                var sessionEnd = ses.StartMinute + 60 * ses.Hours;
                var needs = r.Status == StayBookingStatus.AwaitingPaymentCheck;
                bars.Add(new ServiceDayBarDto(ServiceDayBarKind.Session, ses.Id, ses.StartMinute, sessionEnd,
                    isBaths ? BathsBarLabel(r.GuestName, r.GuestsCount, r.Erased) : r.HouseName ?? "без проживания", r.Status?.ToString(),
                    r.Status is { } st ? BarStateText(st) : null, needs));
                if (ses.BufferMinutesSnapshot > 0)
                    bars.Add(new ServiceDayBarDto(ServiceDayBarKind.Buffer, ses.Id, sessionEnd, Math.Min(sessionEnd + ses.BufferMinutesSnapshot, end), "подготовка", null, null, false));
            }
            bars = bars.OrderBy(x => x.StartMinute).ThenBy(x => x.Kind).ToList();
            foreach (var w in windows) { from = Math.Min(from, w.StartMinute); to = Math.Max(to, w.EndMinute); }
            foreach (var x in bars) { from = Math.Min(from, x.StartMinute); to = Math.Max(to, x.EndMinute); }
            result.Add(new ServiceDayServiceDto(s.Id, s.Name, s.IsPublished, windows.Count == 0, windows.Select(w => ServiceScheduleWriter.WindowDto(w.StartMinute, w.EndMinute)).ToList(), bars));
        }
        if (from == int.MaxValue) { from = b; to = end; }
        from = Math.Max(b, from);
        to = Math.Min(end, Math.Max(to, from + 60));
        return new ServiceDayDto(date, ServiceTimeFormat.BusinessDateLabel(date), slots.TodayOf(company), new ServiceDayAxisDto(from, to, BusinessClock.MinutesPerDay), result);
    }

    private async Task<List<Row>> RowsAsync(Guid companyId, DateOnly date, DateTime now, CancellationToken ct)
    {
        var sessions = await db.StayServiceSessions.AsNoTracking().Where(s => s.CompanyId == companyId && s.BusinessDate == date && s.ReleasedAtUtc == null).ToListAsync(ct);
        if (sessions.Count == 0) return [];
        var bookingIds = sessions.Where(s => s.StayBookingId != null).Select(s => s.StayBookingId!.Value).ToList();
        var orderIds = sessions.Where(s => s.StayServiceOrderId != null).Select(s => s.StayServiceOrderId!.Value).ToList();
        var bookings = await (from bk in db.StayBookings.AsNoTracking() join h in db.Houses.AsNoTracking() on bk.HouseId equals h.Id where bookingIds.Contains(bk.Id)
                              select new { bk.Id, bk.Status, bk.HoldExpiresAtUtc, HouseName = h.Name }).ToDictionaryAsync(x => x.Id, ct);
        var orders = await db.StayServiceOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Status, o.HoldExpiresAtUtc, o.GuestName, o.GuestsCount, o.PersonalDataErased }).ToDictionaryAsync(o => o.Id, ct);
        var rows = new List<Row>();
        foreach (var s in sessions)
        {
            if (s.StayBookingId is { } bid && bookings.TryGetValue(bid, out var bk))
            {
                if (bk.Status == StayBookingStatus.Held && bk.HoldExpiresAtUtc <= now) continue;
                rows.Add(new Row(s, bk.HouseName, bk.Status, bk.HoldExpiresAtUtc));
            }
            else if (s.StayServiceOrderId is { } oid && orders.TryGetValue(oid, out var o))
            {
                if (o.Status == StayBookingStatus.Held && o.HoldExpiresAtUtc <= now) continue;
                rows.Add(new Row(s, null, o.Status, o.HoldExpiresAtUtc, o.GuestName, o.GuestsCount, o.PersonalDataErased));
            }
        }
        return rows;
    }
}
