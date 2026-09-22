using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminController(
    AppDbContext db, UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager,
    PricingCatalogCache pricingCatalogCache, CompanyOwnerWriter companyOwnerWriter) : ControllerBase
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
    public async Task<ActionResult<PagedResult<AdminUserDto>>> GetUsers(
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Phones are stored canonical (digits only, US-26), so a search string that LOOKS like a
            // phone number (≥5 digits, no letters — a surname never satisfies this) is normalized the
            // same way before matching against PhoneNumber. Anything else (e.g. "Иванов", "ivanov@")
            // is searched as typed against every column — normalizing it would just strip letters out
            // of a name search and break it (ARCHITECTURE.md §11.2).
            var digitCount = search.Count(char.IsDigit);
            var looksLikePhone = digitCount >= 5 && !search.Any(char.IsLetter);
            var phoneSearch = looksLikePhone ? PhoneNormalizer.Normalize(search) : search;

            // digitCount above is Unicode-aware (char.IsDigit), but PhoneNormalizer.Normalize only keeps
            // ASCII 0-9 (US-26, kept in sync with the SQL migration on purpose — see PhoneNormalizer's
            // doc comment). A search string made entirely of non-ASCII digits (e.g. Arabic-Indic) passes
            // the "looks like a phone" check yet normalizes to "" — without this guard,
            // PhoneNumber.Contains("") is true for every row and the query silently returns every user
            // regardless of what was searched for (code review finding, data leak).
            query = query.Where(u =>
                (phoneSearch.Length > 0 && u.PhoneNumber!.Contains(phoneSearch)) ||
                u.Email!.Contains(search) ||
                u.FirstName.Contains(search) ||
                u.LastName.Contains(search));
        }

        // US-49 p.6: tie-break by Id — CreatedAt alone doesn't guarantee a deterministic order for rows
        // with equal timestamps, and without one, page 2 can reshow (or skip) a row page 1 already showed.
        var total = await query.CountAsync();
        var users = await query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();

        // Subscriptions are account-level (§45.1) — resolve each user's billing account first, then
        // read plan/owned-company counts off THAT, not off OwnerUserId directly. This Users tab is
        // where an admin manages the tariff, not per company.
        var accounts = await db.BillingAccounts
            .Where(a => userIds.Contains(a.OwnerUserId))
            .Select(a => new { a.OwnerUserId, a.Id })
            .ToListAsync();
        var accountIdByUser = accounts.ToDictionary(a => a.OwnerUserId, a => a.Id);
        var accountIds = accounts.Select(a => a.Id).ToList();
        var subs = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .Where(s => s.BillingAccountId != null && accountIds.Contains(s.BillingAccountId!.Value)).ToListAsync();
        var ownedCounts = await db.Companies
            .Where(c => c.BillingAccountId != null && accountIds.Contains(c.BillingAccountId!.Value))
            .GroupBy(c => c.BillingAccountId!.Value)
            .Select(g => new { BillingAccountId = g.Key, Count = g.Count() })
            .ToListAsync();

        // US-49 p.3: ONE join for the whole page's roles instead of userManager.GetRolesAsync(u) inside
        // the loop below — the previous shape made N extra queries per page, independent of page size
        // only in the sense that it scaled with it instead. Pagination alone would only have masked this
        // (a smaller N is still N), not fixed it.
        var roleMap = await (from ur in db.UserRoles
                              join r in db.Roles on ur.RoleId equals r.Id
                              where userIds.Contains(ur.UserId)
                              select new { ur.UserId, r.Name }).ToListAsync();
        var rolesByUser = roleMap.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name ?? "").ToList());

        var result = users.Select(u =>
        {
            var accountId = accountIdByUser.GetValueOrDefault(u.Id);
            var sub = accountId != Guid.Empty ? subs.FirstOrDefault(s => s.BillingAccountId == accountId) : null;
            var ownedCount = accountId != Guid.Empty ? ownedCounts.FirstOrDefault(x => x.BillingAccountId == accountId)?.Count ?? 0 : 0;
            return new AdminUserDto(u.Id, u.PhoneNumber ?? "", u.Email, u.FirstName, u.LastName, u.AvatarUrl, u.CreatedAt,
                rolesByUser.GetValueOrDefault(u.Id, []), ownedCount, sub?.PlanConfigId, sub?.PlanConfig?.Name ?? "Free",
                sub?.PaidUntil, sub?.IsActive ?? true);
        }).ToList();

        return Ok(Pagination.Create(result, currentPage, currentPageSize, total));
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
    public async Task<ActionResult<PagedResult<AdminCompanyDto>>> GetCompanies(
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.Companies
            .Include(c => c.Members)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search) || c.Email!.Contains(search));

        var total = await query.CountAsync();
        var companies = await query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToListAsync();
        var ids = companies.Select(c => c.Id).ToList();
        var ownerIds = companies.Select(c => c.OwnerUserId).Distinct().ToList();
        // The tariff is account-level (§45.1): it belongs to the company's BillingAccountId, not to
        // OwnerUserId directly — OwnerUserId here is only used to show the responsible person's email.
        var accountIds = companies.Where(c => c.BillingAccountId.HasValue)
            .Select(c => c.BillingAccountId!.Value).Distinct().ToList();
        var subs = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .Where(s => s.BillingAccountId != null && accountIds.Contains(s.BillingAccountId!.Value)).ToListAsync();
        var ownerEmails = await db.Users.Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email ?? u.PhoneNumber ?? u.Id);
        var bookingCounts = await db.Bookings
            .Where(b => ids.Contains(b.CompanyId))
            .GroupBy(b => b.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToListAsync();

        var result = companies.Select(c =>
        {
            var sub = c.BillingAccountId.HasValue ? subs.FirstOrDefault(s => s.BillingAccountId == c.BillingAccountId) : null;
            var count = bookingCounts.FirstOrDefault(x => x.CompanyId == c.Id)?.Count ?? 0;
            return new AdminCompanyDto(c.Id, c.Name, c.Slug, c.Email, c.Phone, c.IsActive, c.AllowSelfBooking, c.CreatedAt,
                c.Members.Count, count, c.OwnerUserId, ownerEmails.GetValueOrDefault(c.OwnerUserId, c.OwnerUserId),
                sub?.PlanConfigId, sub?.PlanConfig?.Name ?? "Free", sub?.PaidUntil, sub?.IsActive ?? true);
        }).ToList();

        return Ok(Pagination.Create(result, currentPage, currentPageSize, total));
    }

    // openapi-cycle5.yaml (legacyAssignOwnerSubscription, redaction 2.1): this route is retired in
    // favor of PUT /admin/billing-accounts/{accountId}/subscription (not yet implemented — see the
    // cycle-07 backend report) and must answer 410 Gone rather than behave as before, so a stale admin
    // client can't silently keep writing tariff/paid-until onto AccountSubscription once the
    // BillingAccount model replaces it.
    [HttpPut("owners/{ownerUserId}/subscription")]
    public IActionResult UpdateSubscription(string ownerUserId, [FromBody] object? dto) =>
        LegacyEndpointGone("PUT /api/admin/billing-accounts/{accountId}/subscription");

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
        // Code review finding: a deleted account is a tombstone (ARCHITECTURE.md §7.4) — no password,
        // permanently locked out, phone/email scrubbed. Without this check SuperAdmin could hand a
        // company to an account nobody can ever log into again.
        if (newOwner.DeletedAtUtc is not null) return BadRequest("User account has been deleted");

        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-members:{id}");

        // ARCHITECTURE_CYCLE5.md §50/§59 grep 8: this is the ONLY place in the codebase allowed to
        // assign Company.OwnerUserId, shared with CompanyTransferService's owner-change branch — see
        // CompanyOwnerWriter's own remarks for why the two call sites must not drift apart.
        var changedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var oldOwnerUserId = await companyOwnerWriter.ChangeOwnerAsync(
            company, newOwner.Id, changedByUserId, withTransfer: false);

        // §47.4 (US-64 p.3) — a deliberate reversal of cycle 4's §25.3 behavior: the notification
        // number belongs to the billing account, not to whoever manages the company, so a stand-alone
        // owner change must not touch ChannelCompanyAssignment or cancel queued messages. That still
        // happens on a company TRANSFER between accounts (§51.3 step 6), because there the company
        // genuinely leaves the account the number belongs to — see CompanyTransferService.

        await db.SaveChangesAsync();
        // US-46: recompute roles for BOTH the new owner (gains CompanyOwner) and the old one (may lose
        // it, unless they still hold it via another company — SyncAsync recomputes from ALL of their
        // CompanyMember rows, not just this company's).
        await IdentityRoleSync.SyncAsync(db, userManager, newOwner.Id);
        if (oldOwnerUserId != newOwner.Id)
            await IdentityRoleSync.SyncAsync(db, userManager, oldOwnerUserId);
        await transaction.CommitAsync();

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
        var subscriberCounts = await GetActiveSubscriberCountsAsync(plans.Select(p => p.Id));
        var rules = await db.PlanOptionRules.Where(r => plans.Select(p => p.Id).Contains(r.PlanConfigId)).ToListAsync();
        return Ok(new AdminPlansListDto(plans.Select(p =>
            MapAdminPlanDto(p, subscriberCounts.GetValueOrDefault(p.Id), rules.Where(r => r.PlanConfigId == p.Id).ToList())).ToList()));
    }

    // openapi-cycle5.yaml AdminPlanInput (BREAKING fix, cycle-07 backend report): the previous shape
    // bound straight into SubscriptionPlanConfig (Highlights as a raw newline-separated string) and had
    // no `options` field at all — every plan created/updated through this endpoint left the whole
    // option-availability matrix untouched, silently leaving every option Unavailable. Both endpoints
    // below now accept the contract's array-of-strings Highlights and a full options matrix.
    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] AdminPlanInput dto)
    {
        var validationError = ValidatePlanInput(dto);
        if (validationError is not null) return validationError;

        var systemFreeError = await ValidateSystemFreeAsync(dto.IsSystemFree ?? false, dto.PricePerMonth, existingPlanId: null);
        if (systemFreeError is not null) return systemFreeError;

        var plan = new SubscriptionPlanConfig
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            Highlights = JoinHighlights(dto.Highlights),
            PricePerMonth = dto.PricePerMonth,
            MaxEmployees = dto.MaxEmployees,
            MaxCompanies = dto.MaxCompanies,
            AllowOnlineBooking = dto.AllowOnlineBooking,
            AllowMailing = dto.AllowMailing,
            AllowAnalytics = dto.AllowAnalytics,
            AllowPublicListing = dto.AllowPublicListing,
            AllowOnlinePayment = dto.AllowOnlinePayment,
            PhotoQuotaMb = dto.PhotoQuotaMb,
            PhotoRetention = dto.PhotoRetention,
            NotifyDaysBefore = dto.NotifyDaysBefore,
            IsPublic = dto.IsPublic ?? false,
            IsActive = dto.IsActive,
            SortOrder = dto.SortOrder ?? 0,
            IsSystemFree = dto.IsSystemFree ?? false,
            CreatedAt = DateTime.UtcNow,
        };
        db.SubscriptionPlanConfigs.Add(plan);
        await ApplyOptionRulesAsync(plan.Id, dto.Options);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (plan.IsSystemFree)
        {
            // See UpdatePlan for why this races the AnyAsync check above; same translation to 409.
            return Conflict("Another plan is already marked as the system free plan.");
        }
        pricingCatalogCache.Invalidate();
        // A brand-new plan has no subscribers yet — no need for the AccountSubscriptions round trip
        // GetActiveSubscriberCountsAsync does for the list/update endpoints.
        // Contract (API_CONTRACT_CYCLE5.md) documents 201 Created for a successful create, not 200.
        var rules = await db.PlanOptionRules.Where(r => r.PlanConfigId == plan.Id).ToListAsync();
        return StatusCode(StatusCodes.Status201Created, MapAdminPlanDto(plan, subscribedAccounts: 0, rules));
    }

    [HttpPut("plans/{id:guid}")]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] AdminPlanInput dto)
    {
        var validationError = ValidatePlanInput(dto);
        if (validationError is not null) return validationError;

        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        var effectiveIsSystemFree = dto.IsSystemFree ?? plan.IsSystemFree;
        var systemFreeError = await ValidateSystemFreeAsync(effectiveIsSystemFree, dto.PricePerMonth, existingPlanId: id);
        if (systemFreeError is not null) return systemFreeError;

        plan.Name = dto.Name;
        plan.PricePerMonth = dto.PricePerMonth;
        plan.MaxEmployees = dto.MaxEmployees;
        plan.MaxCompanies = dto.MaxCompanies;
        plan.AllowOnlineBooking = dto.AllowOnlineBooking;
        plan.AllowMailing = dto.AllowMailing;
        plan.AllowAnalytics = dto.AllowAnalytics;
        plan.AllowPublicListing = dto.AllowPublicListing;
        plan.AllowOnlinePayment = dto.AllowOnlinePayment;
        plan.PhotoQuotaMb = dto.PhotoQuotaMb;
        plan.PhotoRetention = dto.PhotoRetention;
        plan.Description = dto.Description;
        plan.NotifyDaysBefore = dto.NotifyDaysBefore;
        plan.Highlights = JoinHighlights(dto.Highlights);
        if (dto.IsPublic.HasValue) plan.IsPublic = dto.IsPublic.Value;
        if (dto.SortOrder.HasValue) plan.SortOrder = dto.SortOrder.Value;
        plan.IsSystemFree = effectiveIsSystemFree;
        await ApplyOptionRulesAsync(id, dto.Options);

        // Deactivating through this endpoint has exactly the effect DeletePlan refuses below: the
        // resolver treats PlanConfig.IsActive == false as Free, so every subscriber silently loses
        // online booking, analytics and their employee limit on the next request. Same guard, same
        // status, or the 409 there is just a speed bump around a differently-named door. The system
        // free plan (ARCHITECTURE_CYCLE5.md §43.4) additionally can never be deactivated at all — the
        // public price list has no "free" row otherwise and every account resolves to the hardcoded
        // EffectivePlan.Free fallback instead of the configured system row.
        if (plan.IsActive && !dto.IsActive)
        {
            if (plan.IsSystemFree)
                return Conflict("The system free plan cannot be deactivated.");

            var activeSubscribers = await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == id && s.IsActive);
            if (activeSubscribers > 0)
                return Conflict($"Cannot deactivate a plan with {activeSubscribers} active subscriber(s). Move them to another plan first.");
        }
        plan.IsActive = dto.IsActive;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (plan.IsSystemFree)
        {
            // Two concurrent requests can both pass the AnyAsync check above before either commits —
            // the partial unique index on IsSystemFree (AppDbContext) is the real guard; translate its
            // violation into the same 409 instead of letting a 500 leak out (code review finding).
            return Conflict("Another plan is already marked as the system free plan.");
        }
        pricingCatalogCache.Invalidate();
        var subscribedAccounts = await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == id && s.IsActive);
        var rules = await db.PlanOptionRules.Where(r => r.PlanConfigId == id).ToListAsync();
        return Ok(MapAdminPlanDto(plan, subscribedAccounts, rules));
    }

    [HttpDelete("plans/{id:guid}")]
    public async Task<IActionResult> DeletePlan(Guid id)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        // ARCHITECTURE_CYCLE5.md §43.4: the system free plan can be neither deleted nor deactivated —
        // deleting it (this endpoint only soft-deletes via IsActive = false) removes the "Бесплатно" row
        // from the public price list and, like UpdatePlan's deactivation guard above, would push any
        // future free-tier account onto the hardcoded EffectivePlan.Free fallback instead of this row.
        if (plan.IsSystemFree)
            return Conflict("The system free plan cannot be deleted.");

        // Deactivating a plan that still has active subscribers would silently strip their features on
        // their very next request (SubscriptionResolver.Resolve treats PlanConfig.IsActive == false as
        // Free) — the admin must move them off the plan first (see UpdateSubscription).
        var subscriberCount = await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == id && s.IsActive);
        if (subscriberCount > 0)
            return Conflict($"Cannot delete a plan with {subscriberCount} active subscriber(s). Move them to another plan first.");

        plan.IsActive = false;
        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        return NoContent();
    }

    private async Task<Dictionary<Guid, int>> GetActiveSubscriberCountsAsync(IEnumerable<Guid> planIds)
    {
        var ids = planIds.ToList();
        return await db.AccountSubscriptions
            .Where(s => s.IsActive && s.PlanConfigId.HasValue && ids.Contains(s.PlanConfigId.Value))
            .GroupBy(s => s.PlanConfigId!.Value)
            .Select(g => new { PlanConfigId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanConfigId, x => x.Count);
    }

    // openapi-cycle5.yaml AdminPlanDto: projects the entity onto the contract shape rather than
    // returning it directly — the entity also carries AllowNotificationChannel and CreatedAt (neither
    // in the schema, which sets additionalProperties: false) and stores Highlights as a single
    // newline-separated string rather than the array the schema requires. `options` is now the real
    // PlanOptionRule matrix for this plan (cycle-07 backend report fixes the earlier always-`[]` gap);
    // an option with no row is Unavailable by the schema's own documented default, so it's simply
    // omitted here rather than materialized as an explicit Unavailable row.
    internal static AdminPlanDto MapAdminPlanDto(SubscriptionPlanConfig plan, int subscribedAccounts, List<PlanOptionRule> rules) => new(
        plan.Id, plan.Name, plan.Description, SplitHighlights(plan.Highlights), plan.PricePerMonth, "RUB",
        plan.MaxEmployees, plan.MaxCompanies, plan.AllowOnlineBooking, plan.AllowMailing, plan.AllowAnalytics,
        plan.AllowPublicListing, plan.AllowOnlinePayment, plan.PhotoQuotaMb, plan.PhotoRetention,
        plan.NotifyDaysBefore, plan.IsPublic, plan.IsActive, plan.IsSystemFree, plan.SortOrder,
        Options: rules.Where(r => r.Availability != OptionAvailability.Unavailable)
            .Select(r => new AdminPlanOptionRuleDto(r.OptionId, r.Availability.ToString(), r.IncludedQuantity)).ToList(),
        subscribedAccounts);

    internal static List<string> SplitHighlights(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(10).ToList();

    private static string? JoinHighlights(List<string>? highlights) =>
        highlights is null || highlights.Count == 0 ? null : string.Join('\n', highlights.Take(10));

    private static IActionResult? ValidatePlanInput(AdminPlanInput dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 100)
            return new BadRequestObjectResult("Название тарифа обязательно (до 100 символов).");
        if (dto.PricePerMonth < 0)
            return new BadRequestObjectResult("Цена не может быть отрицательной.");
        if (dto.PhotoQuotaMb is < 0)
            return new BadRequestObjectResult("Photo quota must not be negative.");
        return null;
    }

    // openapi-cycle5.yaml AdminPlanInput.options: the FULL desired availability matrix for the plan —
    // rows not present are removed (an option that used to be Included/Extra and is now omitted becomes
    // Unavailable), matching the contract's "полная матрица" wording for the read side.
    private async Task ApplyOptionRulesAsync(Guid planId, List<AdminPlanOptionRuleDtoV2>? desired)
    {
        var existing = await db.PlanOptionRules.Where(r => r.PlanConfigId == planId).ToListAsync();
        var desiredList = desired ?? [];

        foreach (var row in existing.Where(e => desiredList.All(d => d.OptionId != e.OptionId)))
            db.PlanOptionRules.Remove(row);

        foreach (var d in desiredList)
        {
            var availability = Enum.Parse<OptionAvailability>(d.Availability);
            var row = existing.FirstOrDefault(e => e.OptionId == d.OptionId);
            if (row is null)
            {
                db.PlanOptionRules.Add(new PlanOptionRule
                {
                    Id = Guid.NewGuid(), PlanConfigId = planId, OptionId = d.OptionId,
                    Availability = availability, IncludedQuantity = d.IncludedQuantity,
                });
            }
            else
            {
                row.Availability = availability;
                row.IncludedQuantity = d.IncludedQuantity;
            }
        }
    }

    /// <summary>ARCHITECTURE_CYCLE5.md §43.4: exactly one row may have <c>IsSystemFree == true</c>, and
    /// that row's price must be 0 — validated here so a violation surfaces as 400/409 instead of the
    /// partial unique index throwing a raw <c>DbUpdateException</c> (500) on save. The
    /// <see cref="DbUpdateException"/> catch around <c>SaveChangesAsync</c> callers still handles the
    /// race where two concurrent requests both pass this check before either commits.</summary>
    private async Task<IActionResult?> ValidateSystemFreeAsync(bool isSystemFree, decimal pricePerMonth, Guid? existingPlanId)
    {
        if (!isSystemFree) return null;

        if (pricePerMonth != 0)
            return BadRequest("The system free plan must have PricePerMonth = 0.");

        var otherSystemFreeExists = await db.SubscriptionPlanConfigs
            .AnyAsync(p => p.IsSystemFree && p.Id != (existingPlanId ?? Guid.Empty));
        if (otherSystemFreeExists)
            return Conflict("Another plan is already marked as the system free plan.");

        return null;
    }

    // ── Scheduled tasks ────────────────────────────────────────────────────────

    // US-21 p.6: the only visibility this cycle gives the background task component — no admin screen,
    // just an endpoint a human (or a monitor) can curl. SuperAdmin-only like the rest of this controller.
    //
    // scheduledTasks/config are [FromServices] action parameters, not primary-constructor fields (code
    // review finding): a primary-constructor dependency is resolved for EVERY action on this controller,
    // even ones that never touch it — GetStats, GetUsers, etc. would all pay for constructing
    // IEnumerable<IScheduledTask> (which resolves PhotoRetentionCleanupTask and everything IT depends on
    // — AppDbContext, SubscriptionResolver, FileStorage) on every admin request. Scoping it to just this
    // action means that cost is only ever paid here.
    [HttpGet("scheduled-tasks")]
    public async Task<ActionResult<List<ScheduledTaskStatusDto>>> GetScheduledTasks(
        [FromServices] IEnumerable<IScheduledTask> scheduledTasks, [FromServices] IConfiguration config)
    {
        var states = await db.ScheduledTaskStates.ToDictionaryAsync(s => s.Name);
        var nowUtc = DateTime.UtcNow;

        var result = scheduledTasks.Select(task =>
        {
            var options = ScheduledTaskOptions.For(config, task);
            states.TryGetValue(task.Name, out var state);

            var isOverdue = ScheduledTaskSchedule.IsOverdue(state?.LastFinishedAtUtc, options.Period, nowUtc);

            return new ScheduledTaskStatusDto(
                task.Name, options.Enabled, (int)options.Period.TotalMinutes,
                state?.LastStartedAtUtc, state?.LastFinishedAtUtc, state?.LastDurationMs ?? 0,
                state?.LastSucceeded ?? false, state?.LastSummary, state?.LastError, isOverdue);
        }).ToList();

        return Ok(result);
    }

    // ── Notification channels (ARCHITECTURE_CYCLE4.md §34, T4-B11) ───────────────

    [HttpGet("notification-channels")]
    public async Task<ActionResult<PagedResult<AdminChannelDto>>> GetNotificationChannels(
        [FromQuery] ChannelState? state, [FromQuery] ChannelPaymentStatus? paymentState,
        [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.NotificationChannels.AsNoTracking().Include(c => c.Assignments).AsQueryable();
        if (state.HasValue) query = query.Where(c => c.State == state);

        // Payment state is computed, not stored (ChannelPaymentState.Of) — filtering by it means
        // pulling candidates in state-shaped buckets rather than a single indexed WHERE. At this row
        // count (one row per channel, not per message) a full materialize-then-filter is acceptable; see
        // ChannelPaymentState's own doc comment for why this can never become a stored column.
        var nowUtc = DateTime.UtcNow;
        var all = await query.ToListAsync();
        var filtered = paymentState.HasValue
            ? all.Where(c => ChannelPaymentState.Of(c, nowUtc) == paymentState.Value).ToList()
            : all;

        var total = filtered.Count;
        var page1 = filtered.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToList();

        var ownerIds = page1.Select(c => c.OwnerUserId).Distinct().ToList();
        var owners = await db.Users.AsNoTracking().Where(u => ownerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u);

        var items = page1.Select(c =>
        {
            var owner = owners.GetValueOrDefault(c.OwnerUserId);
            return new AdminChannelDto(
                c.Id, c.State, ChannelPaymentState.Of(c, nowUtc),
                owner is null ? "" : $"{owner.FirstName} {owner.LastName}",
                owner?.PhoneNumber is null ? null : PhoneDisplayMask.Mask(owner.PhoneNumber),
                c.PaidFromUtc, c.PaidUntilUtc, c.Assignments.Count, c.IdleSinceUtc, c.RequestedAtUtc);
        }).ToList();

        return Ok(Pagination.Create(items, currentPage, currentPageSize, total));
    }

    [HttpGet("notification-channels/summary")]
    public async Task<ActionResult<AdminChannelSummaryDto>> GetNotificationChannelsSummary()
    {
        var channels = await db.NotificationChannels.AsNoTracking().ToListAsync();
        var nowUtc = DateTime.UtcNow;
        var in7Days = nowUtc.AddDays(7);

        return Ok(new AdminChannelSummaryDto(
            Connected: channels.Count(c => c.State == ChannelState.Connected),
            Connecting: channels.Count(c => c.State == ChannelState.Connecting),
            Disconnected: channels.Count(c => c.State == ChannelState.Disconnected),
            Blocked: channels.Count(c => c.State == ChannelState.Blocked),
            NeedsReconnect: channels.Count(c => c.State == ChannelState.NeedsReconnect),
            Idle: channels.Count(c => c.IdleSinceUtc is not null),
            ExpiringIn7Days: channels.Count(c => c.PaidUntilUtc is { } paidUntil && paidUntil >= nowUtc && paidUntil <= in7Days),
            PendingRequests: channels.Count(c => c.RequestedAtUtc is not null && c.PaidUntilUtc is null)));
    }

    // openapi-cycle5.yaml (legacyChannelPayment, redaction 2.1): retired in favor of
    // PUT /admin/billing-accounts/{accountId}/subscription (not yet implemented — see the cycle-07
    // backend report), which folds the notification-channel option into the account's option matrix.
    // Must answer 410 Gone rather than keep writing ChannelPaymentLog rows against a model that's being
    // replaced.
    [HttpPost("notification-channels/{id:guid}/payment")]
    public IActionResult RecordChannelPayment(Guid id, [FromBody] object? dto) =>
        LegacyEndpointGone("PUT /api/admin/billing-accounts/{accountId}/subscription");

    [HttpPost("notification-channels/{id:guid}/suspend")]
    public Task<IActionResult> SuspendChannel(Guid id, [FromBody] AdminChannelSuspendDto dto) => SetSuspendedAsync(id, true, dto.Comment);

    [HttpPost("notification-channels/{id:guid}/resume")]
    public Task<IActionResult> ResumeChannel(Guid id, [FromBody] AdminChannelSuspendDto dto) => SetSuspendedAsync(id, false, dto.Comment);

    private async Task<IActionResult> SetSuspendedAsync(Guid id, bool suspended, string? comment)
    {
        var channel = await db.NotificationChannels.FindAsync(id);
        if (channel is null) return NotFound();

        channel.IsSuspendedByAdmin = suspended;
        db.ChannelPaymentLogs.Add(new ChannelPaymentLog
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            OldPaidUntil = channel.PaidUntilUtc,
            NewPaidUntil = channel.PaidUntilUtc,
            Comment = comment is null ? (suspended ? "suspended" : "resumed") : $"{(suspended ? "suspended" : "resumed")}: {comment}",
        });

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("platform-settings")]
    public async Task<ActionResult<AdminPlatformSettingsDto>> GetPlatformSettings(
        [FromServices] Services.Notifications.PlatformSettings platformSettings)
    {
        var price = await platformSettings.GetChannelPricePerMonthAsync();
        var idleDays = await platformSettings.GetChannelIdleDaysAsync();
        var pricingPublicEnabled = await pricingCatalogCache.IsPublicEnabledAsync();
        return Ok(new AdminPlatformSettingsDto(price, idleDays, pricingPublicEnabled));
    }

    [HttpPut("platform-settings")]
    public async Task<ActionResult<AdminPlatformSettingsDto>> UpdatePlatformSettings(
        [FromBody] AdminPlatformSettingsDto dto, [FromServices] Services.Notifications.PlatformSettings platformSettings)
    {
        if (dto.ChannelIdleDays is < 0 or > 60) return BadRequest("channelIdleDays must be between 0 and 60");
        if (dto.ChannelPricePerMonth is < 0) return BadRequest("channelPricePerMonth must not be negative");

        var oldPrice = await platformSettings.GetChannelPricePerMonthAsync();
        var oldIdleDays = await platformSettings.GetChannelIdleDaysAsync();
        var oldPricingPublicEnabled = await pricingCatalogCache.IsPublicEnabledAsync();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // Reviewer note: previously wrote (and journaled) both keys unconditionally, even when the
        // request left one of them unchanged — a no-op "save" produced a change-log row that recorded no
        // actual change, and every PUT (again, even a no-op one) went stale-for-60s on the read side.
        // Guarded per key now; cache invalidated only for the key(s) that actually moved.
        if (oldPrice != dto.ChannelPricePerMonth)
        {
            await PlatformSettingsWriter.WriteAsync(
                db, PlatformSettingsWriter.PriceKey, oldPrice?.ToString(CultureInfo.InvariantCulture),
                dto.ChannelPricePerMonth?.ToString(CultureInfo.InvariantCulture) ?? "", userId);
            platformSettings.InvalidateCache(PlatformSettingsWriter.PriceKey);
        }

        if (oldIdleDays != dto.ChannelIdleDays)
        {
            await PlatformSettingsWriter.WriteAsync(
                db, PlatformSettingsWriter.IdleDaysKey, oldIdleDays.ToString(CultureInfo.InvariantCulture),
                dto.ChannelIdleDays.ToString(CultureInfo.InvariantCulture), userId);
            platformSettings.InvalidateCache(PlatformSettingsWriter.IdleDaysKey);
        }

        // ARCHITECTURE_CYCLE5.md §48: the "рубильник" for the public price list (GET /api/pricing). Was
        // previously settable only by hand-editing the PlatformSettings row directly in the database —
        // see the standing comment on PricingCatalogCache.Invalidate — this is the admin lever for it.
        if (oldPricingPublicEnabled != dto.PricingPublicEnabled)
        {
            await PlatformSettingsWriter.WriteAsync(
                db, PricingCatalogCache.PublicEnabledSettingKey,
                oldPricingPublicEnabled ? "true" : "false", dto.PricingPublicEnabled ? "true" : "false", userId);
            pricingCatalogCache.Invalidate();
        }

        await db.SaveChangesAsync();
        return Ok(dto);
    }

    // Plain-text 410 body per openapi-cycle5.yaml's `text/plain: {schema: {type: string}}` response —
    // shared by both cycle-5 retired routes so they always point callers at the same replacement.
    private static IActionResult LegacyEndpointGone(string replacementRoute) =>
        new ContentResult
        {
            StatusCode = StatusCodes.Status410Gone,
            Content = $"Этот маршрут упразднён. Используйте {replacementRoute}.",
            ContentType = "text/plain; charset=utf-8",
        };

    private static AdminChannelDto MapAdminChannelDto(NotificationChannel channel, int idleDays) => new(
        channel.Id, channel.State, ChannelPaymentState.Of(channel, DateTime.UtcNow), "", null,
        channel.PaidFromUtc, channel.PaidUntilUtc, channel.Assignments.Count, channel.IdleSinceUtc, channel.RequestedAtUtc);
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

public record AdminStatsDto(int TotalCompanies, int TotalUsers, int TotalBookings, int CompletedBookings, decimal TotalRevenue);

// CommissionPercent removed (US-22): commission became per-company (CompanyMember.CommissionPercent)
// back in cycle A; this account-level field means nothing any more and AdminPage.tsx never showed it.
public record AdminUserDto(string Id, string Phone, string? Email, string FirstName, string LastName, string? AvatarUrl, DateTime CreatedAt,
    List<string> Roles, int OwnedCompanyCount, Guid? PlanConfigId, string PlanName, DateTime? PaidUntil, bool SubscriptionActive);

// AllowSelfBooking is included (additive) so the admin UI can read the company's CURRENT value before
// re-sending it unchanged to PUT /api/admin/companies/{id} — that endpoint overwrites all three of its
// body fields unconditionally, so a caller that has to guess this one risks silently flipping it
// (found during BE/FE contract integration, US-04).
public record AdminCompanyDto(Guid Id, string Name, string Slug, string? Email, string? Phone, bool IsActive, bool AllowSelfBooking, DateTime CreatedAt,
    int MemberCount, int BookingCount, string OwnerUserId, string OwnerEmail,
    Guid? PlanConfigId, string PlanName, DateTime? PaidUntil, bool SubscriptionActive);

public record UpdateSubscriptionDto(Guid? PlanConfigId, DateTime? PaidUntil, bool IsActive, string? Comment);

// PUT /api/admin/plans/{id} body. Cycle-5 fields are nullable and applied only when present in the
// request (see UpdatePlan) — binding straight into SubscriptionPlanConfig used to reset them to
// false/0/null whenever a caller that doesn't know about them (the current admin UI) omitted them.
public record UpdatePlanDto(
    string Name, decimal PricePerMonth, int? MaxEmployees, int? MaxCompanies,
    bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics, bool AllowPublicListing, bool AllowOnlinePayment,
    int? PhotoQuotaMb, PhotoRetention PhotoRetention, string? Description, bool IsActive, int NotifyDaysBefore,
    string? Highlights = null, bool? IsPublic = null, int? SortOrder = null, bool? IsSystemFree = null);

// openapi-cycle5.yaml AdminPlanDto/PlanOptionRuleDto — `Options` is always empty (see MapAdminPlanDto)
// until the BillingAccount option catalog exists (cycle-07 backend report).
public record AdminPlanOptionRuleDto(Guid OptionId, string Availability, int? IncludedQuantity);

public record AdminPlanDto(
    Guid Id, string Name, string? Description, List<string> Highlights, decimal PricePerMonth, string Currency,
    int? MaxEmployees, int? MaxCompanies, bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics,
    bool AllowPublicListing, bool AllowOnlinePayment, int? PhotoQuotaMb, PhotoRetention PhotoRetention,
    int NotifyDaysBefore, bool IsPublic, bool IsActive, bool IsSystemFree, int SortOrder,
    List<AdminPlanOptionRuleDto> Options, int SubscribedAccounts);

public record AdminPlansListDto(List<AdminPlanDto> Plans);

public record SubscriptionChangeLogDto(Guid Id, DateTime ChangedAt, string ChangedByEmail,
    string OldPlanName, string NewPlanName, DateTime? OldPaidUntil, DateTime? NewPaidUntil,
    bool OldIsActive, bool NewIsActive, string? Comment);

public record AdminUpdateCompanyDto(string Name, bool IsActive, bool AllowSelfBooking);
public record UpdateCompanyOwnerDto(string NewOwnerUserId);

public record AdminBookingDto(Guid Id, string CompanyName, string ServiceName, string MasterName, string ClientName,
    string? ClientPhone, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, BookingStatus Status, decimal Price);

public record ScheduledTaskStatusDto(
    string Name, bool Enabled, int PeriodMinutes,
    DateTime? LastStartedAt, DateTime? LastFinishedAt, int LastDurationMs,
    bool LastSucceeded, string? LastSummary, string? LastError, bool IsOverdue);

// ── Notification channels (API_CONTRACT_CYCLE4.md §34, T4-B11) ────────────────

public record AdminChannelDto(
    Guid Id, ChannelState State, ChannelPaymentStatus PaymentState,
    string OwnerName, string? OwnerPhoneMasked,
    DateTime? PaidFrom, DateTime? PaidUntil, int CompanyCount, DateTime? IdleSince, DateTime? RequestedAt);

public record AdminChannelSummaryDto(
    int Connected, int Connecting, int Disconnected, int Blocked,
    int NeedsReconnect, int Idle, int ExpiringIn7Days, int PendingRequests);

public record AdminChannelPaymentDto(DateOnly PaidFrom, DateOnly PaidUntil, decimal? Amount, string? Comment);

public record AdminChannelSuspendDto(string? Comment);

public record AdminPlatformSettingsDto(decimal? ChannelPricePerMonth, int ChannelIdleDays, bool PricingPublicEnabled);
