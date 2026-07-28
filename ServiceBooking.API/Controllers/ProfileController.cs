using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController(UserManager<AppUser> userManager, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get()
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();
        var roles = await userManager.GetRolesAsync(user);
        return Ok(await MapToDtoAsync(user, roles));
    }

    [HttpPut]
    public async Task<ActionResult<ProfileDto>> Update([FromBody] UpdateProfileDto dto)
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(result.Errors.FirstOrDefault()?.Description);

        var roles = await userManager.GetRolesAsync(user);
        return Ok(await MapToDtoAsync(user, roles));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        var result = await userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded) return BadRequest(result.Errors.FirstOrDefault()?.Description ?? "Неверный текущий пароль");
        return NoContent();
    }

    // Phone doubles as the Identity UserName (see AuthController), so changing it goes through
    // SetUserNameAsync — that's what actually enforces the uniqueness check and persists both
    // PhoneNumber and UserName/NormalizedUserName in one write. Requires the current password,
    // same as changing the password, since it's effectively changing the login identifier.
    [HttpPost("change-phone")]
    public async Task<ActionResult<ProfileDto>> ChangePhone([FromBody] ChangePhoneDto dto)
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        if (!await userManager.CheckPasswordAsync(user, dto.CurrentPassword))
            return BadRequest("Неверный текущий пароль");

        if (user.PhoneNumber != dto.NewPhone)
        {
            user.PhoneNumber = dto.NewPhone;
            var result = await userManager.SetUserNameAsync(user, dto.NewPhone);
            if (!result.Succeeded)
            {
                var isDuplicate = result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName));
                return BadRequest(isDuplicate
                    ? "Этот номер телефона уже используется другим аккаунтом"
                    : result.Errors.FirstOrDefault()?.Description ?? "Не удалось изменить номер телефона");
            }
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(await MapToDtoAsync(user, roles));
    }

    // Plan info is only meaningful for company owners — subscriptions are bound to the owner account
    // (see SubscriptionResolver). Shows the actual subscription row (including an expired/inactive one)
    // rather than the normalized "Free" fallback, so the owner can see WHY they're on the Free baseline.
    private async Task<ProfilePlanDto?> GetPlanInfoAsync(string userId, IList<string> roles)
    {
        if (!roles.Contains("CompanyOwner")) return null;

        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .FirstOrDefaultAsync(s => s.OwnerUserId == userId);

        if (sub is null || sub.PlanConfig is null)
            return new ProfilePlanDto("Free", 0, true, null, false, false, false, false, MaxEmployees: 1, MaxCompanies: 1);

        var isExpired = sub.PaidUntil.HasValue && sub.PaidUntil.Value < DateTime.UtcNow;
        return new ProfilePlanDto(
            sub.PlanConfig.Name, sub.PlanConfig.PricePerMonth, sub.IsActive, sub.PaidUntil, isExpired,
            sub.PlanConfig.AllowOnlineBooking, sub.PlanConfig.AllowMailing, sub.PlanConfig.AllowAnalytics,
            sub.PlanConfig.MaxEmployees, sub.PlanConfig.MaxCompanies);
    }

    private async Task<ProfileDto> MapToDtoAsync(AppUser u, IList<string> roles) =>
        new(u.Id, u.PhoneNumber ?? "", u.Email, u.FirstName, u.LastName, u.AvatarUrl, u.CommissionPercent, [.. roles],
            await GetPlanInfoAsync(u.Id, roles));
}

public record ProfileDto(string Id, string Phone, string? Email, string FirstName, string LastName, string? AvatarUrl,
    decimal CommissionPercent, List<string> Roles, ProfilePlanDto? Plan);

public record ProfilePlanDto(
    string PlanName, decimal PricePerMonth, bool IsActive, DateTime? PaidUntil, bool IsExpired,
    bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics, int? MaxEmployees, int? MaxCompanies);

public record UpdateProfileDto(string FirstName, string LastName);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
public record ChangePhoneDto(string CurrentPassword, string NewPhone);
