using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Controllers.Slots;
using ServiceBooking.API.DTOs.Baths;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Baths;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.4.1, API_CONTRACT_CYCLE42.md §42.27, §42.28 — the «Бани» company: creation, the cabinet list, the address, the card, settings, requisites,
/// executor, QR code, notification settings, the schedule of the bath attendant and the revision. A company is a <c>Company</c> with <c>Kind = Baths</c>; a company of
/// another kind, a missing or a foreign one is 404 with an empty body. The trial itself is <c>BathsTrialController</c>; here it is granted after the creation when <c>trialTermsVersion</c> is passed.
/// </summary>
[ApiController]
[Route("api/baths")]
[Authorize]
public class BathsCompaniesController(
    AppDbContext dbArg, CompanyCreationService companyCreationArg, StaysAccessResolver accessArg, StaysCompanyService companyServiceArg,
    PublicSiteLinks linksArg, ServiceBooking.API.Services.Notifications.AccountMessagingReader messagingReaderArg, IStaysClock clockArg, BathsCompanyService baths, BathsScheduleService schedule,
    StaysTrialService trial, BathsCatalogService catalogCache)
    : SlotCompanySettingsControllerBase(dbArg, companyCreationArg, accessArg, companyServiceArg, linksArg, messagingReaderArg, clockArg)
{
    protected override SlotVertical Vertical => SlotVerticals.Baths;

    protected override async Task<ActionResult> ManageResultAsync(Company company, StaysMyRole role, CancellationToken ct) =>
        Ok(await baths.BuildManageAsync(company, role, ct));

    protected override string NormalizeSlug(string? slug) => BathsSlugPolicy.Normalize(slug);

    protected override Task<StaysConflictDto?> SlugRefusalAsync(string slug, Guid exceptCompanyId) => CompanyCreation.BathsSlugRefusalAsync(slug, exceptCompanyId);

    [HttpPost("companies")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<BathsCompanyCreatedDto>> Create(BathsCompanyCreateInput input)
    {
        var outcome = await CompanyCreation.CreateAsync(
            CompanyKind.Baths,
            new CompanyCreationRequest(input.Name, input.Slug, input.Description, input.Address, input.Phone, Email: null, input.CityId,
                TimeZoneId: null, AllowSelfBooking: false, ShowInPublicListing: true, input.OwnerTermsVersion),
            User, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
        if (outcome.Error is not null) return outcome.Error;

        // After the commit; a refused trial never undoes the creation (API_CONTRACT_CYCLE42.md §42.27.1 п. 7).
        StaysTrialOutcomeDto? trialOutcome = null;
        if (!string.IsNullOrWhiteSpace(input.TrialTermsVersion))
            trialOutcome = await trial.GrantAsync(SlotVerticals.Baths, outcome.AccountId, UserId, input.TrialTermsVersion, HttpContext.RequestAborted);

        var dto = await baths.BuildManageAsync(outcome.Company!, StaysMyRole.Owner, HttpContext.RequestAborted);
        return StatusCode(StatusCodes.Status201Created, new BathsCompanyCreatedDto(dto, outcome.Token!, trialOutcome));
    }

    [HttpGet("companies/my")]
    public async Task<ActionResult<List<BathsCompanyListItemDto>>> GetMine(CancellationToken ct) =>
        Ok(await baths.ListMineAsync(UserId, ct));

    /// <summary>Check an address, or suggest a free one made from the name. Always 200.</summary>
    [HttpGet("slug-check")]
    public async Task<ActionResult<BathsSlugCheckDto>> CheckSlug(
        [FromQuery] string? name, [FromQuery] string? slug, [FromQuery] Guid? companyId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var normalized = BathsSlugPolicy.Normalize(slug);
            var refusal = await CompanyCreation.BathsSlugRefusalAsync(normalized, companyId);
            return Ok(new BathsSlugCheckDto(normalized, refusal is null, refusal));
        }
        if (string.IsNullOrWhiteSpace(name))
            return Ok(new BathsSlugCheckDto(string.Empty, false, new StaysConflictDto("SlugInvalid", ShopTexts.SlugInvalid)));
        return Ok(new BathsSlugCheckDto(await CompanyCreation.SuggestBathsSlugAsync(name), true, null));
    }

    [HttpGet("companies/{companyId:guid}")]
    public async Task<ActionResult<BathsCompanyManageDto>> Get(Guid companyId, CancellationToken ct)
    {
        var result = await Access.ResolveAnyAsync(companyId, User, [StaysPermission.ViewCabinet, StaysPermission.ViewSchedule], asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        return Ok(await baths.BuildManageAsync(result.Company!, result.Role, ct));
    }

    [HttpPut("companies/{companyId:guid}/settings")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<BathsCompanyManageDto>> UpdateSettings(Guid companyId, BathsSettingsDto input, CancellationToken ct)
    {
        var result = await Access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        var error = BathsCompanyService.ValidateSettings(input);
        if (error is not null) return BadRequest(error);

        var settings = await CompanyService.LoadSettingsAsync(companyId, track: true, ct);
        if (Db.Entry(settings).State == EntityState.Detached) Db.StaysSettings.Add(settings);
        BathsCompanyService.ApplySettings(settings, result.Company!, input);
        Touch(settings);
        await Db.SaveChangesAsync(ct);
        // ARCHITECTURE_CYCLE42.md §42.10.2: «Показывать в каталоге» changes the catalog at once, not after the cache TTL.
        catalogCache.InvalidateBase();
        return Ok(await baths.BuildManageAsync(result.Company!, result.Role, ct));
    }

    /// <summary>The schedule of the bath attendant (ViewSchedule): sessions by the business date of the start, a closed shape.</summary>
    [HttpGet("companies/{companyId:guid}/schedule")]
    [EnableRateLimiting("stays-board")]
    public async Task<ActionResult<BathScheduleDto>> Schedule(Guid companyId, [FromQuery] string? from, [FromQuery] int? days, CancellationToken ct)
    {
        var result = await Access.ResolveAsync(companyId, User, StaysPermission.ViewSchedule, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        var error = BathsScheduleService.ParsePeriod(from, days, out var fromDate, out var count);
        if (error is not null) return BadRequest(error);
        return Ok(await schedule.BuildAsync(result.Company!, result.Role, fromDate, count, ct));
    }

    /// <summary>The revision of the bookings: the cabinet polls it (≤ 30 s) to refresh the service day, the list and the schedule.</summary>
    [HttpGet("companies/{companyId:guid}/revision")]
    public async Task<ActionResult<BathsRevisionDto>> Revision(Guid companyId, CancellationToken ct)
    {
        var result = await Access.ResolveAnyAsync(companyId, User, [StaysPermission.ViewCabinet, StaysPermission.ViewSchedule], asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        var revision = await Db.StaysSettings.AsNoTracking().Where(s => s.CompanyId == companyId).Select(s => (long?)s.BookingsRevision).FirstOrDefaultAsync(ct) ?? 0;
        return Ok(new BathsRevisionDto(revision));
    }
}
