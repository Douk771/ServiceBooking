using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public class ProfileController(
    UserManager<AppUser> userManager, AppDbContext db, ImageUploadService imageUploadService, FileStorage storage)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> Get()
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();
        var roles = await userManager.GetRolesAsync(user);
        return Ok(await MapToDtoAsync(user, roles));
    }

    // US-38, ARCHITECTURE.md §7.1/§7.2. Synchronous — one client's data is tens of rows/KB, not worth
    // the four new moving parts a background job would need (task, artifact storage, TTL, download
    // endpoint). Rate-limited to 3/day (policy "data-export") instead, and allow-listed in
    // LegalConsentFilter: the right to a copy of one's own data must not depend on having accepted a
    // new policy revision (ARCHITECTURE.md §19.4).
    //
    // Deliberately excludes: note TEXT (a staff member's judgment about the client, not the client's own
    // data in a clean sense — API_CONTRACT.md §8) and photo CONTENT (the private storage class's access
    // model is staff-of-company-only, SuperAdmin included gets 403 on the raw endpoint — an export that
    // handed the bytes to the subject directly would go around that model sideways). Only counts/sizes
    // are included for both. No foreign ids, no hashes (PasswordHash, SecurityStamp, ContentHash) appear
    // anywhere in the response — human-readable names stand in for every reference to another entity.
    [HttpGet("export")]
    [EnableRateLimiting("data-export")]
    public async Task<IActionResult> Export()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return NotFound();

        var consent = await db.UserConsents
            .Where(c => c.UserId == userId)
            .Select(c => new ExportConsentDto(c.DocumentType.ToString(), c.Version, c.AcceptedAtUtc))
            .ToListAsync();

        var memberships = await db.CompanyMembers
            .Include(cm => cm.Company)
            .Where(cm => cm.UserId == userId)
            .Select(cm => new ExportMembershipDto(cm.Company.Name, cm.Role.ToString(), cm.JoinedAt))
            .ToListAsync();

        var bookings = await db.Bookings
            .Include(b => b.Service).Include(b => b.Master).Include(b => b.Company)
            .Where(b => b.ClientId == userId)
            .OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime)
            .Select(b => new ExportBookingDto(
                b.Id, b.Date, b.StartTime, b.EndTime, b.Company.Name, b.Service.Name,
                b.Master.FirstName + " " + b.Master.LastName, b.Status.ToString(), b.PaymentStatus.ToString(),
                b.Price, b.CancellationReason))
            .ToListAsync();

        var reviews = await db.Reviews
            .Include(r => r.Company)
            .Where(r => r.ClientId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ExportReviewDto(r.Company.Name, r.Rating, r.Comment, r.CreatedAt))
            .ToListAsync();

        // Notes/photos "about me" are found the same way DeleteAccount and MastersController.GetClients
        // do: by ClientId for a registered client, or by canonical GuestPhone for visits made before
        // registering (a client can be both, if they booked as a guest before signing up).
        var canonicalPhone = user.PhoneNumber;
        var notesAboutMe = await db.ClientNotes
            .Include(n => n.Company).Include(n => n.Photos)
            .Where(n => n.ClientId == userId || (canonicalPhone != null && n.GuestPhone == canonicalPhone))
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new ExportNoteMetaDto(n.Company.Name, n.CreatedAt, n.Photos.Count))
            .ToListAsync();

        var photosOfMe = await db.ClientNotePhotos
            .Include(p => p.ClientNote).ThenInclude(n => n.Company)
            .Where(p => p.ClientNote.ClientId == userId || (canonicalPhone != null && p.ClientNote.GuestPhone == canonicalPhone))
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ExportPhotoMetaDto(p.ClientNote.Company.Name, p.CreatedAt, p.SizeBytes))
            .ToListAsync();

        var export = new ProfileExportDto(
            DateTime.UtcNow,
            new ExportProfileDto(user.FirstName, user.LastName, user.PhoneNumber, user.Email, user.AvatarUrl, user.CreatedAt),
            consent, memberships, bookings, reviews, notesAboutMe, photosOfMe,
            "В файл не входят: текст заметок сотрудников салона о вас и содержимое фотографий, " +
            "загруженных салоном. Эти материалы принадлежат салону как результат его работы; запросить " +
            "их можно у салона напрямую. По вопросам обработки ваших данных платформой обращайтесь в " +
            "поддержку сервиса.");

        Response.Headers.ContentDisposition =
            $"attachment; filename=\"servicebooking-export-{DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.json\"";
        return Ok(export);
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

        // US-26: the canonical form is what's stored, never what the user typed — see PhoneNormalizer.
        if (!PhoneNormalizer.TryNormalize(dto.NewPhone, out var canonicalPhone))
            return BadRequest("Phone number must contain 10 to 15 digits.");

        if (user.PhoneNumber != canonicalPhone)
        {
            user.PhoneNumber = canonicalPhone;
            var result = await userManager.SetUserNameAsync(user, canonicalPhone);
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

    // US-25: any authenticated user replaces their OWN avatar — the id comes from the token, there is
    // no route parameter, so a caller can't reach anyone else's avatar through this endpoint (403 is
    // unreachable here, per API_CONTRACT.md §7). Public storage class: the avatar is meant to be seen by
    // anonymous visitors browsing the storefront, so it's served by UseStaticFiles like the logo, not
    // through the private client-photo pipeline (ARCHITECTURE.md §3.1). Does NOT count against the
    // client-photo quota (US-24) — that quota is about privacy-sensitive customer photos, not about a
    // one-file-per-user profile picture (US-25 p.8).
    [HttpPost("avatar")]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<ProfileDto>> UploadAvatar(IFormFile? file)
    {
        var user = await userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (user is null) return NotFound();

        var validation = await imageUploadService.ReadAndProcessAsync(file, ImageProfile.Avatar);
        if (!validation.Success) return BadRequest(validation.ErrorMessage);

        // Order matters (ARCHITECTURE.md §1.4/§21.2, code review finding): write the new file, commit the
        // new URL, THEN delete the old file — never the other way round. Deleting the old file first
        // and having UpdateAsync fail afterwards would leave a live row pointing at a file that no
        // longer exists, which is the one state this pipeline must never produce; a leftover old file
        // is just an orphan the retention sweep would pick up, the acceptable failure mode.
        var oldUrl = user.AvatarUrl;
        var newUrl = await storage.SavePublicAsync(PublicArea.Avatars, validation.Image!.Bytes, validation.Image.Extension);
        user.AvatarUrl = newUrl;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            storage.DeletePublic(newUrl); // the update didn't persist — don't leave the new file orphaned either
            return BadRequest(updateResult.Errors.FirstOrDefault()?.Description);
        }

        storage.DeletePublic(oldUrl); // old file removed on replace (US-25 p.5), only after the swap committed

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
        new(u.Id, u.PhoneNumber ?? "", u.Email, u.FirstName, u.LastName, u.AvatarUrl, [.. roles],
            await GetPlanInfoAsync(u.Id, roles));
}

// CommissionPercent removed (US-22): commission became per-company (CompanyMember.CommissionPercent)
// in cycle A, so the account-level value here no longer means anything and the UI never showed it.
public record ProfileDto(string Id, string Phone, string? Email, string FirstName, string LastName, string? AvatarUrl,
    List<string> Roles, ProfilePlanDto? Plan);

public record ProfilePlanDto(
    string PlanName, decimal PricePerMonth, bool IsActive, DateTime? PaidUntil, bool IsExpired,
    bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics, int? MaxEmployees, int? MaxCompanies);

public record UpdateProfileDto(string FirstName, string LastName);
public record ChangePasswordDto(string CurrentPassword, string NewPassword);
public record ChangePhoneDto(string CurrentPassword, string NewPhone);

// US-38 export DTOs (API_CONTRACT.md §8) — deliberately their own shape, not a reuse of ProfileDto/
// BookingDto/etc.: the export is a legal artifact with its own contract (no ids of other people's
// entities, no hashes), and coupling it to DTOs used elsewhere would mean a change made for an
// unrelated screen could silently change what leaves the product in a data export.
public record ProfileExportDto(
    DateTime GeneratedAt, ExportProfileDto Profile, List<ExportConsentDto> Consent,
    List<ExportMembershipDto> Memberships, List<ExportBookingDto> Bookings, List<ExportReviewDto> Reviews,
    List<ExportNoteMetaDto> NotesAboutMe, List<ExportPhotoMetaDto> PhotosOfMe, string Notice);

public record ExportProfileDto(string FirstName, string LastName, string? Phone, string? Email, string? AvatarUrl, DateTime CreatedAt);
public record ExportConsentDto(string Document, string Version, DateTime AcceptedAt);
public record ExportMembershipDto(string Company, string Role, DateTime JoinedAt);

public record ExportBookingDto(
    Guid Id, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, string Company, string Service, string Master,
    string Status, string PaymentStatus, decimal Price, string? CancellationReason);

public record ExportReviewDto(string Company, int Rating, string? Comment, DateTime CreatedAt);
public record ExportNoteMetaDto(string Company, DateTime CreatedAt, int PhotoCount);
public record ExportPhotoMetaDto(string Company, DateTime CreatedAt, long SizeBytes);
