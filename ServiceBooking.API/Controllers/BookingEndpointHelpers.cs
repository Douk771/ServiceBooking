using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Bookings;
using ServiceBooking.API.DTOs.Notifications;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Bookings;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §385): the private helpers the former <c>BookingsController</c>
/// shared between what are now <see cref="BookingsController"/>, <see cref="BookingAvailabilityController"/>
/// and <see cref="Services.Bookings.BookingCreationService"/> — moved here verbatim rather than duplicated.
/// </summary>
internal static class BookingEndpointHelpers
{
    // The one rule about *when* a booking may sit, shared by Create and Reschedule so the two can't
    // drift apart: not in the past, and not wrapping past midnight (TimeOnly can't represent 24:00, so
    // an overflowing slot would otherwise silently produce EndTime < StartTime). This applies to every
    // path including staff manual bookings — Q7 relaxes working hours, not the past. UTC is the
    // project-wide reference until timezones land; see ARCHITECTURE.md §2.5.
    internal static bool IsBookableMoment(DateOnly date, TimeOnly startTime, int durationMinutes)
    {
        var nowUtc = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(nowUtc);
        var nowTime = TimeOnly.FromDateTime(nowUtc);

        var isInThePast = date < today || (date == today && startTime < nowTime);
        var overflowsIntoNextDay =
            startTime.ToTimeSpan() + TimeSpan.FromMinutes(durationMinutes) >= TimeSpan.FromDays(1);

        return !isInThePast && !overflowsIntoNextDay;
    }

    // Cycle 22 D6: the assigned master, or whoever may manage the booking's company (SuperAdmin or
    // its CompanyOwner — CompanyAccess, the one shared rule; the caller's own id is the claim userId).
    internal static async Task<bool> CanManageBookingAsync(AppDbContext db, ClaimsPrincipal user, Booking booking, string userId) =>
        booking.MasterId == userId || await CompanyAccess.CanManageCompanyAsync(db, user, booking.CompanyId);

    internal static BookingDto MapToDto(Booking b, Service s, AppUser master, string clientName,
        ReminderStatusDto? reminderStatus = null, int? historyEventCount = null,
        bool? clientRescheduleAllowed = null, int? clientRescheduleMinHours = null, int? companyBookingHorizonDays = null,
        bool? clientCancelAllowed = null)
    {
        // US-67 (API_CONTRACT_CYCLE6.md §43.2): `services` is built from BookingServices when loaded
        // (every path except the in-memory object returned by Create, which sets it explicitly before
        // calling here); falls back to the single legacy service `s` only if BookingServices wasn't
        // populated at all, which should never happen after the AddBookingServices backfill.
        var items = b.BookingServices is { Count: > 0 }
            ? b.BookingServices.OrderBy(bs => bs.Position)
                .Select(bs => new BookingServiceItemDto(bs.ServiceId, bs.NameSnapshot, bs.DurationMinutes, bs.Price))
                .ToList()
            : [new BookingServiceItemDto(b.ServiceId, s.Name, s.DurationMinutes, b.Price)];
        var totalDurationMinutes = items.Sum(i => i.DurationMinutes);

        return new(b.Id, b.CompanyId, b.Company?.Name ?? "", b.Company?.Slug ?? "", b.ServiceId, items[0].Name, b.MasterId,
            $"{master.FirstName} {master.LastName}", b.ClientId, clientName,
            b.GuestPhone ?? b.Client?.PhoneNumber, b.GuestEmail ?? b.Client?.Email,
            b.Date, b.StartTime, b.EndTime, b.Status, b.PaymentStatus, b.Price, b.CancellationReason,
            b.Notes, b.CreatedAt,
            b.ConsentPrivacyVersion, b.ConsentTermsVersion, b.ConsentAcceptedAtUtc, b.ClientDeleted, reminderStatus,
            totalDurationMinutes, items,
            b.BookingNoticeVersion, b.BookedForOther, b.GuardianConfirmedAtUtc, historyEventCount,
            clientRescheduleAllowed, clientRescheduleMinHours, companyBookingHorizonDays, clientCancelAllowed);
    }
}
