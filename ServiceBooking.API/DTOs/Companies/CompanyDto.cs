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

// Public-facing master info for the booking flow
public record MasterPublicDto(
    string UserId,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    string? Bio
);

public record UpdateCompanyDto(
    string? Name,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    bool? AllowSelfBooking
);
