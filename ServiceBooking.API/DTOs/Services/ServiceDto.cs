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
    string Name,
    string? Description,
    int DurationMinutes,
    decimal Price,
    string? ImageUrl
);
