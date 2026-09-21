using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    AppDbContext db, UserManager<AppUser> userManager, SubscriptionResolver subscriptionResolver,
    ImageUploadService imageUploadService, FileStorage storage) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CompanyDto>>> GetAll()
    {
        var companies = await db.Companies.Where(c => c.IsActive).ToListAsync();
        var plans = await subscriptionResolver.GetEffectivePlansAsync(companies.Select(c => c.Id));
        var ratings = await GetReviewAggregatesAsync(companies.Select(c => c.Id));
        var cities = await GetCitiesAsync(companies.Select(c => c.CityId));

        // The public directory additionally requires both the owner's own opt-in (ShowInPublicListing)
        // and the tariff's AllowPublicListing — unlike GetMy/GetMemberOf/GetBySlug, which show the
        // company to people who already know about it regardless of directory placement.
        return Ok(companies
            .Where(c => c.ShowInPublicListing && plans[c.Id].AllowPublicListing)
            .Select(c => MapToDto(c, plans[c.Id], ratings[c.Id].AverageRating, ratings[c.Id].ReviewCount,
                c.CityId.HasValue ? cities.GetValueOrDefault(c.CityId.Value) : null)));
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
        var ratings = await GetReviewAggregatesAsync(memberships.Select(cm => cm.CompanyId));
        var cities = await GetCitiesAsync(memberships.Select(cm => cm.Company.CityId));

        return Ok(memberships.Select(cm =>
            MapToDto(cm.Company, plans[cm.CompanyId], ratings[cm.CompanyId].AverageRating, ratings[cm.CompanyId].ReviewCount,
                cm.Company.CityId.HasValue ? cities.GetValueOrDefault(cm.Company.CityId.Value) : null)));
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
        var ratings = await GetReviewAggregatesAsync(memberships.Select(cm => cm.CompanyId));
        var cities = await GetCitiesAsync(memberships.Select(cm => cm.Company.CityId));

        return Ok(memberships.Select(cm =>
            MapToDto(cm.Company, plans[cm.CompanyId], ratings[cm.CompanyId].AverageRating, ratings[cm.CompanyId].ReviewCount,
                cm.Company.CityId.HasValue ? cities.GetValueOrDefault(cm.Company.CityId.Value) : null)));
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CompanyDto>> GetBySlug(string slug)
    {
        var c = await db.Companies.FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive);
        if (c is null) return NotFound();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(c.Id);
        // US-49 regression fix (QA cycle C): rating shown on the public company page must be a true
        // company-wide aggregate computed by the database, not derived from whatever page of reviews
        // GET /api/companies/{companyId}/reviews happens to have loaded (which visibly changed as the
        // caller paged through reviews — ARCHITECTURE.md §11.2/§21.5 pagination note).
        var (averageRating, reviewCount) = await GetReviewAggregateAsync(c.Id);
        var city = c.CityId.HasValue ? await db.Cities.FindAsync(c.CityId.Value) : null;
        return Ok(MapToDto(c, plan, averageRating, reviewCount, city));
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
                (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner) &&
                cm.ProvidesServices);

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
            cm.CommissionPercent,
            cm.ProvidesServices
        )).ToList();

        return Ok(result);
    }

    /// <summary>
    /// US-62 (ARCHITECTURE_CYCLE6.md §40.3): only this company's owner (or SuperAdmin) may flip the
    /// flag, and never for themselves via this endpoint's caller — a master can't hide themselves, and
    /// turning the flag off with future bookings requires an explicit confirm.
    /// </summary>
    [HttpPut("{id:guid}/members/{memberId:guid}/provides-services")]
    [Authorize]
    public async Task<IActionResult> UpdateProvidesServices(Guid id, Guid memberId, [FromBody] ProvidesServicesDto dto)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var member = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.Id == memberId && cm.CompanyId == id);
        if (member is null) return NotFound();

        if (!dto.ProvidesServices && !dto.Confirm)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var futureBookingsCount = await db.Bookings.CountAsync(b =>
                b.CompanyId == id && b.MasterId == member.UserId &&
                b.Date >= today && b.Status != BookingStatus.Cancelled);

            if (futureBookingsCount > 0)
            {
                return Conflict(
                    $"У специалиста {futureBookingsCount} будущие записи. Они останутся в силе и в расписании, " +
                    "но клиенты перестанут видеть его при записи. Повторите с подтверждением.");
            }
        }

        member.ProvidesServices = dto.ProvidesServices;
        await db.SaveChangesAsync();
        return NoContent();
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

        // Cycle 4, API_CONTRACT_CYCLE4.md §31.2 (breaking change): every new company needs a city, so
        // a derived time zone exists for reminder timing. Validated before touching the advisory lock
        // below — no point serializing on the owner-companies lock for a request that's going to 400.
        if (dto.CityId is null)
            return BadRequest("Укажите город салона");

        var city = await db.Cities.FindAsync(dto.CityId.Value);
        if (city is null || !city.IsActive)
            return BadRequest("Город не найден");

        if (!string.IsNullOrWhiteSpace(dto.TimeZoneId) &&
            !TimeZoneOffset.TryGetUtcOffsetMinutes(dto.TimeZoneId, DateTime.UtcNow, out _))
            return BadRequest("Неизвестный часовой пояс");

        var (timeZoneId, timeZoneIsManual) = CompanyTimeZoneResolver.ForNewCompany(city.TimeZoneId, dto.TimeZoneId);

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
            OwnerUserId = userId,
            CityId = city.Id,
            TimeZoneId = timeZoneId,
            TimeZoneIsManual = timeZoneIsManual
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

        await db.SaveChangesAsync();
        // US-46, ARCHITECTURE.md §8.3/§8.4: recompute Identity roles from the CompanyMember rows just
        // written, inside the same transaction and AFTER SaveChangesAsync — UserManager writes its own
        // AspNetUserRoles changes through the same AppDbContext, so this is the order that keeps both
        // writes in one commit instead of a separate round trip.
        await IdentityRoleSync.SyncAsync(db, userManager, userId);
        await limitTransaction.CommitAsync();

        // `plan` was already resolved above for the MaxCompanies check — reuse it so the response
        // reflects the owner's real plan (e.g. their existing paid plan when opening a 2nd+ branch),
        // instead of assuming Free.
        // A brand new company has no reviews yet — skip the query, (null, 0) is correct by construction.
        return CreatedAtAction(nameof(GetBySlug), new { slug = company.Slug }, MapToDto(company, plan, null, 0, city));
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

        // US-65/Q5 (ARCHITECTURE_CYCLE6.md §45.7): omitted/null leaves it untouched; 0 resets to
        // Default; anything else outside [Min, Max] is the one source of this 400.
        if (dto.BookingHorizonDays is not null)
        {
            if (!BookingHorizon.TryNormalize(dto.BookingHorizonDays, out var horizonDays))
                return BadRequest("Горизонт записи — от 1 до 365 дней");
            company.BookingHorizonDays = horizonDays;
        }

        // Cycle 4 (API_CONTRACT_CYCLE4.md §31.3, US-30 p.3): city and time zone. cityChanged tracks
        // whether THIS request moves CityId, since CompanyTimeZoneResolver.ForUpdate needs to know that
        // to decide whether a zone that isn't a manual override should follow the new city.
        var cityChanged = false;
        var city = company.CityId.HasValue ? await db.Cities.FindAsync(company.CityId.Value) : null;
        if (dto.CityId is not null && dto.CityId != company.CityId)
        {
            var newCity = await db.Cities.FindAsync(dto.CityId.Value);
            if (newCity is null || !newCity.IsActive)
                return BadRequest("Город не найден");

            company.CityId = newCity.Id;
            city = newCity;
            cityChanged = true;
        }

        if (dto.TimeZoneId is { IsSpecified: true, Value: { } requestedTimeZoneId } &&
            !TimeZoneOffset.TryGetUtcOffsetMinutes(requestedTimeZoneId, DateTime.UtcNow, out _))
            return BadRequest("Неизвестный часовой пояс");

        if (city is not null)
        {
            var (timeZoneId, timeZoneIsManual) = CompanyTimeZoneResolver.ForUpdate(
                effectiveCityTimeZoneId: city.TimeZoneId,
                cityChanged: cityChanged,
                timeZoneIdFieldProvided: dto.TimeZoneId.IsSpecified,
                requestedTimeZoneId: dto.TimeZoneId.Value,
                currentTimeZoneId: company.TimeZoneId,
                currentIsManual: company.TimeZoneIsManual);
            company.TimeZoneId = timeZoneId;
            company.TimeZoneIsManual = timeZoneIsManual;
        }

        await db.SaveChangesAsync();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        var (averageRating, reviewCount) = await GetReviewAggregateAsync(company.Id);
        return Ok(MapToDto(company, plan, averageRating, reviewCount, city));
    }

    // Uploads/replaces the company's logo. US-25 p.6: moved onto the same ImageUploadService every other
    // upload endpoint uses — this single change is what fixes both findings the previous cycle's review
    // left open (only Content-Type was checked, never the real bytes; no rate limit at all). Public
    // storage class (wwwroot/uploads/companies), served by UseStaticFiles like before — a logo is meant
    // to be visible to anyone browsing the storefront, so it does NOT go through the private client-photo
    // pipeline (see FileStorage's class doc for why the two are split).
    [HttpPost("{id:guid}/logo")]
    [Authorize]
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<CompanyDto>> UploadLogo(Guid id, IFormFile? file)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        if (!await CanManageCompany(id)) return Forbid();

        var validation = await imageUploadService.ReadAndProcessAsync(file, ImageProfile.CompanyLogo);
        if (!validation.Success) return BadRequest(validation.ErrorMessage);

        // Order matters (ARCHITECTURE.md §1.4/§21.2, code review finding): write the new file, commit the
        // new URL, THEN delete the old file. A failed SaveChangesAsync after deleting the old file first
        // would leave a live row pointing at nothing — the one state this pipeline must never produce.
        var oldUrl = company.LogoUrl;
        var newUrl = await storage.SavePublicAsync(PublicArea.Companies, validation.Image!.Bytes, validation.Image.Extension);
        company.LogoUrl = newUrl;
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            storage.DeletePublic(newUrl); // the update didn't persist — don't leave the new file orphaned either
            throw;
        }

        storage.DeletePublic(oldUrl); // old file removed on replace, same as before this cycle, just reordered

        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        var (averageRating, reviewCount) = await GetReviewAggregateAsync(company.Id);
        var city = company.CityId.HasValue ? await db.Cities.FindAsync(company.CityId.Value) : null;
        return Ok(MapToDto(company, plan, averageRating, reviewCount, city));
    }

    // US-24 p.4 / US-19 p.7: the only place a company's client-photo storage usage is exposed. NOT
    // folded into CompanyDto — that DTO is also returned by the public directory and by list endpoints,
    // where a per-company aggregate would either leak private usage data publicly or force an N+1 sum
    // over every company in a list (ARCHITECTURE.md §12.3). One call, one company, one screen.
    [HttpGet("{id:guid}/photo-usage")]
    [Authorize]
    public async Task<ActionResult<CompanyPhotoUsageDto>> GetPhotoUsage(Guid id)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();

        // Staff of THIS company, or SuperAdmin — this is the one place SuperAdmin gets numbers about
        // client photos without ever getting the content itself (decision Q5, ARCHITECTURE.md §12.3).
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAllowed = userId is not null &&
            (User.IsInRole("SuperAdmin") || await CompanyMembership.IsStaffAsync(db, id, userId));
        if (!isAllowed) return Forbid();

        var usedBytes = await db.ClientNotePhotos.Where(p => p.CompanyId == id).SumAsync(p => (long?)p.SizeBytes) ?? 0;
        var photoCount = await db.ClientNotePhotos.CountAsync(p => p.CompanyId == id);
        var plan = await subscriptionResolver.GetEffectivePlanAsync(id);

        double? percentUsed = plan.PhotoQuotaMb is { } quotaMb && quotaMb > 0
            ? Math.Round(usedBytes / (quotaMb * 1024.0 * 1024.0) * 100, 1)
            : null;

        return Ok(new CompanyPhotoUsageDto(id, usedBytes, photoCount, plan.PhotoQuotaMb, percentUsed, plan.PhotoRetention));
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

        // US-26: search AND auto-create both use the canonical form — otherwise adding a colleague by
        // "8 999..." would silently create a second account for someone already registered as
        // "+7 999...".
        // US-61/Q4 (§48.2 p.3): adding a staff member is a new-data entry point too.
        if (!PhoneNormalizer.TryNormalizeRussian(dto.Phone, out var canonicalPhone))
            return BadRequest("Введите номер телефона в формате +7 (900) 000-00-00");

        // Accounts are identified by phone (UserName == phone), so look the member up by phone.
        var user = await userManager.FindByNameAsync(canonicalPhone);

        if (user is null)
        {
            // Auto-create by phone. Derived temporary password: "Sb" + last 6 digits of the phone,
            // right-padded to at least 8 chars — always contains an upper ('S'), a lower ('b') and
            // digits, satisfying the password policy. The owner must pass this on to the new master.
            // Now derived from the CANONICAL phone (US-26 p.4 note): for a "+7 999..." input the result
            // is unchanged, but for "8 999..." it differs from before this cycle — called out in
            // CHANGELOG.md.
            var tail = canonicalPhone.Length >= 6 ? canonicalPhone[^6..] : canonicalPhone;
            var pwd = $"Sb{tail}".PadRight(8, '0');
            user = new AppUser
            {
                UserName = canonicalPhone,
                PhoneNumber = canonicalPhone,
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

        await db.SaveChangesAsync();
        // US-46: same "membership change → SaveChangesAsync → SyncAsync → commit" order as Create.
        await IdentityRoleSync.SyncAsync(db, userManager, user.Id);
        await limitTransaction.CommitAsync();

        // New members always start at 0 commission on this membership — same as before, just no longer
        // sourced from a value that could carry over from a different company (US-15).
        return Ok(new MemberDto(member.Id, user.Id, user.FirstName, user.LastName,
            user.PhoneNumber ?? "", user.Email, user.AvatarUrl, dto.Role, dto.Bio, [], member.CommissionPercent,
            member.ProvidesServices));
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

        var removedUserId = member.UserId;

        // US-46 p.5, ARCHITECTURE.md §8.4: this is the point that used to leave a stale Identity role
        // behind entirely (removing a member never revoked Master/CompanyOwner) — the lock is the same
        // one AddMember already takes for the seat-limit check, extended here for the first time to
        // RemoveMember.
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-members:{id}");

        db.CompanyMembers.Remove(member);
        await db.SaveChangesAsync();
        await IdentityRoleSync.SyncAsync(db, userManager, removedUserId);
        await transaction.CommitAsync();

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

        return await CompanyMembership.IsOwnerAsync(db, companyId, userId);
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
    // the seven call sites that return a CompanyDto. `city` is the resolved City row for c.CityId, or
    // null when CityId is null (pre-cycle-4 edge case, §31.4's doc comment) — resolved by the caller,
    // batched via GetCitiesAsync for the three list endpoints, so this stays a pure mapping function.
    private static CompanyDto MapToDto(Company c, EffectivePlan plan, double? averageRating, int reviewCount, City? city)
    {
        TimeZoneOffset.TryGetUtcOffsetMinutes(c.TimeZoneId, DateTime.UtcNow, out var utcOffsetMinutes);
        return new(
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
            plan.MaxEmployees,
            averageRating,
            reviewCount,
            c.CityId, city?.Name, city?.Region, c.TimeZoneId, c.TimeZoneIsManual, utcOffsetMinutes,
            BookingHorizon.Normalize(c.BookingHorizonDays));
    }

    // Cycle 4: batched City lookup for the three list endpoints (GetAll, GetMy, GetMemberOf) — same
    // pattern as GetReviewAggregatesAsync, one round trip instead of one query per company.
    private async Task<Dictionary<int, City>> GetCitiesAsync(IEnumerable<int?> cityIds)
    {
        var ids = cityIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, City>();
        var cities = await db.Cities.Where(c => ids.Contains(c.Id)).ToListAsync();
        return cities.ToDictionary(c => c.Id);
    }

    // QA cycle C regression fix: the public company page showed an average rating computed from
    // whatever single page of reviews GET /api/companies/{companyId}/reviews had loaded — it visibly
    // changed as the caller paged through reviews. The fix is a true company-wide aggregate, computed
    // by PostgreSQL (AVG/COUNT), not assembled in memory from a bounded page. Single-company variant
    // used by the three endpoints that build/mutate one company at a time.
    private async Task<(double? AverageRating, int ReviewCount)> GetReviewAggregateAsync(Guid companyId)
    {
        var aggregate = await db.Reviews
            .Where(r => r.CompanyId == companyId)
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync();

        return aggregate is null ? (null, 0) : (aggregate.Average, aggregate.Count);
    }

    // Batched variant for the three list endpoints (GetAll, GetMy, GetMemberOf) — one GROUP BY query for
    // the whole page/list instead of one query per company, same pattern as
    // SubscriptionResolver.GetEffectivePlansAsync. Companies with zero reviews (not present in the
    // GROUP BY result) are filled in as (null, 0) so callers can safely index the dictionary by every id
    // they asked for.
    private async Task<Dictionary<Guid, (double? AverageRating, int ReviewCount)>> GetReviewAggregatesAsync(
        IEnumerable<Guid> companyIds)
    {
        var ids = companyIds.ToList();
        var aggregates = await db.Reviews
            .Where(r => ids.Contains(r.CompanyId))
            .GroupBy(r => r.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .ToListAsync();

        var result = aggregates.ToDictionary(
            a => a.CompanyId, a => ((double?)a.Average, a.Count));
        foreach (var id in ids)
            result.TryAdd(id, (null, 0));
        return result;
    }
}
