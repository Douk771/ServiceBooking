using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
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
        var canBypass = request.Mode == TrialGrantMode.SuperAdminOverride;

        // §4 input — this account's own past (an override grant doesn't count as "used").
        var alreadyUsed = account.TrialStartedAtUtc is not null ||
            await db.TrialGrants.AnyAsync(g => g.BillingAccountId == account.Id && g.Line == CompanyKind.Services && g.Source != TrialGrantSource.SuperAdminOverride, ct);
        // §5/§6 input — the owner's latest verified phone (also the uniqueness key's input below).
        var verifiedPhone = await db.VerifiedPhones.AsNoTracking()
            .Where(v => v.UserId == account.OwnerUserId)
            .OrderByDescending(v => v.VerifiedAtUtc)
            .Select(v => v.Phone)
            .FirstOrDefaultAsync(ct);

        // §2–§6 — one ordered set of conditions shared with TrialStateReader's dry run (cycle 22 D11,
        // closes C18-11). §1's platform-state checks already returned above, before the transaction.
        var subUsable = SubscriptionUsability.IsUsable(sub, now);
        var refusal = TrialEligibility.Evaluate(new TrialEligibilityFacts(
            Offered: true,
            SubscriptionUsable: subUsable,
            OnTrialPlan: subUsable && sub!.PlanConfigId == plan.Id,
            OnPaidPlan: sub?.PlanConfig is { PricePerMonth: > 0 },
            SubscriptionPaidUntil: sub?.PaidUntil,
            AlreadyUsed: alreadyUsed,
            TrialStartedAtUtc: account.TrialStartedAtUtc,
            HasVerifiedPhone: verifiedPhone is not null,
            PhoneVerificationEnabled: phoneVerificationRegistry.Get(PhoneVerificationMethod.MaxBot).Enabled,
            CanBypass: canBypass));
        if (refusal is not null) return Refuse(refusal.Code, refusal.Message);

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
            var phoneUsed = await db.TrialPhoneRegistrations.AnyAsync(r => r.Line == CompanyKind.Services && r.PhoneKeyHash == phoneKeyHash, ct);
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

        if (phoneKeyHash is not null && !await db.TrialPhoneRegistrations.AnyAsync(r => r.Line == CompanyKind.Services && r.PhoneKeyHash == phoneKeyHash, ct))
        {
            db.TrialPhoneRegistrations.Add(new TrialPhoneRegistration
            {
                Id = Guid.NewGuid(),
                Line = CompanyKind.Services,
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
            // ARCHITECTURE_CYCLE20.md §403.1 (US-20-02) — only the superadmin regrant path carries a
            // reason; the ordinary self-service/superadmin-initial-grant paths are not a manual
            // reassignment and leave both fields null.
            ReasonCode = request.Mode == TrialGrantMode.SuperAdminOverride ? SubscriptionChangeReason.TrialReissue : null,
            ReasonDetails = request.Mode == TrialGrantMode.SuperAdminOverride ? request.Reason : null,
        });

        // ARCHITECTURE_CYCLE40.md §40.3.5 (О6 with the Р40-Ю1 amendment) — the trial grants the OPEN messenger options and only them. For
        // every channel option (WhatsApp, MAX) that is open at this moment a row is materialized or revived (Quantity = 1, GrantedByTrial = true);
        // a closed option gets no row (it is not sold and not granted). The tariff's PlanOptionRule is NOT read any more: "included in
        // the plan" is not a source of payment (§40.3.1). Opening WhatsApp later does not top up a trial already running. The row's
        // PaidUntilUtc is left null here — until the mailing window actually starts (Д5, no channel authorized yet) the trial end
        // (ChannelOptionFunding) pays, and the window start dates the row (TrialMailingWindowStarter). Each change is journaled
        // (ChannelOptionLog, TrialGrant).
        // The actual GlitchTip network call is deferred until AFTER the transaction commits (see below); this only decides WHETHER one
        // is owed and with what text, so nothing here holds the advisory lock/DB transaction open waiting on an external service.
        string? misconfigurationSignal = null;

        foreach (var optionCode in ChannelOptionCodes.All)
        {
            var transport = AccountMessagingReader.TransportOf(optionCode)!.Value;
            if (TrialOptionGrantRule.Decide(await platformSettings.IsOptionOpenAsync(transport, ct), rowExists: false, rowGrantedByTrial: false)
                == TrialOptionGrantAction.SkipClosed)
            {
                logger.LogInformation(
                    "trial-lifecycle: option {OptionCode} is closed — account {AccountId} granted a trial without it",
                    optionCode, account.Id);
                continue;
            }

            var channelOption = await db.SubscriptionOptions.FirstOrDefaultAsync(o => o.Code == optionCode, ct);
            if (channelOption is null)
            {
                // The option CODE is not in the catalog at all although it is open — an invisible misconfiguration: log + signal.
                logger.LogError(
                    "trial-lifecycle: subscription option {OptionCode} does not exist in the catalog at all " +
                    "— account {AccountId} granted a trial without it",
                    optionCode, account.Id);
                misconfigurationSignal =
                    (misconfigurationSignal is null ? string.Empty : misconfigurationSignal + " ") +
                    $"Опция {optionCode} отсутствует в каталоге целиком — у аккаунта {account.Id} она не будет выдана в пробном периоде.";
                continue;
            }

            var existingOption = await db.AccountSubscriptionOptions
                .FirstOrDefaultAsync(o => o.BillingAccountId == account.Id && o.OptionId == channelOption.Id, ct);
            switch (TrialOptionGrantRule.Decide(optionOpen: true, existingOption is not null, existingOption?.GrantedByTrial == true))
            {
                case TrialOptionGrantAction.Create:
                    db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
                    {
                        Id = Guid.NewGuid(),
                        BillingAccountId = account.Id,
                        OptionId = channelOption.Id,
                        Quantity = 1,
                        PaidUntilUtc = account.TrialMailingWindowEndsAtUtc,
                        ActivatedAtUtc = now,
                        ActivatedByUserId = request.ActorUserId,
                        GrantedByTrial = true,
                    });
                    ChannelOptionLog.Write(db, account.Id, optionCode, ChannelOptionChangeSource.TrialGrant,
                        null, account.TrialMailingWindowEndsAtUtc, null, null, request.ActorUserId, now);
                    break;

                // B1 (code review, cycle 18 late delta) — a row that already exists for this option and is NOT one the trial itself
                // created (GrantedByTrial == false, e.g. an admin's paid grant whose own paid period has since lapsed) is left completely
                // untouched (LeaveAsIs). The (BillingAccountId, OptionId) unique index means there is no way to hold both an admin grant
                // and a trial grant as separate rows for the same option — overwriting the admin's row would silently discard its
                // quantity/date and make it indistinguishable from a trial row, so TrialLifecycleTask's expiry phase would later date
                // out a grant the trial never created. Only a row this service itself materialized before (Revive) is revived.
                case TrialOptionGrantAction.Revive:
                    var (oldOptionPaidUntil, oldOptionEndsAt) = (existingOption!.PaidUntilUtc, existingOption.EndsAtUtc);
                    existingOption.Quantity = 1;
                    existingOption.EndsAtUtc = null;
                    existingOption.PaidUntilUtc = account.TrialMailingWindowEndsAtUtc;
                    existingOption.ActivatedAtUtc = now;
                    existingOption.ActivatedByUserId = request.ActorUserId;
                    ChannelOptionLog.Write(db, account.Id, optionCode, ChannelOptionChangeSource.TrialGrant,
                        oldOptionPaidUntil, existingOption.PaidUntilUtc, oldOptionEndsAt, null, request.ActorUserId, now);
                    break;
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // Б... (code review, cycle 18 4th pass) — deliberately AFTER CommitAsync succeeds: this
            // service holds the per-account advisory lock (AcquireAsync above) for the lifetime of the
            // transaction, and GlitchTip's client has its own 10s timeout. Sending from inside the
            // transaction (as this used to) would keep that lock held for up to 10s whenever GlitchTip is
            // unreachable, AND could alert about a grant that then failed to commit at all. SendAsync
            // itself never throws (fixed 10s timeout, swallows its own errors) so it's safe to await
            // unconditionally once the grant is durably committed.
            if (misconfigurationSignal is not null)
                await signals.SendAsync(misconfigurationSignal, ct);
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
                await db.TrialPhoneRegistrations.AsNoTracking().AnyAsync(r => r.Line == CompanyKind.Services && r.PhoneKeyHash == phoneKeyHash, ct))
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

        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId, ct);

        // The edition shown to the owner is the one recorded in the account (GET /api/billing/trial), which for a trial granted before an edition change is
        // not the current one — that edition must be acknowledgeable too, or the owner could never confirm the terms.
        if (!TrialTermsRegistry.IsAcknowledgeable(termsVersion, account?.TrialTermsVersion))
            return new TrialGrantResult(false, "TrialTermsVersionMismatch", TrialLegalNotices.TrialTermsVersionMismatchNotice);

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
                .Where(g => g.BillingAccountId == account.Id && g.Line == CompanyKind.Services)
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
