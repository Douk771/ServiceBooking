using System.ComponentModel.DataAnnotations;

namespace ServiceBooking.API.DTOs.Auth;

// Registration is phone-primary: the phone number is the account identifier (stored as UserName, which
// carries Identity's unique index). Email is optional. [EmailAddress] treats null as valid, so it only
// validates the format when an email is actually supplied.
public record RegisterDto(
    [Required] string FirstName,
    [Required] string LastName,
    [Required] string Phone,
    [Required, MinLength(8)] string Password,
    [EmailAddress] string? Email
);
