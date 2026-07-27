using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Bookings;

public record BookingDto(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
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
    string? Notes,
    // Guest fields (used when not authenticated)
    string? GuestName,
    string? GuestPhone,
    string? GuestEmail,
    string? CaptchaToken
);
