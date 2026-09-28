using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>
/// Cycle 22 D10 — the one batch lookup of "when does this booking's visit start, in UTC" used by the
/// two dispatch passes (NotificationDispatchTask's §23.4 safety-net refresh and StaffPushDispatchTask's
/// §105.8 expiry journaling): the bookings' current date/start time and their companies' time zones, two
/// queries for the whole batch, never one per row. A booking or company that no longer exists is simply
/// absent from the result.
/// </summary>
public static class VisitStartResolver
{
    public static async Task<Dictionary<Guid, DateTime>> ResolveAsync(
        AppDbContext db, IReadOnlyCollection<Guid> bookingIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, DateTime>();
        if (bookingIds.Count == 0) return result;

        var bookings = await db.Bookings
            .Where(b => bookingIds.Contains(b.Id))
            .Select(b => new { b.Id, b.CompanyId, b.Date, b.StartTime })
            .ToListAsync(ct);
        if (bookings.Count == 0) return result;

        var companyIds = bookings.Select(b => b.CompanyId).Distinct().ToList();
        var timeZonesByCompany = await db.Companies
            .Where(c => companyIds.Contains(c.Id))
            .Select(c => new { c.Id, c.TimeZoneId })
            .ToDictionaryAsync(c => c.Id, c => c.TimeZoneId, ct);

        foreach (var booking in bookings)
        {
            if (!timeZonesByCompany.TryGetValue(booking.CompanyId, out var timeZoneId)) continue;
            result[booking.Id] = NotificationTiming.ComputeVisitStartUtc(booking.Date, booking.StartTime, timeZoneId);
        }
        return result;
    }
}
