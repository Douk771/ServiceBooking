using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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

/// <summary>
/// Address verification against a map, and the public-address-notice acknowledgement
/// (ARCHITECTURE_CYCLE13.md §207, §209, §220; API_CONTRACT_CYCLE13.md §233/§234/§242). A deliberately
/// separate controller from <see cref="CompaniesController"/> (already the largest in the project, same
/// reasoning that split <see cref="CompanyPhotosController"/> out in cycle 10) — reuses
/// <see cref="CompanyMembership"/> and <see cref="CompaniesController.MapToDto"/> rather than a second,
/// drifting copy of "who can manage this company"/"what a CompanyDto looks like".
///
/// All three routes require <see cref="AuthorizeAttribute"/> and share the <c>address-verify</c> rate
/// limit (§210/§238) — the first two can reach the paid geocoder, the third writes a consent-journal row,
/// and none of the three is free for an automated caller.
/// </summary>
[ApiController]
[Route("api/companies")]
public class CompanyAddressController(
    AppDbContext db, AddressLookupService lookupService, IOptions<GeoOptions> geoOptions,
    SubscriptionResolver subscriptionResolver, AccountUsageReader accountUsageReader,
    LegalDocumentProvider legalProvider, ConsentLedger ledger) : ControllerBase
{
    // ── POST /api/companies/address/lookup (§233) ───────────────────────────────────────────────────

    [HttpPost("address/lookup")]
    [Authorize]
    [EnableRateLimiting("address-verify")]
    public async Task<ActionResult<AddressLookupResultDto>> Lookup([FromBody] LookupAddressDto dto)
    {
        // §233: "рубильник проверки выключен — функции нет", not "есть и ничего не делает". The
        // interface is expected to never reach this (company.addressVerification.available gates the
        // button), but a direct call still gets the honest answer.
        if (string.Equals(geoOptions.Value.Provider, "logging", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var address = dto.Address ?? "";
        var trimmed = address.Trim();
        if (trimmed.Length == 0) return BadRequest("Укажите адрес");
        if (trimmed.Length > 300) return BadRequest("Адрес не должен превышать 300 символов");

        Company? company = null;
        if (dto.CompanyId is { } companyId)
        {
            company = await db.Companies.FindAsync(companyId);
            // Existence of a company the caller doesn't manage is never confirmed (§233): a missing
            // company and a company the caller can't manage answer identically.
            if (company is null || !await CanManageCompanyAsync(companyId)) return NotFound();
        }

        City? city = null;
        if (dto.CityId is { } cityId)
        {
            city = await db.Cities.FindAsync(cityId);
            if (city is null || !city.IsActive) return BadRequest("Город не найден");
        }
        else if (company?.CityId is { } companyCityId)
        {
            city = await db.Cities.FindAsync(companyCityId);
        }

        var result = await lookupService.LookupAsync(new AddressQuery(trimmed, city?.Name), HttpContext.RequestAborted);
        return Ok(BuildLookupResult(result, city?.Name));
    }

    // ── PUT /api/companies/{id}/address (§234) ──────────────────────────────────────────────────────

    [HttpPut("{id:guid}/address")]
    [Authorize]
    [EnableRateLimiting("address-verify")]
    public async Task<ActionResult<CompanyAddressUpdateResultDto>> SaveAddress(Guid id, [FromBody] SaveCompanyAddressDto dto)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        if (!await CanManageCompanyAsync(id)) return Forbid();

        var address = dto.Address ?? "";
        if (address.Length > 300) return BadRequest("Адрес не должен превышать 300 символов");

        // §234: written EXACTLY as the client sent it, byte for byte — never trimmed, never normalized,
        // never replaced with the geocoder's own wording (§209.1's central rule, R18). Empty string
        // clears the address (distinct from PUT /api/companies/{id}, where null/absent means "don't
        // touch" — this endpoint's `address` field is never optional).
        company.Address = address.Length == 0 ? null : address;

        // Every save starts from "not verified" — only a successful House-precision check below sets
        // these back. §203: editing the address through ANY path already makes the computed status fall
        // back to Unverified; clearing the raw columns here additionally stops a stale point/precision
        // surviving a save that didn't (or couldn't) re-verify.
        company.AddressVerifiedAt = null;
        company.AddressVerifiedInputKey = null;
        company.AddressPrecision = null;
        company.AddressLatitude = null;
        company.AddressLongitude = null;

        var outcome = GeocodeOutcome.Ok; // §234 table, row "verify:false" — no external call, no failure
        IReadOnlyList<AddressWarning> warnings = [];
        string? attribution = null;

        if (dto.Verify && !string.IsNullOrEmpty(company.Address))
        {
            var city = company.CityId.HasValue ? await db.Cities.FindAsync(company.CityId.Value) : null;
            var result = await lookupService.LookupAsync(
                new AddressQuery(company.Address, city?.Name), HttpContext.RequestAborted);
            outcome = result.Outcome;
            attribution = result.Attribution;

            var best = result.Candidates.Count > 0 ? result.Candidates[0] : null;
            if (outcome == GeocodeOutcome.Ok && best is { Precision: AddressPrecision.House })
            {
                // §209.1: verified means "по этому тексту карта нашла строение" — the OWNER'S OWN text
                // is what gets keyed and stamped, never anything from `best` (R18).
                company.AddressVerifiedInputKey = AddressNormalization.Key(company.Address);
                company.AddressVerifiedAt = DateTime.UtcNow;
                company.AddressPrecision = AddressPrecision.House;

                // §209.2/P3: coordinates are written ONLY when the extended licence's own flag is on.
                if (geoOptions.Value.StoreResults && best.Point is { } point)
                {
                    company.AddressLatitude = point.Latitude;
                    company.AddressLongitude = point.Longitude;
                }
            }
            else if (outcome == GeocodeOutcome.Ok && best is not null)
            {
                warnings = BuildCandidateWarnings(best.Precision, best.CityName, city?.Name);
            }
            else if (outcome == GeocodeOutcome.Empty)
            {
                warnings = [AddressWarnings.NotFound];
            }
            else if (outcome == GeocodeOutcome.Unavailable)
            {
                warnings = [AddressWarnings.Unavailable];
            }
            // Disabled: no warnings — §209.1's outcome table says the owner sees nothing extra here,
            // same as an ordinary save with verify:false.
        }

        await db.SaveChangesAsync();

        var status = AddressVerificationState.Status(company);
        var verification = new AddressVerificationResultDto(
            outcome.ToString(), status.ToString(), company.AddressVerifiedAt, company.AddressPrecision?.ToString(),
            warnings.Select(w => w.ToDto()).ToList(), attribution);

        var companyDto = await BuildCompanyDtoAsync(company);
        return Ok(new CompanyAddressUpdateResultDto(companyDto, verification));
    }

    // ── POST /api/companies/address/notice (§242, §220) ─────────────────────────────────────────────

    [HttpPost("address/notice")]
    [Authorize]
    [EnableRateLimiting("address-verify")]
    public async Task<ActionResult<AddressNoticeResultDto>> ConfirmNotice([FromBody] SubmitAddressNoticeDto dto)
    {
        if (!dto.Confirmed) return BadRequest("Подтверждение обязательно.");

        var text = legalProvider.Current?.GetText(LegalTextKey.PublicAddressNotice);
        if (text is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, "Правовые документы временно недоступны.");
        if (dto.TextVersion != text.Version) return Conflict("Текст был обновлён ещё раз — перечитайте и подтвердите заново.");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var subject = ConsentSubject.ForUser(userId);
        var record = await ledger.GrantAsync(new ConsentGrant(
            subject, LegalTextKey.PublicAddressNotice, text.Version, text.ContentHash,
            Purpose: null, ConsentAct.Acknowledged, ConsentSource.AddressForm,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), UserAgent: Request.Headers.UserAgent.ToString()));

        return Ok(new AddressNoticeResultDto(record.DocumentVersion, record.GrantedAtUtc));
    }

    // ── Shared plumbing ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Same rule as <c>CompaniesController.CanManageCompany</c> (owner or SuperAdmin) —
    /// deliberately not a shared method: every controller in this codebase keeps its own private
    /// wrapper around <see cref="CompanyMembership"/> (project convention, see that class's own doc
    /// comment), only the membership predicate itself is shared.</summary>
    private async Task<bool> CanManageCompanyAsync(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        if (User.IsInRole("SuperAdmin")) return true;
        return await CompanyMembership.IsOwnerAsync(db, companyId, userId);
    }

    private static AddressLookupResultDto BuildLookupResult(GeocodeResult result, string? companyCityName)
    {
        IReadOnlyList<AddressWarning> topLevel = result.Outcome switch
        {
            GeocodeOutcome.Empty => [AddressWarnings.NotFound],
            GeocodeOutcome.Unavailable => [AddressWarnings.Unavailable],
            _ => [],
        };

        var candidates = result.Candidates
            .Select(c => c.ToDto(BuildCandidateWarnings(c.Precision, c.CityName, companyCityName)))
            .ToList();

        return new AddressLookupResultDto(
            result.Outcome.ToString(), result.QueriedAddress, candidates,
            topLevel.Select(w => w.ToDto()).ToList(), result.Attribution);
    }

    private static IReadOnlyList<AddressWarning> BuildCandidateWarnings(
        AddressPrecision precision, string? candidateCityName, string? companyCityName)
    {
        var warnings = new List<AddressWarning>();
        var precisionWarning = AddressWarnings.ForPrecision(precision);
        if (precisionWarning is not null) warnings.Add(precisionWarning);
        var cityWarning = AddressWarnings.ForCityMismatch(candidateCityName, companyCityName);
        if (cityWarning is not null) warnings.Add(cityWarning);
        return warnings;
    }

    /// <summary>Builds the exact same "full CompanyDto, as PUT /api/companies/{id} would" shape §234
    /// promises — same four resolution calls (plan, review aggregate, usage, cover) Update/UploadLogo
    /// make in <c>CompaniesController</c>, feeding the SAME <see cref="CompaniesController.MapToDto"/>
    /// so the two endpoints can never quietly return differently-shaped companies.</summary>
    private async Task<CompanyDto> BuildCompanyDtoAsync(Company company)
    {
        var plan = await subscriptionResolver.GetEffectivePlanAsync(company.Id);
        var city = company.CityId.HasValue ? await db.Cities.FindAsync(company.CityId.Value) : null;

        var aggregate = await db.Reviews
            .Where(r => r.CompanyId == company.Id)
            .GroupBy(r => 1)
            .Select(g => new { Count = g.Count(), Average = g.Average(r => (double)r.Rating) })
            .FirstOrDefaultAsync();
        var (averageRating, reviewCount) = aggregate is null ? ((double?)null, 0) : (aggregate.Average, aggregate.Count);

        var employeeCounts = await accountUsageReader.GetCompanySeatsAsync([company.Id]);
        var usageByAccount = company.BillingAccountId.HasValue
            ? await accountUsageReader.GetAsync([company.BillingAccountId.Value])
            : new Dictionary<Guid, AccountUsage>();

        var coverPhoto = await db.CompanyPhotos.Where(p => p.CompanyId == company.Id && p.Position == 0).ToListAsync();
        var cover = CompanyPhotoOrdering.SelectCovers(coverPhoto).GetValueOrDefault(company.Id);

        return CompaniesController.MapToDto(
            company, plan, averageRating, reviewCount, city,
            employeeCounts.GetValueOrDefault(company.Id),
            company.BillingAccountId.HasValue ? usageByAccount.GetValueOrDefault(company.BillingAccountId.Value) : null,
            geoOptions.Value,
            cover is null ? null : (cover.Url, cover.ThumbnailUrl));
    }
}
