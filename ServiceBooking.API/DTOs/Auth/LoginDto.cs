using System.ComponentModel.DataAnnotations;

namespace ServiceBooking.API.DTOs.Auth;

public record LoginDto(
    [Required, EmailAddress] string Email,
    [Required] string Password
);
