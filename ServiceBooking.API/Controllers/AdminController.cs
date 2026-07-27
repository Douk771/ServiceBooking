using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminController(AppDbContext db, UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager) : ControllerBase
{
    // ── Stats ──────────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    public async Task<ActionResult<AdminStatsDto>> GetStats()
    {
        var totalCompanies = await db.Companies.CountAsync();
        var totalUsers = await db.Users.CountAsync();
        var totalBookings = await db.Bookings.CountAsync();
        var completedBookings = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Completed);

        var revenueByService = await db.Bookings
            .Where(b => b.Status == BookingStatus.Completed)
            .GroupBy(b => 1)
            .Select(g => g.Sum(b => b.Price))
            .FirstOrDefaultAsync();

        return Ok(new AdminStatsDto(totalCompanies, totalUsers, totalBookings, completedBookings, revenueByService));
    }

    // ── Users ──────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    public async Task<ActionResult<List<AdminUserDto>>> GetUsers([FromQuery] string? search)
    {
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(u =>
                u.PhoneNumber!.Contains(search) ||
                u.Email!.Contains(search) ||
                u.FirstName.Contains(search) ||
                u.LastName.Contains(search));

        var users = await query.OrderBy(u => u.CreatedAt).ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();

        // Subscriptions are account-level (bound to the owner), so surface each user's plan and how many
        // companies they own here — this Users tab is where an admin manages the tariff, not per company.
        var subs = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .Where(s => userIds.Contains(s.OwnerUserId)).ToListAsync();
        var ownedCounts = await db.Companies
            .Where(c => userIds.Contains(c.OwnerUserId))
            .GroupBy(c => c.OwnerUserId)
            .Select(g => new { OwnerUserId = g.Key, Count = g.Count() })
            .ToListAsync();

        var result = new List<AdminUserDto>();
        foreach (var u in users)
        {
            var roles = await userManager.GetRolesAsync(u);
            var sub = subs.FirstOrDefault(s => s.OwnerUserId == u.Id);
            var ownedCount = ownedCounts.FirstOrDefault(x => x.OwnerUserId == u.Id)?.Count ?? 0;
            result.Add(new AdminUserDto(u.Id, u.PhoneNumber ?? "", u.Email, u.FirstName, u.LastName, u.AvatarUrl, u.CommissionPercent, u.CreatedAt,
                [.. roles], ownedCount, sub?.PlanConfigId, sub?.PlanConfig?.Name ?? "Free", sub?.PaidUntil, sub?.IsActive ?? true));
        }
        return Ok(result);
    }

    [HttpPut("users/{id}/roles")]
    public async Task<IActionResult> UpdateRoles(string id, [FromBody] List<string> roles)
    {
        var user = await userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        // Validate every role exists BEFORE touching the user's current roles — AddToRolesAsync
        // throws an unhandled InvalidOperationException for an unknown role name (not a graceful
        // IdentityResult failure), which previously meant the user could lose their existing roles
        // (removed first) and then get a 500 with nothing re-added.
        var unknown = new List<string>();
        foreach (var role in roles.Distinct())
            if (!await roleManager.RoleExistsAsync(role))
                unknown.Add(role);
        if (unknown.Count > 0)
            return BadRequest(new { message = $"Unknown role(s): {string.Join(", ", unknown)}" });

        var current = await userManager.GetRolesAsync(user);
        var removeResult = await userManager.RemoveFromRolesAsync(user, current);
        if (!removeResult.Succeeded)
            return BadRequest(removeResult.Errors.Select(e => e.Description));

        var addResult = await userManager.AddToRolesAsync(user, roles);
        if (!addResult.Succeeded)
            return BadRequest(addResult.Errors.Select(e => e.Description));

        return NoContent();
    }

    // ── Companies ──────────────────────────────────────────────────────────────

    [HttpGet("companies")]
    public async Task<ActionResult<List<AdminCompanyDto>>> GetCompanies([FromQuery] string? search)
    {
        var query = db.Companies
            .Include(c => c.Members)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search) || c.Email!.Contains(search));

        var companies = await query.OrderBy(c => c.CreatedAt).ToListAsync();
        var ids = companies.Select(c => c.Id).ToList();
        var ownerIds = companies.Select(c => c.OwnerUserId).Distinct().ToList();
        var subs = await db.AccountSubscriptions.Include(s => s.PlanConfig).Where(s => ownerIds.Contains(s.OwnerUserId)).ToListAsync();
        var ownerEmails = await db.Users.Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email ?? u.PhoneNumber ?? u.Id);
        var bookingCounts = await db.Bookings
            .Where(b => ids.Contains(b.CompanyId))
            .GroupBy(b => b.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToListAsync();

        var result = companies.Select(c =>
        {
            // The tariff is account-level: it belongs to the owner and covers all their companies.
            var sub = subs.FirstOrDefault(s => s.OwnerUserId == c.OwnerUserId);
            var count = bookingCounts.FirstOrDefault(x => x.CompanyId == c.Id)?.Count ?? 0;
            return new AdminCompanyDto(c.Id, c.Name, c.Slug, c.Email, c.Phone, c.IsActive, c.CreatedAt,
                c.Members.Count, count, c.OwnerUserId, ownerEmails.GetValueOrDefault(c.OwnerUserId, c.OwnerUserId),
                sub?.PlanConfigId, sub?.PlanConfig?.Name ?? "Free", sub?.PaidUntil, sub?.IsActive ?? true);
        }).ToList();

        return Ok(result);
    }

    [HttpPut("owners/{ownerUserId}/subscription")]
    public async Task<IActionResult> UpdateSubscription(string ownerUserId, [FromBody] UpdateSubscriptionDto dto)
    {
        // PaidUntil arrives from a plain <input type="date"> as a bare "2026-08-01" string, which
        // System.Text.Json deserializes into a DateTime with Kind=Unspecified. Npgsql requires
        // Kind=Utc for a "timestamp with time zone" column, so write it explicitly as UTC.
        var paidUntilUtc = dto.PaidUntil.HasValue && dto.PaidUntil.Value.Kind != DateTimeKind.Utc
            ? DateTime.SpecifyKind(dto.PaidUntil.Value, DateTimeKind.Utc)
            : dto.PaidUntil;

        var sub = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId);
        var oldPlanConfigId = sub?.PlanConfigId;
        var oldPaidUntil = sub?.PaidUntil;
        var oldIsActive = sub?.IsActive ?? true;

        if (sub is null)
        {
            sub = new AccountSubscription { Id = Guid.NewGuid(), OwnerUserId = ownerUserId };
            db.AccountSubscriptions.Add(sub);
        }
        sub.PlanConfigId = dto.PlanConfigId;
        sub.PaidUntil = paidUntilUtc;
        sub.IsActive = dto.IsActive;
        sub.UpdatedAt = DateTime.UtcNow;

        db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            OldPlanConfigId = oldPlanConfigId,
            NewPlanConfigId = dto.PlanConfigId,
            OldPaidUntil = oldPaidUntil,
            NewPaidUntil = paidUntilUtc,
            OldIsActive = oldIsActive,
            NewIsActive = dto.IsActive,
            Comment = dto.Comment,
        });

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("owners/{ownerUserId}/subscription-history")]
    public async Task<ActionResult<List<SubscriptionChangeLogDto>>> GetSubscriptionHistory(string ownerUserId)
    {
        var logs = await db.SubscriptionChangeLogs
            .Where(l => l.OwnerUserId == ownerUserId)
            .OrderByDescending(l => l.ChangedAt)
            .ToListAsync();

        var configIds = logs.SelectMany(l => new[] { l.OldPlanConfigId, l.NewPlanConfigId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var configNames = await db.SubscriptionPlanConfigs
            .Where(p => configIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name);

        var changedByIds = logs.Select(l => l.ChangedByUserId).Distinct().ToList();
        var changedByEmails = await db.Users
            .Where(u => changedByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.PhoneNumber ?? u.Id);

        return Ok(logs.Select(l => new SubscriptionChangeLogDto(
            l.Id, l.ChangedAt, changedByEmails.GetValueOrDefault(l.ChangedByUserId, l.ChangedByUserId),
            l.OldPlanConfigId.HasValue ? configNames.GetValueOrDefault(l.OldPlanConfigId.Value, "—") : "Free",
            l.NewPlanConfigId.HasValue ? configNames.GetValueOrDefault(l.NewPlanConfigId.Value, "—") : "Free",
            l.OldPaidUntil, l.NewPaidUntil, l.OldIsActive, l.NewIsActive, l.Comment)));
    }

    [HttpPut("companies/{id:guid}")]
    public async Task<IActionResult> UpdateCompany(Guid id, [FromBody] AdminUpdateCompanyDto dto)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        company.Name = dto.Name;
        company.IsActive = dto.IsActive;
        company.AllowSelfBooking = dto.AllowSelfBooking;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Reassigns the primary owner of a company to an existing user. Company.OwnerUserId drives billing
    // attribution (which account's subscription/tariff governs this company — see SubscriptionResolver)
    // and the MaxCompanies count; it is NOT what grants management access (that's CompanyMembers, see
    // CompaniesController.CanManageCompany). So the new owner is also given/promoted to a CompanyOwner
    // membership row here, otherwise changing OwnerUserId alone would silently leave them unable to
    // manage the company they were just made the owner of. The previous owner's membership (if any) is
    // left untouched — this is a reassignment of billing ownership, not a removal of the old owner's
    // access, which the caller can still revoke separately via DELETE .../members/{memberId}.
    [HttpPut("companies/{id:guid}/owner")]
    public async Task<IActionResult> UpdateCompanyOwner(Guid id, [FromBody] UpdateCompanyOwnerDto dto)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();

        var newOwner = await userManager.FindByIdAsync(dto.NewOwnerUserId);
        if (newOwner is null) return BadRequest("User not found");

        company.OwnerUserId = newOwner.Id;

        var membership = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.CompanyId == id && cm.UserId == newOwner.Id);
        if (membership is null)
            db.CompanyMembers.Add(new CompanyMember { Id = Guid.NewGuid(), CompanyId = id, UserId = newOwner.Id, Role = UserRole.CompanyOwner });
        else
            membership.Role = UserRole.CompanyOwner;

        if (!await userManager.IsInRoleAsync(newOwner, "CompanyOwner"))
            await userManager.AddToRoleAsync(newOwner, "CompanyOwner");

        await db.SaveChangesAsync();
        return NoContent();
    }

    // ── Bookings ───────────────────────────────────────────────────────────────

    [HttpGet("bookings")]
    public async Task<ActionResult<List<AdminBookingDto>>> GetBookings(
        [FromQuery] Guid? companyId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] BookingStatus? status)
    {
        var query = db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Include(b => b.Client)
            .Include(b => b.Company)
            .AsQueryable();

        if (companyId.HasValue) query = query.Where(b => b.CompanyId == companyId);
        if (from.HasValue) query = query.Where(b => b.Date >= from);
        if (to.HasValue) query = query.Where(b => b.Date <= to);
        if (status.HasValue) query = query.Where(b => b.Status == status);

        var bookings = await query.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime).Take(500).ToListAsync();

        return Ok(bookings.Select(b => new AdminBookingDto(
            b.Id, b.Company.Name, b.Service.Name,
            $"{b.Master.FirstName} {b.Master.LastName}",
            b.Client is not null ? $"{b.Client.FirstName} {b.Client.LastName}" : b.GuestName ?? "Гость",
            b.GuestPhone ?? b.Client?.PhoneNumber,
            b.Date, b.StartTime, b.EndTime, b.Status, b.Price)));
    }
    // ── Subscription Plan Configs ──────────────────────────────────────────────

    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await db.SubscriptionPlanConfigs.OrderBy(p => p.PricePerMonth).ToListAsync();
        return Ok(plans);
    }

    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] SubscriptionPlanConfig dto)
    {
        dto.Id = Guid.NewGuid();
        dto.CreatedAt = DateTime.UtcNow;
        db.SubscriptionPlanConfigs.Add(dto);
        await db.SaveChangesAsync();
        return Ok(dto);
    }

    [HttpPut("plans/{id:guid}")]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] SubscriptionPlanConfig dto)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();
        plan.Name = dto.Name;
        plan.PricePerMonth = dto.PricePerMonth;
        plan.MaxEmployees = dto.MaxEmployees;
        plan.MaxCompanies = dto.MaxCompanies;
        plan.AllowOnlineBooking = dto.AllowOnlineBooking;
        plan.AllowMailing = dto.AllowMailing;
        plan.AllowAnalytics = dto.AllowAnalytics;
        plan.AllowPublicListing = dto.AllowPublicListing;
        plan.AllowOnlinePayment = dto.AllowOnlinePayment;
        plan.Description = dto.Description;
        plan.IsActive = dto.IsActive;
        plan.NotifyDaysBefore = dto.NotifyDaysBefore;
        await db.SaveChangesAsync();
        return Ok(plan);
    }

    [HttpDelete("plans/{id:guid}")]
    public async Task<IActionResult> DeletePlan(Guid id)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();
        plan.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

public record AdminStatsDto(int TotalCompanies, int TotalUsers, int TotalBookings, int CompletedBookings, decimal TotalRevenue);

public record AdminUserDto(string Id, string Phone, string? Email, string FirstName, string LastName, string? AvatarUrl, decimal CommissionPercent, DateTime CreatedAt,
    List<string> Roles, int OwnedCompanyCount, Guid? PlanConfigId, string PlanName, DateTime? PaidUntil, bool SubscriptionActive);

public record AdminCompanyDto(Guid Id, string Name, string Slug, string? Email, string? Phone, bool IsActive, DateTime CreatedAt,
    int MemberCount, int BookingCount, string OwnerUserId, string OwnerEmail,
    Guid? PlanConfigId, string PlanName, DateTime? PaidUntil, bool SubscriptionActive);

public record UpdateSubscriptionDto(Guid? PlanConfigId, DateTime? PaidUntil, bool IsActive, string? Comment);

public record SubscriptionChangeLogDto(Guid Id, DateTime ChangedAt, string ChangedByEmail,
    string OldPlanName, string NewPlanName, DateTime? OldPaidUntil, DateTime? NewPaidUntil,
    bool OldIsActive, bool NewIsActive, string? Comment);

public record AdminUpdateCompanyDto(string Name, bool IsActive, bool AllowSelfBooking);
public record UpdateCompanyOwnerDto(string NewOwnerUserId);

public record AdminBookingDto(Guid Id, string CompanyName, string ServiceName, string MasterName, string ClientName,
    string? ClientPhone, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, BookingStatus Status, decimal Price);
