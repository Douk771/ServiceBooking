using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.2.2, §37.10.3 — the company-level read model of the "Дома" vertical: settings (a missing row means defaults), the
/// provider (ЮР-3), the booking gate, the owner's checklist and the manage DTO. Every public page and the cabinet get the gate from here.
/// </summary>
public class StaysCompanyService(AppDbContext db, StaysPlanResolver plans, PublicSiteLinks links, IStaysClock clock)
{
    public async Task<StaysSettings> LoadSettingsAsync(Guid companyId, bool track = false, CancellationToken ct = default)
    {
        var query = track ? db.StaysSettings : db.StaysSettings.AsNoTracking();
        return await query.FirstOrDefaultAsync(s => s.CompanyId == companyId, ct) ?? new StaysSettings { CompanyId = companyId };
    }

    public static StayProviderFacts ProviderFacts(StaysSettings s) =>
        new(s.ProviderStatus, s.ProviderName, s.ProviderInn, s.ProviderOgrn, s.ProviderClaimsAddress);

    public async Task<GateResult> EvaluateGateAsync(Company company, StaysSettings settings, CancellationToken ct = default)
    {
        var plan = await plans.GetForCompanyAsync(company, clock.UtcNow, ct);
        var published = await plans.CountPublishedHousesForCompanyAsync(company, ct);
        return StaysBookingGate.Evaluate(company.IsActive, plan.HasActivePlan, published, plan.MaxHouses, settings.PrepayPercent, settings.PaymentDetails, ProviderFacts(settings));
    }

    public static ProviderFullDto? ProviderFull(StaysSettings s) =>
        s.ProviderStatus is { } st && s.ProviderName is not null && s.ProviderInn is not null && s.ProviderClaimsAddress is not null
            ? new ProviderFullDto(st, StaysTexts.ProviderStatusLabel(st), s.ProviderName, s.ProviderInn, s.ProviderOgrn, s.ProviderClaimsAddress)
            : null;

    /// <summary>ЮР-3: an organisation / sole proprietor is shown in full; a self-employed person or an individual — only the status and the INN.</summary>
    public static ProviderPublicDto? ProviderPublic(StaysSettings s)
    {
        if (s.ProviderStatus is not { } st || s.ProviderInn is null) return null;
        var full = st is StayProviderStatus.Organization or StayProviderStatus.IndividualEntrepreneur;
        return new ProviderPublicDto(st, StaysTexts.ProviderStatusLabel(st), full ? s.ProviderName : null, s.ProviderInn,
            full ? s.ProviderOgrn : null, full ? s.ProviderClaimsAddress : null);
    }

    /// <summary>The provider snapshot kept in a booking (JSON of <see cref="ProviderFullDto"/>).</summary>
    public static string? ProviderSnapshotJson(StaysSettings s) =>
        ProviderFull(s) is { } p ? System.Text.Json.JsonSerializer.Serialize(p, SnapshotJson) : null;

    public static ProviderFullDto? ProviderFromSnapshot(string? json) =>
        string.IsNullOrEmpty(json) ? null : System.Text.Json.JsonSerializer.Deserialize<ProviderFullDto>(json, SnapshotJson);

    public static readonly System.Text.Json.JsonSerializerOptions SnapshotJson = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public StaysSettingsDto ToDto(StaysSettings s, Company company) => new(
        StayFormat.Time(s.CheckInTime), StayFormat.Time(s.CheckOutTime), s.MinNights, s.MaxNights, s.HorizonDays, s.AllowGapFill,
        s.AllowSameDayCheckIn, s.HoldMinutes, s.PrepayPercent, s.CancellationPolicy, s.DogFeeRub, s.CotFeeRub, StayFormat.Time(s.CheckInInfoSendTime),
        s.CheckInInfoText, s.CheckInInfoSendFullText, s.ArrivalReminderEnabled, s.HousekeeperSeesGuestComment, company.ShowInPublicListing);

    public async Task<StaysCompanyManageDto> BuildManageAsync(Company company, StaysMyRole role, CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(company.Id, ct: ct);
        var permissions = StaysAccess.For(role).ToList();
        var canManage = permissions.Contains(StaysPermission.ManageCompany);
        var city = company.CityId is { } cid ? await db.Cities.AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        var now = clock.UtcNow;
        var plan = await plans.GetForCompanyAsync(company, now, ct);
        var published = await plans.CountPublishedHousesForCompanyAsync(company, ct);
        var gate = StaysBookingGate.Evaluate(company.IsActive, plan.HasActivePlan, published, plan.MaxHouses, settings.PrepayPercent, settings.PaymentDetails, ProviderFacts(settings));
        var (level, text) = StaysPlanResolver.Warning(plan, published, now);
        var anyPublished = await db.Houses.AsNoTracking().AnyAsync(h => h.CompanyId == company.Id && h.IsPublished && h.ArchivedAtUtc == null, ct);
        int? awaiting = role == StaysMyRole.Housekeeper ? null
            : await db.StayBookings.AsNoTracking().CountAsync(b => b.CompanyId == company.Id && b.Status == StayBookingStatus.AwaitingPaymentCheck, ct);

        var checklist = new List<ChecklistItemDto>
        {
            new("ProfileFilled", !string.IsNullOrWhiteSpace(company.Name) && !string.IsNullOrWhiteSpace(company.Phone), "Заполните название и телефон для гостей"),
            new("PaymentDetails", settings.PrepayPercent == 0 || !string.IsNullOrWhiteSpace(settings.PaymentDetails), "Заполните реквизиты для оплаты — без них гости не могут бронировать с предоплатой"),
            new("ProviderInfo", settings.PrepayPercent == 0 || StaysBookingGate.ProviderComplete(ProviderFacts(settings)), "Заполните сведения об исполнителе"),
            new("HousePublished", anyPublished, "Опубликуйте хотя бы один дом"),
            new("Plan", plan.HasActivePlan, "Выберите тариф или активируйте пробный период"),
        };

        return new StaysCompanyManageDto(
            company.Id, company.Name, company.Slug, company.Description, company.Phone, company.Email, company.LogoUrl, company.Address,
            company.YandexMapsUrl, company.TwoGisUrl, city ?? string.Empty, company.TimeZoneId, company.IsActive, company.ShowInPublicListing,
            links.CompanyPageUrl(company), role, permissions,
            role == StaysMyRole.Housekeeper ? null : ToDto(settings, company),
            canManage ? new PaymentDetailsDto(settings.PaymentDetails, settings.PaymentPurpose) : null,
            canManage ? ProviderFull(settings) : null,
            new GateDto(gate.Accepting, gate.ReasonCode?.ToString(), gate.ReasonText), checklist,
            new StaysPlanSummaryDto(plan.PlanName, plan.IsTrial, plan.PaidUntilUtc, plan.MaxHouses, published, level, text), awaiting);
    }
}
