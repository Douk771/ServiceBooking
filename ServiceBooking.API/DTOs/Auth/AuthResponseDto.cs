namespace ServiceBooking.API.DTOs.Auth;

public record AuthResponseDto(
    string Token,
    string UserId,
    string Email,
    string FirstName,
    string LastName,
    IList<string> Roles
);
