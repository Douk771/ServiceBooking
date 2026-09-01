using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController(
    AppDbContext db, UserManager<AppUser> userManager, SubscriptionResolver subscriptionResolver, IWebHostEnvironment env) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CompanyDto>>> GetAll()
    {
        var companies = await db.Companies.Where(c => c.IsActive).ToListAsync();
        var plans = await subscriptionResolver.GetEffectivePlansAsync(companies.Select(c => c.Id));

        // The public directory additionally requires both the owner's own opt-in (ShowInPublicListing)
        // and the tariff's AllowPublicListing — unlike GetMy/GetMemberOf/GetBySlug, which show the
        // company to people who already know about it regardless of directory placement.
        return Ok(companies
            .Where(c => c.ShowInPublicListing && plans[c.Id].AllowPublicListing)
            .Select(c => MapToDto(c, plans[c.Id])));
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<List<CompanyDto>>> GetMy()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var memberships = await db.CompanyMembers
            .Include(cm => cm.Company)
            .Where(cm => cm.UserId == userId && cm.Role == UserRole.CompanyOwner && cm.Company.IsActive)
            .ToListAsync();
        var plans = await subscriptionResolver.GetEffectivePlansAsync(memberships.Select(cm => cm.CompanyId));

        return Ok(memberships.Select(cm => MapToDto(cm.Company, plans[cm.CompanyId])));
    }

    // Returns all companies where the current user is a member (any role)
    [HttpGet("member")]
    [Authorize]
    public async Task<ActionResult<List<CompanyDto>>> GetMemberOf()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var memberships = await db.CompanyMembers
            .Include(cm => cm.Company)
            .Where(cm => cm.UserId == userId && cm.Company.IsActive)
            .ToListAsync();
        var plans = await subscriptionResolver.GetEffectivePlansAsync(memberships.Select(cm => cm.CompanyId));

        return Ok(memberships.Select(cm => MapToDto(cm.Company, plans[cm.CompanyId])));
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CompanyDto>> GetBySlug(string slug)
    {
        var c = await db.Companies.FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive);
        if (c is null) return NotFound();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(c.Id);
        return Ok(MapToDto(c, plan));
    }

    // Public: list masters for a company, optionally filtered by serviceId
    [HttpGet("{id:guid}/masters")]
    public async Task<ActionResult<List<MasterPublicDto>>> GetMasters(Guid id, [FromQuery] Guid? serviceId)
    {
        var memberQuery = db.CompanyMembers
            .Include(cm => cm.User)
            // A Client-role membership row exists for a company's own customers (e.g. anyone who books
            // there), never for staff — without this filter they'd show up in the public "book a
            // master" picker (audit Q6/US-12).
            .Where(cm => cm.CompanyId == id && cm.Company.IsActive &&
                (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner));

        if (serviceId.HasValue)
        {
            var masterIdsForService = await db.MasterServices
                .Where(ms => ms.ServiceId == serviceId.Value)
                .Select(ms => ms.MasterId)
                .ToListAsync();

            // If no service assignments exist for anyone, show all masters (fallback)
            if (masterIdsForService.Count > 0)
                memberQuery = memberQuery.Where(cm => masterIdsForService.Contains(cm.UserId));
        }

        var members = await memberQuery.ToListAsync();

        return Ok(members.Select(cm => new MasterPublicDto(
            cm.UserId, cm.User.FirstName, cm.User.LastName, cm.User.AvatarUrl, cm.Bio
        )).ToList());
    }

    [HttpGet("{id:guid}/members")]
    [Authorize]
    public async Task<ActionResult<List<MemberDto>>> GetMembers(Guid id)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var members = await db.CompanyMembers
            .Include(cm => cm.User)
            .Where(cm => cm.CompanyId == id)
            .ToListAsync();

        var userIds = members.Select(m => m.UserId).ToList();
        var masterServices = await db.MasterServices
            .Where(ms => userIds.Contains(ms.MasterId))
            .ToListAsync();

        var result = members.Select(cm => new MemberDto(
            cm.Id, cm.UserId, cm.User.FirstName, cm.User.LastName,
            cm.User.PhoneNumber ?? "", cm.User.Email, cm.User.AvatarUrl, cm.Role.ToString(), cm.Bio,
            masterServices.Where(ms => ms.MasterId == cm.UserId).Select(ms => ms.ServiceId).ToList(),
            cm.CommissionPercent
        )).ToList();

        return Ok(result);
    }

    [HttpPut("{id:guid}/members/{memberId:guid}/services")]
    [Authorize]
    public async Task<IActionResult> UpdateMemberServices(Guid id, Guid memberId, [FromBody] List<Guid> serviceIds)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var member = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.Id == memberId && cm.CompanyId == id);
        if (member is null) return NotFound();

        var requested = serviceIds.Distinct().ToList();

        // Remove existing services for this master that belong to this company
        var companyServiceIds = await db.Services
            .Where(s => s.CompanyId == id)
            .Select(s => s.Id)
            .ToListAsync();

        // Every requested id must belong to THIS company — checked before any write (US-16, audit B2)
        // so a bad id can't leave the link table half-updated. A stray Guid that matches nothing would
        // otherwise slip through Contains() checks below and either silently vanish or (for an id from
        // another company) attach a master to a service that isn't theirs to serve.
        if (requested.Any(sid => !companyServiceIds.Contains(sid)))
            return BadRequest("One or more services do not belong to this company.");

        var existing = await db.MasterServices
            .Where(ms => ms.MasterId == member.UserId && companyServiceIds.Contains(ms.ServiceId))
            .ToListAsync();

        db.MasterServices.RemoveRange(existing);

        foreach (var sid in requested)
            db.MasterServices.Add(new MasterService { Id = Guid.NewGuid(), MasterId = member.UserId, ServiceId = sid });

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<CompanyDto>> Create(CreateCompanyDto dto)
    {
        if (await db.Companies.AnyAsync(c => c.Slug == dto.Slug))
            return Conflict("Slug already taken");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Branch limit: the account plan caps how many companies this owner may create. Without a plan
        // (Free) that's 1 — so a brand-new owner can open their first company, but a second branch needs
        // a paid plan with MaxCompanies >= 2. Existing companies over a since-lowered limit are untouched.
        //
        // Serialize concurrent creates for this owner: without a lock, two simultaneous requests could
        // both count the same (pre-insert) number of companies, both pass the check, and both insert —
        // letting the owner end up over the limit the plan was supposed to enforce.
        await using var limitTransaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"owner-companies:{userId}");

        var plan = await subscriptionResolver.GetEffectivePlanForOwnerAsync(userId);
        if (plan.MaxCompanies.HasValue)
        {
            var ownedCount = await db.Companies.CountAsync(c => c.OwnerUserId == userId);
            if (ownedCount >= plan.MaxCompanies.Value)
                return StatusCode(402, "Company limit reached for the current tariff plan.");
        }

        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Slug = dto.Slug,
            Description = dto.Description,
            Address = dto.Address,
            Phone = dto.Phone,
            Email = dto.Email,
            AllowSelfBooking = dto.AllowSelfBooking,
            ShowInPublicListing = dto.ShowInPublicListing,
            OwnerUserId = userId
        };

        var member = new CompanyMember
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            UserId = userId,
            Role = UserRole.CompanyOwner
        };

        db.Companies.Add(company);
        db.CompanyMembers.Add(member);

        // Ensure user has CompanyOwner role
        var user = await userManager.FindByIdAsync(userId);
        if (user != null && !await userManager.IsInRoleAsync(user, "CompanyOwner"))
            await userManager.AddToRoleAsync(user, "CompanyOwner");

        await db.SaveChangesAsync();
        await limitTransaction.CommitAsync();

        // `plan` was already resolved above for the MaxCompanies check — reuse it so the response
        // reflects the owner's real plan (e.g. their existing paid plan when opening a 2nd+ branch),
        // instead of assuming Free.
        return CreatedAtAction(nameof(GetBySlug), new { slug = company.Slug }, MapToDto(company, plan));
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<CompanyDto>> Update(Guid id, UpdateCompanyDto dto)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        if (!await CanManageCompany(id)) return Forbid();

        if (dto.Name is not null) company.Name = dto.Name;
        if (dto.Description is not null) company.Description = dto.Description;
        if (dto.Address is not null) company.Address = dto.Address;
        if (dto.Phone is not null) company.Phone = dto.Phone;
        if (dto.Email is not null) company.Email = dto.Email;
        if (dto.AllowSelfBooking is not null) company.AllowSelfBooking = dto.AllowSelfBooking.Value;
        if (dto.RequirePrepayment is not null) company.RequirePrepayment = dto.RequirePrepayment.Value;
        if (dto.ShowInPublicListing is not null) company.ShowInPublicListing = dto.ShowInPublicListing.Value;

        await db.SaveChangesAsync();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        return Ok(MapToDto(company, plan));
    }

    // Uploads/replaces the company's logo image. Stored on local disk under wwwroot/uploads/companies
    // and served back via static files (see Program.cs UseStaticFiles) — there's no cloud storage
    // configured in this project. The extension is derived from the validated Content-Type, never from
    // the client-supplied filename, so this can't be used for path traversal or to serve an arbitrary
    // extension.
    [HttpPost("{id:guid}/logo")]
    [Authorize]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<CompanyDto>> UploadLogo(Guid id, IFormFile file)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        if (!await CanManageCompany(id)) return Forbid();

        if (file is null || file.Length == 0) return BadRequest("No file uploaded");
        if (file.Length > 5 * 1024 * 1024) return BadRequest("File too large (max 5MB)");

        var extension = file.ContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => null
        };
        if (extension is null) return BadRequest("Unsupported image type — use JPEG, PNG or WEBP");

        var uploadsDir = Path.Combine(env.ContentRootPath, "wwwroot", "uploads", "companies");
        Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Combine(uploadsDir, fileName);
        await using (var stream = new FileStream(filePath, FileMode.Create))
            await file.CopyToAsync(stream);

        // Clean up the previous logo file if it's one we stored ourselves (skip external URLs).
        if (company.LogoUrl is { } oldUrl && oldUrl.StartsWith("/uploads/companies/"))
        {
            var oldPath = Path.Combine(env.ContentRootPath, "wwwroot", oldUrl.TrimStart('/'));
            if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        }

        company.LogoUrl = $"/uploads/companies/{fileName}";
        await db.SaveChangesAsync();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        return Ok(MapToDto(company, plan));
    }

    [HttpPost("{id:guid}/members")]
    [Authorize]
    public async Task<ActionResult<MemberDto>> AddMember(Guid id, AddMemberDto dto)
    {
        if (!await CanManageCompany(id)) return Forbid();

        // Validate that the caller is allowed to assign the requested role
        if (!await CanAssignRole(dto.Role)) return Forbid();

        // SuperAdmin's CanAssignRole above accepts any string sight unseen — so a typo'd role name from
        // a SuperAdmin caller used to sail through and blow up Enum.Parse<UserRole> below with an
        // unhandled 500 (audit D3). Reject an unknown role name explicitly instead.
        if (!Enum.TryParse<UserRole>(dto.Role, out _)) return BadRequest("Unknown role");

        // Tariff seat limit: counts ALL members (the owner already occupies one seat), so a Free plan
        // (MaxEmployees = 1) leaves room for the owner only — no staff can be added until upgraded.
        // Only blocks adding NEW members once at/over the cap; existing members are never removed.
        //
        // Serialize concurrent adds for this company: without a lock, two simultaneous requests could
        // both count the same (pre-insert) number of members, both pass the check, and both insert —
        // letting the company end up over the seat limit the plan was supposed to enforce.
        await using var limitTransaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-members:{id}");

        var plan = await subscriptionResolver.GetEffectivePlanAsync(id);
        if (plan.MaxEmployees.HasValue)
        {
            var currentCount = await db.CompanyMembers.CountAsync(cm => cm.CompanyId == id);
            if (currentCount >= plan.MaxEmployees.Value)
                return StatusCode(402, "Employee limit reached for the current tariff plan.");
        }

        // Accounts are identified by phone (UserName == phone), so look the member up by phone.
        var user = await userManager.FindByNameAsync(dto.Phone);

        if (user is null)
        {
            // Auto-create by phone. Derived temporary password: "Sb" + last 6 digits of the phone,
            // right-padded to at least 8 chars — always contains an upper ('S'), a lower ('b') and
            // digits, satisfying the password policy. The owner must pass this on to the new master.
            var digits = new string(dto.Phone.Where(char.IsDigit).ToArray());
            var tail = digits.Length >= 6 ? digits[^6..] : digits;
            var pwd = $"Sb{tail}".PadRight(8, '0');
            user = new AppUser
            {
                UserName = dto.Phone,
                PhoneNumber = dto.Phone,
                PhoneNumberConfirmed = true, // added by the owner → treated as a verified number
                Email = dto.Email,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
            };
            var createResult = await userManager.CreateAsync(user, pwd);
            if (!createResult.Succeeded)
                return BadRequest(createResult.Errors.Select(e => e.Description));
        }

        var exists = await db.CompanyMembers.AnyAsync(cm => cm.CompanyId == id && cm.UserId == user.Id);
        if (exists) return Conflict("User is already a member");

        var role = Enum.Parse<UserRole>(dto.Role);
        var member = new CompanyMember
        {
            Id = Guid.NewGuid(),
            CompanyId = id,
            UserId = user.Id,
            Role = role,
            Bio = dto.Bio
        };

        db.CompanyMembers.Add(member);

        if (!await userManager.IsInRoleAsync(user, dto.Role))
            await userManager.AddToRoleAsync(user, dto.Role);

        await db.SaveChangesAsync();
        await limitTransaction.CommitAsync();

        // New members always start at 0 commission on this membership — same as before, just no longer
        // sourced from a value that could carry over from a different company (US-15).
        return Ok(new MemberDto(member.Id, user.Id, user.FirstName, user.LastName,
            user.PhoneNumber ?? "", user.Email, user.AvatarUrl, dto.Role, dto.Bio, [], member.CommissionPercent));
    }

    [HttpPut("{id:guid}/members/{memberId:guid}/commission")]
    [Authorize]
    public async Task<IActionResult> UpdateMemberCommission(Guid id, Guid memberId, [FromBody] UpdateMemberCommissionDto dto)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var member = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.Id == memberId && cm.CompanyId == id);
        if (member is null) return NotFound();

        member.CommissionPercent = Math.Clamp(dto.CommissionPercent, 0, 100);
        await db.SaveChangesAsync();

        return Ok(new { member.UserId, member.CommissionPercent });
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var member = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.Id == memberId && cm.CompanyId == id);
        if (member is null) return NotFound();

        db.CompanyMembers.Remove(member);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/stats")]
    [Authorize]
    public async Task<IActionResult> GetStats(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!await CanManageCompany(id)) return Forbid();

        if (from is null || to is null) return BadRequest("Both 'from' and 'to' are required.");
        if (to < from) return BadRequest("Invalid date range: 'to' must not be earlier than 'from'.");

        // The date of truth is the VISIT date (Booking.Date), not the date the booking row was created
        // (decision Q11) — a booking made on June 30th for a July 5th visit must land in the July report,
        // matching GET /api/reports/masters (ReportsController.cs), which already filters by Date. Before
        // this fix the two reports could disagree about which period a booking belonged to.
        var fromDate = DateOnly.FromDateTime(from.Value);
        var toDate = DateOnly.FromDateTime(to.Value);

        var bookings = await db.Bookings
            .Include(b => b.Service)
            .Include(b => b.Master)
            .Where(b => b.CompanyId == id && b.Date >= fromDate && b.Date <= toDate)
            .ToListAsync();

        var completed = bookings.Where(b => b.Status == BookingStatus.Completed).ToList();
        var cancelled = bookings.Where(b => b.Status == BookingStatus.Cancelled).ToList();

        var totalRevenue = completed.Sum(b => b.Price);

        // New clients: first VISIT in this company falls in [fromDate, toDate] — same date semantics as
        // the revenue filter above (ARCHITECTURE.md §14.3), so a single response never mixes "first
        // created" and "first visited" as two different meanings of "new".
        var allCompanyBookings = await db.Bookings
            .Where(b => b.CompanyId == id && b.ClientId != null)
            .GroupBy(b => b.ClientId!)
            .Select(g => new { ClientId = g.Key, FirstDate = g.Min(b => b.Date) })
            .ToListAsync();

        var newClientsCount = allCompanyBookings.Count(c => c.FirstDate >= fromDate && c.FirstDate <= toDate);

        var masterStats = completed
            .GroupBy(b => b.MasterId)
            .Select(g =>
            {
                var master = g.First().Master;
                return new
                {
                    masterId = g.Key,
                    masterName = master != null ? $"{master.FirstName} {master.LastName}".Trim() : g.Key,
                    bookingsCount = g.Count(),
                    revenue = g.Sum(b => b.Price)
                };
            }).ToList();

        var popularServices = bookings
            .GroupBy(b => b.ServiceId)
            .Select(g =>
            {
                var svc = g.First().Service;
                return new
                {
                    serviceId = g.Key,
                    serviceName = svc?.Name ?? g.Key.ToString(),
                    count = g.Count()
                };
            })
            .OrderByDescending(s => s.count)
            .ToList();

        var dailyRevenue = completed
            .GroupBy(b => b.Date)
            .Select(g => new
            {
                date = g.Key,
                revenue = g.Sum(b => b.Price)
            })
            .OrderBy(d => d.date)
            .ToList();

        return Ok(new
        {
            totalRevenue,
            bookingsCount = bookings.Count,
            completedCount = completed.Count,
            cancelledCount = cancelled.Count,
            newClientsCount,
            masterStats,
            popularServices,
            dailyRevenue
        });
    }

    private async Task<bool> CanManageCompany(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        if (User.IsInRole("SuperAdmin")) return true;

        return await db.CompanyMembers.AnyAsync(cm =>
            cm.CompanyId == companyId &&
            cm.UserId == userId &&
            cm.Role == UserRole.CompanyOwner);
    }

    // SuperAdmin can assign any role; CompanyOwner can assign Master or CompanyOwner only.
    private Task<bool> CanAssignRole(string role)
    {
        if (User.IsInRole("SuperAdmin")) return Task.FromResult(true);
        var allowed = new[] { "Master", "CompanyOwner" };
        return Task.FromResult(allowed.Contains(role));
    }

    // Single source of truth for building a CompanyDto from an entity + its resolved plan, so the
    // combined flags (OnlineBookingEnabled, PublicListingEnabled, PrepaymentEnabled) can't drift between
    // the five endpoints that return a CompanyDto.
    private static CompanyDto MapToDto(Company c, EffectivePlan plan) => new(
        c.Id, c.Name, c.Slug, c.Description, c.LogoUrl, c.Address, c.Phone, c.Email,
        c.AllowSelfBooking, c.RequirePrepayment,
        c.AllowSelfBooking && plan.AllowOnlineBooking,
        plan.AllowAnalytics,
        plan.AllowMailing,
        c.ShowInPublicListing,
        c.ShowInPublicListing && plan.AllowPublicListing,
        c.RequirePrepayment && plan.AllowOnlinePayment,
        plan.AllowOnlineBooking,
        plan.AllowOnlinePayment,
        plan.AllowPublicListing,
        plan.MaxEmployees);
}
