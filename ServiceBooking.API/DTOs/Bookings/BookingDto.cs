using System.ComponentModel.DataAnnotations;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Bookings;

public record BookingDto(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    // US-06 + Q12: lets the client cabinet build a "book again" link without a second lookup.
    string CompanySlug,
    Guid ServiceId,
    string ServiceName,
    string MasterId,
    string MasterName,
    string? ClientId,
    string ClientName,
    string? ClientPhone,
    string? ClientEmail,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    BookingStatus Status,
    PaymentStatus PaymentStatus,
    // US-06 + Q12: a snapshot of Service.Price at booking time (already on the entity — no migration).
    decimal Price,
    // US-06: null unless the booking was cancelled WITH a reason.
    string? CancellationReason,
    string? Notes,
    DateTime CreatedAt
);

public record OccupiedRangeDto(TimeOnly Start, TimeOnly End);

public record RescheduleDto(DateOnly Date, TimeOnly StartTime);

public record CreateBookingDto(
    Guid CompanyId,
    Guid ServiceId,
    string MasterId,
    DateOnly Date,
    TimeOnly StartTime,
    // Attributes go directly on the positional record parameter, not as `[property: ...]` — see
    // ServiceDto.cs for why: on this runtime (net8.0, Microsoft.AspNetCore.App 8.0.3) the `[property:
    // ...]` form throws InvalidOperationException out of ASP.NET Core's record model-binding.
    [MaxLength(2000)] string? Notes,
    // Guest fields (used when not authenticated)
    [MaxLength(200)] string? GuestName,
    [MaxLength(32)] string? GuestPhone,
    [MaxLength(256)] string? GuestEmail,
    string? CaptchaToken
);
