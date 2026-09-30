using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.DTOs.Legal;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Bookings;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "SuperAdmin")]
public class AdminController(
    AppDbContext db, UserManager<AppUser> userManager, RoleManager<IdentityRole> roleManager,
    CompanyOwnerWriter companyOwnerWriter, SubscriptionResolver subscriptionResolver,
    // ARCHITECTURE_CYCLE20.md §407.2 (LG6) and §410 (manual subject requests' background signal).
    CompanyTransferService companyTransferService, ILogger<AdminController> logger,
    ServiceBooking.API.Services.PublicSites.PublicSiteLinks siteLinks) : ControllerBase
{
    // ── Stats ──────────────────────────────────────────────────────────────────

    [HttpGet("stats")]
    public async Task<ActionResult<AdminStatsDto>> GetStats(CancellationToken ct)
    {
        // ARCHITECTURE_CYCLE28.md §578, API_CONTRACT_CYCLE28.md §594.3: the platform totals are WITHOUT the showcase (fictional companies, their
        // users and bookings); three new counters carry the showcase itself.
        var totalCompanies = await db.Companies.CountAsync(c => !c.IsShowcase, ct);
        var totalUsers = await db.Users.CountAsync(u => !u.IsShowcase, ct);
        var totalBookings = await db.Bookings.CountAsync(b => b.ShowcaseKind == ShowcaseBookingKind.None, ct);
        var completedBookings = await db.Bookings.CountAsync(
            b => b.Status == BookingStatus.Completed && b.ShowcaseKind == ShowcaseBookingKind.None, ct);
        var showcaseCompanies = await db.Companies.CountAsync(c => c.IsShowcase, ct);
        var showcaseUsers = await db.Users.CountAsync(u => u.IsShowcase, ct);
        var showcaseBookings = await db.Bookings.CountAsync(b => b.ShowcaseKind != ShowcaseBookingKind.None, ct);

        var revenueByService = await db.Bookings
            .Where(b => b.Status == BookingStatus.Completed && b.ShowcaseKind == ShowcaseBookingKind.None)
            .GroupBy(b => 1)
            .Select(g => g.Sum(b => b.Price))
            .FirstOrDefaultAsync(ct);

        // §50.1: visible without opening the subject-requests section — a one-person, no-shift-rotation
        // operator (Р8) must see this without remembering to go looking for it.
        var nowUtc = DateTime.UtcNow;
        var overdueSubjectRequests = await db.SubjectRequests.CountAsync(r =>
            r.DueAtUtc < nowUtc && r.Status != SubjectRequestStatus.Answered && r.Status != SubjectRequestStatus.Rejected, ct);

        return Ok(new AdminStatsDto(totalCompanies, totalUsers, totalBookings, completedBookings, revenueByService, overdueSubjectRequests,
            showcaseCompanies, showcaseUsers, showcaseBookings));
    }

    // ── Subject requests (T5-B10, ARCHITECTURE_CYCLE5.md §50.1, US-74) ─────────────────────────────

    [HttpGet("subject-requests")]
    public async Task<ActionResult<PagedResult<SubjectRequestDto>>> GetSubjectRequests(
        [FromQuery] SubjectRequestStatus? status, [FromQuery] SubjectRequestKind? kind, [FromQuery] string? dueState,
        [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken ct)
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

        var total = await query.CountAsync(ct);
        // Urgent-first, always — §50.1: "самое горящее сверху", not a caller-chosen sort.
        var rows = await query.OrderBy(r => r.DueAtUtc)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .ToListAsync(ct);

        var handlerIds = rows.Where(r => r.HandlerUserId is not null).Select(r => r.HandlerUserId!).Distinct().ToList();
        var handlerNames = handlerIds.Count == 0 ? new Dictionary<string, string>()
            : await db.Users.Where(u => handlerIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        // ARCHITECTURE_CYCLE20.md §410 (US-20-09) — RegisteredByUserId is set only for a manually
        // registered (Email/PostalMail) row; batched the same way HandlerName already is above.
        var registeredByIds = rows.Where(r => r.RegisteredByUserId is not null).Select(r => r.RegisteredByUserId!).Distinct().ToList();
        var registeredByNames = registeredByIds.Count == 0 ? new Dictionary<string, string>()
            : await db.Users.Where(u => registeredByIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim(), ct);

        var items = rows.Select(r => new SubjectRequestDto(
            r.Id, r.Reference, r.Kind.ToString(), r.Status.ToString(), PhoneDisplayMask.Mask(r.SubjectPhone),
            r.ContactValue, r.Message, r.ReceivedAtUtc, r.DueAtUtc, ComputeDueState(r, nowUtc),
            r.AnsweredAtUtc, r.HandlerUserId is not null ? handlerNames.GetValueOrDefault(r.HandlerUserId) : null,
            r.Resolution, r.Channel.ToString(),
            r.RegisteredByUserId is not null ? registeredByNames.GetValueOrDefault(r.RegisteredByUserId) : null)).ToList();

        return Ok(Pagination.Create(items, currentPage, currentPageSize, total));
    }

    // ARCHITECTURE_CYCLE20.md §410, API_CONTRACT_CYCLE20.md §438 (US-20-09, Т20-13) — the manual
    // registration counterpart to SubjectRequestsController.Submit (the public, anonymous form), which
    // this action deliberately does NOT touch or reuse: no captcha, no rate limit (admin-only route),
    // and Channel is never WebForm here.
    [HttpPost("subject-requests")]
    public async Task<ActionResult<SubjectRequestDto>> RegisterSubjectRequest(
        [FromBody] RegisterSubjectRequestDto dto,
        [FromServices] Microsoft.Extensions.Options.IOptions<SubjectRequestOptions> options,
        [FromServices] Services.Signals.IGlitchTipSignalService signals)
    {
        if (!Enum.TryParse<SubjectRequestKind>(dto.Kind, ignoreCase: true, out var kind))
            return BadRequest("Укажите тип обращения.");

        if (!Enum.TryParse<SubjectRequestChannel>(dto.Channel, ignoreCase: true, out var channel)
            || channel == SubjectRequestChannel.WebForm)
            return BadRequest("Канал должен быть Email или PostalMail.");

        var nowUtc = DateTime.UtcNow;
        // Code-review finding (cycle 20) — see ManualSubjectRequestReceivedAt's own doc comment: the old
        // `DateTime.SpecifyKind(dto.ReceivedAt.Value, DateTimeKind.Utc)` shifted the deadline by the
        // server's local UTC offset whenever the caller supplied an explicit non-'Z' offset.
        var (receivedAtUtcOrNull, receivedAtError) = ManualSubjectRequestReceivedAt.Validate(dto.ReceivedAt, nowUtc);
        if (receivedAtError is not null) return BadRequest(receivedAtError);
        var receivedAtUtc = receivedAtUtcOrNull!.Value;

        // §438: phone is OPTIONAL here (unlike the public form) — a postal letter may not carry one.
        // An empty/omitted value is stored as "" (SubjectRequest.SubjectPhone is non-nullable), matching
        // §438's "для обращения без телефона phoneMasked — пустая строка".
        var canonicalPhone = "";
        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            if (!PhoneNormalizer.TryNormalize(dto.Phone, out canonicalPhone))
                return BadRequest("Укажите корректный номер телефона.");
        }

        if (string.IsNullOrWhiteSpace(dto.ContactValue))
            return BadRequest("Укажите контакт для ответа.");
        if (dto.ContactValue.Length > 200)
            return BadRequest("Контакт для ответа не должен превышать 200 символов.");
        if (string.IsNullOrWhiteSpace(dto.Message))
            return BadRequest("Опишите обращение.");
        if (dto.Message.Length > 4000)
            return BadRequest("Текст обращения не должен превышать 4000 символов.");

        // §410: DueAtUtc is computed from receivedAtUtc (the date the request actually arrived), not
        // from "now" (the date a superadmin got around to typing it in) — the deadline the customer
        // deserves under 152-ФЗ does not move just because logging it was delayed.
        var (dueAtUtc, _) = SubjectRequestDeadline.For(kind, receivedAtUtc, options.Value);
        var registeredByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        SubjectRequest? created = null;
        for (var attempt = 0; attempt < 5 && created is null; attempt++)
        {
            var reference = SubjectRequestReference.Generate();
            if (await db.SubjectRequests.AnyAsync(r => r.Reference == reference)) continue;

            created = new SubjectRequest
            {
                Id = Guid.NewGuid(),
                Reference = reference,
                Kind = kind,
                SubjectPhone = canonicalPhone,
                ContactValue = dto.ContactValue,
                Message = dto.Message,
                Status = SubjectRequestStatus.Received,
                ReceivedAtUtc = receivedAtUtc,
                DueAtUtc = dueAtUtc,
                Channel = channel,
                RegisteredByUserId = registeredByUserId,
            };
            db.SubjectRequests.Add(created);
            await db.SaveChangesAsync();
        }

        if (created is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Не удалось зарегистрировать обращение, попробуйте ещё раз.");

        // §410 — the same intake signal the public form sends, fire-and-forget after the row is durably
        // committed, for the same reason (a GlitchTip outage must not affect whether the request was
        // recorded). No phone, no message text — same composition as the public form's own signal.
        var signalMessage = $"Новое обращение субъекта: вид={kind}, референс={created.Reference}, срок={dueAtUtc:yyyy-MM-dd}";
        _ = SendSubjectRequestSignalInBackgroundAsync(signals, signalMessage, created.Reference);

        string? registeredByName = null;
        if (registeredByUserId is not null)
        {
            var registeredByUser = await userManager.FindByIdAsync(registeredByUserId);
            registeredByName = registeredByUser is null ? null : $"{registeredByUser.FirstName} {registeredByUser.LastName}".Trim();
        }
        var responseDto = new SubjectRequestDto(
            created.Id, created.Reference, created.Kind.ToString(), created.Status.ToString(),
            PhoneDisplayMask.Mask(created.SubjectPhone), created.ContactValue, created.Message,
            created.ReceivedAtUtc, created.DueAtUtc, ComputeDueState(created, nowUtc),
            created.AnsweredAtUtc, null, created.Resolution, created.Channel.ToString(), registeredByName);

        return CreatedAtAction(nameof(GetSubjectRequests), null, responseDto);
    }

    private async Task SendSubjectRequestSignalInBackgroundAsync(Services.Signals.IGlitchTipSignalService signals, string message, string reference)
    {
        try
        {
            await signals.SendAsync(message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GlitchTip intake signal failed to send for manually registered subject request {Reference}.", reference);
        }
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
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] string? showcase,
        CancellationToken ct)
    {
        if (!ShowcaseFilterParser.TryParse(showcase, out var showcaseFilter)) return BadRequest(ShowcaseFilterParser.InvalidText);
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        search = Pagination.SanitizeSearch(search);
        var query = db.Users.AsQueryable();
        query = showcaseFilter switch
        {
            ShowcaseFilter.Only => query.Where(u => u.IsShowcase),
            ShowcaseFilter.Exclude => query.Where(u => !u.IsShowcase),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Phones are stored canonical (digits only, US-26), so a search string that LOOKS like a
            // phone number (≥5 digits, no letters — a surname never satisfies this) is normalized the
            // same way before matching against PhoneNumber. Anything else (e.g. "Иванов", "ivanov@")
            // is searched as typed against every column — normalizing it would just strip letters out
            // of a name search and break it (ARCHITECTURE.md §11.2).
            var phoneSearch = PhoneNormalizer.ParseSearch(search).Term;

            // ParseSearch counts digits Unicode-aware (char.IsDigit), but PhoneNormalizer.Normalize only keeps
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
        var total = await query.CountAsync(ct);
        var users = await query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize).ToListAsync(ct);
        var userIds = users.Select(u => u.Id).ToList();

        // Subscriptions are account-level (§45.1) — resolve each user's billing account first, then
        // read plan/owned-company counts off THAT, not off OwnerUserId directly. This Users tab is
        // where an admin manages the tariff, not per company.
        var accounts = await db.BillingAccounts
            .Where(a => userIds.Contains(a.OwnerUserId))
            .Select(a => new { a.OwnerUserId, a.Id })
            .ToListAsync(ct);
        var accountIdByUser = accounts.ToDictionary(a => a.OwnerUserId, a => a.Id);
        var accountIds = accounts.Select(a => a.Id).ToList();
        var subs = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .Where(s => s.BillingAccountId != null && accountIds.Contains(s.BillingAccountId!.Value)).ToListAsync(ct);
        var ownedCounts = await db.Companies
            .Where(c => c.BillingAccountId != null && accountIds.Contains(c.BillingAccountId!.Value))
            .GroupBy(c => c.BillingAccountId!.Value)
            .Select(g => new { BillingAccountId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // US-49 p.3: ONE join for the whole page's roles instead of userManager.GetRolesAsync(u) inside
        // the loop below — the previous shape made N extra queries per page, independent of page size
        // only in the sense that it scaled with it instead. Pagination alone would only have masked this
        // (a smaller N is still N), not fixed it.
        var roleMap = await (from ur in db.UserRoles
                              join r in db.Roles on ur.RoleId equals r.Id
                              where userIds.Contains(ur.UserId)
                              select new { ur.UserId, r.Name }).ToListAsync(ct);
        var rolesByUser = roleMap.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name ?? "").ToList());

        var result = users.Select(u =>
        {
            var accountId = accountIdByUser.GetValueOrDefault(u.Id);
            var sub = accountId != Guid.Empty ? subs.FirstOrDefault(s => s.BillingAccountId == accountId) : null;
            var ownedCount = accountId != Guid.Empty ? ownedCounts.FirstOrDefault(x => x.BillingAccountId == accountId)?.Count ?? 0 : 0;
            return new AdminUserDto(u.Id, u.PhoneNumber ?? "", u.Email, u.FirstName, u.LastName, u.AvatarUrl, u.CreatedAt,
                rolesByUser.GetValueOrDefault(u.Id, []), ownedCount, sub?.PlanConfigId, sub?.PlanConfig?.Name ?? "Free",
                sub?.PaidUntil, sub?.IsActive ?? true, u.IsShowcase);
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
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize,
        [FromQuery] string? kind, [FromQuery] string? showcase, CancellationToken ct)
    {
        if (!ShowcaseFilterParser.TryParse(showcase, out var showcaseFilter)) return BadRequest(ShowcaseFilterParser.InvalidText);
        var (currentPage, currentPageSize) = Pagination.Normalize(page, pageSize);
        search = Pagination.SanitizeSearch(search);
        var query = db.Companies.AsNoTracking();
        query = showcaseFilter switch
        {
            ShowcaseFilter.Only => query.Where(c => c.IsShowcase),
            ShowcaseFilter.Exclude => query.Where(c => !c.IsShowcase),
            _ => query,
        };
        // Cycle 23 (§408.6): unlike the cabinet lists, "not passed" means ALL kinds here — the admin panel
        // manages both products.
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Services.Companies.CompanyKindQuery.TryParse(kind, out var wantedKind))
                return BadRequest(Services.Companies.CompanyKindQuery.UnknownKindText);
            query = query.Where(c => c.Kind == wantedKind);
        }
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.Name.Contains(search) || c.Email!.Contains(search));

        var total = await query.CountAsync(ct);
        // §375 F10: the member count is projected (COUNT in SQL), not every member row Include()d.
        var companies = await query.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip((currentPage - 1) * currentPageSize).Take(currentPageSize)
            .Select(c => new
            {
                c.Id, c.Name, c.Slug, c.Email, c.Phone, c.IsActive, c.AllowSelfBooking, c.CreatedAt,
                c.OwnerUserId, c.BillingAccountId, MemberCount = c.Members.Count, c.Kind, c.IsShowcase, c.ShowcaseBookingOpen,
            })
            .ToListAsync(ct);
        var ids = companies.Select(c => c.Id).ToList();
        var ownerIds = companies.Select(c => c.OwnerUserId).Distinct().ToList();
        // The tariff is account-level (§45.1): it belongs to the company's BillingAccountId, not to
        // OwnerUserId directly — OwnerUserId here is only used to show the responsible person's email.
        var accountIds = companies.Where(c => c.BillingAccountId.HasValue)
            .Select(c => c.BillingAccountId!.Value).Distinct().ToList();
        var subs = await db.AccountSubscriptions.AsNoTracking().Include(s => s.PlanConfig)
            .Where(s => s.BillingAccountId != null && accountIds.Contains(s.BillingAccountId!.Value)).ToListAsync(ct);
        // BillingAccountId is unique on AccountSubscriptions — one row per account at most.
        var subByAccount = subs.ToDictionary(s => s.BillingAccountId!.Value);
        var ownerEmails = await db.Users.Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email ?? u.PhoneNumber ?? u.Id, ct);
        var bookingCounts = await db.Bookings
            .Where(b => ids.Contains(b.CompanyId))
            .GroupBy(b => b.CompanyId)
            .Select(g => new { CompanyId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CompanyId, x => x.Count, ct);

        var result = companies.Select(c =>
        {
            var sub = c.BillingAccountId.HasValue ? subByAccount.GetValueOrDefault(c.BillingAccountId.Value) : null;
            var count = bookingCounts.GetValueOrDefault(c.Id);
            return new AdminCompanyDto(c.Id, c.Name, c.Slug, c.Email, c.Phone, c.IsActive, c.AllowSelfBooking, c.CreatedAt,
                c.MemberCount, count, c.OwnerUserId, ownerEmails.GetValueOrDefault(c.OwnerUserId, c.OwnerUserId),
                sub?.PlanConfigId, sub?.PlanConfig?.Name ?? "Free", sub?.PaidUntil, sub?.IsActive ?? true,
                c.Kind.ToString(), siteLinks.CompanyPageUrl(c.Kind, c.Slug), c.IsShowcase, c.ShowcaseBookingOpen);
        }).ToList();

        return Ok(Pagination.Create(result, currentPage, currentPageSize, total));
    }

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
    public async Task<ActionResult<SubscriptionDiagnosticsDto>> GetSubscriptionDiagnostics(string ownerUserId, CancellationToken ct)
    {
        var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == ownerUserId, ct);
        if (owner is null) return NotFound("Owner not found");

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId, ct);

        var sub = account is null ? null : await db.AccountSubscriptions
            .Include(s => s.PlanConfig)
            .FirstOrDefaultAsync(s => s.BillingAccountId == account.Id, ct);

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
                .ToListAsync(ct);

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
    public async Task<ActionResult<List<SubscriptionChangeLogDto>>> GetSubscriptionHistory(string ownerUserId, CancellationToken ct)
    {
        var logs = await db.SubscriptionChangeLogs
            .Where(l => l.OwnerUserId == ownerUserId)
            .OrderByDescending(l => l.ChangedAt)
            .ToListAsync(ct);

        var configIds = logs.SelectMany(l => new[] { l.OldPlanConfigId, l.NewPlanConfigId })
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var configNames = await db.SubscriptionPlanConfigs
            .Where(p => configIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var changedByIds = logs.Select(l => l.ChangedByUserId).Distinct().ToList();
        var changedByEmails = await db.Users
            .Where(u => changedByIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.PhoneNumber ?? u.Id, ct);

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
    [DemoForbidden]
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

        // ARCHITECTURE_CYCLE28.md §574.2, API_CONTRACT_CYCLE28.md §595: after existence checks, before any write.
        if (ShowcaseMixingGuard.CheckMember(company.IsShowcase, newOwner.IsShowcase) is { } mixing)
            return new ContentResult { StatusCode = StatusCodes.Status409Conflict, Content = mixing, ContentType = "text/plain; charset=utf-8" };

        // ARCHITECTURE_CYCLE20.md §407.2, API_CONTRACT_CYCLE20.md §437.3 (US-20-07, LG6) — same rule and
        // same source (CompanyTransferService.ValidateNewOwnerAsync) as CompanyTransferController's own
        // owner-change branch, "один источник правила". Skipped when the company has no billing account
        // at all (BillingAccountId is null) — nothing to be linked to.
        if (company.BillingAccountId is { } billingAccountId)
        {
            var (_, failure) = await companyTransferService.ValidateNewOwnerAsync(dto.NewOwnerUserId, billingAccountId);
            if (failure is not null)
                return new ContentResult
                {
                    StatusCode = StatusCodes.Status409Conflict,
                    Content = "Новый ответственный не связан с биллинг-аккаунтом компании: он должен быть держателем аккаунта или сотрудником одной из его компаний.",
                    ContentType = "text/plain; charset=utf-8",
                };
        }

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
        [FromQuery] BookingStatus? status,
        CancellationToken ct)
    {
        var query = db.Bookings
            .AsNoTracking()
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

        var bookings = await query.OrderByDescending(b => b.Date).ThenByDescending(b => b.StartTime).Take(500).ToListAsync(ct);

        return Ok(bookings.Select(b => new AdminBookingDto(
            b.Id, b.Company.Name,
            // US-67 (ARCHITECTURE_CYCLE6.md §47.2): the visit shown as one line — comma-joined service
            // names — rather than one row per service. Falls back to Service.Name only if
            // BookingServices somehow has no rows (should never happen after the backfill).
            string.Join(", ", b.ServiceNames()),
            $"{b.Master.FirstName} {b.Master.LastName}",
            b.Client is not null ? $"{b.Client.FirstName} {b.Client.LastName}" : b.GuestName ?? "Гость",
            b.GuestPhone ?? b.Client?.PhoneNumber,
            b.Date, b.StartTime, b.EndTime, b.Status, b.Price)));
    }
}

// ── DTOs ───────────────────────────────────────────────────────────────────────

// T5-B10 (ARCHITECTURE_CYCLE5.md §50.1: "счётчик просроченных попадает в существующую админскую
// сводку") — appended at the end with a default so any existing positional construction keeps compiling.
public record AdminStatsDto(
    int TotalCompanies, int TotalUsers, int TotalBookings, int CompletedBookings, decimal TotalRevenue,
    int OverdueSubjectRequests = 0,
    // ARCHITECTURE_CYCLE28.md §594.3 — additive: the showcase's own counters (the totals above exclude it).
    int ShowcaseCompanies = 0, int ShowcaseUsers = 0, int ShowcaseBookings = 0);

// CommissionPercent removed (US-22): commission became per-company (CompanyMember.CommissionPercent)
// back in cycle A; this account-level field means nothing any more and AdminPage.tsx never showed it.
public record AdminUserDto(string Id, string Phone, string? Email, string FirstName, string LastName, string? AvatarUrl, DateTime CreatedAt,
    List<string> Roles, int OwnedCompanyCount, Guid? PlanConfigId, string PlanName, DateTime? PaidUntil, bool SubscriptionActive,
    // ARCHITECTURE_CYCLE28.md §594.2 — additive: the account was created by the showcase generator.
    bool IsShowcase = false);

// AllowSelfBooking is included (additive) so the admin UI can read the company's CURRENT value before
// re-sending it unchanged to PUT /api/admin/companies/{id} — that endpoint overwrites all three of its
// body fields unconditionally, so a caller that has to guess this one risks silently flipping it
// (found during BE/FE contract integration, US-04).
public record AdminCompanyDto(Guid Id, string Name, string Slug, string? Email, string? Phone, bool IsActive, bool AllowSelfBooking, DateTime CreatedAt,
    int MemberCount, int BookingCount, string OwnerUserId, string OwnerEmail,
    Guid? PlanConfigId, string PlanName, DateTime? PaidUntil, bool SubscriptionActive,
    // ARCHITECTURE_CYCLE23.md §408.6 — additive, appended with defaults: the company's product type and the
    // absolute link to its public page (PublicSiteLinks).
    // Kind: the enum's name as a string, for the same reason as CompanyDto.Kind (readable without the enum converter).
    string Kind = nameof(CompanyKind.Services), string PublicUrl = "",
    // ARCHITECTURE_CYCLE28.md §594.2 — additive: showcase mark and whether the showcase company takes online booking.
    bool IsShowcase = false, bool ShowcaseBookingOpen = false);


public record SubscriptionDiagnosticsDto(
    string OwnerUserId, string OwnerName, Guid? PlanConfigId, string? PlanName, DateTime? PaidUntil,
    bool IsActive, bool PlanIsActive, SubscriptionStatus Status, string StatusText,
    EffectivePlan Effective, List<SubscriptionDiagnosticsCompanyDto> Companies);

public record SubscriptionDiagnosticsCompanyDto(
    Guid CompanyId, string Name, bool AllowSelfBooking, bool OnlineBookingEnabled,
    PlanNotAppliedReason BlockingReason);

public record SubscriptionChangeLogDto(Guid Id, DateTime ChangedAt, string ChangedByEmail,
    string OldPlanName, string NewPlanName, DateTime? OldPaidUntil, DateTime? NewPaidUntil,
    bool OldIsActive, bool NewIsActive, string? Comment);

public record AdminUpdateCompanyDto(string Name, bool IsActive, bool AllowSelfBooking);
public record UpdateCompanyOwnerDto(string NewOwnerUserId);

public record AdminBookingDto(Guid Id, string CompanyName, string ServiceName, string MasterName, string ClientName,
    string? ClientPhone, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, BookingStatus Status, decimal Price);
