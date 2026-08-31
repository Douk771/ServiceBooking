using System.ComponentModel.DataAnnotations;

namespace ServiceBooking.API.DTOs.Services;

public record ServiceDto(
    Guid Id,
    Guid CompanyId,
    string Name,
    string? Description,
    int DurationMinutes,
    decimal Price,
    string? ImageUrl
);

public record CreateServiceDto(
    Guid CompanyId,
    // Attributes go directly on the positional record parameter, NOT as `[property: ...]`: on this
    // runtime (net8.0, Microsoft.AspNetCore.App 8.0.3) ASP.NET Core's record model-binding throws
    // InvalidOperationException ("validation metadata must be associated with the constructor
    // parameter") if a validation attribute lands on the generated property instead — the opposite of
    // what ARCHITECTURE.md §3.5 assumed; corrected here after the functional test suite caught it.
    [MaxLength(200)] string Name,
    [MaxLength(2000)] string? Description,
    // 0 or negative would make the booking conflict-check interval degenerate (b.StartTime < slotEnd &&
    // b.EndTime > startTime never true), allowing unlimited overlapping bookings on the same slot
    // (audit E1) — SVC-015 pins this down.
    [Range(1, 1440)] int DurationMinutes,
    [Range(0, 1_000_000)] decimal Price,
    string? ImageUrl
);
