using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
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
    AppDbContext db, PlatformSettings platformSettings, IOptions<TrialOptions> trialOptions)
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
        // NOTE (backend report, cycle 18 recheck): R7's PhoneVerificationUnavailable is deliberately
        // NOT wired to IPhoneVerificationMethodAdapter.Enabled here. That flag is false under
        // PhoneVerification:Provider = "stub" — the documented, currently-shipped default in every
        // environment (§140.1's "невыпущенность") — and this codebase's own committed functional tests
        // (Cycle18TrialPlanTests.ActivateTrial_WithoutVerifiedPhone_Returns409PhoneNotVerified) already
        // require PhoneNotVerified, not PhoneVerificationUnavailable, for exactly that configuration:
        // VerifiedPhones rows are established directly (bypassing the real MAX flow) and "stub" is
        // treated as a normal operating mode, not an outage. Gating on Enabled would make
        // PhoneVerificationUnavailable fire in EVERY environment that hasn't turned MAX on yet — the
        // opposite failure from today's "unreachable", and one that contradicts an existing, passing
        // test. There is no OTHER existing signal in this codebase for "the phone-verification adapter
        // is unavailable" (a genuine runtime/outage condition, as opposed to the feature simply not
        // being turned on) — left as an explicit, named gap for architect/code-reviewer rather than
        // guessed at with a value that would just flip which of the two codes is unreachable.
        if (!canBypass && verifiedPhone is null)
            return Refuse("PhoneNotVerified", TrialLegalNotices.TrialRefusedPhoneNotVerified);

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
