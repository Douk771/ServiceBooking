using System.ComponentModel.DataAnnotations;

namespace ServiceBooking.API.DTOs.Auth;

public record LoginDto(
    [Required] string Phone,
    [Required] string Password
);
