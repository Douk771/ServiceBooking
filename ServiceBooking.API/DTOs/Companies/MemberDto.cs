namespace ServiceBooking.API.DTOs.Companies;

public record MemberDto(
    Guid Id,
    string UserId,
    string FirstName,
    string LastName,
    string Email,
    string? AvatarUrl,
    string Role,
    string? Bio,
    List<Guid> ServiceIds
);

public record AddMemberDto(
    string Email,
    string FirstName,
    string LastName,
    string Role,
    string? Bio
);
