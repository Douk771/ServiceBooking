namespace ServiceBooking.API.DTOs.Companies;

public record CompanyDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string? Address,
    string? Phone,
    string? Email,
    bool AllowSelfBooking
);

public record CreateCompanyDto(
    string Name,
    string Slug,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    bool AllowSelfBooking = true
);

public record UpdateCompanyDto(
    string? Name,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    bool? AllowSelfBooking
);
