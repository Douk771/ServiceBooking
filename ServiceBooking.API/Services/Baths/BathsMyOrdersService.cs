using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.API.Services.Subjects;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.10.5, API_CONTRACT_CYCLE42.md §42.26 — «Мои брони» of the «Бани» site: the bookings of «Бани» companies that belong to the account by
/// <c>GuestUserId</c> or, ONLY when the account's number is confirmed by it (<see cref="SubjectScopeResolver"/>, TD-03), by the guest's phone. Anonymised bookings carry neither
/// and never match. The window is 12 months by the start of the session.
/// </summary>
public class BathsMyOrdersService(AppDbContext db, SubjectScopeResolver scopes, IStaysClock clock, PublicSiteLinks links)
{
    public const int WindowMonths = 12;

    /// <summary>A booking is active while it holds the time: a hold, a payment under check, or a confirmed booking that has not ended yet.</summary>
    public static bool IsActive(StayBookingStatus status, DateTime endUtc, DateTime nowUtc) =>
        status is StayBookingStatus.Held or StayBookingStatus.AwaitingPaymentCheck || (status == StayBookingStatus.Confirmed && nowUtc < endUtc);

    /// <summary>Active first by the start ascending, then the rest by the start descending (the contract's order).</summary>
    public static List<(MyBathOrderDto Dto, DateTime StartUtc)> Arrange(IEnumerable<(MyBathOrderDto Dto, DateTime StartUtc)> rows) =>
        rows.OrderBy(r => r.Dto.IsActive ? 0 : 1)
            .ThenBy(r => r.Dto.IsActive ? r.StartUtc : DateTime.MaxValue)
            .ThenByDescending(r => r.Dto.IsActive ? DateTime.MinValue : r.StartUtc).ToList();

    public async Task<MyBathOrdersDto> ListAsync(string userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return new MyBathOrdersDto([]);
        var scope = await scopes.ForAccountAsync(user, ct);
        var guestMatchPhone = scope.GuestMatchPhone;
        var now = clock.UtcNow;
        var since = now.AddMonths(-WindowMonths);

        var rows = await (from o in db.StayServiceOrders.AsNoTracking()
                          join c in db.Companies.AsNoTracking() on o.CompanyId equals c.Id
                          join s in db.StayServiceSessions.AsNoTracking() on o.Id equals s.StayServiceOrderId
                          where c.Kind == CompanyKind.Baths && s.StartUtc >= since
                                && (o.GuestUserId == userId || (guestMatchPhone != null && o.GuestPhone == guestMatchPhone))  // SUBJECT-PHONE-GATE: gated — guestMatchPhone is non-null only for a number this account confirmed (SubjectScopeResolver, TD-03)
                          select new { Order = o, Session = s, CompanyName = c.Name, c.CityId }).ToListAsync(ct);
        if (rows.Count == 0) return new MyBathOrdersDto([]);

        var cityIds = rows.Where(r => r.CityId != null).Select(r => r.CityId!.Value).Distinct().ToList();
        var cities = await db.Cities.AsNoTracking().Where(c => cityIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var wording = ServiceWording.Baths;
        var items = rows.Select(r =>
        {
            var display = ServiceDtoMapper.DisplayStatusOf(r.Order, r.Session.EndUtc, now);
            var city = r.CityId is { } id ? cities.GetValueOrDefault(id) : null;
            var dto = new MyBathOrderDto(
                links.ServiceOrderRelativePath(CompanyKind.Baths, r.Order.PublicToken), r.CompanyName, r.Session.ServiceNameSnapshot,
                ServiceTimeFormat.Guest(r.Session.BusinessDate, r.Session.StartMinute, r.Session.Hours), ServiceWording.LocalTimeNote(city), r.Order.Status, display,
                wording.StatusText(display), r.Order.TotalRub, IsActive(r.Order.Status, r.Session.EndUtc, now));
            return (dto, r.Session.StartUtc);
        });
        return new MyBathOrdersDto(Arrange(items).Select(r => r.Dto).ToList());
    }
}
