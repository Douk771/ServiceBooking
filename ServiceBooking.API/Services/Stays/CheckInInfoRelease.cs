using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>ARCHITECTURE_CYCLE37.md §37.7.4 — when the check-in information becomes visible/sent. Pure.</summary>
public static class CheckInInfoRelease
{
    public static bool IsDue(
        StayBookingStatus status, bool hasText, bool alreadyReleased, string timeZoneId, DateTime nowUtc,
        DateOnly checkIn, TimeOnly sendTime, DateOnly checkOut, TimeOnly checkOutTime)
    {
        if (alreadyReleased || !hasText || status != StayBookingStatus.Confirmed) return false;
        return nowUtc >= StayTime.ToUtc(timeZoneId, checkIn, sendTime) && nowUtc < StayTime.ToUtc(timeZoneId, checkOut, checkOutTime);
    }
}
