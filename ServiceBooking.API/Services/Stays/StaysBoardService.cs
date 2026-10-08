using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.7.2, API_CONTRACT_CYCLE37.md §37.29.1 — the board (шахматка): houses × days. The frontend polls with the last revision; an unchanged
/// revision costs one primary-key lookup. Expired-but-unprocessed holds and final bookings are not shown.
/// </summary>
public class StaysBoardService(AppDbContext db, IStaysClock clock)
{
    public const int MinDays = 7, MaxDays = 62, DefaultDays = 30;

    public static HouseBlockDto ToDto(HouseBlock b, string createdByName) =>
        new(b.Id, b.HouseId, b.StartDate, b.EndDate, b.Kind, StaysTexts.BlockKindText(b.Kind), b.Comment, createdByName, b.CreatedAtUtc, b.UpdatedAtUtc);

    public async Task<StaysBoardDto> BuildAsync(Company company, DateOnly? fromParam, int? daysParam, long? sinceRevision, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var today = StayTime.LocalDate(company.TimeZoneId, now);
        var revision = await db.StaysSettings.AsNoTracking().Where(s => s.CompanyId == company.Id).Select(s => (long?)s.BookingsRevision).FirstOrDefaultAsync(ct) ?? 0;
        if (sinceRevision is { } since && since == revision)
            return new StaysBoardDto(false, revision, now, today, null, null, null, null, null);

        var from = fromParam ?? today;
        var days = Math.Clamp(daysParam ?? DefaultDays, MinDays, MaxDays);
        var to = from.AddDays(days);

        var occ = await db.HouseOccupancies.AsNoTracking()
            .Where(o => o.CompanyId == company.Id && o.ReleasedAtUtc == null && o.StartDate < to && o.EndDate > from).ToListAsync(ct);
        var bookingIds = occ.Where(o => o.StayBookingId != null).Select(o => o.StayBookingId!.Value).ToList();
        var blockIds = occ.Where(o => o.HouseBlockId != null).Select(o => o.HouseBlockId!.Value).ToList();
        var bookings = await db.StayBookings.AsNoTracking().Where(b => bookingIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var blocks = await db.HouseBlocks.AsNoTracking().Where(b => blockIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

        var items = new List<BoardItemDto>();
        foreach (var o in occ.OrderBy(o => o.StartDate))
        {
            if (o.StayBookingId is { } bid && bookings.TryGetValue(bid, out var b))
            {
                if (b.Status == StayBookingStatus.Held && b.HoldExpiresAtUtc <= now) continue;
                var state = b.Status switch
                {
                    StayBookingStatus.Held => BoardItemState.Held,
                    StayBookingStatus.AwaitingPaymentCheck => BoardItemState.AwaitingPaymentCheck,
                    _ => BoardItemState.Confirmed
                };
                items.Add(new BoardItemDto(BoardItemKind.Booking, b.Id, o.HouseId, o.StartDate, o.EndDate, state, StaysTexts.StatusText(b.Status.ToString()),
                    b.GuestName ?? "Гость", b.Status == StayBookingStatus.Held ? b.HoldExpiresAtUtc : null, null, b.Status == StayBookingStatus.AwaitingPaymentCheck));
            }
            else if (o.HouseBlockId is { } kid && blocks.TryGetValue(kid, out var block))
                items.Add(new BoardItemDto(BoardItemKind.Block, block.Id, o.HouseId, o.StartDate, o.EndDate, BoardItemState.Block, "Блокировка",
                    StaysTexts.BlockKindText(block.Kind), null, block.Kind, false, block.Comment));
            else if (o.Source == OccupancySource.ExternalCalendar)
                items.Add(new BoardItemDto(BoardItemKind.External, o.Id, o.HouseId, o.StartDate, o.EndDate, BoardItemState.External, "Внешний календарь", "Внешний календарь", null, null, false));
        }

        var withBookings = items.Select(i => i.HouseId).ToHashSet();
        var houses = await db.Houses.AsNoTracking().Where(h => h.CompanyId == company.Id && (h.ArchivedAtUtc == null || withBookings.Contains(h.Id)))
            .OrderBy(h => h.ArchivedAtUtc != null).ThenBy(h => h.Position).ThenBy(h => h.Name)
            .Select(h => new BoardHouseDto(h.Id, h.Name, h.IsPublished, h.ArchivedAtUtc != null)).ToListAsync(ct);
        var awaiting = await db.StayBookings.AsNoTracking().CountAsync(b => b.CompanyId == company.Id && b.Status == StayBookingStatus.AwaitingPaymentCheck, ct)
            + await db.StayServiceOrders.AsNoTracking().CountAsync(o => o.CompanyId == company.Id && o.Status == StayBookingStatus.AwaitingPaymentCheck, ct);
        var (services, cells) = await ServicesAsync(company, from, to, now, ct);
        return new StaysBoardDto(true, revision, now, today, from, days, houses, items, awaiting, services, cells);
    }

    /// <summary>
    /// ARCHITECTURE_CYCLE39.md §39.10 — the group «Услуги»: one cell per (service, business date) with the active sessions; a session sits in the cell of the business date of its START. A hold that
    /// has already run out and a released session are not shown.
    /// </summary>
    private async Task<(List<BoardServiceDto> Services, List<BoardServiceCellDto> Cells)> ServicesAsync(Company company, DateOnly from, DateOnly to, DateTime now, CancellationToken ct)
    {
        var sessions = await db.StayServiceSessions.AsNoTracking()
            .Where(x => x.CompanyId == company.Id && x.ReleasedAtUtc == null && x.BusinessDate >= from && x.BusinessDate < to).ToListAsync(ct);
        var bookingIds = sessions.Where(x => x.StayBookingId != null).Select(x => x.StayBookingId!.Value).ToList();
        var orderIds = sessions.Where(x => x.StayServiceOrderId != null).Select(x => x.StayServiceOrderId!.Value).ToList();
        var bookings = await db.StayBookings.AsNoTracking().Where(b => bookingIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => (b.Status, b.HoldExpiresAtUtc), ct);
        var orders = await db.StayServiceOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => (o.Status, o.HoldExpiresAtUtc), ct);
        var live = new List<(StayServiceSession Session, StayBookingStatus Status)>();
        foreach (var x in sessions)
        {
            (StayBookingStatus Status, DateTime? Hold) parent;
            if (x.StayBookingId is { } bid && bookings.TryGetValue(bid, out var bk)) parent = bk;
            else if (x.StayServiceOrderId is { } oid && orders.TryGetValue(oid, out var ok)) parent = ok;
            else continue;
            if (parent.Status == StayBookingStatus.Held && parent.Hold <= now) continue;
            live.Add((x, parent.Status));
        }
        var cells = live.GroupBy(x => (x.Session.ServiceId, x.Session.BusinessDate)).OrderBy(g => g.Key.BusinessDate).Select(g =>
        {
            var first = g.OrderBy(x => x.Session.StartMinute).First().Session;
            var lastEnd = g.Max(x => x.Session.StartMinute + 60 * x.Session.Hours);
            return new BoardServiceCellDto(g.Key.ServiceId, g.Key.BusinessDate, g.Count(), "с " + ServiceTimeFormat.StartLabel(first.BusinessDate, first.StartMinute).Split(' ')[0],
                lastEnd > BusinessClock.MinutesPerDay ? "до " + ServiceTimeFormat.StaffMoment(first.BusinessDate, lastEnd).Split(' ')[0] : null,
                g.Any(x => x.Status == StayBookingStatus.AwaitingPaymentCheck));
        }).ToList();
        var withSessions = cells.Select(c => c.ServiceId).ToHashSet();
        var services = await db.StayServices.AsNoTracking().Where(sv => sv.CompanyId == company.Id && (sv.ArchivedAtUtc == null || withSessions.Contains(sv.Id)))
            .OrderBy(sv => sv.ArchivedAtUtc != null).ThenBy(sv => sv.Position).ThenBy(sv => sv.Name)
            .Select(sv => new BoardServiceDto(sv.Id, sv.Name, sv.IsPublished, sv.ArchivedAtUtc != null)).ToListAsync(ct);
        return (services, cells);
    }
}
