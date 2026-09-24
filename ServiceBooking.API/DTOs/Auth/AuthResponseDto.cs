namespace ServiceBooking.API.DTOs.Auth;

// ARCHITECTURE_CYCLE14.md §148.1, API_CONTRACT_CYCLE14.md §168: PhoneVerified is ADDITIVE. For Register
// it reflects whether the account was actually created WITH a confirmed mirror (false if no session was
// presented, or if presented but the per-MAX-account ceiling was hit at the last moment — §148.1 step 5,
// Р1: the account is still created either way). Login reuses the same DTO shape — its value there is
// simply the account's current mirror (AppUser.PhoneNumberConfirmed), which is exactly what it already
// means everywhere else this field appears (ProfileDto, MasterClientDto).
public record AuthResponseDto(
    string Token,
    string UserId,
    string Phone,
    string? Email,
    string FirstName,
    string LastName,
    IList<string> Roles,
    bool PhoneVerified
);
