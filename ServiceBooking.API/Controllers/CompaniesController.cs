using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Bookings;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;
[ApiController]
[Route("api/[controller]")]
public class CompaniesController(
    AppDbContext db, SubscriptionResolver subscriptionResolver,
    ServiceBooking.API.Services.Billing.AccountUsageReader accountUsageReader,
    ImageUploadService imageUploadService, FileStorage storage,
    CompanyDtoAssembler companyDtoAssembler, CompanyStatsService companyStatsService,
    ServiceBooking.API.Services.PublicSites.PublicSiteLinks siteLinks, CompanyCreationService companyCreation) : ControllerBase
{
    // Cycle 22 P5 (§378): the member endpoints moved to CompanyMembersController, the DTO assembly to
    // CompanyDtoAssembler and the stats body to CompanyStatsService — all unchanged.
    // Cycle 23 (§389.2): the salon directory lists salons only — shops live on goods.ezbook.ru.
    [HttpGet]
    public async Task<ActionResult<List<CompanyDto>>> GetAll(CancellationToken ct)
    {
        // §375 F21: the owner's own opt-in (ShowInPublicListing, see below) is a plain column — filtered
        // in SQL, so opted-out companies are never loaded, nor resolved/rated/covered below.
        var companies = await db.Companies.AsNoTracking().Where(c => c.IsActive && c.ShowInPublicListing && c.Kind == CompanyKind.Services).ToListAsync(ct);
        var plans = await subscriptionResolver.GetEffectivePlansAsync(companies.Select(c => c.Id));
        var ratings = await companyDtoAssembler.GetReviewAggregatesAsync(companies.Select(c => c.Id));
        var cities = await companyDtoAssembler.GetCitiesAsync(companies.Select(c => c.CityId));
        // §109.3: catalog gets covers only (one batched query for the whole page), never the full
        // `photos` list — a hundred companies × up to 10 photos each is a thousand rows nobody sees here.
        var covers = await companyDtoAssembler.GetCoversAsync(companies.Select(c => c.Id));

        // The public directory additionally requires both the owner's own opt-in (ShowInPublicListing)
        // and the tariff's AllowPublicListing — unlike GetMy/GetMemberOf/GetBySlug, which show the
        // company to people who already know about it regardless of directory placement.
        //
        // §46.2: the hottest, fully anonymous list on the site — AccountUsageReader is NOT called here
        // at all (not "called and cached", not called), so employeeCount/accountSeatsUsed/
        // accountSeatsLimit/canAddEmployee cost this endpoint exactly zero extra queries.
        return Ok(companies
            .Where(c => plans[c.Id].AllowPublicListing)
            .Select(c => companyDtoAssembler.MapToDto(c, plans[c.Id], ratings[c.Id].AverageRating, ratings[c.Id].ReviewCount,
                c.CityId.HasValue ? cities.GetValueOrDefault(c.CityId.Value) : null, employeeCount: 0, usage: null,
                covers.GetValueOrDefault(c.Id))));
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
        [FromQuery] int? cityId, [FromQuery] string? search, [FromQuery] string? page, [FromQuery] string? pageSize,
        CancellationToken ct)
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
            .Where(c => c.IsActive && c.ShowInPublicListing && c.Kind == CompanyKind.Services)
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

        var total = await query.CountAsync(ct);
        var pageItems = await query
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(ct);

        // Rating/city/cover lookups and plan resolution (for the DTO's plan-derived fields, not for
        // filtering) stay batched for the page only, same as GetAll/GetMy/GetMemberOf above — covers are
        // fetched for pageItems (the current page), never the full candidate set, per §109.3.
        var plans = await subscriptionResolver.GetEffectivePlansAsync(pageItems.Select(c => c.Id));
        var ratings = await companyDtoAssembler.GetReviewAggregatesAsync(pageItems.Select(c => c.Id));
        var cities = await companyDtoAssembler.GetCitiesAsync(pageItems.Select(c => c.CityId));
        var covers = await companyDtoAssembler.GetCoversAsync(pageItems.Select(c => c.Id));

        var items = pageItems.Select(c => companyDtoAssembler.MapToDto(c, plans[c.Id], ratings[c.Id].AverageRating, ratings[c.Id].ReviewCount,
            c.CityId.HasValue ? cities.GetValueOrDefault(c.CityId.Value) : null, employeeCount: 0, usage: null,
            covers.GetValueOrDefault(c.Id))).ToList();

        return Ok(ServiceBooking.API.DTOs.Common.Pagination.Create(items, normalizedPage, normalizedPageSize, total));
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<List<CompanyDto>>> GetMy([FromQuery] string? kind, CancellationToken ct)
    {
        // Cycle 23 (§389.2, §408.2): not passed → Services, so the ezbook frontend (which never sends it)
        // stops seeing shops in its cabinet without a single frontend change.
        if (!CompanyKindQuery.TryParse(kind, out var wantedKind)) return BadRequest(CompanyKindQuery.UnknownKindText);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var memberships = await db.CompanyMembers
            .Include(cm => cm.Company)
            .Where(cm => cm.UserId == userId && cm.Role == UserRole.CompanyOwner && cm.Company.IsActive && cm.Company.Kind == wantedKind)
            .ToListAsync(ct);
        // GetMy only ever returns CompanyOwner memberships (the query above filters on
        // cm.Role == UserRole.CompanyOwner), so every row here is a company this caller manages.
        return Ok(await companyDtoAssembler.MapMembershipsToDtosAsync(memberships, canManage: _ => true));
    }

    // Returns all companies where the current user is a member (any role)
    [HttpGet("member")]
    [Authorize]
    public async Task<ActionResult<List<CompanyDto>>> GetMemberOf([FromQuery] string? kind, CancellationToken ct)
    {
        // Cycle 23 (§389.2, §408.2): same default as GetMy — Services when the parameter is absent.
        if (!CompanyKindQuery.TryParse(kind, out var wantedKind)) return BadRequest(CompanyKindQuery.UnknownKindText);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var memberships = await db.CompanyMembers
            .Include(cm => cm.Company)
            .Where(cm => cm.UserId == userId && cm.Company.IsActive && cm.Company.Kind == wantedKind)
            .ToListAsync(ct);
        // §232/§237, review finding (cycle 13 review, blocking #2): unlike GetMy, this endpoint returns
        // companies for EVERY membership role — a Master's own membership row must not light up
        // addressVerification.available. SuperAdmin manages every company regardless of membership role.
        var isSuperAdmin = User.IsInRole("SuperAdmin");

        return Ok(await companyDtoAssembler.MapMembershipsToDtosAsync(memberships,
            canManage: cm => isSuperAdmin || cm.Role == UserRole.CompanyOwner));
    }

    // Cycle 23 (§408.3): how many active companies of each kind the caller belongs to (any role) and where
    // to manage them — feeds "Ваши магазины управляются на goods.ezbook.ru" (ezbook) and its mirror (goods).
    [HttpGet("kinds-summary")]
    [Authorize]
    public async Task<ActionResult<CompanyKindsSummaryDto>> GetKindsSummary(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var counts = await db.CompanyMembers.AsNoTracking()
            .Where(cm => cm.UserId == userId && cm.Company.IsActive)
            .GroupBy(cm => cm.Company.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int CountOf(CompanyKind k) => counts.FirstOrDefault(x => x.Kind == k)?.Count ?? 0;
        return Ok(new CompanyKindsSummaryDto(
            new CompanyKindSummaryItemDto(CountOf(CompanyKind.Services), siteLinks.SiteBaseUrl(CompanyKind.Services)),
            new CompanyKindSummaryItemDto(CountOf(CompanyKind.Orders), siteLinks.SiteBaseUrl(CompanyKind.Orders))));
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CompanyDto>> GetBySlug(string slug, CancellationToken ct)
    {
        var c = await db.Companies.FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive, ct);
        if (c is null) return NotFound();

        var plan = await subscriptionResolver.GetEffectivePlanAsync(c.Id);
        // US-49 regression fix (QA cycle C): rating shown on the public company page must be a true
        // company-wide aggregate computed by the database, not derived from whatever page of reviews
        // GET /api/companies/{companyId}/reviews happens to have loaded (which visibly changed as the
        // caller paged through reviews — ARCHITECTURE.md §11.2/§21.5 pagination note).
        var (averageRating, reviewCount) = await companyDtoAssembler.GetReviewAggregateAsync(c.Id);
        var city = c.CityId.HasValue ? await db.Cities.FindAsync([c.CityId.Value], ct) : null;
        // §109.3: the public page is the ONE place `photos` is filled — gallery with zero extra
        // requests. The cover is just photos[0] here, so it's derived rather than queried a second time.
        var photos = await companyDtoAssembler.GetPhotosOrderedAsync(c.Id);
        var cover = photos.Count > 0 ? (photos[0].Url, photos[0].ThumbnailUrl) : ((string, string)?)null;
        // Reachable anonymously (no [Authorize]) — same §46.2 treatment as GetAll: no usage computed.
        return Ok(companyDtoAssembler.MapToDto(c, plan, averageRating, reviewCount, city, employeeCount: 0, usage: null,
            cover, photos));
    }

    // Public: list masters for a company, optionally filtered by serviceId
    [HttpGet("{id:guid}/masters")]
    public async Task<ActionResult<List<MasterPublicDto>>> GetMasters(
        Guid id, [FromQuery] string? serviceId, [FromQuery] bool includeHidden = false,
        CancellationToken ct = default)
    {
        // §389.2: anonymous public route — no rights to check first. A shop has no masters to pick.
        if (await CompanyKindGuard.RejectShopAsync(db, id, ct) is { } shopRefusal) return shopRefusal;

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
            .Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.CompanyId == id && cm.Company.IsActive &&
                (honorIncludeHidden || cm.ProvidesServices));

        if (parsedServiceId.HasValue)
        {
            var masterIdsForService = await db.MasterServices
                .Where(ms => ms.ServiceId == parsedServiceId.Value)
                .Select(ms => ms.MasterId)
                .ToListAsync(ct);

            // If no service assignments exist for anyone, show all masters (fallback)
            if (masterIdsForService.Count > 0)
                memberQuery = memberQuery.Where(cm => masterIdsForService.Contains(cm.UserId));
        }

        var members = await memberQuery.ToListAsync(ct);

        return Ok(members.Select(cm => new MasterPublicDto(
            cm.UserId, cm.User.FirstName, cm.User.LastName, cm.User.AvatarUrl, cm.Bio, cm.ProvidesServices
        )).ToList());
    }

    // Cycle 23 (§395.1): the body moved to CompanyCreationService, unchanged for salons — POST /api/shops shares it.
    [HttpPost]
    [Authorize]
    [RequiresOwnerTerms]
    public async Task<ActionResult<CreateCompanyResponseDto>> Create(CreateCompanyDto dto)
    {
        var outcome = await companyCreation.CreateAsync(
            CompanyKind.Services,
            new CompanyCreationRequest(dto.Name, dto.Slug, dto.Description, dto.Address, dto.Phone, dto.Email, dto.CityId,
                dto.TimeZoneId, dto.AllowSelfBooking, dto.ShowInPublicListing, dto.OwnerTerms?.Version),
            User, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
        if (outcome.Error is not null) return outcome.Error;
        var (company, city, plan, accountId) = (outcome.Company!, outcome.City!, outcome.Plan!, outcome.AccountId);

        // `plan` was already resolved during creation for the MaxCompanies check — reuse it so the response
        // reflects the owner's real plan (e.g. their existing paid plan when opening a 2nd+ branch),
        // instead of assuming Free.
        // A brand new company has no reviews yet — skip the query, (null, 0) is correct by construction.
        var createUsage = accountId != Guid.Empty ? await accountUsageReader.GetAsync([accountId]) : new Dictionary<Guid, AccountUsage>();
        var companyDto = companyDtoAssembler.MapToDto(company, plan, null, 0, city, employeeCount: 1, createUsage.GetValueOrDefault(accountId), canManage: true);
        return CreatedAtAction(nameof(GetBySlug), new { slug = company.Slug }, new CreateCompanyResponseDto(companyDto, outcome.Token!));
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

        // ARCHITECTURE_CYCLE15.md §253.4/§283 — the ONLY write path for these two fields. Order matters
        // (§283: "если оба поля ссылок невалидны — сервер отвечает первым отказом"): Yandex is checked
        // before 2ГИС.
        if (dto.YandexMapsUrl is not null)
        {
            if (!MapLinkValidation.TryNormalize(dto.YandexMapsUrl, MapLinkService.Yandex, out var normalizedYandexUrl, out var yandexError))
                return BadRequest(yandexError);
            company.YandexMapsUrl = normalizedYandexUrl;
        }

        if (dto.TwoGisUrl is not null)
        {
            if (!MapLinkValidation.TryNormalize(dto.TwoGisUrl, MapLinkService.TwoGis, out var normalizedTwoGisUrl, out var twoGisError))
                return BadRequest(twoGisError);
            company.TwoGisUrl = normalizedTwoGisUrl;
        }

        // ARCHITECTURE_CYCLE15.md §252.3/§283: omitted/null leaves it untouched; 0 IS a legitimate
        // explicit value here (unlike BookingHorizonDays's 0), so it is not special-cased.
        if (dto.ClientRescheduleMinHours is not null)
        {
            if (!ClientRescheduleWindow.TryNormalize(dto.ClientRescheduleMinHours, out var minHours))
                return BadRequest("Окно переноса — от 0 до 168 часов");
            company.ClientRescheduleMinHours = minHours;
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

        return Ok(await companyDtoAssembler.MapManagedCompanyToDtoAsync(company, city));
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

        var city = company.CityId.HasValue ? await db.Cities.FindAsync(company.CityId.Value) : null;
        return Ok(await companyDtoAssembler.MapManagedCompanyToDtoAsync(company, city));
    }

    // US-24 p.4 / US-19 p.7: the only place a company's client-photo storage usage is exposed. NOT
    // folded into CompanyDto — that DTO is also returned by the public directory and by list endpoints,
    // where a per-company aggregate would either leak private usage data publicly or force an N+1 sum
    // over every company in a list (ARCHITECTURE.md §12.3). One call, one company, one screen.
    [HttpGet("{id:guid}/photo-usage")]
    [Authorize]
    public async Task<ActionResult<CompanyPhotoUsageDto>> GetPhotoUsage(Guid id, CancellationToken ct)
    {
        var company = await db.Companies.FindAsync([id], ct);
        if (company is null) return NotFound();

        // Staff of THIS company, or SuperAdmin — this is the one place SuperAdmin gets numbers about
        // client photos without ever getting the content itself (decision Q5, ARCHITECTURE.md §12.3).
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAllowed = userId is not null &&
            (User.IsInRole("SuperAdmin") || await CompanyMembership.IsStaffAsync(db, id, userId));
        if (!isAllowed) return Forbid();
        // §389.2: salon-only route (rights first, kind second).
        if (CompanyKindGuard.RejectShop(company.Kind) is { } shopRefusal) return shopRefusal;

        var usedBytes = await db.ClientNotePhotos.Where(p => p.CompanyId == id).SumAsync(p => (long?)p.SizeBytes, ct) ?? 0;
        var photoCount = await db.ClientNotePhotos.CountAsync(p => p.CompanyId == id, ct);
        var plan = await subscriptionResolver.GetEffectivePlanAsync(id);

        double? percentUsed = plan.PhotoQuotaMb is { } quotaMb && quotaMb > 0
            ? Math.Round(usedBytes / (quotaMb * 1024.0 * 1024.0) * 100, 1)
            : null;

        return Ok(new CompanyPhotoUsageDto(id, usedBytes, photoCount, plan.PhotoQuotaMb, percentUsed, plan.PhotoRetention));
    }

    [HttpGet("{id:guid}/stats")]
    [Authorize]
    public async Task<IActionResult> GetStats(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        if (!await CanManageCompany(id)) return Forbid();
        // §389.2: salon-only route (rights first, kind second).
        if (await CompanyKindGuard.RejectShopAsync(db, id, ct) is { } shopRefusal) return shopRefusal;

        if (from is null || to is null) return BadRequest("Both 'from' and 'to' are required.");
        if (to < from) return BadRequest("Invalid date range: 'to' must not be earlier than 'from'.");
        return Ok(await companyStatsService.GetStatsAsync(id, from.Value, to.Value, ct));
    }

    // TD-11 (ARCHITECTURE_CYCLE16.md §254): delegates to the single shared implementation.
    // superAdminBypass stays true — this controller's existing behavior.
    private Task<bool> CanManageCompany(Guid companyId) =>
        CompanyAccess.CanManageCompanyAsync(db, User, companyId);


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
    public static string EscapeLikeWildcards(string value) => ServiceBooking.API.Services.LikePattern.Escape(value);
}
