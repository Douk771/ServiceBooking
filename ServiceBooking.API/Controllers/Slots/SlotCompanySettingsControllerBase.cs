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
using ServiceBooking.API.Services.Slots;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

using ServiceBooking.API.Controllers.Stays;
namespace ServiceBooking.API.Controllers.Slots;

/// <summary>
/// ARCHITECTURE_CYCLE42.md §42.4.1 — the actions of a slot company shared by both verticals: payment details, executor, address, QR code and notification settings.
/// The manage card and the address rules differ by vertical and come from the heir.
/// </summary>
public abstract class SlotCompanySettingsControllerBase(
    AppDbContext db, CompanyCreationService companyCreation, StaysAccessResolver access, StaysCompanyService companyService,
    PublicSiteLinks links, ServiceBooking.API.Services.Notifications.AccountMessagingReader messagingReader, IStaysClock clock) : ControllerBase
{
    public const string MessengerUnavailableText = "Подключите канал WhatsApp или MAX, чтобы отправлять сообщения гостям";

    protected abstract SlotVertical Vertical { get; }

    /// <summary>The company card of the vertical (<c>StaysCompanyManageDto</c> / <c>BathsCompanyManageDto</c>).</summary>
    protected abstract Task<ActionResult> ManageResultAsync(Company company, StaysMyRole role, CancellationToken ct);

    /// <summary>The address of the company is a part of every link of the public catalog: the vertical drops what it cached.</summary>
    protected virtual void OnCompanyAddressChanged() { }

    protected abstract string NormalizeSlug(string? slug);

    protected abstract Task<StaysConflictDto?> SlugRefusalAsync(string slug, Guid exceptCompanyId);

    protected AppDbContext Db => db;
    protected CompanyCreationService CompanyCreation => companyCreation;
    protected StaysAccessResolver Access => access;
    protected StaysCompanyService CompanyService => companyService;
    protected PublicSiteLinks Links => links;
    protected IStaysClock Clock => clock;

    [HttpPut("companies/{companyId:guid}/payment-details")]
    [RequiresOwnerTerms]
    public async Task<ActionResult> UpdatePaymentDetails(Guid companyId, PaymentDetailsDto input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        var error = StaysSettingsRules.ValidatePaymentDetails(input);
        if (error is not null) return BadRequest(error);

        var settings = await companyService.LoadSettingsAsync(companyId, track: true, ct);
        if (db.Entry(settings).State == EntityState.Detached) db.StaysSettings.Add(settings);
        settings.PaymentDetails = StaysSettingsRules.Trim(input.PaymentDetails);
        settings.PaymentPurpose = StaysSettingsRules.Trim(input.PaymentPurpose);
        Touch(settings);
        await db.SaveChangesAsync(ct);
        return await ManageResultAsync(result.Company!, result.Role, ct);
    }

    /// <summary>ЮР-3: the executor's details. Full replacement.</summary>
    [HttpPut("companies/{companyId:guid}/provider")]
    [RequiresOwnerTerms]
    public async Task<ActionResult> UpdateProvider(Guid companyId, ProviderInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, ct: ct, kind: Vertical.Kind);
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
        return await ManageResultAsync(result.Company!, result.Role, ct);
    }

    /// <summary>The old link and printed QR codes stop working: no redirect (SPEC).</summary>
    [DemoForbidden]
    [HttpPut("companies/{companyId:guid}/slug")]
    [RequiresOwnerTerms]
    public async Task<ActionResult> ChangeSlug(Guid companyId, SlugInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        var company = result.Company!;

        var slug = NormalizeSlug(input.Slug);
        if (slug != company.Slug)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await AdvisoryLock.AcquireAsync(db, "company-slug");
            var refusal = await SlugRefusalAsync(slug, company.Id);
            if (refusal is not null) return Conflict(refusal);
            company.Slug = slug;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            OnCompanyAddressChanged();
        }
        return await ManageResultAsync(company, result.Role, ct);
    }

    [HttpGet("companies/{companyId:guid}/qr")]
    public async Task<IActionResult> GetQr(Guid companyId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ViewCabinet, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        return File(ShopQrCode.EncodePng(links.CompanyPageUrl(result.Company!)), "image/png", $"{result.Company!.Slug}-qr.png");
    }

    [HttpGet("companies/{companyId:guid}/notification-settings")]
    public async Task<ActionResult<StaysNotificationSettingsDto>> GetNotificationSettings(Guid companyId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ViewCabinet, asNoTracking: true, ct: ct, kind: Vertical.Kind);
        if (!result.Ok) return result.Error!;
        return Ok(await BuildNotificationSettingsAsync(companyId, ct));
    }

    [HttpPut("companies/{companyId:guid}/notification-settings")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<StaysNotificationSettingsDto>> UpdateNotificationSettings(Guid companyId, StaysNotificationSettingsInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(companyId, User, StaysPermission.ManageCompany, asNoTracking: true, ct: ct, kind: Vertical.Kind);
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

        // Cycle 40 (§40.6.4): "available" = the account has a PAID transport; the 409 for switching the flag on without one stays, the 400 for a priority
        // that is not funded is gone.
        var messagingAtWrite = await messagingReader.ForCompanyAsync(companyId, ct: ct);
        if (input.GuestMessengerEnabled && !messagingAtWrite.AnyPaid)
            return Conflict(new StaysConflictDto("MessengerUnavailable", MessengerUnavailableText));

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
        var messaging = await messagingReader.ForCompanyAsync(companyId, ct: ct);
        var status = ServiceBooking.API.Services.Notifications.CompanyMessagingStatus.Evaluate(
            messaging.PlatformEnabled, n.DeliveryMode, n.PriorityTransport,
            messaging.Transports.Select(t => new ServiceBooking.API.Services.Notifications.TransportMessagingFacts(t.Transport, t.Option.Open, t.Paid, t.Routable, t.Working)).ToList());
        return new StaysNotificationSettingsDto(
            n.StaffPushEnabled, settings.StaffMaxEnabled, settings.GuestWebPushEnabled, settings.GuestMessengerEnabled, messaging.AnyPaid,
            n.DeliveryMode.ToString(), n.PriorityTransport.ToString(), status.MessagingActive, status.DeliveryChoiceVisible, status.PriorityWarning);
    }

    protected void Touch(StaysSettings s)
    {
        s.UpdatedAtUtc = clock.UtcNow;
        s.UpdatedByUserId = UserId;
    }

    protected string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
