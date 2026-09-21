using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services;
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
    AppDbContext db, UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager) : ControllerBase
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

        // Subscriptions are account-level (bound to the owner), so surface each user's plan and how many
        // companies they own here — this Users tab is where an admin manages the tariff, not per company.
        var subs = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .Where(s => userIds.Contains(s.OwnerUserId)).ToListAsync();
        var ownedCounts = await db.Companies
            .Where(c => userIds.Contains(c.OwnerUserId))
            .GroupBy(c => c.OwnerUserId)
            .Select(g => new { OwnerUserId = g.Key, Count = g.Count() })
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
            var sub = subs.FirstOrDefault(s => s.OwnerUserId == u.Id);
            var ownedCount = ownedCounts.FirstOrDefault(x => x.OwnerUserId == u.Id)?.Count ?? 0;
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
            return new AdminCompanyDto(c.Id, c.Name, c.Slug, c.Email, c.Phone, c.IsActive, c.AllowSelfBooking, c.CreatedAt,
                c.Members.Count, count, c.OwnerUserId, ownerEmails.GetValueOrDefault(c.OwnerUserId, c.OwnerUserId),
                sub?.PlanConfigId, sub?.PlanConfig?.Name ?? "Free", sub?.PaidUntil, sub?.IsActive ?? true);
        }).ToList();

        return Ok(Pagination.Create(result, currentPage, currentPageSize, total));
    }

    [HttpPut("owners/{ownerUserId}/subscription")]
    public async Task<IActionResult> UpdateSubscription(string ownerUserId, [FromBody] UpdateSubscriptionDto dto)
    {
        // Both existence checks happen BEFORE any write: previously a typo'd ownerUserId or planConfigId
        // sailed through to SaveChangesAsync and failed on the FK constraint with an unhandled 500
        // (audit D2) instead of a clean 404.
        var ownerExists = await db.Users.AnyAsync(u => u.Id == ownerUserId);
        if (!ownerExists) return NotFound("Owner not found");

        if (dto.PlanConfigId.HasValue)
        {
            var plan = await db.SubscriptionPlanConfigs.FindAsync(dto.PlanConfigId.Value);
            if (plan is null) return NotFound("Plan not found");
            if (!plan.IsActive) return BadRequest("Plan is not active");
        }

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
        // Code review finding: a deleted account is a tombstone (ARCHITECTURE.md §7.4) — no password,
        // permanently locked out, phone/email scrubbed. Without this check SuperAdmin could hand a
        // company to an account nobody can ever log into again.
        if (newOwner.DeletedAtUtc is not null) return BadRequest("User account has been deleted");

        var oldOwnerUserId = company.OwnerUserId;
        company.OwnerUserId = newOwner.Id;

        await using var transaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"company-members:{id}");

        // US-46, ARCHITECTURE.md §19.3 (bug found during that history's design): Company.OwnerUserId
        // and the CompanyMember row carrying the CompanyOwner role must move together. Before this fix
        // they didn't — this endpoint changed OwnerUserId and granted the Identity role to the new
        // owner, but never touched the old owner's CompanyMember row, so IdentityRoleSync (which is
        // driven purely by CompanyMember rows) would recompute CompanyOwner for someone no longer the
        // owner. The old owner keeps their membership — they may still work here — but is demoted to
        // Master rather than left holding a CompanyOwner row for a company they no longer own.
        if (oldOwnerUserId != newOwner.Id)
        {
            var oldMembership = await db.CompanyMembers.FirstOrDefaultAsync(cm =>
                cm.CompanyId == id && cm.UserId == oldOwnerUserId && cm.Role == UserRole.CompanyOwner);
            if (oldMembership is not null)
                oldMembership.Role = UserRole.Master;
        }

        var membership = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.CompanyId == id && cm.UserId == newOwner.Id);
        if (membership is null)
            db.CompanyMembers.Add(new CompanyMember { Id = Guid.NewGuid(), CompanyId = id, UserId = newOwner.Id, Role = UserRole.CompanyOwner });
        else
            membership.Role = UserRole.CompanyOwner;

        // B9 / SPEC US-56 п. 3: a WhatsApp channel is bound to the OWNER's account, not the company —
        // handing the company to someone else must not leave it (and its clients' replies) going through
        // the previous owner's personal number. §25.3 names this exact call site. Same transaction as the
        // ownership move: no window where the assignment survives a committed owner change.
        if (oldOwnerUserId != newOwner.Id)
        {
            var assignment = await db.ChannelCompanyAssignments.FirstOrDefaultAsync(a => a.CompanyId == id);
            if (assignment is not null)
            {
                db.ChannelCompanyAssignments.Remove(assignment);

                var pending = await db.OutboundNotifications
                    .Where(n => n.CompanyId == id && n.Status == NotificationStatus.Pending)
                    .ToListAsync();
                foreach (var row in pending)
                {
                    row.Status = NotificationStatus.Cancelled;
                    row.Reason = NotificationReason.BookingOrAssignmentCancelled;
                }
            }
        }

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
        return Ok(plans);
    }

    [HttpPost("plans")]
    public async Task<IActionResult> CreatePlan([FromBody] SubscriptionPlanConfig dto)
    {
        // Negative quota has no sensible meaning (unlike null, which means "unlimited") — reject before
        // the row exists, the same way every other tariff validation in this controller does (US-24 p.3).
        if (dto.PhotoQuotaMb is < 0) return BadRequest("Photo quota must not be negative.");

        dto.Id = Guid.NewGuid();
        dto.CreatedAt = DateTime.UtcNow;
        db.SubscriptionPlanConfigs.Add(dto);
        await db.SaveChangesAsync();
        return Ok(dto);
    }

    [HttpPut("plans/{id:guid}")]
    public async Task<IActionResult> UpdatePlan(Guid id, [FromBody] SubscriptionPlanConfig dto)
    {
        if (dto.PhotoQuotaMb is < 0) return BadRequest("Photo quota must not be negative.");

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
        plan.PhotoQuotaMb = dto.PhotoQuotaMb;
        plan.PhotoRetention = dto.PhotoRetention;
        plan.Description = dto.Description;
        plan.NotifyDaysBefore = dto.NotifyDaysBefore;

        // Deactivating through this endpoint has exactly the effect DeletePlan refuses below: the
        // resolver treats PlanConfig.IsActive == false as Free, so every subscriber silently loses
        // online booking, analytics and their employee limit on the next request. Same guard, same
        // status, or the 409 there is just a speed bump around a differently-named door.
        if (plan.IsActive && !dto.IsActive)
        {
            var activeSubscribers = await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == id && s.IsActive);
            if (activeSubscribers > 0)
                return Conflict($"Cannot deactivate a plan with {activeSubscribers} active subscriber(s). Move them to another plan first.");
        }
        plan.IsActive = dto.IsActive;

        await db.SaveChangesAsync();
        return Ok(plan);
    }

    [HttpDelete("plans/{id:guid}")]
    public async Task<IActionResult> DeletePlan(Guid id)
    {
        var plan = await db.SubscriptionPlanConfigs.FindAsync(id);
        if (plan is null) return NotFound();

        // Deactivating a plan that still has active subscribers would silently strip their features on
        // their very next request (SubscriptionResolver.Resolve treats PlanConfig.IsActive == false as
        // Free) — the admin must move them off the plan first (see UpdateSubscription).
        var subscriberCount = await db.AccountSubscriptions.CountAsync(s => s.PlanConfigId == id && s.IsActive);
        if (subscriberCount > 0)
            return Conflict($"Cannot delete a plan with {subscriberCount} active subscriber(s). Move them to another plan first.");

        plan.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
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

    [HttpPost("notification-channels/{id:guid}/payment")]
    public async Task<ActionResult<AdminChannelDto>> RecordChannelPayment(Guid id, [FromBody] AdminChannelPaymentDto dto)
    {
        var channel = await db.NotificationChannels.Include(c => c.Assignments).FirstOrDefaultAsync(c => c.Id == id);
        if (channel is null) return NotFound();

        var paidFromUtc = DateTime.SpecifyKind(dto.PaidFrom.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var paidUntilUtc = DateTime.SpecifyKind(dto.PaidUntil.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        if (paidUntilUtc <= paidFromUtc) return BadRequest("paidUntil must be after paidFrom");

        var oldPaidUntil = channel.PaidUntilUtc;
        db.ChannelPaymentLogs.Add(new ChannelPaymentLog
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            ChangedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
            OldPaidUntil = oldPaidUntil,
            NewPaidUntil = paidUntilUtc,
            Amount = dto.Amount,
            Comment = dto.Comment,
        });

        channel.PaidFromUtc = paidFromUtc;
        channel.PaidUntilUtc = paidUntilUtc;
        // API_CONTRACT_CYCLE4.md §34.3: marking payment clears idleness — ChannelHealthTask's own next
        // pass would clear it anyway (a live paid period with an active company means "not idle"), but
        // clearing it here means an admin doesn't have to explain a 15-minute lag to an owner watching.
        channel.IdleSinceUtc = null;
        channel.IdleWarningSentAtUtc = null;

        await db.SaveChangesAsync();

        var idleDays = await HttpContext.RequestServices.GetRequiredService<Services.Notifications.PlatformSettings>().GetChannelIdleDaysAsync();
        return Ok(MapAdminChannelDto(channel, idleDays));
    }

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
        return Ok(new AdminPlatformSettingsDto(price, idleDays));
    }

    [HttpPut("platform-settings")]
    public async Task<ActionResult<AdminPlatformSettingsDto>> UpdatePlatformSettings(
        [FromBody] AdminPlatformSettingsDto dto, [FromServices] Services.Notifications.PlatformSettings platformSettings)
    {
        if (dto.ChannelIdleDays is < 0 or > 60) return BadRequest("channelIdleDays must be between 0 and 60");
        if (dto.ChannelPricePerMonth is < 0) return BadRequest("channelPricePerMonth must not be negative");

        var oldPrice = await platformSettings.GetChannelPricePerMonthAsync();
        var oldIdleDays = await platformSettings.GetChannelIdleDaysAsync();
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

        await db.SaveChangesAsync();
        return Ok(dto);
    }

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

public record AdminPlatformSettingsDto(decimal? ChannelPricePerMonth, int ChannelIdleDays);
