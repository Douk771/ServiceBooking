using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Bookings;
using ServiceBooking.API.Services.Geo;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Companies;

/// <summary>
/// Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): everything that turns a <see cref="Company"/> into a
/// <see cref="CompanyDto"/> — the pure <see cref="MapToDto"/> and the batched enrichment helpers (plans,
/// ratings, cities, seat usage, covers, gallery) — moved verbatim out of <c>CompaniesController</c>, so
/// <c>CompaniesController</c> and <c>CompanyAddressController</c> build the exact same shape from one place.
/// </summary>
public sealed class CompanyDtoAssembler(
    AppDbContext db, SubscriptionResolver subscriptionResolver, AccountUsageReader accountUsageReader,
    IOptions<GeoOptions> geoOptions, PublicSites.PublicSiteLinks siteLinks)
{
    /// <summary>
    /// Cycle 22 (D7; moved here from CompaniesController in P5, §378): the batched enrichment
    /// GetMy and GetMemberOf share — plans, ratings, cities, seat usage and covers for every membership's
    /// company in one query each — then one CompanyDto per membership, in membership order.
    /// </summary>
    public async Task<List<CompanyDto>> MapMembershipsToDtosAsync(
        List<CompanyMember> memberships, Func<CompanyMember, bool> canManage)
    {
        var plans = await subscriptionResolver.GetEffectivePlansAsync(memberships.Select(cm => cm.CompanyId));
        var ratings = await GetReviewAggregatesAsync(memberships.Select(cm => cm.CompanyId));
        var cities = await GetCitiesAsync(memberships.Select(cm => cm.Company.CityId));
        var (employeeCounts, usageByAccount) = await GetUsageAsync(memberships.Select(cm => cm.Company));
        var covers = await GetCoversAsync(memberships.Select(cm => cm.CompanyId));

        return memberships.Select(cm =>
            MapToDto(cm.Company, plans[cm.CompanyId], ratings[cm.CompanyId].AverageRating, ratings[cm.CompanyId].ReviewCount,
                cm.Company.CityId.HasValue ? cities.GetValueOrDefault(cm.Company.CityId.Value) : null,
                employeeCounts.GetValueOrDefault(cm.CompanyId),
                cm.Company.BillingAccountId.HasValue ? usageByAccount.GetValueOrDefault(cm.Company.BillingAccountId.Value) : null,
                geoOptions.Value, covers.GetValueOrDefault(cm.CompanyId), canManage: canManage(cm)))
            .ToList();
    }


    /// <summary>
    /// Cycle 22 (D7; moved here from CompaniesController in P5, §378): the full CompanyDto that PUT /api/companies/{id} and the logo
    /// upload both return to the company's manager after a write — plan, rating, seat usage and cover
    /// re-read for this one company. <paramref name="city"/> is resolved by the caller.
    /// </summary>
    public async Task<CompanyDto> MapManagedCompanyToDtoAsync(Company company, City? city)
    {
        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        var (averageRating, reviewCount) = await GetReviewAggregateAsync(company.Id);
        var (employeeCounts, usageByAccount) = await GetUsageAsync([company]);
        var covers = await GetCoversAsync([company.Id]);
        return MapToDto(company, plan, averageRating, reviewCount, city,
            employeeCounts.GetValueOrDefault(company.Id),
            company.BillingAccountId.HasValue ? usageByAccount.GetValueOrDefault(company.BillingAccountId.Value) : null,
            geoOptions.Value, covers.GetValueOrDefault(company.Id), canManage: true);
    }

    // ARCHITECTURE_CYCLE23.md §391: an instance method since cycle 23 — the public link (PublicUrl) comes from
    // PublicSiteLinks, the one place that knows the two sites' base addresses.
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
    public CompanyDto MapToDto(
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
        bool includeAddressPoint = false,
        // ARCHITECTURE_CYCLE13.md §208/API_CONTRACT_CYCLE13.md §232/§237: "available" (and the whole
        // addressVerification block) must reflect "does THIS caller manage THIS company" — NOT "did the
        // caller pass a non-null usage". `usage` alone conflates two different things: GetMemberOf passes
        // usage for every membership role (so a Master would get a non-null addressVerification if this
        // gated on `usage is null`), and a company with no BillingAccountId yet gets `usage: null` from
        // GetUsageAsync even for its own owner (so addressVerification would silently vanish for the one
        // caller §237 is written for). Review finding (cycle 13 review, blocking #2) — every call site
        // below passes this explicitly rather than leaving it to infer from `usage`.
        bool canManage = false)
    {
        TimeZoneOffset.TryGetUtcOffsetMinutes(c.TimeZoneId, DateTime.UtcNow, out var utcOffsetMinutes);
        var accountSeatsUsed = usage?.SeatsUsed;
        var accountSeatsLimit = usage is null ? (int?)null : plan.AccountMaxEmployees;
        var canAddEmployee = usage is null
            ? (bool?)null
            : !plan.AccountMaxEmployees.HasValue || usage.SeatsUsed < plan.AccountMaxEmployees.Value;

        // ARCHITECTURE_CYCLE13.md §208/§232: gated on the explicit `canManage` flag, not on `usage`
        // (see that parameter's doc comment above) — `available` additionally requires the switch itself
        // to be on (Provider != "logging").
        var addressVerification = !canManage
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
            addressVerification, addressPoint,
            c.YandexMapsUrl, c.TwoGisUrl, ClientRescheduleWindow.Normalize(c.ClientRescheduleMinHours),
            c.Kind.ToString(), siteLinks.CompanyPageUrl(c));
    }

    // ARCHITECTURE_CYCLE10.md §109.3: one batched query for the whole page's cover photos (Position ==
    // 0), never one query per company — same pattern as GetCitiesAsync/GetReviewAggregatesAsync above.
    // A company with no CompanyPhoto rows simply has no entry in the result.
    public async Task<Dictionary<Guid, (string Url, string ThumbnailUrl)>> GetCoversAsync(IEnumerable<Guid> companyIds)
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
    public async Task<List<CompanyPhotoDto>> GetPhotosOrderedAsync(Guid companyId)
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
    public async Task<(Dictionary<Guid, int> EmployeeCounts, Dictionary<Guid, AccountUsage> UsageByAccount)> GetUsageAsync(
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
    public async Task<Dictionary<int, City>> GetCitiesAsync(IEnumerable<int?> cityIds)
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
    public async Task<(double? AverageRating, int ReviewCount)> GetReviewAggregateAsync(Guid companyId)
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
    public async Task<Dictionary<Guid, (double? AverageRating, int ReviewCount)>> GetReviewAggregatesAsync(
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
