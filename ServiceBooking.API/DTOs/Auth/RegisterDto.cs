using System.ComponentModel.DataAnnotations;

namespace ServiceBooking.API.DTOs.Auth;

public record RegisterDto(
    [Required] string FirstName,
    [Required] string LastName,
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    string? Phone
);
