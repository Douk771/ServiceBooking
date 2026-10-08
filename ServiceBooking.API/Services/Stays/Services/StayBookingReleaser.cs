using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.5.3, A39-14 — the SINGLE point where a booking that reached a final status frees what it holds: first the nights (through the one writer of
/// nights — cycle 40 hangs the application of external occupancy here), then its sessions of services, in the same transaction, and one journal event when there were any.
/// A guard test makes sure <c>HouseOccupancyWriter.ReleaseBookingAsync</c> is called from here only.
/// </summary>
public class StayBookingReleaser(HouseOccupancyWriter occupancy, ServiceSessionWriter sessions, StayBookingEventLog eventLog)
{
    public async Task ReleaseAsync(StayBooking booking, DateTime nowUtc)
    {
        await occupancy.ReleaseBookingAsync(booking.Id, nowUtc);
        var released = await sessions.ReleaseForBookingAsync(booking.Id, nowUtc);
        if (released.Count == 0) return;
        await eventLog.AppendAsync(booking, StayBookingEventKind.ServiceSessionsReleased, StayActor.System, null, null,
            detailsJson: System.Text.Json.JsonSerializer.Serialize(new { sessionIds = released }));
    }
}
