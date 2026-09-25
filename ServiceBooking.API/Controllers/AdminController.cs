using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Legal;
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
    PricingCatalogCache pricingCatalogCache, CompanyOwnerWriter companyOwnerWriter,
    SubscriptionResolver subscriptionResolver, ILogger<AdminController> logger) : ControllerBase
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

        // §50.1: visible without opening the subject-requests section — a one-person, no-shift-rotation
        // operator (Р8) must see this without remembering to go looking for it.
        var nowUtc = DateTime.UtcNow;
        var overdueSubjectRequests = await db.SubjectRequests.CountAsync(r =>
            r.DueAtUtc < nowUtc && r.Status != SubjectRequestStatus.Answered && r.Status != SubjectRequestStatus.Rejected);

        return Ok(new AdminStatsDto(totalCompanies, totalUsers, totalBookings, completedBookings, revenueByService, overdueSubjectRequests));
    }

    // ── Subject requests (T5-B10, ARCHITECTURE_CYCLE5.md §50.1, US-74) ─────────────────────────────

    [HttpGet("subject-requests")]
    public async Task<ActionResult<PagedResult<SubjectRequestDto>>> GetSubjectRequests(
        [FromQuery] SubjectRequestStatus? status, [FromQuery] SubjectRequestKind? kind, [FromQuery] string? dueState,
        [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.SubjectRequests.AsNoTracking().AsQueryable();
        if (status is not null) query = query.Where(r => r.Status == status);
        if (kind is not null) query = query.Where(r => r.Kind == kind);

        // dueState is computed server-side (ARCHITECTURE_CYCLE5.md §50.1: "считает сервер, не фронт") —
        // filtered here the same way, not left to the frontend to derive from raw dates.
        var nowUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(dueState))
        {
            // Code review, "заодно": an unrecognized value used to fall through to `_ => query` — the
            // filter silently did nothing instead of telling the caller their query string was wrong.
            if (dueState is not ("Overdue" or "DueSoon" or "OnTime"))
                return BadRequest($"Неизвестное значение dueState '{dueState}'. Ожидается Overdue, DueSoon или OnTime.");

            query = dueState switch
            {
                "Overdue" => query.Where(r => r.DueAtUtc < nowUtc && r.Status != SubjectRequestStatus.Answered && r.Status != SubjectRequestStatus.Rejected),
                "DueSoon" => query.Where(r => r.DueAtUtc >= nowUtc && r.DueAtUtc < nowUtc.AddDays(2) && r.Status != SubjectRequestStatus.Answered && r.Status != SubjectRequestStatus.Rejected),
                _ => query.Where(r => r.DueAtUtc >= nowUtc.AddDays(2) || r.Status == SubjectRequestStatus.Answered || r.Status == SubjectRequestStatus.Rejected),
            };
        }

        var total = await query.CountAsync();
        // Urgent-first, always — §50.1: "самое горящее сверху", not a caller-chosen sort.
        var rows = await query.OrderBy(r => r.DueAtUtc)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToListAsync();

        var handlerIds = rows.Where(r => r.HandlerUserId is not null).Select(r => r.HandlerUserId!).Distinct().ToList();
        var handlerNames = handlerIds.Count == 0 ? new Dictionary<string, string>()
            : await db.Users.Where(u => handlerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        var items = rows.Select(r => new SubjectRequestDto(
            r.Id, r.Reference, r.Kind.ToString(), r.Status.ToString(), PhoneDisplayMask.Mask(r.SubjectPhone),
            r.ContactValue, r.Message, r.ReceivedAtUtc, r.DueAtUtc, ComputeDueState(r, nowUtc),
            r.AnsweredAtUtc, r.HandlerUserId is not null ? handlerNames.GetValueOrDefault(r.HandlerUserId) : null,
            r.Resolution)).ToList();

        return Ok(Pagination.Create(items, currentPage, currentPageSize, total));
    }

    [HttpPost("subject-requests/{id:guid}/status")]
    public async Task<IActionResult> UpdateSubjectRequestStatus(Guid id, [FromBody] UpdateSubjectRequestStatusDto dto)
    {
        var request = await db.SubjectRequests.FindAsync(id);
        if (request is null) return NotFound();

        // §48.3: a terminal status without a resolution would leave the journal unable to prove what was
        // actually done — the same "doesn't count as evidence" reasoning behind ConsentRecord's own
        // required fields.
        if (dto.Status is SubjectRequestStatus.Answered or SubjectRequestStatus.Rejected && string.IsNullOrWhiteSpace(dto.Resolution))
            return BadRequest("Для этого статуса нужно указать резолюцию.");

        request.Status = dto.Status;
        // Code review, "заодно": Resolution/AnsweredAtUtc/HandlerUserId are only ever WRITTEN when moving
        // TO a terminal status — an earlier version wrote dto.Resolution unconditionally, so moving an
        // already-Answered request back to a non-terminal status (e.g. reopening it for more work) wiped
        // the resolution that was already on record, even though dto.Resolution is null for that call
        // (the BadRequest check above only requires it for the terminal statuses).
        if (dto.Status is SubjectRequestStatus.Answered or SubjectRequestStatus.Rejected)
        {
            request.Resolution = dto.Resolution;
            request.AnsweredAtUtc = DateTime.UtcNow;
            request.HandlerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        }

        await db.SaveChangesAsync();
        return Ok();
    }

    private static string ComputeDueState(SubjectRequest r, DateTime nowUtc)
    {
        if (r.Status is SubjectRequestStatus.Answered or SubjectRequestStatus.Rejected) return "OnTime";
        if (r.DueAtUtc < nowUtc) return "Overdue";
        return r.DueAtUtc < nowUtc.AddDays(2) ? "DueSoon" : "OnTime";
    }

    // ── Users ──────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> GetUsers(
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        search = Pagination.SanitizeSearch(search);
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
        search = Pagination.SanitizeSearch(search);
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

    // contracts/cycle7/openapi.yaml (legacyAssignOwnerSubscription, redaction 2.1): this route is retired in
    // favor of PUT /admin/billing-accounts/{accountId}/subscription (AdminBillingController,
    // implemented this cycle) and must answer 410 Gone rather than behave as before, so a stale admin
    // client can't silently keep writing tariff/paid-until onto AccountSubscription once the
    // BillingAccount model replaces it.
    [HttpPut("owners/{ownerUserId}/subscription")]
    public IActionResult UpdateSubscription(string ownerUserId, [FromBody] object? dto) =>
        LegacyEndpointGone("PUT /api/admin/billing-accounts/{accountId}/subscription");

    /// <summary>
    /// US-63 diagnostic endpoint (ARCHITECTURE_CYCLE6.md §43.2, API_CONTRACT_CYCLE6.md §42.2):
    /// answers "the plan is assigned — why doesn't it work" in one round trip, instead of a support
    /// engineer guessing across six independent failure points (§43.1).
    ///
    /// Merge-review finding (cycle 7 on top of cycle 6): this survived the BillingAccount rework
    /// keyed off <c>AccountSubscription.OwnerUserId</c>, which ARCHITECTURE_CYCLE7.md §43.4 says
    /// business logic no longer reads, and resolved the effective plan with
    /// <c>grandfatheredEmployeeBonus: 0, paidNotificationNumbers: 0</c> hard-coded — any purchased
    /// options were invisible, so this could show a plan poorer than what's actually applied.
    /// Repointed at the owner's <see cref="BillingAccount"/> (1:1, same guarantee the old unique
    /// index gave) and <see cref="SubscriptionResolver.GetEffectivePlanForAccountAsync"/>, the same
    /// option-aware resolution every other endpoint uses. Not retired by §54: the frontend doesn't
    /// call it, but the contract didn't mark it Gone, and support engineers may still hit it directly.
    /// </summary>
    [HttpGet("owners/{ownerUserId}/subscription")]
    public async Task<ActionResult<SubscriptionDiagnosticsDto>> GetSubscriptionDiagnostics(string ownerUserId)
    {
        var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == ownerUserId);
        if (owner is null) return NotFound("Owner not found");

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId);

        var sub = account is null ? null : await db.AccountSubscriptions
            .Include(s => s.PlanConfig)
            .FirstOrDefaultAsync(s => s.BillingAccountId == account.Id);

        var nowUtc = DateTime.UtcNow;
        var effective = account is null
            ? EffectivePlan.Free
            : await subscriptionResolver.GetEffectivePlanForAccountAsync(account.Id);
        var (status, statusText) = SubscriptionDiagnostics.Describe(sub, nowUtc);

        // Merge-review finding: filter by the billing account being diagnosed, not by
        // Company.OwnerUserId. After US-77 (company transfer) a company can be MANAGED by this
        // owner while being PAID FOR by a different billing account (or vice versa) — mixing the two
        // axes here would explain "why the plan doesn't apply" using a plan that isn't even the one
        // the company is subject to. This endpoint answers for the account, so it must list that
        // account's companies.
        var companies = account is null
            ? new List<Company>()
            : await db.Companies.Where(c => c.BillingAccountId == account.Id)
                .Select(c => new Company { Id = c.Id, Name = c.Name, AllowSelfBooking = c.AllowSelfBooking })
                .ToListAsync();

        var companyDtos = companies.Select(c =>
        {
            var blockingReason = SubscriptionDiagnostics.BlockingReasonFor(sub, effective, c.AllowSelfBooking, nowUtc);
            return new SubscriptionDiagnosticsCompanyDto(
                c.Id, c.Name, c.AllowSelfBooking,
                OnlineBookingEnabled: blockingReason == PlanNotAppliedReason.None,
                BlockingReason: blockingReason);
        }).ToList();

        var ownerName = string.Join(" ", new[] { owner.FirstName, owner.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (string.IsNullOrWhiteSpace(ownerName)) ownerName = owner.Email ?? owner.PhoneNumber ?? ownerUserId;

        return Ok(new SubscriptionDiagnosticsDto(
            ownerUserId, ownerName,
            sub?.PlanConfigId, sub?.PlanConfig?.Name, sub?.PaidUntil, sub?.IsActive ?? true,
            sub?.PlanConfig?.IsActive ?? true, status, statusText, effective, companyDtos));
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
        // Code review finding: a deleted account is a tombstone (ARCHITECTURE.md §7.4) — no password,
        // permanently locked out, phone/email scrubbed. Without this check SuperAdmin could hand a
        // company to an account nobody can ever log into again.
        if (newOwner.DeletedAtUtc is not null) return BadRequest("User account has been deleted");

        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-members:{id}");

        // ARCHITECTURE_CYCLE7.md §50/§59 grep 8: this is the ONLY place in the codebase allowed to
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
            .Include(b => b.BookingServices)
            .AsQueryable();

        if (companyId.HasValue) query = query.Where(b => b.CompanyId == companyId);
        if (from.HasValue) query = query.Where(b => b.Date >= from);
        if (to.HasValue) query = query.Where(b => b.Date <= to);
        if (status.HasValue) query = query.Where(b => b.Status == status);

        var bookings = await query.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime).Take(500).ToListAsync();

        return Ok(bookings.Select(b => new AdminBookingDto(
            b.Id, b.Company.Name,
            // US-67 (ARCHITECTURE_CYCLE6.md §47.2): the visit shown as one line — comma-joined service
            // names — rather than one row per service. Falls back to Service.Name only if
            // BookingServices somehow has no rows (should never happen after the backfill).
            b.BookingServices.Count > 0
                ? string.Join(", ", b.BookingServices.OrderBy(bs => bs.Position).Select(bs => bs.NameSnapshot))
                : b.Service.Name,
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
        var totalOptionsInCatalog = await db.SubscriptionOptions.CountAsync();
        return Ok(new AdminPlansListDto(plans.Select(p =>
            MapAdminPlanDto(p, subscriberCounts.GetValueOrDefault(p.Id), rules.Where(r => r.PlanConfigId == p.Id).ToList(), totalOptionsInCatalog)).ToList()));
    }

    // contracts/cycle7/openapi.yaml AdminPlanInput (BREAKING fix, cycle-07 backend report): the previous shape
    // bound straight into SubscriptionPlanConfig (Highlights as a raw newline-separated string) and had
    // no `options` field at all — every plan created/updated through this endpoint left the whole
    // option-availability matrix untouched, silently leaving every option Unavailable. Both endpoints
    // below now accept the contract's array-of-strings Highlights and a full options matrix.
    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] AdminPlanInput dto)
    {
        var validationError = ValidatePlanInput(dto);
        if (validationError is not null) return validationError;

        var systemFreeError = await ValidateSystemFreeAsync(false, dto.PricePerMonth, existingPlanId: null);
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
            IsSystemFree = false,
            CreatedAt = DateTime.UtcNow,
        };
        var optionRulesError = dto.Options is not null ? await ApplyOptionRulesAsync(plan.Id, dto.Options) : null;
        if (optionRulesError is not null) return optionRulesError;
        // NB-8 (cycle-07 backend report): no try/catch DbUpdateException-when-IsSystemFree here, unlike
        // UpdatePlan/SetSystemFree — IsSystemFree is hardcoded false a few lines up, so that guard could
        // never fire; keeping it would have been dead code masking a real conflict as an unhandled 500.
        // A brand-new plan can never race the "one system-free plan" unique index because it never asks
        // to be the system-free plan in the first place (see PUT .../system-free for that transition).
        db.SubscriptionPlanConfigs.Add(plan);
        await db.SaveChangesAsync();
        pricingCatalogCache.Invalidate();
        // A brand-new plan has no subscribers yet — no need for the AccountSubscriptions round trip
        // GetActiveSubscriberCountsAsync does for the list/update endpoints.
        // Contract (API_CONTRACT_CYCLE7.md) documents 201 Created for a successful create, not 200.
        var rules = await db.PlanOptionRules.Where(r => r.PlanConfigId == plan.Id).ToListAsync();
        var totalOptionsInCatalog = await db.SubscriptionOptions.CountAsync();
        return StatusCode(StatusCodes.Status201Created, MapAdminPlanDto(plan, subscribedAccounts: 0, rules, totalOptionsInCatalog));
    }

    [HttpPut("plans/{id:guid}")]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] AdminPlanInput dto)
    {
        var validationError = ValidatePlanInput(dto);
        if (validationError is not null) return validationError;

        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        var systemFreeError = await ValidateSystemFreeAsync(plan.IsSystemFree, dto.PricePerMonth, existingPlanId: id);
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
        // B4: same "apply only if present" semantics as IsPublic/SortOrder below — the existing admin
        // UI never sends `highlights`/`options`, and applying them unconditionally used to wipe every
        // highlight bullet (JoinHighlights(null) => null) and every PlanOptionRule (ApplyOptionRulesAsync
        // treating a missing `options` as "remove all rules") on every ordinary field edit.
        if (dto.Highlights is not null) plan.Highlights = JoinHighlights(dto.Highlights);
        // ARCHITECTURE_CYCLE15.md §255.4/API_CONTRACT_CYCLE15.md §288 — checked ONLY on the transition
        // (plan.IsPublic true -> dto.IsPublic false), never unconditionally: the shipped system free
        // plan actually ships with IsPublic == false (20260922154148_FixSeedBillingCatalogCapabilityKeys),
        // so an unconditional guard would block every save of it, including ones that don't touch
        // isPublic at all.
        if (plan.IsSystemFree && plan.IsPublic && dto.IsPublic == false)
            return Conflict("Системный бесплатный тариф нельзя убрать с витрины.");
        if (dto.IsPublic.HasValue) plan.IsPublic = dto.IsPublic.Value;
        if (dto.SortOrder.HasValue) plan.SortOrder = dto.SortOrder.Value;
        if (dto.Options is not null)
        {
            var optionRulesError = await ApplyOptionRulesAsync(id, dto.Options);
            if (optionRulesError is not null) return optionRulesError;
        }

        // Deactivating through this endpoint has exactly the effect DeletePlan refuses below: the
        // resolver treats PlanConfig.IsActive == false as Free, so every subscriber silently loses
        // online booking, analytics and their employee limit on the next request. Same guard, same
        // status, or the 409 there is just a speed bump around a differently-named door. The system
        // free plan (ARCHITECTURE_CYCLE7.md §43.4) additionally can never be deactivated at all — the
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
        var totalOptionsInCatalog = await db.SubscriptionOptions.CountAsync();
        return Ok(MapAdminPlanDto(plan, subscribedAccounts, rules, totalOptionsInCatalog));
    }

    /// <summary>
    /// Separate from PUT /plans/{id} on purpose (code review finding B "isSystemFree removal") —
    /// contracts/cycle7/openapi.yaml's AdminPlanInput has no isSystemFree property, and folding "make
    /// this THE system free plan" into an ordinary field-edit DTO made it too easy for an unrelated PUT
    /// to accidentally flip the flag guarded by the partial unique index (AppDbContext).
    /// </summary>
    [HttpPut("plans/{id:guid}/system-free")]
    public async Task<IActionResult> SetSystemFree(Guid id, [FromBody] SetSystemFreeInput dto)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        if (plan.IsSystemFree == dto.IsSystemFree)
        {
            var unchangedAccounts = await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == id && s.IsActive);
            var unchangedRules = await db.PlanOptionRules.Where(r => r.PlanConfigId == id).ToListAsync();
            return Ok(MapAdminPlanDto(plan, unchangedAccounts, unchangedRules, await db.SubscriptionOptions.CountAsync()));
        }

        if (!dto.IsSystemFree && plan.IsSystemFree)
        {
            // §43.4 says exactly one system free plan must exist AT ALL TIMES. This endpoint does NOT
            // guarantee that invariant, and cannot: moving the flag from one plan to another is
            // necessarily two separate requests (turn the old one off, then turn the new one on), and
            // between those two requests the system genuinely has ZERO system-free plans for however
            // long the admin takes to make the second call — there is no transaction spanning both. The
            // check below only guards against the WORSE failure of a permanent dead end: it refuses to
            // turn this plan's flag off unless another active, zero-priced candidate already exists to
            // receive it, so the flag can always eventually be moved. It does not, and cannot, stop an
            // admin from leaving the system without a system-free plan indefinitely by simply never
            // making the second call. Before this guard existed, turning the flag off was refused
            // unconditionally, which made moving it to another plan impossible altogether (cycle-07 QA
            // finding #1) — the seeded plan could never be replaced.
            var transferCandidateExists = await db.SubscriptionPlanConfigs
                .AnyAsync(p => p.Id != id && p.IsActive && p.PricePerMonth == 0);
            if (!transferCandidateExists)
                return Conflict("Ровно один тариф должен быть системным бесплатным — создайте или подготовьте тариф с ценой 0, прежде чем снимать этот флаг.");
        }

        // Code-review finding — symmetric to SetSystemTrial's own check (`plan.IsSystemFree` there):
        // a trial plan can never also become the system free plan. Checked here explicitly rather than
        // relying only on ValidateSystemFreeAsync's own-price/other-system-free checks, because on an
        // otherwise-empty database (no system free plan seeded yet) those checks pass fine even for a
        // trial plan — which would silently produce one row with BOTH flags set, exactly what the two
        // flags together are supposed to make impossible.
        if (dto.IsSystemFree && plan.IsSystemTrial)
            return Conflict("Этот тариф уже пробный период — тариф не может быть одновременно системным бесплатным.");

        var systemFreeError = await ValidateSystemFreeAsync(dto.IsSystemFree, plan.PricePerMonth, existingPlanId: id);
        if (systemFreeError is not null) return systemFreeError;

        plan.IsSystemFree = dto.IsSystemFree;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Another plan is already marked as the system free plan.");
        }
        pricingCatalogCache.Invalidate();
        var subscribedAccounts = await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == id && s.IsActive);
        var rules = await db.PlanOptionRules.Where(r => r.PlanConfigId == id).ToListAsync();
        return Ok(MapAdminPlanDto(plan, subscribedAccounts, rules, await db.SubscriptionOptions.CountAsync()));
    }

    /// <summary>
    /// Cycle 18 (API_CONTRACT_CYCLE18.md §366) — the trial-plan flag, following exactly the same
    /// separate-endpoint pattern as <see cref="SetSystemFree"/> above and for the same reason: keeping
    /// it out of <see cref="AdminPlanInput"/> means an ordinary field edit can never accidentally flip
    /// it, and the partial unique index on IsSystemTrial (AppDbContext) is still the real guard against
    /// a race, this endpoint's own check is just the friendly 409.
    /// </summary>
    [HttpPut("plans/{id:guid}/system-trial")]
    public async Task<IActionResult> SetSystemTrial(Guid id, [FromBody] SetSystemTrialInput dto)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        var totalOptionsInCatalog = await db.SubscriptionOptions.CountAsync();
        var rules = await db.PlanOptionRules.Where(r => r.PlanConfigId == id).ToListAsync();
        var subscribedAccounts = await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == id && s.IsActive);

        // Idempotent — same value is a no-op 200 (§366).
        if (plan.IsSystemTrial == dto.IsSystemTrial)
            return Ok(MapAdminPlanDto(plan, subscribedAccounts, rules, totalOptionsInCatalog));

        if (dto.IsSystemTrial)
        {
            if (plan.PricePerMonth != 0)
                return BadRequest("Триал не оплачивается — цена тарифа должна быть равна 0.");
            if (plan.IsSystemFree)
                return Conflict("Этот тариф уже системный бесплатный — тариф не может быть одновременно триалом.");
            var anotherTrialExists = await db.SubscriptionPlanConfigs.AnyAsync(p => p.Id != id && p.IsSystemTrial);
            if (anotherTrialExists)
                return Conflict("Другой тариф уже помечен как пробный период.");
        }

        plan.IsSystemTrial = dto.IsSystemTrial;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Другой тариф уже помечен как пробный период.");
        }
        pricingCatalogCache.Invalidate();
        return Ok(MapAdminPlanDto(plan, subscribedAccounts, rules, totalOptionsInCatalog));
    }

    [HttpDelete("plans/{id:guid}")]
    public async Task<IActionResult> DeletePlan(Guid id)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        // ARCHITECTURE_CYCLE7.md §43.4: the system free plan can be neither deleted nor deactivated —
        // deleting it (this endpoint only soft-deletes via IsActive = false) removes the "Бесплатно" row
        // from the public price list and, like UpdatePlan's deactivation guard above, would push any
        // future free-tier account onto the hardcoded EffectivePlan.Free fallback instead of this row.
        if (plan.IsSystemFree)
            return Conflict("The system free plan cannot be deleted.");

        // Cycle 18 (API_CONTRACT_CYCLE18.md §366) — same reasoning as the system-free guard above: the
        // trial plan is the one row TrialActivationService looks up by IsSystemTrial, and removing it
        // would turn "trial is offered" into a silent TrialNotOffered for every future activation.
        if (plan.IsSystemTrial)
            return Conflict("Тариф пробного периода нельзя удалить.");

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

    // contracts/cycle7/openapi.yaml AdminPlanDto: projects the entity onto the contract shape rather than
    // returning it directly — the entity also carries AllowNotificationChannel and CreatedAt (neither
    // in the schema, which sets additionalProperties: false) and stores Highlights as a single
    // newline-separated string rather than the array the schema requires. `options` is now the real
    // PlanOptionRule matrix for this plan (cycle-07 backend report fixes the earlier always-`[]` gap);
    // an option with no row is Unavailable by the schema's own documented default, so it's simply
    // omitted here rather than materialized as an explicit Unavailable row.
    internal static AdminPlanDto MapAdminPlanDto(SubscriptionPlanConfig plan, int subscribedAccounts, List<PlanOptionRule> rules, int? totalOptionsInCatalog = null)
    {
        var configured = rules.Count(r => r.Availability != OptionAvailability.Unavailable);
        var total = totalOptionsInCatalog ?? configured;
        return new(
            plan.Id, plan.Name, plan.Description, SplitHighlights(plan.Highlights), plan.PricePerMonth, "RUB",
            plan.MaxEmployees, plan.MaxCompanies, plan.AllowOnlineBooking, plan.AllowMailing, plan.AllowAnalytics,
            plan.AllowPublicListing, plan.AllowOnlinePayment, plan.PhotoQuotaMb, plan.PhotoRetention,
            plan.NotifyDaysBefore, plan.IsPublic, plan.IsActive, plan.IsSystemFree, plan.SortOrder,
            Options: rules.Where(r => r.Availability != OptionAvailability.Unavailable)
                .Select(r => new AdminPlanOptionRuleDto(r.OptionId, r.Availability.ToString(), r.IncludedQuantity)).ToList(),
            subscribedAccounts,
            IsSystemTrial: plan.IsSystemTrial,
            OptionCoverage: new AdminPlanOptionCoverageDto(configured, total, $"В тариф включено {configured} из {total} опций каталога"));
    }

    // N25 — shares its cap with PricingCatalogBuilder.MaxHighlights so the admin editor and the public
    // storefront agree on how many bullets survive.
    internal static List<string> SplitHighlights(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(Services.Billing.PricingCatalogBuilder.MaxHighlights).ToList();

    private static string? JoinHighlights(List<string>? highlights) =>
        highlights is null || highlights.Count == 0
            ? null
            : string.Join('\n', highlights.Take(Services.Billing.PricingCatalogBuilder.MaxHighlights));

    // SubscriptionPlanConfigs.PricePerMonth is an unbounded `numeric` column (AppDbContext/migrations),
    // so an extreme value here doesn't overflow the DB the way SubscriptionOption.PricePerMonth's
    // `numeric(10,2)` does. Bounded anyway (cycle-07 backend report, item 3): the pricing screen treats
    // plans and options as one catalog, a plan's price and a subscribed option's price are summed in the
    // same `decimal` arithmetic (BillingCalculator.TotalMonthlyPrice), and a denormalized plan price is
    // exactly the kind of value that turns a later addition/multiplication into an OverflowException
    // (an unhandled 500) even though nothing overflowed at write time. Same ceiling as
    // AdminBillingController.MaxOptionPricePerMonth so the two halves of the catalog agree on what
    // "too large" means.
    public const decimal MaxPlanPricePerMonth = 99_999_999.99m;

    internal static IActionResult? ValidatePlanInput(AdminPlanInput dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 100)
            return new BadRequestObjectResult("Название тарифа обязательно (до 100 символов).");
        if (dto.PricePerMonth < 0)
            return new BadRequestObjectResult("Цена не может быть отрицательной.");
        if (dto.PricePerMonth > MaxPlanPricePerMonth)
            return new BadRequestObjectResult($"Цена не может превышать {MaxPlanPricePerMonth}.");
        if (dto.PhotoQuotaMb is < 0)
            return new BadRequestObjectResult("Photo quota must not be negative.");
        if (dto.MaxEmployees is < 0)
            return new BadRequestObjectResult("MaxEmployees must not be negative.");
        if (dto.MaxCompanies is < 0)
            return new BadRequestObjectResult("MaxCompanies must not be negative.");
        var highlightsError = Services.Billing.PricingCatalogBuilder.ValidateHighlights(dto.Highlights);
        if (highlightsError is not null)
            return new BadRequestObjectResult(highlightsError);
        return null;
    }

    // contracts/cycle7/openapi.yaml AdminPlanInput.options: the FULL desired availability matrix for the plan —
    // rows not present are removed (an option that used to be Included/Extra and is now omitted becomes
    // Unavailable), matching the contract's "полная матрица" wording for the read side.
    // B6: an unknown Availability string or a nonexistent OptionId used to throw (Enum.Parse, no
    // existence check) and surface as a 500 instead of a 400 — validate everything up front, without
    // writing anything, before touching the DbContext.
    private async Task<IActionResult?> ApplyOptionRulesAsync(Guid planId, List<AdminPlanOptionRuleDtoV2>? desired)
    {
        var desiredList = desired ?? [];

        foreach (var d in desiredList)
        {
            if (!Enum.TryParse<OptionAvailability>(d.Availability, out _))
                return new BadRequestObjectResult($"Неизвестное значение availability: «{d.Availability}».");
            // NB-7 — an unvalidated negative IncludedQuantity feeds straight into
            // BillingCalculator.MonthlyPriceFor's Math.Max(0, quantity - includedQuantity), which for a
            // negative includedQuantity charges for MORE than the account actually bought.
            if (d.IncludedQuantity is < 0)
                return new BadRequestObjectResult("IncludedQuantity must not be negative.");
        }

        var optionIds = desiredList.Select(d => d.OptionId).ToList();
        var knownOptionIds = await db.SubscriptionOptions.Where(o => optionIds.Contains(o.Id)).Select(o => o.Id).ToListAsync();
        var unknown = optionIds.Except(knownOptionIds).ToList();
        if (unknown.Count > 0)
            return new BadRequestObjectResult($"Опция(и) не найдены: {string.Join(", ", unknown)}.");

        var existing = await db.PlanOptionRules.Where(r => r.PlanConfigId == planId).ToListAsync();

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
        return null;
    }

    /// <summary>ARCHITECTURE_CYCLE7.md §43.4: exactly one row may have <c>IsSystemFree == true</c>, and
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
        [FromQuery] NotificationTransport? transport,
        [FromQuery] int? page, [FromQuery] int? pageSize)
    {
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        var query = db.NotificationChannels.AsNoTracking().Include(c => c.Assignments).AsQueryable();
        if (state.HasValue) query = query.Where(c => c.State == state);
        // ARCHITECTURE_CYCLE9.md §114.3 (US-121) — ?transport= filter, additive.
        if (transport.HasValue) query = query.Where(c => c.Transport == transport);

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
                c.Id, c.Transport, c.State, ChannelPaymentState.Of(c, nowUtc),
                owner is null ? "" : $"{owner.FirstName} {owner.LastName}",
                owner?.PhoneNumber is null ? null : PhoneDisplayMask.Mask(owner.PhoneNumber),
                c.PaidFromUtc, c.PaidUntilUtc, c.Assignments.Count, c.IdleSinceUtc, c.RequestedAtUtc,
                c.Inn, c.LegalEntityForm);
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

    // contracts/cycle7/openapi.yaml (legacyChannelPayment, redaction 2.1): retired in favor of
    // PUT /admin/billing-accounts/{accountId}/subscription (AdminBillingController, implemented this
    // cycle), which folds the notification-channel option into the account's option matrix.
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
        [FromServices] Services.Notifications.PlatformSettings platformSettings,
        [FromServices] Services.Legal.LegalDocumentProvider legalDocuments)
    {
        var price = await platformSettings.GetChannelPricePerMonthAsync();
        var idleDays = await platformSettings.GetChannelIdleDaysAsync();
        var pricingPublicEnabled = await pricingCatalogCache.IsPublicEnabledAsync();
        var blockedReason = PricingCatalogCache.GetPublicationBlockReason(legalDocuments.Current);
        var trialDurationDays = await platformSettings.GetTrialDurationDaysAsync();
        var trialMailingWindowDays = await platformSettings.GetTrialMailingWindowDaysAsync();
        var trialWarningThresholdsDays = (await platformSettings.GetTrialWarningThresholdsDaysAsync()).ToList();
        return Ok(new AdminPlatformSettingsDto(
            price, idleDays, pricingPublicEnabled, blockedReason,
            trialDurationDays, trialMailingWindowDays, trialWarningThresholdsDays));
    }

    [HttpPut("platform-settings")]
    public async Task<ActionResult<AdminPlatformSettingsDto>> UpdatePlatformSettings(
        [FromBody] AdminPlatformSettingsDto dto, [FromServices] Services.Notifications.PlatformSettings platformSettings,
        [FromServices] Services.Legal.LegalDocumentProvider legalDocuments)
    {
        if (dto.ChannelIdleDays is < 0 or > 60) return BadRequest("channelIdleDays must be between 0 and 60");
        if (dto.ChannelPricePerMonth is < 0) return BadRequest("channelPricePerMonth must not be negative");

        // Cycle 18 (§367): trialDurationDays/trialMailingWindowDays out of 1..365, or the window bigger
        // than the duration, or the thresholds not 1..5 distinct positive values not exceeding the
        // duration, or the thresholds disagreeing with what the CURRENT activation-terms edition
        // literally promises (§367.1 — "текст называет числа буквально") are all 400, not silently
        // clamped or ignored.
        if (dto.TrialDurationDays is { } trialDurationDays && trialDurationDays is < 1 or > 365)
            return BadRequest("trialDurationDays должен быть от 1 до 365.");
        if (dto.TrialMailingWindowDays is { } trialMailingWindowDaysInput)
        {
            if (trialMailingWindowDaysInput is < 1 or > 365)
                return BadRequest("trialMailingWindowDays должен быть от 1 до 365.");
            var effectiveDuration = dto.TrialDurationDays ?? await platformSettings.GetTrialDurationDaysAsync();
            if (effectiveDuration is { } d && trialMailingWindowDaysInput > d)
                return BadRequest("trialMailingWindowDays не может быть больше trialDurationDays.");
        }
        if (dto.TrialWarningThresholdsDays is { } thresholdsInput)
        {
            if (thresholdsInput.Count is 0 or > 5 || thresholdsInput.Any(t => t <= 0) || thresholdsInput.Distinct().Count() != thresholdsInput.Count)
                return BadRequest("trialWarningThresholdsDays должен содержать от 1 до 5 различных положительных значений.");
            var effectiveDuration = dto.TrialDurationDays ?? await platformSettings.GetTrialDurationDaysAsync();
            if (effectiveDuration is { } d && thresholdsInput.Any(t => t > d))
                return BadRequest("trialWarningThresholdsDays не может превышать trialDurationDays.");
            var promised = Services.Billing.TrialTermsRegistry.CurrentPromisedThresholds;
            if (!thresholdsInput.OrderByDescending(t => t).SequenceEqual(promised.OrderByDescending(t => t)))
                return BadRequest(
                    $"Текущая редакция текста активации обещает пороги {string.Join(", ", promised)}; " +
                    "другие значения возможны только с новой редакцией текста от legal-counsel.");
        }

        var oldPrice = await platformSettings.GetChannelPricePerMonthAsync();
        var oldIdleDays = await platformSettings.GetChannelIdleDaysAsync();
        var oldPricingPublicEnabled = await pricingCatalogCache.IsPublicEnabledAsync();
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // ARCHITECTURE_CYCLE11.md §102.7/§114.2: turning the switch ON while the channel offer is a
        // draft (or unreadable) is rejected wholesale — the switch doesn't move, nothing else in this
        // request is applied either, and no change-log row is written. Turning it OFF is always allowed.
        if (dto.PricingPublicEnabled && !oldPricingPublicEnabled)
        {
            var blockReason = PricingCatalogCache.GetPublicationBlockReason(legalDocuments.Current);
            if (blockReason is not null)
            {
                var offer = legalDocuments.Current?.Get(Core.Enums.LegalDocumentType.TermsOwner);
                return Conflict(new PricingPublicationBlockedDto(
                    blockReason,
                    blockReason == "OfferIsDraft"
                        ? "Публичные цены нельзя включить: оферта на подключение канала (Приложение № 1 к Соглашению с компанией) — черновая редакция."
                        : "Публичные цены нельзя включить: снимок правовых документов недоступен.",
                    "TermsOwner",
                    offer?.Version));
            }
        }

        // Reviewer note: previously wrote (and journaled) both keys unconditionally, even when the
        // request left one of them unchanged — a no-op "save" produced a change-log row that recorded no
        // actual change, and every PUT (again, even a no-op one) went stale-for-60s on the read side.
        // Guarded per key now; cache invalidated only for the key(s) that actually moved. Invalidation
        // itself is deferred until after SaveChangesAsync below — invalidating first opens a window where
        // a concurrent reader repopulates the cache from the not-yet-committed old row and pins the stale
        // value for the cache's full TTL even though the write already succeeded.
        var priceChanged = oldPrice != dto.ChannelPricePerMonth;
        if (priceChanged)
        {
            await PlatformSettingsWriter.WriteAsync(
                db, PlatformSettingsWriter.PriceKey, oldPrice?.ToString(CultureInfo.InvariantCulture),
                dto.ChannelPricePerMonth?.ToString(CultureInfo.InvariantCulture) ?? "", userId);
        }

        var idleDaysChanged = oldIdleDays != dto.ChannelIdleDays;
        if (idleDaysChanged)
        {
            await PlatformSettingsWriter.WriteAsync(
                db, PlatformSettingsWriter.IdleDaysKey, oldIdleDays.ToString(CultureInfo.InvariantCulture),
                dto.ChannelIdleDays.ToString(CultureInfo.InvariantCulture), userId);
        }

        // ARCHITECTURE_CYCLE7.md §48: the "рубильник" for the public price list (GET /api/pricing). Was
        // previously settable only by hand-editing the PlatformSettings row directly in the database —
        // see the standing comment on PricingCatalogCache.Invalidate — this is the admin lever for it.
        var pricingPublicEnabledChanged = oldPricingPublicEnabled != dto.PricingPublicEnabled;
        if (pricingPublicEnabledChanged)
        {
            await PlatformSettingsWriter.WriteAsync(
                db, PricingCatalogCache.PublicEnabledSettingKey,
                oldPricingPublicEnabled ? "true" : "false", dto.PricingPublicEnabled ? "true" : "false", userId);

            // §59: Information-level log for the price-list publication toggle, by whom.
            logger.LogInformation(
                "Public pricing catalog {State} by {UserId}",
                dto.PricingPublicEnabled ? "enabled" : "disabled", userId);
        }

        // Cycle 18 (§367): null/absent means "не менять" — only a present value ever gets written.
        bool trialDurationChanged = false, trialWindowChanged = false, trialThresholdsChanged = false;
        if (dto.TrialDurationDays.HasValue)
        {
            var oldTrialDuration = await platformSettings.GetTrialDurationDaysAsync();
            trialDurationChanged = oldTrialDuration != dto.TrialDurationDays;
            if (trialDurationChanged)
                await PlatformSettingsWriter.WriteAsync(
                    db, Services.Notifications.PlatformSettings.TrialDurationDaysKey,
                    oldTrialDuration?.ToString(CultureInfo.InvariantCulture),
                    dto.TrialDurationDays.Value.ToString(CultureInfo.InvariantCulture), userId);
        }
        if (dto.TrialMailingWindowDays.HasValue)
        {
            var oldTrialWindow = await platformSettings.GetTrialMailingWindowDaysAsync();
            trialWindowChanged = oldTrialWindow != dto.TrialMailingWindowDays;
            if (trialWindowChanged)
                await PlatformSettingsWriter.WriteAsync(
                    db, Services.Notifications.PlatformSettings.TrialMailingWindowDaysKey,
                    oldTrialWindow?.ToString(CultureInfo.InvariantCulture),
                    dto.TrialMailingWindowDays.Value.ToString(CultureInfo.InvariantCulture), userId);
        }
        if (dto.TrialWarningThresholdsDays is { } newThresholds)
        {
            var oldThresholds = await platformSettings.GetTrialWarningThresholdsDaysAsync();
            var oldThresholdsRaw = string.Join(",", oldThresholds);
            var newThresholdsRaw = string.Join(",", newThresholds);
            trialThresholdsChanged = oldThresholdsRaw != newThresholdsRaw;
            if (trialThresholdsChanged)
                await PlatformSettingsWriter.WriteAsync(
                    db, Services.Notifications.PlatformSettings.TrialWarningThresholdsDaysKey,
                    oldThresholdsRaw, newThresholdsRaw, userId);
        }

        await db.SaveChangesAsync();

        if (priceChanged) platformSettings.InvalidateCache(PlatformSettingsWriter.PriceKey);
        if (idleDaysChanged) platformSettings.InvalidateCache(PlatformSettingsWriter.IdleDaysKey);
        if (pricingPublicEnabledChanged) pricingCatalogCache.Invalidate();
        if (trialDurationChanged) platformSettings.InvalidateCache(Services.Notifications.PlatformSettings.TrialDurationDaysKey);
        if (trialWindowChanged) platformSettings.InvalidateCache(Services.Notifications.PlatformSettings.TrialMailingWindowDaysKey);
        if (trialThresholdsChanged) platformSettings.InvalidateCache(Services.Notifications.PlatformSettings.TrialWarningThresholdsDaysKey);

        // Reviewer note: echoing `dto` back here would leak client-supplied fields the server never
        // validated or stored as-is (e.g. `pricingPublicBlockedReason`, which GET always recomputes from
        // the live legal snapshot). Recompute and return the same shape GET produces: re-read price/idle
        // days back from the writer (post-invalidation, so this reflects exactly what was persisted rather
        // than trusting the client-supplied `dto` values verbatim) and recompute the blocked reason.
        var freshPrice = await platformSettings.GetChannelPricePerMonthAsync();
        var freshIdleDays = await platformSettings.GetChannelIdleDaysAsync();
        var freshBlockedReason = PricingCatalogCache.GetPublicationBlockReason(legalDocuments.Current);
        var freshTrialDuration = await platformSettings.GetTrialDurationDaysAsync();
        var freshTrialWindow = await platformSettings.GetTrialMailingWindowDaysAsync();
        var freshTrialThresholds = (await platformSettings.GetTrialWarningThresholdsDaysAsync()).ToList();
        return Ok(new AdminPlatformSettingsDto(
            freshPrice, freshIdleDays, dto.PricingPublicEnabled, freshBlockedReason,
            freshTrialDuration, freshTrialWindow, freshTrialThresholds));
    }

    // Plain-text 410 body per contracts/cycle7/openapi.yaml's `text/plain: {schema: {type: string}}` response —
    // shared by both cycle-5 retired routes so they always point callers at the same replacement.
    private static IActionResult LegacyEndpointGone(string replacementRoute) =>
        new ContentResult
        {
            StatusCode = StatusCodes.Status410Gone,
            Content = $"Этот маршрут упразднён. Используйте {replacementRoute}.",
            ContentType = "text/plain; charset=utf-8",
        };

    private static AdminChannelDto MapAdminChannelDto(NotificationChannel channel, int idleDays) => new(
        channel.Id, channel.Transport, channel.State, ChannelPaymentState.Of(channel, DateTime.UtcNow), "", null,
        channel.PaidFromUtc, channel.PaidUntilUtc, channel.Assignments.Count, channel.IdleSinceUtc, channel.RequestedAtUtc,
        channel.Inn, channel.LegalEntityForm);

    // ── Retention policy (T5-B8/B9, ARCHITECTURE_CYCLE5.md §49.5) ────────────────

    // §49.5: "сроки не переписываются руками в документ, а выгружаются из работающей конфигурации" —
    // this endpoint reads the SAME IOptions<RetentionPeriods> every rule reads, so a declared-vs-actual
    // mismatch is structurally impossible. [FromServices], same reasoning as GetScheduledTasks above:
    // only this one action pays for resolving it.
    [HttpGet("retention/policy")]
    public ActionResult<RetentionPolicyDto> GetRetentionPolicy(
        [FromServices] Microsoft.Extensions.Options.IOptions<Services.Retention.RetentionPeriods> periods,
        [FromServices] IConfiguration config)
    {
        var p = periods.Value;
        var dryRun = config.GetSection("ScheduledTasks:data-retention").GetValue("DryRun", true);

        return Ok(new RetentionPolicyDto(
            p.NotificationBodyDays, p.NotificationMetadataDays, p.TemplateHistoryDays,
            p.InactiveAccountDays, p.BookingPersonalizationDays, p.ClientNoteDays, p.ClientNotePhotoDays,
            p.ClientHealthNoteDays, p.ConsentRecordDays, p.ChannelStateEventDays, p.PaymentLogDays,
            p.MailLogDays, p.AppLogDays, dryRun));
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

// T5-B10 (ARCHITECTURE_CYCLE5.md §50.1: "счётчик просроченных попадает в существующую админскую
// сводку") — appended at the end with a default so any existing positional construction keeps compiling.
public record AdminStatsDto(
    int TotalCompanies, int TotalUsers, int TotalBookings, int CompletedBookings, decimal TotalRevenue,
    int OverdueSubjectRequests = 0);

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


public record SubscriptionDiagnosticsDto(
    string OwnerUserId, string OwnerName, Guid? PlanConfigId, string? PlanName, DateTime? PaidUntil,
    bool IsActive, bool PlanIsActive, SubscriptionStatus Status, string StatusText,
    EffectivePlan Effective, List<SubscriptionDiagnosticsCompanyDto> Companies);

public record SubscriptionDiagnosticsCompanyDto(
    Guid CompanyId, string Name, bool AllowSelfBooking, bool OnlineBookingEnabled,
    PlanNotAppliedReason BlockingReason);

// PUT /api/admin/plans/{id} body. Cycle-5 fields are nullable and applied only when present in the
// request (see UpdatePlan) — binding straight into SubscriptionPlanConfig used to reset them to
// false/0/null whenever a caller that doesn't know about them (the current admin UI) omitted them.
public record UpdatePlanDto(
    string Name, decimal PricePerMonth, int? MaxEmployees, int? MaxCompanies,
    bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics, bool AllowPublicListing, bool AllowOnlinePayment,
    int? PhotoQuotaMb, PhotoRetention PhotoRetention, string? Description, bool IsActive, int NotifyDaysBefore,
    string? Highlights = null, bool? IsPublic = null, int? SortOrder = null, bool? IsSystemFree = null);

// contracts/cycle7/openapi.yaml AdminPlanDto/PlanOptionRuleDto — `Options` is always empty (see MapAdminPlanDto)
// until the BillingAccount option catalog exists (cycle-07 backend report).
public record AdminPlanOptionRuleDto(Guid OptionId, string Availability, int? IncludedQuantity);

public record AdminPlanDto(
    Guid Id, string Name, string? Description, List<string> Highlights, decimal PricePerMonth, string Currency,
    int? MaxEmployees, int? MaxCompanies, bool AllowOnlineBooking, bool AllowMailing, bool AllowAnalytics,
    bool AllowPublicListing, bool AllowOnlinePayment, int? PhotoQuotaMb, PhotoRetention PhotoRetention,
    int NotifyDaysBefore, bool IsPublic, bool IsActive, bool IsSystemFree, int SortOrder,
    List<AdminPlanOptionRuleDto> Options, int SubscribedAccounts,
    bool IsSystemTrial = false, AdminPlanOptionCoverageDto? OptionCoverage = null);

// Cycle 18 (API_CONTRACT_CYCLE18.md §366) — "отсутствие строки PlanOptionRule = Unavailable" is
// fail-closed behaviour, not a defect, but a superadmin must be able to SEE it on the plan's own card.
public record AdminPlanOptionCoverageDto(int Configured, int Total, string Text);

public record SetSystemTrialInput(bool IsSystemTrial);

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

// Inn/LegalEntityForm appended (code review, "заодно"): the owner-facing channel read already exposes
// both (NotificationChannelsController); SuperAdmin — who has to reconcile the same channel against
// invoicing/compliance — was the one reader who couldn't see either.
// ARCHITECTURE_CYCLE9.md §104.3/§114.3 (US-121) — Transport is additive, inserted right after Id;
// every other field keeps its name and position.
public record AdminChannelDto(
    Guid Id, NotificationTransport Transport, ChannelState State, ChannelPaymentStatus PaymentState,
    string OwnerName, string? OwnerPhoneMasked,
    DateTime? PaidFrom, DateTime? PaidUntil, int CompanyCount, DateTime? IdleSince, DateTime? RequestedAt,
    string? Inn = null, LegalEntityForm? LegalEntityForm = null);

// T5-B8/B9 (ARCHITECTURE_CYCLE5.md §49.5) — the actual configured retention values, for publication in
// the platform's privacy policy and for the lawyer's own periodic check (US-73 п. 7, US-80 п. 5).
public record RetentionPolicyDto(
    int NotificationBodyDays, int NotificationMetadataDays, int TemplateHistoryDays,
    int InactiveAccountDays, int BookingPersonalizationDays, int ClientNoteDays, int ClientNotePhotoDays,
    int ClientHealthNoteDays, int ConsentRecordDays, int ChannelStateEventDays, int PaymentLogDays,
    int MailLogDays, int AppLogDays, bool DryRun);

public record AdminChannelSummaryDto(
    int Connected, int Connecting, int Disconnected, int Blocked,
    int NeedsReconnect, int Idle, int ExpiringIn7Days, int PendingRequests);

public record AdminChannelPaymentDto(DateOnly PaidFrom, DateOnly PaidUntil, decimal? Amount, string? Comment);

public record AdminChannelSuspendDto(string? Comment);

// pricingPublicBlockedReason: ARCHITECTURE_CYCLE11.md §114.1 — nullable, added by cycle 11. Absent/null
// means no obstacle to turning the switch on; a non-null value is one of "OfferIsDraft"/"LegalUnavailable"
// and the request body never needs to set it (round-tripped by GetPlatformSettings/UpdatePlatformSettings
// sharing this one DTO, its value on write is ignored).
// Cycle 18 (API_CONTRACT_CYCLE18.md §367) — three trailing trial fields, all nullable on input (PUT):
// null/absent = "не менять" (§367), same convention as AdminPlanInput.IsPublic/SortOrder above.
public record AdminPlatformSettingsDto(
    decimal? ChannelPricePerMonth, int ChannelIdleDays, bool PricingPublicEnabled, string? PricingPublicBlockedReason = null,
    int? TrialDurationDays = null, int? TrialMailingWindowDays = null, List<int>? TrialWarningThresholdsDays = null);

// ARCHITECTURE_CYCLE11.md §114.2 — 409 body for PUT /api/admin/platform-settings when
// pricingPublicEnabled: true is rejected because the channel offer isn't published.
public record PricingPublicationBlockedDto(string Reason, string Message, string DocumentType, string? Version);
