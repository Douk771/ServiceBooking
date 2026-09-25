using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 18 (ARCHITECTURE_CYCLE18.md §341, API_CONTRACT_CYCLE18.md §362) — assembles
/// <see cref="TrialStateDto"/> for GET /api/billing/trial. The frontend does not compute availability
/// itself (US-18-12): every field here is server output.
///
/// Deliberate simplification against the full architecture (named explicitly in the backend report):
/// <c>mailingWindow</c> and <c>warning</c> are populated from what is already on the account today
/// (they degrade to "NotApplicable"/null) rather than from the channel-authorization hook (§336.1) or
/// the trial-lifecycle background task (§337), neither of which this slice implements yet.
/// </summary>
public class TrialStateReader(AppDbContext db, SubscriptionResolver subscriptionResolver, PlatformSettings platformSettings)
{
    public async Task<TrialStateDto?> GetAsync(string ownerUserId, CancellationToken ct = default)
    {
        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId, ct);
        if (account is null) return null;

        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .FirstOrDefaultAsync(s => s.BillingAccountId == account.Id, ct);
        var effectivePlan = await subscriptionResolver.GetEffectivePlanForAccountAsync(account.Id);
        return await BuildAsync(account, sub, effectivePlan, ct);
    }

    /// <summary>Code-review finding (cycle 18 recheck) — OwnerSubscriptionService.BuildAsync already
    /// loads the account's AccountSubscription and calls GetEffectivePlanForAccountAsync itself before
    /// asking this reader for the §365 "same TrialStateDto" card; re-loading/re-resolving them here on
    /// every GET /api/billing/subscription was ~5 avoidable round-trips on the owner's most-visited
    /// screen. Callers that already have both may pass them in; <see cref="GetAsync"/> above still loads
    /// them itself for its own direct GET /api/billing/trial call site.</summary>
    public async Task<TrialStateDto?> BuildAsync(
        BillingAccount account, AccountSubscription? sub, EffectivePlan effectivePlan, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var plan = await db.SubscriptionPlanConfigs.FirstOrDefaultAsync(p => p.IsSystemTrial, ct);
        var subUsable = sub is not null && sub.IsActive && (!sub.PaidUntil.HasValue || sub.PaidUntil >= now);
        var onTrialNow = subUsable && plan is not null && sub!.PlanConfigId == plan.Id;
        var everHadTrial = account.TrialStartedAtUtc is not null;

        var includes = OwnerSubscriptionService.BuildPlanIncludes(effectivePlan);
        var limits = new TrialLimitsDto(effectivePlan.AccountMaxCompanies, effectivePlan.AccountMaxEmployees, effectivePlan.PhotoQuotaMb);

        if (onTrialNow)
        {
            var daysLeft = (int)Math.Ceiling((sub!.PaidUntil!.Value - now).TotalDays);
            var termsDto = new TrialActivationTermsDto(
                account.TrialTermsVersion ?? TrialTermsRegistry.CurrentVersion,
                TrialTermsRegistry.Sha256Of(account.TrialTermsVersion ?? TrialTermsRegistry.CurrentVersion) ?? string.Empty,
                account.TrialTermsVersion == TrialTermsRegistry.CurrentVersion
                    ? TrialTermsRegistry.RenderCurrent(plan!.Name, account.TrialDurationDays ?? 0, sub.PaidUntil.Value, account.TrialMailingWindowDays ?? 0)
                    : TrialTermsRegistry.TryGetTemplate(account.TrialTermsVersion ?? string.Empty) ?? string.Empty,
                AcknowledgementRequired: account.TrialTermsAcknowledgedAtUtc is null,
                ShownAt: account.TrialTermsAcknowledgedAtUtc is null ? null : account.TrialStartedAtUtc,
                AcknowledgedAt: account.TrialTermsAcknowledgedAtUtc);

            return new TrialStateDto(
                State: "Active",
                PlanId: plan!.Id,
                PlanName: plan.Name,
                DurationDays: account.TrialDurationDays,
                MailingWindowDays: account.TrialMailingWindowDays,
                WarningThresholdsDays: TrialWindow.ParseThresholds(account.TrialWarningThresholdsDays),
                EndsAtPreview: sub.PaidUntil,
                RefusalCode: null,
                Message: termsDto.Text,
                ActivationTerms: termsDto,
                PlanChangeNotice: string.Format(TrialLegalNotices.TrialRemainderForfeitedOnPlanChange, sub.PaidUntil.Value.ToString("dd.MM.yyyy")),
                StartedAt: account.TrialStartedAtUtc,
                EndsAt: sub.PaidUntil,
                DaysLeft: Math.Max(daysLeft, 0),
                GrantSource: account.TrialGrantSource?.ToString(),
                MailingWindow: BuildMailingWindow(account, sub, now),
                Warning: null,
                Includes: includes,
                Limits: limits);
        }

        if (everHadTrial)
        {
            var visibleUntil = (account.TrialExpiredHandledAtUtc ?? account.TrialEndsAtUtc ?? now).AddDays(TrialWindow.ExpiredNoticeMinDays);
            return new TrialStateDto(
                State: "Expired",
                PlanId: plan?.Id,
                PlanName: plan?.Name,
                DurationDays: account.TrialDurationDays,
                MailingWindowDays: account.TrialMailingWindowDays,
                WarningThresholdsDays: TrialWindow.ParseThresholds(account.TrialWarningThresholdsDays),
                EndsAtPreview: account.TrialEndsAtUtc,
                RefusalCode: "TrialAlreadyUsed",
                Message: string.Format(TrialLegalNotices.TrialExpiredSwitchedToFree, account.TrialEndsAtUtc?.ToString("dd.MM.yyyy")),
                ActivationTerms: null,
                PlanChangeNotice: null,
                StartedAt: account.TrialStartedAtUtc,
                EndsAt: account.TrialEndsAtUtc,
                DaysLeft: 0,
                GrantSource: account.TrialGrantSource?.ToString(),
                MailingWindow: BuildExpiredMailingWindow(account),
                Warning: new TrialWarningDto(
                    "TrialExpired",
                    string.Format(TrialLegalNotices.TrialExpiredSwitchedToFree, account.TrialEndsAtUtc?.ToString("dd.MM.yyyy")),
                    includes, Dismissible: false, VisibleUntilUtc: visibleUntil),
                Includes: includes,
                Limits: limits);
        }

        // Available or Unavailable — dry-run the same order of checks GrantAsync uses, without writing
        // anything, so the reason shown here can never drift from the reason a real POST would give.
        var durationDays = await platformSettings.GetTrialDurationDaysAsync(ct);
        var windowDays = await platformSettings.GetTrialMailingWindowDaysAsync(ct);
        var thresholds = await platformSettings.GetTrialWarningThresholdsDaysAsync(ct);

        string? refusalCode = null;
        string message;
        if (plan is null || !plan.IsActive || !plan.IsPublic || durationDays is null || windowDays is null || windowDays > durationDays)
        {
            refusalCode = "TrialNotOffered";
            message = TrialLegalNotices.TrialRefusedPlanUnavailable;
        }
        else if (subUsable && sub!.PlanConfig is { PricePerMonth: > 0 })
        {
            refusalCode = "AlreadyOnPaidPlan";
            message = string.Format(TrialLegalNotices.TrialRefusedActivePaidSubscription, sub.PaidUntil?.ToString("dd.MM.yyyy"));
        }
        else
        {
            var verifiedPhone = await db.VerifiedPhones.AsNoTracking()
                .AnyAsync(v => v.UserId == account.OwnerUserId, ct);
            if (!verifiedPhone)
            {
                refusalCode = "PhoneNotVerified";
                message = TrialLegalNotices.TrialRefusedPhoneNotVerified;
            }
            else
            {
                message = plan is null ? string.Empty
                    : TrialTermsRegistry.RenderCurrent(plan.Name, durationDays!.Value, now.AddDays(durationDays.Value), windowDays!.Value);
            }
        }

        var available = refusalCode is null;
        var endsAtPreview = available ? now.AddDays(durationDays!.Value) : (DateTime?)null;
        TrialActivationTermsDto? availableTerms = null;
        TrialMailingWindowDto mailingWindow = NotApplicableMailingWindow();
        if (available)
        {
            var text = TrialTermsRegistry.RenderCurrent(plan!.Name, durationDays!.Value, endsAtPreview!.Value, windowDays!.Value);
            availableTerms = new TrialActivationTermsDto(
                TrialTermsRegistry.CurrentVersion, TrialTermsRegistry.Sha256Of(TrialTermsRegistry.CurrentVersion) ?? string.Empty,
                text, AcknowledgementRequired: false, ShownAt: null, AcknowledgedAt: null);
            message = text;
            // §362 example — even before activation, the preview already shows the mailing window is
            // NotStarted and explains it starts counting from first channel authorization, so the owner
            // isn't surprised that mailings run on a shorter clock than the trial itself.
            mailingWindow = new TrialMailingWindowDto("NotStarted", null, null, DaysLeft: null,
                Text: string.Format(TrialLegalNotices.TrialMailingWindowNotStarted, windowDays!.Value, endsAtPreview!.Value.ToString("dd.MM.yyyy")));
        }

        return new TrialStateDto(
            State: available ? "Available" : "Unavailable",
            PlanId: plan?.Id,
            PlanName: plan?.Name,
            DurationDays: available ? durationDays : null,
            MailingWindowDays: available ? windowDays : null,
            WarningThresholdsDays: available ? thresholds : null,
            EndsAtPreview: endsAtPreview,
            RefusalCode: refusalCode,
            Message: message,
            ActivationTerms: availableTerms,
            PlanChangeNotice: null,
            StartedAt: null,
            EndsAt: null,
            DaysLeft: null,
            GrantSource: null,
            MailingWindow: mailingWindow,
            Warning: null,
            Includes: includes,
            Limits: limits);
    }

    /// <summary>§362 — MailingWindow is in the schema's `required`/non-nullable set: every TrialStateDto
    /// response must carry an object, never null. "NotApplicable" is the value for states where a
    /// mailing window simply doesn't mean anything yet (no trial has ever run, or it already ended
    /// without ever being on the trial plan) — distinct from "NotStarted", which means a trial IS
    /// running/available and its window specifically hasn't begun.</summary>
    private static TrialMailingWindowDto NotApplicableMailingWindow() =>
        new("NotApplicable", null, null, null, string.Empty);

    /// <summary>Code-review finding (cycle 18 recheck) — a trial that already ended must not report
    /// "NotApplicable" (§362.1: reserved for "mailings were never part of this trial's option matrix")
    /// when the owner DID authorize a channel and the window simply closed; otherwise
    /// <c>TrialMailingWindowClosed</c> (API_CONTRACT_CYCLE18.md §363) is unreachable in the Expired
    /// state and the owner sees no explanation for mailings stopping (TrialCard.tsx hides an empty
    /// text). "NotApplicable" is reserved for trials where the window never started at all.</summary>
    private static TrialMailingWindowDto BuildExpiredMailingWindow(BillingAccount account)
    {
        if (account.TrialChannelFirstAuthorizedAtUtc is null) return NotApplicableMailingWindow();

        var end = account.TrialMailingWindowEndsAtUtc;
        return new TrialMailingWindowDto("Ended", account.TrialChannelFirstAuthorizedAtUtc, end, DaysLeft: 0,
            Text: string.Format(TrialLegalNotices.TrialMailingWindowClosed, end?.ToString("dd.MM.yyyy"), account.TrialEndsAtUtc?.ToString("dd.MM.yyyy")));
    }

    private static TrialMailingWindowDto BuildMailingWindow(BillingAccount account, AccountSubscription sub, DateTime now)
    {
        if (account.TrialChannelFirstAuthorizedAtUtc is null)
        {
            return new TrialMailingWindowDto("NotStarted", null, null,
                DaysLeft: null,
                Text: string.Format(TrialLegalNotices.TrialMailingWindowNotStarted, account.TrialMailingWindowDays, sub.PaidUntil?.ToString("dd.MM.yyyy")));
        }

        var end = account.TrialMailingWindowEndsAtUtc;
        if (end is null || end <= now)
        {
            return new TrialMailingWindowDto("Ended", account.TrialChannelFirstAuthorizedAtUtc, end, DaysLeft: 0,
                Text: string.Format(TrialLegalNotices.TrialMailingWindowClosed, end?.ToString("dd.MM.yyyy"), sub.PaidUntil?.ToString("dd.MM.yyyy")));
        }

        var daysLeft = (int)Math.Ceiling((end.Value - now).TotalDays);
        var state = daysLeft <= 3 ? "EndingSoon" : "Running";
        var text = daysLeft <= 3
            ? string.Format(TrialLegalNotices.TrialMailingWindowEndingSoon, end.Value.ToString("dd.MM.yyyy"), sub.PaidUntil?.ToString("dd.MM.yyyy"))
            : $"Рассылки клиентам работают до {end.Value:dd.MM.yyyy}.";
        return new TrialMailingWindowDto(state, account.TrialChannelFirstAuthorizedAtUtc, end, daysLeft, text);
    }
}
