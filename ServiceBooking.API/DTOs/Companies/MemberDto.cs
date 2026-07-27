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
    decimal CommissionPercent
);

public record AddMemberDto(
    string Phone,
    string FirstName,
    string LastName,
    string Role,
    string? Bio,
    string? Email
);

public record UpdateMemberCommissionDto(decimal CommissionPercent);
