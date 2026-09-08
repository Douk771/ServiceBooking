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
    [EmailAddress] string? Email,
    // US-37 (BREAKING № 1, API_CONTRACT.md §5): must be true, not just present — a missing field
    // defaults to false the same way an explicit `false` does, since bool is non-nullable. Not
    // [Required]: [Required] on a non-nullable bool only rejects the type's default when the property
    // is annotated [property: Required], which record positional parameters can't use on this runtime
    // (see CreateBookingDto's note on the same constraint) — checked by hand in AuthController instead,
    // which also lets the response be the exact contract string rather than a generic ValidationProblem.
    bool AcceptedLegal = false
);
