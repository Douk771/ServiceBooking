using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.API.Services.Baths;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.Slots;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>Terms of the "Дома" trial (Т1). [legal L10]: a DRAFT until a lawyer reads it — a separate edition from the salon trial's.</summary>
public static class StaysTrialTerms
{
    public const string Version = "stays-2026-10-08";

    public static string Text(int durationDays) =>
        $"Пробный период линейки «Дома» — {durationDays} дней с момента активации. В это время вы пользуетесь кабинетом и принимаете брони " +
        "без оплаты тарифа. Пробный период даётся один раз на один подтверждённый номер телефона и один аккаунт. По окончании гости не смогут " +
        "бронировать ваши дома, пока вы не выберете тариф; уже созданные брони и ваши данные сохраняются.";

    public static string Sha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Text(0)))).ToLowerInvariant();

    public const string NotOffered = "Пробный период сейчас не предлагается.";
    public const string AlreadyActive = "Пробный период уже активен.";
    public const string AlreadyOnPaidPlan = "Пробный период не предоставляется, пока действует платный тариф.";
    public const string AlreadyUsed = "Пробный период этого аккаунта уже предоставлялся и повторно не выдаётся.";
    public const string PhoneNotVerified = "Чтобы получить пробный период, подтвердите номер телефона в профиле.";
    public const string PhoneVerificationUnavailable = "Подтверждение номера телефона сейчас недоступно — пробный период предоставить нельзя. Попробуйте позже.";
    public const string PhoneAlreadyUsed = "На этот номер телефона пробный период уже предоставлялся: он даётся один раз на один подтверждённый номер.";
    public const string UniquenessUnavailable = "Пробный период сейчас предоставить нельзя: не работает проверка однократности. Попробуйте позже.";
    public const string VersionMismatch = "Условия пробного периода были обновлены — перечитайте и примите новую редакцию.";
}

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.10.2, ARCHITECTURE_CYCLE42.md §42.5.4 — the once-only trial of a slot line ("Дома" or «Бани»; same checks, order and uniqueness
/// registry as the salon trial, with Line = the vertical's kind). The methods without a vertical are the "Дома" wrappers of cycle 37. The trial plan is found
/// by the vertical's <see cref="SlotVertical.TrialPlanId"/> (the «Бани» trial has IsSystemTrial = false: the unique index is taken by the salon trial).
/// </summary>
public class StaysTrialService(
    AppDbContext db, StaysPlanResolver plans, IOptions<TrialOptions> trialOptions, IOptions<StaysOptions> options,
    IPhoneVerificationMethodRegistry phoneRegistry, IStaysClock clock, IOptions<BathsOptions> bathsOptions)
{
    private int TrialDays(SlotVertical v) => v.Kind == CompanyKind.Baths ? bathsOptions.Value.TrialDays : options.Value.TrialDays;

    private bool PhoneVerificationEnabled => phoneRegistry.Get(PhoneVerificationMethod.MaxBot).Enabled;

    public Task<StaysTrialStateDto> GetStateAsync(Guid accountId, string ownerUserId, CancellationToken ct = default) =>
        GetStateAsync(SlotVerticals.Stays, accountId, ownerUserId, ct);

    public Task<StaysTrialOutcomeDto> GrantAsync(Guid accountId, string ownerUserId, string? acknowledgedTermsVersion, CancellationToken ct = default) =>
        GrantAsync(SlotVerticals.Stays, accountId, ownerUserId, acknowledgedTermsVersion, ct);

    public async Task<StaysTrialStateDto> GetStateAsync(SlotVertical vertical, Guid accountId, string ownerUserId, CancellationToken ct = default)
    {
        var days = TrialDays(vertical);
        var plan = await db.SubscriptionPlanConfigs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == vertical.TrialPlanId, ct);
        var offered = plan is { IsActive: true };
        var current = await plans.GetForAccountAsync(vertical, accountId, clock.UtcNow, ct);
        var terms = (vertical.TrialTerms.Version, vertical.TrialTerms.Text(days));

        if (!offered) return new(false, false, "TrialNotOffered", StaysTrialTerms.NotOffered, terms.Version, terms.Item2, days, null);
        if (current.HasActivePlan && current.IsTrial) return new(true, false, "TrialAlreadyActive", StaysTrialTerms.AlreadyActive, terms.Version, terms.Item2, days, current.PaidUntilUtc);
        if (current.HasActivePlan) return new(true, false, "AlreadyOnPaidPlan", StaysTrialTerms.AlreadyOnPaidPlan, terms.Version, terms.Item2, days, null);
        if (await db.TrialGrants.AsNoTracking().AnyAsync(g => g.BillingAccountId == accountId && g.Line == vertical.Kind && g.Source != TrialGrantSource.SuperAdminOverride, ct))
            return new(true, false, "TrialAlreadyUsed", StaysTrialTerms.AlreadyUsed, terms.Version, terms.Item2, days, null);
        var phone = await VerifiedPhoneOfAsync(ownerUserId, ct);
        if (phone is null)
            return new(true, false, PhoneVerificationEnabled ? "PhoneNotVerified" : "PhoneVerificationUnavailable",
                PhoneVerificationEnabled ? StaysTrialTerms.PhoneNotVerified : StaysTrialTerms.PhoneVerificationUnavailable, terms.Version, terms.Item2, days, null);
        return new(true, true, null, null, terms.Version, terms.Item2, days, null);
    }

    public async Task<StaysTrialOutcomeDto> GrantAsync(SlotVertical vertical, Guid accountId, string ownerUserId, string? acknowledgedTermsVersion, CancellationToken ct = default)
    {
        if (acknowledgedTermsVersion != vertical.TrialTerms.Version) return Refuse("TrialTermsVersionMismatch", StaysTrialTerms.VersionMismatch);
        var now = clock.UtcNow;
        var days = TrialDays(vertical);

        var plan = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.Id == vertical.TrialPlanId, ct);
        if (plan is not { IsActive: true }) return Refuse("TrialNotOffered", StaysTrialTerms.NotOffered);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"billing-account:{accountId}");

        var isBaths = vertical.Kind == CompanyKind.Baths;
        var staysSub = isBaths ? null : await db.StaysSubscriptions.Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == accountId, ct);
        var bathsSub = isBaths ? await db.BathsSubscriptions.Include(s => s.PlanConfig).FirstOrDefaultAsync(s => s.BillingAccountId == accountId, ct) : null;
        var current = isBaths ? StaysPlanResolver.Resolve(bathsSub, now) : StaysPlanResolver.Resolve(staysSub, now);
        if (current.HasActivePlan && current.IsTrial) return Refuse("TrialAlreadyActive", StaysTrialTerms.AlreadyActive);
        if (current.HasActivePlan) return Refuse("AlreadyOnPaidPlan", StaysTrialTerms.AlreadyOnPaidPlan);
        if (await db.TrialGrants.AnyAsync(g => g.BillingAccountId == accountId && g.Line == vertical.Kind && g.Source != TrialGrantSource.SuperAdminOverride, ct))
            return Refuse("TrialAlreadyUsed", StaysTrialTerms.AlreadyUsed);

        var phone = await VerifiedPhoneOfAsync(ownerUserId, ct);
        if (phone is null)
            return PhoneVerificationEnabled ? Refuse("PhoneNotVerified", StaysTrialTerms.PhoneNotVerified) : Refuse("PhoneVerificationUnavailable", StaysTrialTerms.PhoneVerificationUnavailable);

        var o = trialOptions.Value;
        var keyUsable = o.UniquenessCheck.Enabled && TrialPhoneKey.IsKeyUsable(o.PhoneKeyHmac) && !string.IsNullOrWhiteSpace(o.PhoneKeyId);
        if (o.UniquenessCheck.Enabled && !keyUsable) return Refuse("TrialUniquenessCheckUnavailable", StaysTrialTerms.UniquenessUnavailable);
        string? keyHash = null;
        if (o.UniquenessCheck.Enabled)
        {
            keyHash = TrialPhoneKey.Compute(o.PhoneKeyHmac!, phone);
            if (await db.TrialPhoneRegistrations.AnyAsync(r => r.Line == vertical.Kind && r.PhoneKeyHash == keyHash, ct))
                return Refuse("TrialPhoneAlreadyUsed", StaysTrialTerms.PhoneAlreadyUsed);
        }

        var endsAt = now.AddDays(days);
        if (isBaths)
        {
            if (bathsSub is null)
            {
                bathsSub = new BathsSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId, CreatedAtUtc = now };
                db.BathsSubscriptions.Add(bathsSub);
            }
            bathsSub.PlanConfigId = plan.Id;
            bathsSub.IsActive = true;
            bathsSub.PaidUntil = endsAt;
            bathsSub.UpdatedAtUtc = now;
            bathsSub.UpdatedByUserId = ownerUserId;
        }
        else
        {
            if (staysSub is null)
            {
                staysSub = new StaysSubscription { Id = Guid.NewGuid(), BillingAccountId = accountId, CreatedAtUtc = now };
                db.StaysSubscriptions.Add(staysSub);
            }
            staysSub.PlanConfigId = plan.Id;
            staysSub.IsActive = true;
            staysSub.PaidUntil = endsAt;
            staysSub.UpdatedAtUtc = now;
            staysSub.UpdatedByUserId = ownerUserId;
        }
        db.TrialGrants.Add(new TrialGrant
        {
            Id = Guid.NewGuid(), BillingAccountId = accountId, Line = vertical.Kind, PlanConfigId = plan.Id, GrantedAtUtc = now, EndsAtUtc = endsAt,
            DurationDays = days, MailingWindowDays = 0, WarningThresholdsDays = string.Empty, Source = TrialGrantSource.OwnerSelfService,
            GrantedByUserId = ownerUserId, TermsVersion = vertical.TrialTerms.Version, TermsTextSha256 = vertical.TrialTerms.Sha256,
            TermsShownAtUtc = now, TermsAcknowledgedAtUtc = now,
        });
        if (keyHash is not null)
            db.TrialPhoneRegistrations.Add(new TrialPhoneRegistration
            {
                Id = Guid.NewGuid(), Line = vertical.Kind, PhoneKeyHash = keyHash, RegisteredAtUtc = now, KeyId = o.PhoneKeyId!
            });
        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return Refuse("TrialAlreadyUsed", StaysTrialTerms.AlreadyUsed);
        }
        return new StaysTrialOutcomeDto(true, null, "Пробный период активирован.", endsAt);
    }

    private Task<string?> VerifiedPhoneOfAsync(string userId, CancellationToken ct) =>
        db.VerifiedPhones.AsNoTracking().Where(v => v.UserId == userId).OrderByDescending(v => v.VerifiedAtUtc).Select(v => (string?)v.Phone).FirstOrDefaultAsync(ct);

    private static StaysTrialOutcomeDto Refuse(string code, string message) => new(false, code, message, null);
}
