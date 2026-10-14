using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Demo;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.API.Controllers.Slots;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.3, §37.9, §37.27 — the "Дома" company: creation, the cabinet list, the address, settings, requisites, provider, QR code,
/// notification settings and the trial. A company is a <c>Company</c> with <c>Kind = Stays</c>; profile edits, logo and address reuse the common company
/// routes. Order of checks: token → the company exists, is a «Дома» company and the caller belongs to it (404) → the permission (403, empty body).
/// </summary>
[ApiController]
[Route("api/stays")]
[Authorize]
public class StaysCompaniesController(
    AppDbContext dbArg, CompanyCreationService companyCreationArg, StaysAccessResolver accessArg, StaysCompanyService companyServiceArg,
    PublicSiteLinks linksArg, StaysTrialService trial, BillingAccountProvisioner accounts,
    ServiceBooking.API.Services.Notifications.AccountMessagingReader messagingReaderArg,
    IStaysClock clockArg, ArrivalReminderService reminder, StayActorResolver actors, Microsoft.Extensions.Options.IOptions<StaysOptions> staysOptions)
    : SlotCompanySettingsControllerBase(dbArg, companyCreationArg, accessArg, companyServiceArg, linksArg, messagingReaderArg, clockArg)
{
    protected override SlotVertical Vertical => SlotVerticals.Stays;

    protected override async Task<ActionResult> ManageResultAsync(Company company, StaysMyRole role, CancellationToken ct) =>
        Ok(await CompanyService.BuildManageAsync(company, role, ct));

    protected override string NormalizeSlug(string? slug) => StaysSlugPolicy.Normalize(slug);

    protected override Task<StaysConflictDto?> SlugRefusalAsync(string slug, Guid exceptCompanyId) => CompanyCreation.StaysSlugRefusalAsync(slug, exceptCompanyId);

    [HttpPost("companies")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysCompanyCreatedDto>> Create(StaysCompanyCreateInput input)
    {
        var outcome = await CompanyCreation.CreateAsync(
            CompanyKind.Stays,
            new CompanyCreationRequest(input.Name, input.Slug, input.Description, Address: null, input.Phone, Email: null, CityId: null,
                TimeZoneId: null, AllowSelfBooking: false, ShowInPublicListing: true, input.OwnerTermsVersion),
            User, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
        if (outcome.Error is not null) return outcome.Error;

        var company = outcome.Company!;
        // After the commit; a refused trial never undoes the creation (API_CONTRACT_CYCLE37.md §37.27.1).
        StaysTrialOutcomeDto? trialOutcome = null;
        if (!string.IsNullOrWhiteSpace(input.TrialTermsVersion))
            trialOutcome = await trial.GrantAsync(outcome.AccountId, UserId, input.TrialTermsVersion, HttpContext.RequestAborted);

        var dto = await CompanyService.BuildManageAsync(company, StaysMyRole.Owner, HttpContext.RequestAborted);
        return StatusCode(StatusCodes.Status201Created, new StaysCompanyCreatedDto(dto, outcome.Token!, trialOutcome));
    }

    [HttpGet("companies/my")]
    public async Task<ActionResult<List<StaysCompanyListItemDto>>> GetMine(CancellationToken ct)
    {
        var rows = await Db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.UserId == UserId && cm.Company.Kind == CompanyKind.Stays)
            .OrderBy(cm => cm.Company.Name).ThenBy(cm => cm.CompanyId)
            .Select(cm => new { cm.Company, cm.Role, cm.StaffPosition }).ToListAsync(ct);
        var result = new List<StaysCompanyListItemDto>();
        foreach (var r in rows)
        {
            var role = StaysAccess.RoleOfMember(r.Role == UserRole.CompanyOwner, r.StaffPosition);
            var gate = await CompanyService.EvaluateGateAsync(r.Company, await CompanyService.LoadSettingsAsync(r.Company.Id, ct: ct), ct);
            int? awaiting = role == StaysMyRole.Housekeeper ? null
                : await Db.StayBookings.AsNoTracking().CountAsync(b => b.CompanyId == r.Company.Id && b.Status == StayBookingStatus.AwaitingPaymentCheck, ct)
                  + await Db.StayServiceOrders.AsNoTracking().CountAsync(o => o.CompanyId == r.Company.Id && o.Status == StayBookingStatus.AwaitingPaymentCheck, ct);
            result.Add(new StaysCompanyListItemDto(r.Company.Id, r.Company.Name, r.Company.Slug, r.Company.LogoUrl, Links.CompanyPageUrl(r.Company), role, gate.Accepting, awaiting));
        }
        return Ok(result);
    }

    /// <summary>Check an address, or suggest a free one made from the name. Always 200 (API_CONTRACT_CYCLE37.md §37.27).</summary>
    [HttpGet("slug-check")]
    public async Task<ActionResult<StaysSlugCheckDto>> CheckSlug(
        [FromQuery] string? name, [FromQuery] string? slug, [FromQuery] Guid? companyId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(slug))
        {
            var normalized = StaysSlugPolicy.Normalize(slug);
            var refusal = await CompanyCreation.StaysSlugRefusalAsync(normalized, companyId);
            return Ok(new StaysSlugCheckDto(normalized, refusal is null, refusal));
        }
        if (string.IsNullOrWhiteSpace(name))
            return Ok(new StaysSlugCheckDto(string.Empty, false, new StaysConflictDto("SlugInvalid", ShopTexts.SlugInvalid)));
        return Ok(new StaysSlugCheckDto(await CompanyCreation.SuggestStaysSlugAsync(name), true, null));
    }

    [HttpGet("trial")]
    public async Task<ActionResult<StaysTrialStateDto>> GetTrial(CancellationToken ct)
    {
        var accountId = await accounts.EnsureAccountAsync(UserId);
        return Ok(await trial.GetStateAsync(accountId, UserId, ct));
    }

    [HttpPost("trial")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysTrialOutcomeDto>> ActivateTrial(StaysTrialInput input, CancellationToken ct)
    {
        var accountId = await accounts.EnsureAccountAsync(UserId);
        var outcome = await trial.GrantAsync(accountId, UserId, input.TermsVersion, ct);
        return outcome.Granted ? Ok(outcome) : Conflict(outcome);
    }

    [HttpGet("companies/{companyId:guid}")]
    public async Task<ActionResult<StaysCompanyManageDto>> Get(Guid companyId, CancellationToken ct)
    {
        var result = await Access.ResolveAnyAsync(companyId, User, [StaysPermission.ViewCabinet, StaysPermission.ViewSchedule], asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        return Ok(await CompanyService.BuildManageAsync(result.Company!, result.Role, ct));
    }

    [HttpPut("companies/{companyId:guid}/settings")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysCompanyManageDto>> UpdateSettings(Guid companyId, StaysSettingsDto input, CancellationToken ct)
    {
        var result = await Access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        var error = StaysSettingsRules.Validate(input, out var checkIn, out var checkOut, out var infoSend);
        if (error is not null) return BadRequest(error);

        var settings = await CompanyService.LoadSettingsAsync(companyId, track: true, ct);
        if (Db.Entry(settings).State == EntityState.Detached) Db.StaysSettings.Add(settings);
        settings.CheckInTime = checkIn;
        settings.CheckOutTime = checkOut;
        settings.MinNights = input.MinNights;
        settings.MaxNights = input.MaxNights;
        settings.HorizonDays = input.HorizonDays;
        settings.AllowGapFill = input.AllowGapFill;
        settings.AllowSameDayCheckIn = input.AllowSameDayCheckIn;
        settings.HoldMinutes = input.HoldMinutes;
        settings.PrepayPercent = input.PrepayPercent;
        settings.CancellationPolicy = input.CancellationPolicy;
        settings.DogFeeRub = input.DogFeeRub;
        settings.CotFeeRub = input.CotFeeRub;
        settings.CheckInInfoSendTime = infoSend;
        settings.CheckInInfoText = StaysSettingsRules.Trim(input.CheckInInfoText);
        settings.CheckInInfoSendFullText = input.CheckInInfoSendFullText;
        settings.ArrivalReminderEnabled = input.ArrivalReminderEnabled;
        settings.HousekeeperSeesGuestComment = input.HousekeeperSeesGuestComment;
        // Cycle 39: null / absent = do not change (an old client replacing the whole form must not switch the orders off).
        if (input.AcceptServiceOrdersWithoutStay is { } accept)
        {
            if (accept && !StaysBookingGate.ProviderComplete(StaysCompanyService.ProviderFacts(settings)))
                return Conflict(new StaysConflictDto("ProviderRequiredForServiceOrders", ServiceTexts.ProviderRequiredForServiceOrders));
            settings.AcceptServiceOrdersWithoutStay = accept;
        }
        Touch(settings);
        result.Company!.ShowInPublicListing = input.ShowInCatalog;
        await Db.SaveChangesAsync(ct);
        return Ok(await CompanyService.BuildManageAsync(result.Company!, result.Role, ct));
    }

    // ── cycle 39: the arrival reminder (API_CONTRACT_CYCLE39.md §39.33) ──

    [HttpGet("companies/{companyId:guid}/arrival-reminder")]
    public async Task<ActionResult<ArrivalReminderSettingsDto>> GetArrivalReminder(Guid companyId, CancellationToken ct)
    {
        var result = await Access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        return Ok(reminder.ToDto(await CompanyService.LoadSettingsAsync(companyId, ct: ct)));
    }

    [HttpPut("companies/{companyId:guid}/arrival-reminder")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ArrivalReminderSettingsDto>> SaveArrivalReminder(Guid companyId, ArrivalReminderInput input, CancellationToken ct)
    {
        var result = await Access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        var saved = await reminder.SaveAsync(result.Company!, await CompanyService.LoadSettingsAsync(companyId, ct: ct), input, await actors.ResolveStaffAsync(User, ct), ct);
        if (saved.BadRequest is not null) return BadRequest(saved.BadRequest);
        if (saved.Conflict is not null) return Conflict(saved.Conflict);
        return Ok(saved.Settings);
    }

    [HttpPost("companies/{companyId:guid}/arrival-reminder/preview")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("stays-board")]
    public async Task<ActionResult<ArrivalReminderPreviewDto>> PreviewArrivalReminder(Guid companyId, ArrivalReminderPreviewInput input, CancellationToken ct)
    {
        var result = await Access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        if (input.Template is { Length: > 2000 }) return BadRequest("Текст напоминания — не длиннее 700 символов");
        var preview = await reminder.PreviewAsync(result.Company!, input, ct);
        return preview is null ? NotFound() : Ok(preview);
    }

    [HttpGet("companies/{companyId:guid}/arrival-reminder/history")]
    public async Task<ActionResult<List<ArrivalReminderChangeDto>>> ArrivalReminderHistory(Guid companyId, CancellationToken ct)
    {
        var result = await Access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        return Ok(await reminder.HistoryAsync(companyId, staysOptions.Value.Services.HistoryRows, ct));
    }
}
