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
        var awaiting = await db.StayBookings.AsNoTracking().CountAsync(b => b.CompanyId == company.Id && b.Status == StayBookingStatus.AwaitingPaymentCheck, ct);
        return new StaysBoardDto(true, revision, now, today, from, days, houses, items, awaiting);
    }
}
