using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.PhoneVerification;
using ServiceBooking.API.Services.Signals;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

public enum TrialGrantMode { Normal, SuperAdminOverride }

/// <summary>
/// <paramref name="AcknowledgedTermsVersion"/> — Т1, the terms version the client just showed the
/// owner. Null on both admin paths (§335.3 step 1: the step is skipped there — nobody showed the owner
/// anything yet).
/// </summary>
public sealed record TrialGrantRequest(
    Guid BillingAccountId, string ActorUserId, TrialGrantSource Source,
    TrialGrantMode Mode, string? Reason, string? AcknowledgedTermsVersion);

public sealed record TrialGrantResult(bool Granted, string? RefusalCode, string? Message);

/// <summary>
/// Cycle 18 (ARCHITECTURE_CYCLE18.md §335) — the ONE place a trial is ever granted. Both the owner's
/// own button (<c>BillingController</c>) and a superadmin's action
/// (<c>AdminBillingController.GrantTrial</c>/<c>RegrantTrial</c>) call this service so the two paths
/// can never diverge on which checks apply (Д1).
///
/// Order of checks is part of the contract (§335.2): form first, then platform state, then the
/// account's own past, then someone else's. Reversing it would turn the response into an oracle
/// (§4.27 🔒/§6 конвенций).
/// </summary>
public class TrialActivationService(
    AppDbContext db, PlatformSettings platformSettings, IOptions<TrialOptions> trialOptions,
    IPhoneVerificationMethodRegistry phoneVerificationRegistry,
    IGlitchTipSignalService signals, ILogger<TrialActivationService> logger)
{
    public async Task<TrialGrantResult> GrantAsync(TrialGrantRequest request, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // §335.3 step 1 — Т1, before anything else, before opening a transaction. Skipped on admin
        // paths (AcknowledgedTermsVersion is null there — nobody was shown anything to sign off on).
        if (request.AcknowledgedTermsVersion is not null &&
            request.AcknowledgedTermsVersion != TrialTermsRegistry.CurrentVersion)
        {
            return Refuse("TrialTermsVersionMismatch", TrialLegalNotices.TrialTermsVersionMismatchNotice);
        }

        if (request.Mode == TrialGrantMode.SuperAdminOverride && string.IsNullOrWhiteSpace(request.Reason))
            return Refuse("TrialRegrantReasonRequired", "Причина аварийной повторной выдачи обязательна.");

        var plan = await db.SubscriptionPlanConfigs
            .FirstOrDefaultAsync(p => p.IsSystemTrial, ct);
        // §1 — TrialNotOffered: no trial row, inactive, off the public price list, or a corrupt
        // duration setting downstream (checked again below once we know the account is otherwise
        // eligible, to keep the "form → platform state → own past → someone else's" ordering honest).
        if (plan is null || !plan.IsActive || !plan.IsPublic)
            return Refuse("TrialNotOffered", TrialLegalNotices.TrialRefusedPlanUnavailable);

        var durationDays = await platformSettings.GetTrialDurationDaysAsync(ct);
        var windowDays = await platformSettings.GetTrialMailingWindowDaysAsync(ct);
        if (durationDays is null || windowDays is null || windowDays > durationDays)
            return Refuse("TrialNotOffered", TrialLegalNotices.TrialRefusedPlanUnavailable);
        var thresholds = await platformSettings.GetTrialWarningThresholdsDaysAsync(ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"billing-account:{request.BillingAccountId}");

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.Id == request.BillingAccountId, ct);
        if (account is null) return Refuse("TrialNotOffered", TrialLegalNotices.TrialRefusedPlanUnavailable);

        var sub = await db.AccountSubscriptions.Include(s => s.PlanConfig)
            .FirstOrDefaultAsync(s => s.BillingAccountId == account.Id, ct);
        var subUsable = sub is not null && sub.IsActive && (!sub.PaidUntil.HasValue || sub.PaidUntil >= now);

        // §2 — TrialAlreadyActive: a currently usable subscription already on the trial plan.
        if (subUsable && sub!.PlanConfigId == plan.Id)
            return Refuse("TrialAlreadyActive", TrialLegalNotices.TrialAlreadyActiveNotice);

        // §3 — AlreadyOnPaidPlan (Д9): a usable subscription on ANY plan with a positive price.
        if (subUsable && sub!.PlanConfig is { PricePerMonth: > 0 })
            return Refuse("AlreadyOnPaidPlan",
                string.Format(TrialLegalNotices.TrialRefusedActivePaidSubscription, sub.PaidUntil?.ToString("dd.MM.yyyy")));

        var canBypass = request.Mode == TrialGrantMode.SuperAdminOverride;

        // §4 — TrialAlreadyUsed: this account's own past. Not bypassable by form alone — the caller
        // must have explicitly asked for Mode.SuperAdminOverride with a reason.
        var alreadyUsed = account.TrialStartedAtUtc is not null ||
            await db.TrialGrants.AnyAsync(g => g.BillingAccountId == account.Id && g.Source != TrialGrantSource.SuperAdminOverride, ct);
        if (alreadyUsed && !canBypass)
            return Refuse("TrialAlreadyUsed",
                string.Format(TrialLegalNotices.TrialRefusedAlreadyUsedByAccount, account.TrialStartedAtUtc?.ToString("dd.MM.yyyy") ?? "ранее"));

        // §5/§6 — the owner's phone must be verified, UNLESS this is an emergency regrant.
        var verifiedPhone = await db.VerifiedPhones.AsNoTracking()
            .Where(v => v.UserId == account.OwnerUserId)
            .OrderByDescending(v => v.VerifiedAtUtc)
            .Select(v => v.Phone)
            .FirstOrDefaultAsync(ct);
        // R7/§9 — PhoneVerificationUnavailable follows the same shape as ChangePhoneSubsystemDisabled
        // (GuestBookingGateDecision, ProfileController): the gate only asks the subsystem's state when
        // verification is actually required. A verified phone already satisfies §5/§6 regardless of
        // whether the subsystem happens to be enabled right now — it exists to CONFIRM a number, not to
        // re-attest one that's already confirmed. Only when there's no verified phone AND the subsystem
        // is switched off is the honest "cannot verify right now" refusal correct; with the subsystem
        // enabled, an unverified owner still gets the ordinary PhoneNotVerified prompt.
        if (!canBypass && verifiedPhone is null)
        {
            var maxAdapter = phoneVerificationRegistry.Get(PhoneVerificationMethod.MaxBot);
            if (!maxAdapter.Enabled)
                return Refuse("PhoneVerificationUnavailable", TrialLegalNotices.TrialRefusedPhoneVerificationUnavailable);

            return Refuse("PhoneNotVerified", TrialLegalNotices.TrialRefusedPhoneNotVerified);
        }

        // §7 — fail-closed uniqueness check (Д6/К1). Never bypassable — bypassing it means granting
        // without the check, which Д6 forbids outright.
        var keyUsable = trialOptions.Value.UniquenessCheck.Enabled &&
            TrialPhoneKey.IsKeyUsable(trialOptions.Value.PhoneKeyHmac) &&
            !string.IsNullOrWhiteSpace(trialOptions.Value.PhoneKeyId);
        if (trialOptions.Value.UniquenessCheck.Enabled && !keyUsable)
            return Refuse("TrialUniquenessCheckUnavailable", TrialLegalNotices.TrialRefusedUniquenessCheckUnavailable);

        string? phoneKeyHash = null;
        if (trialOptions.Value.UniquenessCheck.Enabled && verifiedPhone is not null)
        {
            phoneKeyHash = TrialPhoneKey.Compute(trialOptions.Value.PhoneKeyHmac!, verifiedPhone);
            // §8 — TrialPhoneAlreadyUsed: someone else's past. Text carries no date/name/existence hint
            // (§4.27 🔒) regardless of what is actually found.
            var phoneUsed = await db.TrialPhoneRegistrations.AnyAsync(r => r.PhoneKeyHash == phoneKeyHash, ct);
            if (phoneUsed && !canBypass)
                return Refuse("TrialPhoneAlreadyUsed", TrialLegalNotices.TrialRefusedPhoneAlreadyUsed);
        }

        // ── Success path (§335.3) ──────────────────────────────────────────────────────────────────
        var endsAt = now.AddDays(durationDays.Value);
        var thresholdsSnapshot = TrialWindow.FormatThresholds(thresholds);

        if (sub is null)
        {
            sub = new AccountSubscription { Id = Guid.NewGuid(), OwnerUserId = account.OwnerUserId, BillingAccountId = account.Id };
            db.AccountSubscriptions.Add(sub);
        }
        var oldPlanConfigId = sub.PlanConfigId;
        var oldPaidUntil = sub.PaidUntil;
        var oldIsActive = sub.IsActive;
        sub.PlanConfigId = plan.Id;
        sub.IsActive = true;
        sub.PaidUntil = endsAt;
        sub.MailingUntilUtc = null; // §336.1 — starts only once a channel is authorized
        sub.UpdatedAt = now;

        var isOwnerPath = request.Mode == TrialGrantMode.Normal && request.Source == TrialGrantSource.OwnerSelfService;

        account.TrialStartedAtUtc = now;
        account.TrialEndsAtUtc = endsAt;
        account.TrialDurationDays = durationDays;
        account.TrialMailingWindowDays = windowDays;
        account.TrialWarningThresholdsDays = thresholdsSnapshot;
        account.TrialTermsVersion = TrialTermsRegistry.CurrentVersion;
        account.TrialTermsAcknowledgedAtUtc = isOwnerPath ? now : null;
        account.TrialGrantSource = request.Source;
        account.TrialGrantedByUserId = request.ActorUserId;
        account.TrialExpiredHandledAtUtc = null;
        account.TrialWarnedAtThresholdDays = null;
        account.TrialMailingClosureLoggedAtUtc = null;
        // Б3 (customer decision, cycle 18 3rd pass) — every GrantAsync call is a fresh grant of the
        // trial itself, not a "channel reconnected inside the same trial" event (that's Д5's territory,
        // enforced separately by TrialMailingWindowStarter's own guard). Reset the mailing-window
        // columns here so a re-grant (ordinary or SuperAdminOverride) always gets a clean window that
        // opens on the FIRST authorization of this new trial, exactly like a first-time grant. Without
        // this, a re-grant after an earlier trial's window already closed leaves
        // TrialChannelFirstAuthorizedAtUtc non-null forever (StartIfDueAsync's guard at
        // TrialMailingWindowStarter.cs:43 then never opens a window again) while the owner is shown the
        // terms text promising mailings that can now never start.
        account.TrialChannelFirstAuthorizedAtUtc = null;
        account.TrialMailingWindowEndsAtUtc = null;

        var termsHash = TrialTermsRegistry.Sha256Of(TrialTermsRegistry.CurrentVersion) ?? string.Empty;
        db.TrialGrants.Add(new TrialGrant
        {
            Id = Guid.NewGuid(),
            BillingAccountId = account.Id,
            PlanConfigId = plan.Id,
            GrantedAtUtc = now,
            EndsAtUtc = endsAt,
            DurationDays = durationDays.Value,
            MailingWindowDays = windowDays.Value,
            WarningThresholdsDays = thresholdsSnapshot,
            Source = request.Source,
            GrantedByUserId = request.ActorUserId,
            Reason = request.Mode == TrialGrantMode.SuperAdminOverride ? request.Reason : null,
            TermsVersion = TrialTermsRegistry.CurrentVersion,
            TermsTextSha256 = termsHash,
            TermsShownAtUtc = isOwnerPath ? now : null,
            TermsAcknowledgedAtUtc = isOwnerPath ? now : null,
        });

        if (phoneKeyHash is not null && !await db.TrialPhoneRegistrations.AnyAsync(r => r.PhoneKeyHash == phoneKeyHash, ct))
        {
            db.TrialPhoneRegistrations.Add(new TrialPhoneRegistration
            {
                Id = Guid.NewGuid(),
                PhoneKeyHash = phoneKeyHash,
                RegisteredAtUtc = now,
                KeyId = trialOptions.Value.PhoneKeyId!,
            });
        }

        var comment = request.Mode switch
        {
            TrialGrantMode.SuperAdminOverride => $"Пробный период ({durationDays} дн.), выдан повторно, причина: {request.Reason}",
            _ when request.Source == TrialGrantSource.SuperAdmin =>
                $"Пробный период ({durationDays} дн.), выдан суперадмином, условия версии {TrialTermsRegistry.CurrentVersion}, подтверждение владельцем ожидается",
            _ => $"Пробный период ({durationDays} дн.), выдан владельцем, условия версии {TrialTermsRegistry.CurrentVersion}",
        };
        db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
        {
            Id = Guid.NewGuid(),
            OwnerUserId = account.OwnerUserId,
            ChangedByUserId = request.ActorUserId,
            ChangedAt = now,
            OldPlanConfigId = oldPlanConfigId,
            NewPlanConfigId = plan.Id,
            OldPaidUntil = oldPaidUntil,
            NewPaidUntil = endsAt,
            OldIsActive = oldIsActive,
            NewIsActive = true,
            Comment = comment,
            BillingAccountId = account.Id,
            ChangeKind = SubscriptionChangeKind.TrialGranted,
        });

        // §333.3/§336.1 п.6 — closes R3 ("included on the plan" != "counted as paid" in
        // SubscriptionResolver, which only ever reads AccountSubscriptionOptions.Quantity). Materializes
        // (or revives, EndsAtUtc = null) the notifications.whatsapp row so a trial owner who connects a
        // channel actually has paid notification numbers, not just a channel the plan lets them connect.
        // PaidUntilUtc is left null here — until the mailing window actually starts (Д5, no channel
        // authorized yet) there is nothing to send with, and Н5 wires the eventual window end in here.
        var whatsappOption = await db.SubscriptionOptions
            .FirstOrDefaultAsync(o => o.Code == SubscriptionResolver.WhatsAppOptionCode, ct);
        if (whatsappOption is not null)
        {
            var rule = await db.PlanOptionRules
                .FirstOrDefaultAsync(r => r.PlanConfigId == plan.Id && r.OptionId == whatsappOption.Id, ct);
            // Extra or no rule at all → fail-closed, no row created (§333.3, §0.2 п.4): the trial plan's
            // matrix must say Included for this to mean anything.
            if (rule is { Availability: OptionAvailability.Included })
            {
                var existingOption = await db.AccountSubscriptionOptions
                    .FirstOrDefaultAsync(o => o.BillingAccountId == account.Id && o.OptionId == whatsappOption.Id, ct);
                if (existingOption is null)
                {
                    db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
                    {
                        Id = Guid.NewGuid(),
                        BillingAccountId = account.Id,
                        OptionId = whatsappOption.Id,
                        Quantity = rule.IncludedQuantity ?? 1,
                        PaidUntilUtc = account.TrialMailingWindowEndsAtUtc,
                        ActivatedAtUtc = now,
                        ActivatedByUserId = request.ActorUserId,
                        GrantedByTrial = true,
                    });
                }
                // B1 (code review, cycle 18 late delta) — a row that already exists for this option and
                // is NOT one the trial itself created (GrantedByTrial == false, e.g. an admin's paid
                // AssignSubscription grant whose own paid period has since lapsed) is left completely
                // untouched here. The (BillingAccountId, OptionId) unique index means there is no way to
                // hold both an admin grant and a trial grant as separate rows for the same option — and
                // overwriting the admin's row would (a) silently discard whatever quantity/date it
                // carried and (b) make it indistinguishable from a trial row, so TrialLifecycleTask's
                // expiry phase would later date out a grant the trial never created. Only a row this
                // service itself materialized before (GrantedByTrial == true, e.g. a second trial after
                // an earlier one already ran its course and dated this same row out) is revived.
                else if (existingOption.GrantedByTrial)
                {
                    existingOption.Quantity = rule.IncludedQuantity ?? 1;
                    existingOption.EndsAtUtc = null;
                    existingOption.PaidUntilUtc = account.TrialMailingWindowEndsAtUtc;
                    existingOption.ActivatedAtUtc = now;
                    existingOption.ActivatedByUserId = request.ActorUserId;
                }
            }
            else
            {
                // Н2 (code review, cycle 18 3rd pass) — no PlanOptionRule for notifications.whatsapp on
                // the trial plan, or an Extra one: fail-closed per §333.3/§0.2 п.4, no row created, so the
                // owner gets a "mailings included" terms text and a mailing-window countdown while every
                // mailing attempt silently hits NotOnPaidPlan in NotificationGate. That misconfiguration
                // must be visible to an operator, not just consistent with the contract on paper.
                logger.LogError(
                    "trial-lifecycle: trial plan {PlanId} has no Included PlanOptionRule for {OptionCode} " +
                    "— account {AccountId} granted a trial with no paid notification numbers materialized",
                    plan.Id, SubscriptionResolver.WhatsAppOptionCode, account.Id);
                await signals.SendAsync(
                    $"Пробный тариф {plan.Id} не даёт правило Included на {SubscriptionResolver.WhatsAppOptionCode} " +
                    $"— у аккаунта {account.Id} рассылки не будут работать несмотря на активный триал.", ct);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The race this index exists for (§334.1). Two distinct unique indexes can trip here, and
            // they mean different things to the caller: the account's OWN "one trial ever" index (a
            // concurrent activation/regrant of the SAME account — the loser gets the same honest
            // refusal a sequential second request would have) versus the cross-account phone-registry
            // index (this account lost a race to a DIFFERENT account that just claimed the same phone
            // number). The latter must answer with §8's phone-privacy refusal — no date, no hint that
            // another account exists — not with "you already used it".
            await transaction.RollbackAsync(ct);
            // Code-review finding (cycle 18 recheck) — RollbackAsync only undoes the DATABASE transaction;
            // the change tracker still holds the Added TrialGrant/SubscriptionChangeLog/AccountSubscription
            // and the Modified account from the failed attempt above. Nothing in THIS method's remaining
            // code writes again, but leaving them tracked is a live trap for any future write added to the
            // same request scope (it would silently commit the very grant that was just refused). Clearing
            // here costs nothing and removes the trap regardless of what callers do later.
            db.ChangeTracker.Clear();
            // Which index actually tripped: reload this account fresh (its own attempt is rolled back)
            // and ask whether IT now has a trial on record — if so, the account's own "one trial ever"
            // index is what raced (a concurrent activation/regrant of the SAME account), regardless of
            // the phone registry's state.
            var ownAccount = await db.BillingAccounts.AsNoTracking()
                .Where(a => a.Id == account.Id).Select(a => new { a.TrialStartedAtUtc }).FirstOrDefaultAsync(ct);
            if (ownAccount?.TrialStartedAtUtc is null && phoneKeyHash is not null &&
                await db.TrialPhoneRegistrations.AsNoTracking().AnyAsync(r => r.PhoneKeyHash == phoneKeyHash, ct))
                return Refuse("TrialPhoneAlreadyUsed", TrialLegalNotices.TrialRefusedPhoneAlreadyUsed);
            // Same honest refusal the sequential path (§4, line ~92) gives — quote the WINNER's real
            // TrialStartedAtUtc, not "now", so the date in the message is never fabricated.
            return Refuse("TrialAlreadyUsed",
                string.Format(TrialLegalNotices.TrialRefusedAlreadyUsedByAccount,
                    ownAccount?.TrialStartedAtUtc?.ToString("dd.MM.yyyy") ?? "ранее"));
        }

        return new TrialGrantResult(true, null, "Пробный период активирован.");
    }

    /// <summary>POST /api/billing/trial/terms-acknowledgement (§363.1) — set-once (§335.4): a second
    /// call after the first successful one is a no-op, not an error and not a re-stamp.</summary>
    public async Task<TrialGrantResult> AcknowledgeTermsAsync(string ownerUserId, string termsVersion, CancellationToken ct = default)
    {
        // §363.1: empty termsVersion is a 400 (malformed request), never the 409 JSON refusal shape —
        // that's reserved for "the version you showed isn't current".
        if (string.IsNullOrWhiteSpace(termsVersion))
            return new TrialGrantResult(false, "TrialTermsVersionRequired", "Не указана версия условий.");

        if (termsVersion != TrialTermsRegistry.CurrentVersion)
            return new TrialGrantResult(false, "TrialTermsVersionMismatch", TrialLegalNotices.TrialTermsVersionMismatchNotice);

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId, ct);
        // §363.1: 404 (empty body) — "у аккаунта нет ни одной выдачи триала, подтверждать нечего". No
        // billing account at all, or a billing account that has never had a trial granted, are both
        // that case — this must not silently stamp AcknowledgedAtUtc on an account with nothing to
        // acknowledge.
        if (account is null || account.TrialStartedAtUtc is null)
            return new TrialGrantResult(false, "TrialNotFound", null);

        var now = DateTime.UtcNow;
        if (account.TrialTermsAcknowledgedAtUtc is null)
        {
            account.TrialTermsAcknowledgedAtUtc = now;
            var grant = await db.TrialGrants
                .Where(g => g.BillingAccountId == account.Id)
                .OrderByDescending(g => g.GrantedAtUtc)
                .FirstOrDefaultAsync(ct);
            if (grant is not null)
            {
                grant.TermsShownAtUtc ??= now;
                grant.TermsAcknowledgedAtUtc ??= now;
            }
            await db.SaveChangesAsync(ct);
        }
        return new TrialGrantResult(true, null, "Подтверждение записано.");
    }

    private static TrialGrantResult Refuse(string code, string message) => new(false, code, message);
}
