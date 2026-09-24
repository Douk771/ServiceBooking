using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Geo;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController(
    AppDbContext db, UserManager<AppUser> userManager, SubscriptionResolver subscriptionResolver,
    ServiceBooking.API.Services.Billing.BillingAccountProvisioner billingAccountProvisioner,
    ServiceBooking.API.Services.Billing.AccountUsageReader accountUsageReader,
    ImageUploadService imageUploadService, FileStorage storage,
    LegalDocumentProvider legalProvider, ConsentLedger ledger, TokenService tokenService,
    IOptions<GeoOptions> geoOptions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CompanyDto>>> GetAll()
    {
        var companies = await db.Companies.Where(c => c.IsActive).ToListAsync();
        var plans = await subscriptionResolver.GetEffectivePlansAsync(companies.Select(c => c.Id));
        var ratings = await GetReviewAggregatesAsync(companies.Select(c => c.Id));
        var cities = await GetCitiesAsync(companies.Select(c => c.CityId));
        // §109.3: catalog gets covers only (one batched query for the whole page), never the full
        // `photos` list — a hundred companies × up to 10 photos each is a thousand rows nobody sees here.
        var covers = await GetCoversAsync(companies.Select(c => c.Id));

        // The public directory additionally requires both the owner's own opt-in (ShowInPublicListing)
        // and the tariff's AllowPublicListing — unlike GetMy/GetMemberOf/GetBySlug, which show the
        // company to people who already know about it regardless of directory placement.
        //
        // §46.2: the hottest, fully anonymous list on the site — AccountUsageReader is NOT called here
        // at all (not "called and cached", not called), so employeeCount/accountSeatsUsed/
        // accountSeatsLimit/canAddEmployee cost this endpoint exactly zero extra queries.
        return Ok(companies
            .Where(c => c.ShowInPublicListing && plans[c.Id].AllowPublicListing)
            .Select(c => MapToDto(c, plans[c.Id], ratings[c.Id].AverageRating, ratings[c.Id].ReviewCount,
                c.CityId.HasValue ? cities.GetValueOrDefault(c.CityId.Value) : null, employeeCount: 0, usage: null,
                geoOptions.Value, covers.GetValueOrDefault(c.Id))));
    }

    // GET /api/companies/public — US-115 (API_CONTRACT_CYCLE9.md §113.2). Anonymous; replaces GET
    // /api/companies for the home page while GET /api/companies keeps working unchanged. Filtering by
    // city/search and the page limit are all applied in SQL — no "download everything, filter in the
    // browser" regression. An unknown cityId yields an empty page, not 404 (a public, anonymous catalog
    // never confirms whether a reference row exists).
    // page/pageSize are bound as raw strings, not int?, on purpose: contracts/cycle9/openapi.yaml §pageSize
    // explicitly promises "клампится к [1,100], а не отвергается 400-м" (clamped, never rejected with
    // 400). With [FromQuery] int? and [ApiController]'s automatic model validation, a non-integer value
    // (e.g. pageSize=false) fails model binding and short-circuits to a 400 before this method body ever
    // runs — contradicting that documented guarantee. Parsing manually and falling back to "unset" (→
    // Pagination.Normalize's existing default/clamp path) for anything that doesn't parse keeps the
    // promised behavior for malformed input, not just out-of-range input.
    [HttpGet("public")]
    public async Task<ActionResult<ServiceBooking.API.DTOs.Common.PagedResult<CompanyDto>>> GetPublic(
        [FromQuery] int? cityId, [FromQuery] string? search, [FromQuery] string? page, [FromQuery] string? pageSize)
    {
        var (normalizedPage, normalizedPageSize) = ServiceBooking.API.DTOs.Common.Pagination.Normalize(
            ServiceBooking.API.DTOs.Common.Pagination.ParseNullableInt(page),
            ServiceBooking.API.DTOs.Common.Pagination.ParseNullableInt(pageSize));

        // contracts/cycle9/openapi.yaml declares search with maxLength: 200 — the server must honor that
        // as an input constraint, not just document it. Rather than reject an overlong search with 400
        // (nothing in the schema's description for `search` documents a 400 the way pageSize's does),
        // truncate to the declared limit, consistent with pageSize's own "clamp, don't reject" contract
        // for this anonymous, best-effort catalog endpoint.
        var truncatedSearch = search is { Length: > 200 } ? search[..200] : search;
        var sanitizedSearch = ServiceBooking.API.DTOs.Common.Pagination.SanitizeSearch(truncatedSearch);

        // Visibility rules are unchanged from GET /api/companies: isActive AND ShowInPublicListing AND
        // the tariff's AllowPublicListing. All three legs — including the tariff check, via
        // PublicListingQuery.WhereAllowsPublicListing (kept in lockstep with SubscriptionResolver's own
        // AllowPublicListing rule, see that method's remarks) — are applied in SQL, so filtering and
        // paging never require materializing the full candidate set (ARCHITECTURE_CYCLE9.md §103.5).
        var query = db.Companies
            .Where(c => c.IsActive && c.ShowInPublicListing)
            .WhereAllowsPublicListing(db, DateTime.UtcNow);

        if (cityId.HasValue) query = query.Where(c => c.CityId == cityId.Value);
        if (!string.IsNullOrWhiteSpace(sanitizedSearch))
        {
            // EF.Functions.ILike does not auto-escape LIKE wildcards the way EF Core's own
            // Contains/StartsWith translation does — unlike AdminController/AdminBillingController's
            // string.Contains(search) call sites, a raw ILIKE pattern built by interpolating user input
            // treats '%' and '_' from the caller as wildcards too (search=% would match every company,
            // search=_ would match any single character). Escape both, plus the escape character itself,
            // before wrapping in the leading/trailing '%'.
            var likePattern = $"%{EscapeLikeWildcards(sanitizedSearch)}%";
            query = query.Where(c => EF.Functions.ILike(c.Name, likePattern)
                                      || (c.Address != null && EF.Functions.ILike(c.Address, likePattern)));
        }

        query = query.OrderBy(c => c.Name).ThenBy(c => c.Id);

        var total = await query.CountAsync();
        var pageItems = await query
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync();

        // Rating/city/cover lookups and plan resolution (for the DTO's plan-derived fields, not for
        // filtering) stay batched for the page only, same as GetAll/GetMy/GetMemberOf above — covers are
        // fetched for pageItems (the current page), never the full candidate set, per §109.3.
        var plans = await subscriptionResolver.GetEffectivePlansAsync(pageItems.Select(c => c.Id));
        var ratings = await GetReviewAggregatesAsync(pageItems.Select(c => c.Id));
        var cities = await GetCitiesAsync(pageItems.Select(c => c.CityId));
        var covers = await GetCoversAsync(pageItems.Select(c => c.Id));

        var items = pageItems.Select(c => MapToDto(c, plans[c.Id], ratings[c.Id].AverageRating, ratings[c.Id].ReviewCount,
            c.CityId.HasValue ? cities.GetValueOrDefault(c.CityId.Value) : null, employeeCount: 0, usage: null,
            geoOptions.Value, covers.GetValueOrDefault(c.Id))).ToList();

        return Ok(ServiceBooking.API.DTOs.Common.Pagination.Create(items, normalizedPage, normalizedPageSize, total));
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
        var (employeeCounts, usageByAccount) = await GetUsageAsync(memberships.Select(cm => cm.Company));
        var covers = await GetCoversAsync(memberships.Select(cm => cm.CompanyId));

        return Ok(memberships.Select(cm =>
            MapToDto(cm.Company, plans[cm.CompanyId], ratings[cm.CompanyId].AverageRating, ratings[cm.CompanyId].ReviewCount,
                cm.Company.CityId.HasValue ? cities.GetValueOrDefault(cm.Company.CityId.Value) : null,
                employeeCounts.GetValueOrDefault(cm.CompanyId),
                cm.Company.BillingAccountId.HasValue ? usageByAccount.GetValueOrDefault(cm.Company.BillingAccountId.Value) : null,
                geoOptions.Value, covers.GetValueOrDefault(cm.CompanyId))));
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
        var (employeeCounts, usageByAccount) = await GetUsageAsync(memberships.Select(cm => cm.Company));
        var covers = await GetCoversAsync(memberships.Select(cm => cm.CompanyId));

        return Ok(memberships.Select(cm =>
            MapToDto(cm.Company, plans[cm.CompanyId], ratings[cm.CompanyId].AverageRating, ratings[cm.CompanyId].ReviewCount,
                cm.Company.CityId.HasValue ? cities.GetValueOrDefault(cm.Company.CityId.Value) : null,
                employeeCounts.GetValueOrDefault(cm.CompanyId),
                cm.Company.BillingAccountId.HasValue ? usageByAccount.GetValueOrDefault(cm.Company.BillingAccountId.Value) : null,
                geoOptions.Value, covers.GetValueOrDefault(cm.CompanyId))));
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
        // §109.3: the public page is the ONE place `photos` is filled — gallery with zero extra
        // requests. The cover is just photos[0] here, so it's derived rather than queried a second time.
        var photos = await GetPhotosOrderedAsync(c.Id);
        var cover = photos.Count > 0 ? (photos[0].Url, photos[0].ThumbnailUrl) : ((string, string)?)null;
        // Reachable anonymously (no [Authorize]) — same §46.2 treatment as GetAll: no usage computed.
        return Ok(MapToDto(c, plan, averageRating, reviewCount, city, employeeCount: 0, usage: null, geoOptions.Value,
            cover, photos, includeAddressPoint: true));
    }

    // Public: list masters for a company, optionally filtered by serviceId
    [HttpGet("{id:guid}/masters")]
    public async Task<ActionResult<List<MasterPublicDto>>> GetMasters(
        Guid id, [FromQuery] string? serviceId, [FromQuery] bool includeHidden = false)
    {
        // serviceId is bound as string (not Guid?) on purpose: ASP.NET Core's default model binder
        // treats an empty string for a nullable Guid query param as "absent" and silently maps it to
        // null, so a caller sending `?serviceId=` got a 200 with no filter applied instead of a 400 for
        // a malformed uuid (schemathesis finding, API_CONTRACT_CYCLE6.md §53). Only reject when the
        // query param was actually supplied with a non-empty, non-uuid value; omitted/empty stays "no
        // filter", matching the optional-parameter contract.
        Guid? parsedServiceId = null;
        if (!string.IsNullOrEmpty(serviceId))
        {
            if (!Guid.TryParse(serviceId, out var parsed))
                return BadRequest("serviceId must be a valid uuid.");
            parsedServiceId = parsed;
        }

        // ARCHITECTURE_CYCLE10.md §103.5: includeHidden is a request, not a permission — only honored
        // once we've independently verified the caller actually works in THIS company (or is
        // SuperAdmin), same trust bar as manual/extendedHours on availability/slots. Everyone else gets
        // the filter silently kept, same as if they hadn't asked.
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var honorIncludeHidden = includeHidden && userId is not null &&
            (User.IsInRole("SuperAdmin") || await CompanyMembership.IsStaffAsync(db, id, userId));

        var memberQuery = db.CompanyMembers
            .Include(cm => cm.User)
            // A Client-role membership row exists for a company's own customers (e.g. anyone who books
            // there), never for staff — without this filter they'd show up in the public "book a
            // master" picker (audit Q6/US-12).
            .Where(cm => cm.CompanyId == id && cm.Company.IsActive &&
                (cm.Role == UserRole.Master || cm.Role == UserRole.CompanyOwner) &&
                (honorIncludeHidden || cm.ProvidesServices));

        if (parsedServiceId.HasValue)
        {
            var masterIdsForService = await db.MasterServices
                .Where(ms => ms.ServiceId == parsedServiceId.Value)
                .Select(ms => ms.MasterId)
                .ToListAsync();

            // If no service assignments exist for anyone, show all masters (fallback)
            if (masterIdsForService.Count > 0)
                memberQuery = memberQuery.Where(cm => masterIdsForService.Contains(cm.UserId));
        }

        var members = await memberQuery.ToListAsync();

        return Ok(members.Select(cm => new MasterPublicDto(
            cm.UserId, cm.User.FirstName, cm.User.LastName, cm.User.AvatarUrl, cm.Bio, cm.ProvidesServices
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
    [RequiresOwnerTerms]
    public async Task<ActionResult<CreateCompanyResponseDto>> Create(CreateCompanyDto dto)
    {
        if (await db.Companies.AnyAsync(c => c.Slug == dto.Slug))
            return Conflict("Slug already taken");

        // ARCHITECTURE_CYCLE5.md §42.1, API_CONTRACT_CYCLE5.md §42.1 (BREAKING № 3). Checked by hand
        // (RegisterDto.Legal's own note explains why), before anything else touches the database — an
        // unaccepted company creation must never create a row to begin with.
        if (string.IsNullOrWhiteSpace(dto.OwnerTerms?.Version))
            return BadRequest("Для создания компании нужно принять соглашение с владельцем.");

        var ownerTermsDoc = legalProvider.Current?.Get(LegalDocumentType.TermsOwner);
        if (ownerTermsDoc is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");
        if (dto.OwnerTerms.Version != ownerTermsDoc.Version)
            return Conflict("Соглашение было обновлено ещё раз — перечитайте и примите новую редакцию.");

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
        // Serialize concurrent creates against everything else that can change how many companies fit
        // on this billing account (§52) — a company transfer moving a company IN, or another create —
        // by using the SAME lock key ("billing-account:{id}") those operations already take
        // (AdminBillingController.AssignSubscription, CompanyTransferService). Locking by userId alone
        // (the previous key) let a create race a concurrent transfer: both would read the pre-write
        // company count and both pass the limit check.
        var accountId = await billingAccountProvisioner.EnsureAccountAsync(userId);
        await using var limitTransaction = await db.Database.BeginTransactionAsync();
        await AdvisoryLock.AcquireAsync(db, $"billing-account:{accountId}");

        var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(accountId);
        if (plan.AccountMaxCompanies.HasValue)
        {
            var ownedCount = await db.Companies.CountAsync(c => c.BillingAccountId == accountId);
            if (ownedCount >= plan.AccountMaxCompanies.Value)
                return StatusCode(402, BillingTexts.CompanyLimitReached(ownedCount, plan.AccountMaxCompanies.Value));
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
            BillingAccountId = accountId,
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

        // ARCHITECTURE_CYCLE5.md §42.1 — the acceptance itself, recorded AFTER the company/membership
        // commit above (nothing before this point can fail because of it, and a failure here must not
        // undo an otherwise-successful company creation — best-effort would be wrong here though: US-66
        // needs this row to exist, so it's still inside the overall request, just its own grant/lock).
        var ownerSubject = ConsentSubject.ForUser(userId);
        await ledger.GrantAsync(new ConsentGrant(
            ownerSubject, LegalDocumentType.TermsOwner.ToString(), ownerTermsDoc.Version, ownerTermsDoc.ContentHash,
            Purpose: null, ConsentAct.Accepted, ConsentSource.CompanyCreation,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent: Request.Headers.UserAgent.ToString()));

        // A fresh token, carrying the new "lco" claim — see CreateCompanyResponseDto's own doc comment
        // for why this is mandatory, not an optimization. Privacy/TermsClient claims are re-resolved the
        // same way AuthController.Login does, so this token is complete, not just augmented.
        var user = await userManager.FindByIdAsync(userId);
        var roles = await userManager.GetRolesAsync(user!);
        var privacyState = await ledger.CurrentAsync(ownerSubject, LegalDocumentType.Privacy.ToString(), purpose: null);
        var termsState = await ledger.CurrentAsync(ownerSubject, LegalDocumentType.TermsClient.ToString(), purpose: null);
        var token = tokenService.GenerateToken(user!, roles, privacyState?.DocumentVersion, termsState?.DocumentVersion, ownerTermsDoc.Version);

        // `plan` was already resolved above for the MaxCompanies check — reuse it so the response
        // reflects the owner's real plan (e.g. their existing paid plan when opening a 2nd+ branch),
        // instead of assuming Free.
        // A brand new company has no reviews yet — skip the query, (null, 0) is correct by construction.
        var createUsage = accountId != Guid.Empty ? await accountUsageReader.GetAsync([accountId]) : new Dictionary<Guid, AccountUsage>();
        var companyDto = MapToDto(company, plan, null, 0, city, employeeCount: 1, createUsage.GetValueOrDefault(accountId), geoOptions.Value);
        return CreatedAtAction(nameof(GetBySlug), new { slug = company.Slug }, new CreateCompanyResponseDto(companyDto, token));
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    [RequiresOwnerTerms]
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
        var (updateEmployeeCounts, updateUsageByAccount) = await GetUsageAsync([company]);
        var updateCovers = await GetCoversAsync([company.Id]);
        return Ok(MapToDto(company, plan, averageRating, reviewCount, city,
            updateEmployeeCounts.GetValueOrDefault(company.Id),
            company.BillingAccountId.HasValue ? updateUsageByAccount.GetValueOrDefault(company.BillingAccountId.Value) : null,
            geoOptions.Value, updateCovers.GetValueOrDefault(company.Id)));
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
        var (logoEmployeeCounts, logoUsageByAccount) = await GetUsageAsync([company]);
        var logoCovers = await GetCoversAsync([company.Id]);
        return Ok(MapToDto(company, plan, averageRating, reviewCount, city,
            logoEmployeeCounts.GetValueOrDefault(company.Id),
            company.BillingAccountId.HasValue ? logoUsageByAccount.GetValueOrDefault(company.BillingAccountId.Value) : null,
            geoOptions.Value, logoCovers.GetValueOrDefault(company.Id)));
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
    [RequiresOwnerTerms]
    public async Task<ActionResult<MemberDto>> AddMember(Guid id, AddMemberDto dto)
    {
        if (!await CanManageCompany(id)) return Forbid();

        // Validate that the caller is allowed to assign the requested role
        if (!await CanAssignRole(dto.Role)) return Forbid();

        // SuperAdmin's CanAssignRole above accepts any string sight unseen — so a typo'd role name from
        // a SuperAdmin caller used to sail through and blow up Enum.Parse<UserRole> below with an
        // unhandled 500 (audit D3). Reject an unknown role name explicitly instead.
        if (!Enum.TryParse<UserRole>(dto.Role, out _)) return BadRequest("Unknown role");

        // Tariff seat limit (ARCHITECTURE_CYCLE7.md §46.4): SUMMED across every company on the
        // account, not just this one — a customer with 3 branches on an 8-seat plan can put all 8
        // anywhere, not 8-per-branch. Only blocks adding NEW members once at/over the cap; existing
        // members are never removed.
        //
        // Serialize concurrent adds ACROSS THE WHOLE ACCOUNT: without a lock keyed on the account (not
        // just this company), two simultaneous adds to two DIFFERENT companies of the same account could
        // each count the same pre-insert account-wide total, both pass the check, and both insert —
        // letting the account end up over the seat limit the plan was supposed to enforce. Falls back to
        // a per-company key only for the (pre-cycle-5-backfill) edge case of a company with no billing
        // account yet.
        var billingAccountId = await db.Companies.Where(c => c.Id == id).Select(c => c.BillingAccountId).FirstOrDefaultAsync();
        await using var limitTransaction = await db.Database.BeginTransactionAsync();
        // §52: "billing-account:{accountId}" → "company-members:{companyId}", strictly in that order
        // (account before company) — seats are counted per account but the row is written to a
        // specific company, and taking both locks (not just the account one) closes the same race for
        // members deleted/added directly against this company concurrently with an account-wide count.
        // Falls back to just the per-company key for the (pre-cycle-5-backfill) edge case of a company
        // with no billing account yet.
        if (billingAccountId.HasValue)
            await AdvisoryLock.AcquireAsync(db, $"billing-account:{billingAccountId}");
        await AdvisoryLock.AcquireAsync(db, $"company-members:{id}");

        var plan = await subscriptionResolver.GetEffectivePlanAsync(id);
        if (plan.AccountMaxEmployees.HasValue)
        {
            var seatsUsed = billingAccountId.HasValue
                ? (await accountUsageReader.GetAsync([billingAccountId.Value])).GetValueOrDefault(billingAccountId.Value)?.SeatsUsed ?? 0
                : await db.CompanyMembers.CountAsync(cm => cm.CompanyId == id);
            if (seatsUsed >= plan.AccountMaxEmployees.Value)
            {
                // §53.4 breakdown: how much of the summed limit is "included in the plan" vs
                // "purchased as an option" vs "grandfathered bonus" — the plan's own included quantity
                // is whatever's left after subtracting the bonus and every option's contribution from
                // the already-resolved total (both are 0 when there's no billing account at all, the
                // pre-cycle-5-backfill edge case).
                var bonus = billingAccountId.HasValue
                    ? await db.BillingAccounts.Where(a => a.Id == billingAccountId.Value).Select(a => a.GrandfatheredEmployeeBonus).FirstOrDefaultAsync()
                    : 0;
                var sub = billingAccountId.HasValue
                    ? await db.AccountSubscriptions.Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == billingAccountId.Value)
                    : null;
                // NB-1: must use the SAME "is this subscription usable right now" gate as the limit
                // itself (SubscriptionResolver.Resolve) — otherwise an expired subscription's raw plan
                // name/quantity leaks into the 402 text (e.g. "8 included") while the limit that was
                // actually enforced came from Free (1).
                var subUsableNow = sub is not null && sub.IsActive
                    && (!sub.PaidUntil.HasValue || sub.PaidUntil >= DateTime.UtcNow)
                    && sub.PlanConfig is { IsActive: true };
                var planIncluded = subUsableNow ? sub!.PlanConfig!.MaxEmployees ?? EffectivePlan.Free.AccountMaxEmployees!.Value
                    : EffectivePlan.Free.AccountMaxEmployees!.Value;
                var purchased = Math.Max(0, plan.AccountMaxEmployees.Value - planIncluded - bonus);
                var planName = subUsableNow ? sub!.PlanConfig!.Name : "Бесплатный";
                return StatusCode(402, BillingTexts.SeatLimitReached(seatsUsed, planName, planIncluded, purchased, bonus));
            }
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
    [RequiresOwnerTerms]
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
            .Include(b => b.BookingServices)
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

        // US-67 (ARCHITECTURE_CYCLE6.md §44.2 p.4): the "top services" breakdown counts individual
        // services from BookingServices, not visits — a 3-service visit contributes 3 counts here,
        // one per line item, while totalRevenue above (computed from Booking.Price) still counts the
        // visit exactly once. Pre-cycle bookings have exactly one BookingServices row each (backfilled),
        // so this is unchanged for them.
        var popularServices = bookings
            .SelectMany(b => b.BookingServices)
            .GroupBy(bs => bs.ServiceId)
            .Select(g =>
            {
                return new
                {
                    serviceId = g.Key,
                    serviceName = g.First().NameSnapshot,
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
    // `employeeCount`/`usage` (ARCHITECTURE_CYCLE7.md §46, §53.1): `usage` is null for a caller who
    // doesn't manage this company (§46.2's public paths pass 0/null explicitly) — that null propagates
    // to AccountSeatsUsed/AccountSeatsLimit/CanAddEmployee, deliberately distinct from "no limit"
    // (AccountSeatsLimit is a real null when the plan itself is unlimited, WITH a non-null usage).
    // ARCHITECTURE_CYCLE13.md §234: the address-save endpoint returns "полный CompanyDto, как у
    // PUT /api/companies/{id}" — internal (not private) so CompanyAddressController, a deliberately
    // separate controller (§207's own "тот уже самый большой в проекте" precedent), can build the exact
    // same shape without a second, drifting copy of this mapping.
    internal static CompanyDto MapToDto(
        Company c, EffectivePlan plan, double? averageRating, int reviewCount, City? city,
        int employeeCount, AccountUsage? usage, GeoOptions geoOptions,
        // ARCHITECTURE_CYCLE10.md §109.3/API_CONTRACT_CYCLE10.md §129: cover is a single (Url,
        // ThumbnailUrl) pair resolved by the caller (batched via GetCoversAsync for list endpoints, or a
        // single lookup for the others) — null when the company has no photos yet. `photos` stays null
        // everywhere except GET /api/companies/{slug} (GetBySlug), which is the only caller that passes
        // the full ordered list.
        (string Url, string ThumbnailUrl)? cover = null, List<CompanyPhotoDto>? photos = null,
        // ARCHITECTURE_CYCLE13.md §208/API_CONTRACT_CYCLE13.md §232 — true only for GetBySlug, the one
        // endpoint that may expose a point at all.
        bool includeAddressPoint = false)
    {
        TimeZoneOffset.TryGetUtcOffsetMinutes(c.TimeZoneId, DateTime.UtcNow, out var utcOffsetMinutes);
        var accountSeatsUsed = usage?.SeatsUsed;
        var accountSeatsLimit = usage is null ? (int?)null : plan.AccountMaxEmployees;
        var canAddEmployee = usage is null
            ? (bool?)null
            : !plan.AccountMaxEmployees.HasValue || usage.SeatsUsed < plan.AccountMaxEmployees.Value;

        // ARCHITECTURE_CYCLE13.md §208/§232: same "only for a caller who manages this company" signal
        // employeeCount/usage already use — `usage is not null` marks exactly that caller. `available`
        // additionally requires the switch itself to be on (Provider != "logging").
        var addressVerification = usage is null
            ? null
            : new CompanyAddressVerificationDto(
                Available: !string.Equals(geoOptions.Provider, "logging", StringComparison.OrdinalIgnoreCase),
                Status: AddressVerificationState.Status(c).ToString(),
                VerifiedAt: c.AddressVerifiedAt,
                Precision: c.AddressPrecision?.ToString());

        // §208: a point is only ever exposed on GetBySlug, and only when it is BOTH stored (StoreResults
        // — extended licence, §209.2) AND the address is currently Verified — gating on Verified here
        // (not just "columns are non-null") stops a stale point surviving an address edit that hasn't
        // been re-verified yet (§203/§235: editing Address never clears these columns, it just makes the
        // computed status fall back to Unverified).
        var addressPoint = includeAddressPoint && geoOptions.StoreResults &&
                            c.AddressLatitude.HasValue && c.AddressLongitude.HasValue &&
                            AddressVerificationState.Status(c) == AddressVerificationStatus.Verified
            ? new GeoPointDto(c.AddressLatitude.Value, c.AddressLongitude.Value)
            : null;

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
            plan.AccountMaxEmployees,
            employeeCount,
            accountSeatsUsed,
            accountSeatsLimit,
            canAddEmployee,
            averageRating,
            reviewCount,
            c.CityId, city?.Name, city?.Region, c.TimeZoneId, c.TimeZoneIsManual, utcOffsetMinutes,
            BookingHorizon.Normalize(c.BookingHorizonDays),
            cover?.Url, cover?.ThumbnailUrl, photos,
            addressVerification, addressPoint);
    }

    // ARCHITECTURE_CYCLE10.md §109.3: one batched query for the whole page's cover photos (Position ==
    // 0), never one query per company — same pattern as GetCitiesAsync/GetReviewAggregatesAsync above.
    // A company with no CompanyPhoto rows simply has no entry in the result.
    private async Task<Dictionary<Guid, (string Url, string ThumbnailUrl)>> GetCoversAsync(IEnumerable<Guid> companyIds)
    {
        var ids = companyIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, (string, string)>();

        // The (CompanyId, Position) index is deliberately NON-unique (§102.2) — integrity is only
        // maintained by server-side renumbering, so a reader must tolerate a duplicate Position == 0
        // row for the same company. Plain ToDictionary throws ArgumentException on the duplicate key and
        // would 500 the entire public catalog page; CompanyPhotoOrdering.SelectCovers picks a single
        // deterministic winner instead.
        var covers = await db.CompanyPhotos
            .Where(p => ids.Contains(p.CompanyId) && p.Position == 0)
            .ToListAsync();
        return CompanyPhotoOrdering.SelectCovers(covers)
            .ToDictionary(kv => kv.Key, kv => (kv.Value.Url, kv.Value.ThumbnailUrl));
    }

    // The full ordered gallery for exactly one company — only GET /api/companies/{slug} needs this
    // (§109.3: the public page gets its gallery with zero extra requests; every other endpoint gets
    // `photos: null` and, at most, the batched cover above).
    private async Task<List<CompanyPhotoDto>> GetPhotosOrderedAsync(Guid companyId)
    {
        var photos = await db.CompanyPhotos
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.Position).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .ToListAsync();
        return photos.Select(CompanyPhotoDto.From).ToList();
    }

    // ARCHITECTURE_CYCLE7.md §46.1: batched account-usage lookup for the two AUTHENTICATED list/detail
    // endpoints (GetMy/GetMemberOf/Update/UploadLogo) — two grouped queries total regardless of how
    // many companies/accounts are in `companies`, never one query per company.
    private async Task<(Dictionary<Guid, int> EmployeeCounts, Dictionary<Guid, AccountUsage> UsageByAccount)> GetUsageAsync(
        IEnumerable<Company> companies)
    {
        var companyList = companies.ToList();
        var employeeCounts = await accountUsageReader.GetCompanySeatsAsync(companyList.Select(c => c.Id));
        var accountIds = companyList.Where(c => c.BillingAccountId.HasValue).Select(c => c.BillingAccountId!.Value).Distinct();
        var usageByAccount = await accountUsageReader.GetAsync(accountIds);
        return (employeeCounts, usageByAccount);
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

    /// <summary>
    /// Escapes Postgres' default ILIKE wildcards ('%' any-run, '_' any-single-char) and the escape
    /// character itself ('\') out of GetPublic's user-supplied search term before it is wrapped in
    /// leading/trailing '%' and handed to EF.Functions.ILike. Without this, a caller-supplied '%'/'_'
    /// is interpreted as a wildcard rather than a literal character — e.g. search=% matches every
    /// company in the public, anonymous catalog, search=_ matches any single-character name/address —
    /// not a SQL-injection risk (the pattern is still bound as a parameter), just a filtering-bypass one.
    /// Backslash must be escaped first, or escaping '%'/'_' afterward would double-escape their own
    /// backslashes.
    /// </summary>
    private static string EscapeLikeWildcards(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
