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
    AppDbContext db, CompanyCreationService companyCreation, StaysAccessResolver access, StaysCompanyService companyService,
    PublicSiteLinks links, StaysTrialService trial, BillingAccountProvisioner accounts, ShopChannelReader channelReader,
    IStaysClock clock) : ControllerBase
{
    public const string MessengerUnavailableText = "Подключите канал WhatsApp или MAX, чтобы отправлять сообщения гостям";

    [HttpPost("companies")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysCompanyCreatedDto>> Create(StaysCompanyCreateInput input)
    {
        var outcome = await companyCreation.CreateAsync(
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

        var dto = await companyService.BuildManageAsync(company, StaysMyRole.Owner, HttpContext.RequestAborted);
        return StatusCode(StatusCodes.Status201Created, new StaysCompanyCreatedDto(dto, outcome.Token!, trialOutcome));
    }

    [HttpGet("companies/my")]
    public async Task<ActionResult<List<StaysCompanyListItemDto>>> GetMine(CancellationToken ct)
    {
        var rows = await db.CompanyMembers.AsNoTracking().Where(CompanyMembership.IsStaffRole)
            .Where(cm => cm.UserId == UserId && cm.Company.Kind == CompanyKind.Stays)
            .OrderBy(cm => cm.Company.Name).ThenBy(cm => cm.CompanyId)
            .Select(cm => new { cm.Company, cm.Role, cm.StaffPosition }).ToListAsync(ct);
        var result = new List<StaysCompanyListItemDto>();
        foreach (var r in rows)
        {
            var role = StaysAccess.RoleOfMember(r.Role == UserRole.CompanyOwner, r.StaffPosition);
            var gate = await companyService.EvaluateGateAsync(r.Company, await companyService.LoadSettingsAsync(r.Company.Id, ct: ct), ct);
            int? awaiting = role == StaysMyRole.Housekeeper ? null
                : await db.StayBookings.AsNoTracking().CountAsync(b => b.CompanyId == r.Company.Id && b.Status == StayBookingStatus.AwaitingPaymentCheck, ct);
            result.Add(new StaysCompanyListItemDto(r.Company.Id, r.Company.Name, r.Company.Slug, r.Company.LogoUrl, links.CompanyPageUrl(r.Company), role, gate.Accepting, awaiting));
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
            var refusal = await companyCreation.StaysSlugRefusalAsync(normalized, companyId);
            return Ok(new StaysSlugCheckDto(normalized, refusal is null, refusal));
        }
        if (string.IsNullOrWhiteSpace(name))
            return Ok(new StaysSlugCheckDto(string.Empty, false, new StaysConflictDto("SlugInvalid", ShopTexts.SlugInvalid)));
        return Ok(new StaysSlugCheckDto(await companyCreation.SuggestStaysSlugAsync(name), true, null));
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
        var result = await access.ResolveAnyAsync(companyId, User, [StaysPermission.ViewCabinet, StaysPermission.ViewSchedule], asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        return Ok(await companyService.BuildManageAsync(result.Company!, result.Role, ct));
    }

    [HttpPut("companies/{companyId:guid}/settings")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysCompanyManageDto>> UpdateSettings(Guid companyId, StaysSettingsDto input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, ct: ct);
        if (!result.Ok) return result.Error!;
        var error = StaysSettingsRules.Validate(input, out var checkIn, out var checkOut, out var infoSend);
        if (error is not null) return BadRequest(error);

        var settings = await companyService.LoadSettingsAsync(companyId, track: true, ct);
        if (db.Entry(settings).State == EntityState.Detached) db.StaysSettings.Add(settings);
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
        Touch(settings);
        result.Company!.ShowInPublicListing = input.ShowInCatalog;
        await db.SaveChangesAsync(ct);
        return Ok(await companyService.BuildManageAsync(result.Company!, result.Role, ct));
    }

    [HttpPut("companies/{companyId:guid}/payment-details")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysCompanyManageDto>> UpdatePaymentDetails(Guid companyId, PaymentDetailsDto input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, ct: ct);
        if (!result.Ok) return result.Error!;
        var error = StaysSettingsRules.ValidatePaymentDetails(input);
        if (error is not null) return BadRequest(error);

        var settings = await companyService.LoadSettingsAsync(companyId, track: true, ct);
        if (db.Entry(settings).State == EntityState.Detached) db.StaysSettings.Add(settings);
        settings.PaymentDetails = StaysSettingsRules.Trim(input.PaymentDetails);
        settings.PaymentPurpose = StaysSettingsRules.Trim(input.PaymentPurpose);
        Touch(settings);
        await db.SaveChangesAsync(ct);
        return Ok(await companyService.BuildManageAsync(result.Company!, result.Role, ct));
    }

    /// <summary>ЮР-3: the executor's details. Full replacement.</summary>
    [HttpPut("companies/{companyId:guid}/provider")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysCompanyManageDto>> UpdateProvider(Guid companyId, ProviderInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, ct: ct);
        if (!result.Ok) return result.Error!;
        var error = StaysSettingsRules.ValidateProvider(input, out var name, out var inn, out var ogrn, out var address);
        if (error is not null) return BadRequest(error);

        var settings = await companyService.LoadSettingsAsync(companyId, track: true, ct);
        if (db.Entry(settings).State == EntityState.Detached) db.StaysSettings.Add(settings);
        settings.ProviderStatus = input.Status;
        settings.ProviderName = name;
        settings.ProviderInn = inn;
        settings.ProviderOgrn = ogrn;
        settings.ProviderClaimsAddress = address;
        Touch(settings);
        await db.SaveChangesAsync(ct);
        return Ok(await companyService.BuildManageAsync(result.Company!, result.Role, ct));
    }

    /// <summary>The old link and printed QR codes stop working: no redirect (SPEC).</summary>
    [DemoForbidden]
    [HttpPut("companies/{companyId:guid}/slug")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysCompanyManageDto>> ChangeSlug(Guid companyId, SlugInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, ct: ct);
        if (!result.Ok) return result.Error!;
        var company = result.Company!;

        var slug = StaysSlugPolicy.Normalize(input.Slug);
        if (slug != company.Slug)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await AdvisoryLock.AcquireAsync(db, "company-slug");
            var refusal = await companyCreation.StaysSlugRefusalAsync(slug, exceptCompanyId: company.Id);
            if (refusal is not null) return Conflict(refusal);
            company.Slug = slug;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        return Ok(await companyService.BuildManageAsync(company, result.Role, ct));
    }

    [HttpGet("companies/{companyId:guid}/qr")]
    public async Task<IActionResult> GetQr(Guid companyId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ViewCabinet, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        return File(ShopQrCode.EncodePng(links.CompanyPageUrl(result.Company!)), "image/png", $"{result.Company!.Slug}-qr.png");
    }

    [HttpGet("companies/{companyId:guid}/notification-settings")]
    public async Task<ActionResult<StaysNotificationSettingsDto>> GetNotificationSettings(Guid companyId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ViewCabinet, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        return Ok(await BuildNotificationSettingsAsync(companyId, ct));
    }

    [HttpPut("companies/{companyId:guid}/notification-settings")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysNotificationSettingsDto>> UpdateNotificationSettings(Guid companyId, StaysNotificationSettingsInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        NotificationDeliveryMode? mode = null;
        NotificationTransport? priority = null;
        if (!string.IsNullOrWhiteSpace(input.DeliveryMode))
        {
            if (!Enum.TryParse<NotificationDeliveryMode>(input.DeliveryMode, true, out var m) || !Enum.IsDefined(m)) return BadRequest("Неизвестный режим доставки");
            mode = m;
        }
        if (!string.IsNullOrWhiteSpace(input.PriorityTransport))
        {
            if (!Enum.TryParse<NotificationTransport>(input.PriorityTransport, true, out var t) || !Enum.IsDefined(t)) return BadRequest("Неизвестный канал");
            priority = t;
        }

        var channels = await channelReader.LoadAsync(companyId, ct);
        var funded = channels.Where(c => c.IsFunded).ToList();
        if (input.GuestMessengerEnabled && funded.Count == 0)
            return Conflict(new StaysConflictDto("MessengerUnavailable", MessengerUnavailableText));
        if (priority is { } p && funded.All(c => c.Channel.Transport != p))
            return BadRequest("Приоритетный канал должен быть среди оплаченных каналов компании");

        var settings = await companyService.LoadSettingsAsync(companyId, track: true, ct);
        if (db.Entry(settings).State == EntityState.Detached) db.StaysSettings.Add(settings);
        settings.StaffMaxEnabled = input.StaffMaxEnabled;
        settings.GuestWebPushEnabled = input.GuestWebPushEnabled;
        settings.GuestMessengerEnabled = input.GuestMessengerEnabled;
        Touch(settings);

        var notificationSettings = await db.CompanyNotificationSettings.FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);
        if (notificationSettings is null)
        {
            notificationSettings = new CompanyNotificationSettings { CompanyId = companyId };
            db.CompanyNotificationSettings.Add(notificationSettings);
        }
        notificationSettings.StaffPushEnabled = input.StaffPushEnabled;
        if (mode is { } dm) notificationSettings.DeliveryMode = dm;
        if (priority is { } pt) notificationSettings.PriorityTransport = pt;
        notificationSettings.UpdatedAt = clock.UtcNow;
        notificationSettings.UpdatedByUserId = UserId;
        await db.SaveChangesAsync(ct);
        return Ok(await BuildNotificationSettingsAsync(companyId, ct));
    }

    private async Task<StaysNotificationSettingsDto> BuildNotificationSettingsAsync(Guid companyId, CancellationToken ct)
    {
        var settings = await companyService.LoadSettingsAsync(companyId, ct: ct);
        var n = await db.CompanyNotificationSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == companyId, ct) ?? new CompanyNotificationSettings { CompanyId = companyId };
        var available = await channelReader.IsMessengerAvailableAsync(companyId, ct);
        return new StaysNotificationSettingsDto(
            n.StaffPushEnabled, settings.StaffMaxEnabled, settings.GuestWebPushEnabled, settings.GuestMessengerEnabled, available,
            n.DeliveryMode.ToString(), n.PriorityTransport.ToString());
    }

    private void Touch(StaysSettings s)
    {
        s.UpdatedAtUtc = clock.UtcNow;
        s.UpdatedByUserId = UserId;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
