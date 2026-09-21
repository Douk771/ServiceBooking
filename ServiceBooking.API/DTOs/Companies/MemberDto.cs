namespace ServiceBooking.API.DTOs.Companies;

public record MemberDto(
    Guid Id,
    string UserId,
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    string? AvatarUrl,
    string Role,
    string? Bio,
    List<Guid> ServiceIds,
    decimal CommissionPercent,
    bool ProvidesServices
);

// US-62 (ARCHITECTURE_CYCLE6.md §40.3): confirm is required only when turning the flag off AND the
// member has future bookings — turning it on never needs confirmation.
public record ProvidesServicesDto(bool ProvidesServices, bool Confirm);

public record AddMemberDto(
    string Phone,
    string FirstName,
    string LastName,
    string Role,
    string? Bio,
    string? Email
);

public record UpdateMemberCommissionDto(decimal CommissionPercent);
