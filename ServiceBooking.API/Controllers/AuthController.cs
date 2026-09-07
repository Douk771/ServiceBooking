using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using ServiceBooking.API.DTOs.Auth;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    TokenService tokenService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        // US-26: the account is identified by the CANONICAL phone — otherwise "8 999..." and
        // "+7 999..." would register as two different accounts (the exact problem this history fixes).
        if (!PhoneNormalizer.TryNormalize(dto.Phone, out var canonicalPhone))
            return BadRequest("Phone number must contain 10 to 15 digits.");

        // Phone is the account identifier: it goes into UserName (which has Identity's unique index),
        // giving phone uniqueness for free. Email is optional.
        var user = new AppUser
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            UserName = canonicalPhone,
            PhoneNumber = canonicalPhone
        };

        var result = await userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        await userManager.AddToRoleAsync(user, "Client");
        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.GenerateToken(user, roles);

        return Ok(new AuthResponseDto(token, user.Id, user.PhoneNumber!, user.Email, user.FirstName, user.LastName, roles));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        // Same canonical form the account was created/normalized to — this is what lets someone who
        // registered as "8 999..." log in typing "+7 999..." (US-26, AUTH-0xx). An invalid-looking
        // number simply won't match anything and falls through to the same 401 as any other bad login —
        // no separate 400 here, to avoid leaking whether a number "looks right" to an attacker probing
        // logins.
        var canonicalPhone = PhoneNormalizer.Normalize(dto.Phone);
        var user = await userManager.FindByNameAsync(canonicalPhone);
        if (user is null)
            return Unauthorized("Invalid credentials");

        var result = await signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized("Invalid credentials");

        var roles = await userManager.GetRolesAsync(user);
        var token = tokenService.GenerateToken(user, roles);

        return Ok(new AuthResponseDto(token, user.Id, user.PhoneNumber!, user.Email, user.FirstName, user.LastName, roles));
    }
}
