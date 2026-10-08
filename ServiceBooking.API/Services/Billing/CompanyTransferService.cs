using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>Why a transfer/preview was refused — stage 5's controller maps these to HTTP status codes
/// (§51: 404/409/402/409 respectively).</summary>
public enum TransferFailureKind
{
    CompanyNotFound,
    TargetAccountNotFound,
    SameAccount,
    NewOwnerNotFound,
    NewOwnerDeleted,
    NewOwnerNotLinkedToTargetAccount,
    CompanyLimitExceeded,
    SeatOverflowConfirmationRequired,
    // ARCHITECTURE_CYCLE20.md §407.2 (US-20-07, Т20-09, LG6) — a transfer WITHOUT an owner change,
    // where the company's CURRENT owner has no link to the receiving account. Distinct from
    // NewOwnerNotLinkedToTargetAccount above (that one is about a newOwnerUserId the caller supplied;
    // this one fires when no new owner was supplied at all, and the existing owner would otherwise stay
    // in place, unlinked, "closing LG6" — no path may leave a company owned by someone unconnected to
    // the account paying for it).
    CurrentOwnerNotLinkedToTargetAccount,
    // ARCHITECTURE_CYCLE20.md §407.2, API_CONTRACT_CYCLE20.md §437.2 (Т20-09 п. 1) — ConfirmRightsTransfer
    // was not true. Checked FIRST, before any calculation (§437.2's own ordering).
    RightsTransferNotConfirmed,
    // ARCHITECTURE_CYCLE28.md §574.2 — showcase (fictional) and real accounts may not be mixed by a transfer.
    ShowcaseMixing,
}

public sealed record TransferFailure(TransferFailureKind Kind, string Message);

/// <summary>§51.2's numbers, computed for both the preview endpoint and (internally) the transfer
/// endpoint's own pre-write checks — see §51.2/§60 A8 for why <c>newOwnerUserId</c> must be supplied to
/// get a truthful <see cref="SeatsAfter"/>.</summary>
public sealed record TransferPreview(
    int CompaniesUsedOnTarget, int? CompanyLimit, bool CompanyLimitExceeded,
    int SeatsAfter, int? SeatLimit, bool SeatOverflow, bool OwnerWillChange);

public sealed record TransferPreviewResult(bool Success, TransferFailure? Failure, TransferPreview? Preview)
{
    public static TransferPreviewResult Fail(TransferFailureKind kind, string message) => new(false, new TransferFailure(kind, message), null);
    public static TransferPreviewResult Ok(TransferPreview preview) => new(true, null, preview);
}

public sealed record TransferResult(bool Success, TransferFailure? Failure)
{
    public static TransferResult Fail(TransferFailureKind kind, string message) => new(false, new TransferFailure(kind, message));
    public static readonly TransferResult Succeeded = new(true, null);
}

/// <summary>
/// Cycle 7 (ARCHITECTURE_CYCLE7.md §51) — moves a company to another billing account, optionally
/// changing its responsible owner in the same transaction (§51.0's two scenarios). Reuses
/// <see cref="CompanyOwnerWriter"/> for the owner-change branch so it can never drift from
/// <c>PUT /api/admin/companies/{id}/owner</c> (§50).
/// </summary>
public class CompanyTransferService(
    AppDbContext db, UserManager<AppUser> userManager, SubscriptionResolver subscriptionResolver,
    AccountUsageReader usageReader, CompanyOwnerWriter companyOwnerWriter, OrdersPlanResolver ordersPlans, Stays.StaysPlanResolver staysPlans, ILogger<CompanyTransferService> logger)
{
    /// <summary>
    /// §51.1's linkage rule, evaluated against the database, wrapping the pure
    /// <see cref="CompanyTransferCalculator.IsNewOwnerLinkedToTargetAccount"/>. Returns the resolved
    /// user on success.
    /// </summary>
    public async Task<(AppUser? User, TransferFailure? Failure)> ValidateNewOwnerAsync(string newOwnerUserId, Guid targetBillingAccountId)
    {
        var newOwner = await userManager.FindByIdAsync(newOwnerUserId);
        if (newOwner is null)
            return (null, new TransferFailure(TransferFailureKind.NewOwnerNotFound, "Пользователь не найден"));
        if (newOwner.DeletedAtUtc is not null)
            return (null, new TransferFailure(TransferFailureKind.NewOwnerDeleted, "Учётная запись пользователя удалена"));

        var isHolder = await db.BillingAccounts.AnyAsync(a => a.Id == targetBillingAccountId && a.OwnerUserId == newOwnerUserId);
        var isMember = await db.CompanyMembers
            .Where(cm => cm.UserId == newOwnerUserId)
            .Join(db.Companies, cm => cm.CompanyId, c => c.Id, (cm, c) => c.BillingAccountId)
            .AnyAsync(accountId => accountId == targetBillingAccountId);

        if (!CompanyTransferCalculator.IsNewOwnerLinkedToTargetAccount(isHolder, isMember))
        {
            var displayName = newOwner.Email ?? newOwner.PhoneNumber ?? newOwner.Id;
            return (null, new TransferFailure(
                TransferFailureKind.NewOwnerNotLinkedToTargetAccount, BillingTexts.TransferRejectedUnlinkedOwner(displayName)));
        }

        return (newOwner, null);
    }

    /// <summary>ARCHITECTURE_CYCLE20.md §407.1/§407.2 (US-20-07, LG6) — the same linkage rule
    /// <see cref="ValidateNewOwnerAsync"/> applies to a REQUESTED new owner, applied instead to the
    /// company's EXISTING owner for a transfer that doesn't ask to change it. Deliberately does not
    /// re-check <c>DeletedAtUtc</c> — an already-tombstoned current owner is a pre-existing state this
    /// transfer isn't introducing, and is not this check's job to catch.</summary>
    private async Task<TransferFailure?> ValidateCurrentOwnerLinkedAsync(Company company, Guid targetBillingAccountId)
    {
        var currentOwnerId = company.OwnerUserId;
        var isHolder = await db.BillingAccounts.AnyAsync(a => a.Id == targetBillingAccountId && a.OwnerUserId == currentOwnerId);
        var isMember = await db.CompanyMembers
            .Where(cm => cm.UserId == currentOwnerId)
            .Join(db.Companies, cm => cm.CompanyId, c => c.Id, (cm, c) => c.BillingAccountId)
            .AnyAsync(accountId => accountId == targetBillingAccountId);

        if (CompanyTransferCalculator.IsNewOwnerLinkedToTargetAccount(isHolder, isMember))
            return null;

        return new TransferFailure(
            TransferFailureKind.CurrentOwnerNotLinkedToTargetAccount, BillingTexts.TransferRejectedCurrentOwnerUnlinked(company.Name));
    }

    /// <summary>
    /// Read-only preview (§51.2): what would happen to the receiving account's limits if this transfer
    /// went ahead. Does not take locks or write anything.
    /// </summary>
    public async Task<TransferPreviewResult> PreviewAsync(Guid companyId, Guid targetBillingAccountId, string? newOwnerUserId)
    {
        var company = await db.Companies.FindAsync(companyId);
        if (company is null)
            return TransferPreviewResult.Fail(TransferFailureKind.CompanyNotFound, "Компания не найдена");

        var targetAccount = await db.BillingAccounts.FindAsync(targetBillingAccountId);
        if (targetAccount is null)
            return TransferPreviewResult.Fail(TransferFailureKind.TargetAccountNotFound, "Принимающий аккаунт не найден");

        if (company.BillingAccountId == targetBillingAccountId)
            return TransferPreviewResult.Fail(TransferFailureKind.SameAccount, "Компания уже в этом аккаунте");

        if (ShowcaseMixingGuard.CheckTransfer(company.IsShowcase, targetAccount.IsShowcase) is { } mixing)
            return TransferPreviewResult.Fail(TransferFailureKind.ShowcaseMixing, mixing);

        var newOwnerAddsSeat = false;
        if (!string.IsNullOrEmpty(newOwnerUserId))
        {
            var (newOwner, failure) = await ValidateNewOwnerAsync(newOwnerUserId, targetBillingAccountId);
            if (failure is not null) return new TransferPreviewResult(false, failure, null);
            if (ShowcaseMixingGuard.CheckTransfer(company.IsShowcase, targetAccount.IsShowcase, newOwner!.IsShowcase) is { } ownerMixing)
                return TransferPreviewResult.Fail(TransferFailureKind.ShowcaseMixing, ownerMixing);
            var isAlreadyMember = await db.CompanyMembers.AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == newOwner!.Id);
            newOwnerAddsSeat = !isAlreadyMember;
        }
        else
        {
            // ARCHITECTURE_CYCLE20.md §407.2, API_CONTRACT_CYCLE20.md §437.1 (US-20-07, LG6) — no new
            // owner requested: the company's EXISTING owner must be linked to the receiving account, or
            // it would keep an owner unconnected to whoever is about to pay for it.
            var currentOwnerFailure = await ValidateCurrentOwnerLinkedAsync(company, targetBillingAccountId);
            if (currentOwnerFailure is not null) return new TransferPreviewResult(false, currentOwnerFailure, null);
        }

        var preview = await ComputePreviewAsync(companyId, targetBillingAccountId, newOwnerAddsSeat, ownerWillChange: !string.IsNullOrEmpty(newOwnerUserId), company.Kind);
        return TransferPreviewResult.Ok(preview);
    }

    /// <summary>
    /// The receiving account's numbers for the LINE the company belongs to (ARCHITECTURE_CYCLE24.md §459.3): a salon is counted against the "Записи" tariff and the
    /// salons of the account, a shop against the "Заказы" tariff and its shops. A mixed account moves neither into the other's limits.
    /// </summary>
    private async Task<TransferPreview> ComputePreviewAsync(
        Guid companyId, Guid targetBillingAccountId, bool newOwnerAddsSeat, bool ownerWillChange, CompanyKind kind = CompanyKind.Services)
    {
        var (maxCompanies, maxEmployees) = await LimitsAsync(targetBillingAccountId, kind);
        var usage = (await usageReader.GetAsync([targetBillingAccountId], kind)).GetValueOrDefault(targetBillingAccountId)
                    ?? new AccountUsage(targetBillingAccountId, 0, 0);
        var companySeats = (await usageReader.GetCompanySeatsAsync([companyId])).GetValueOrDefault(companyId);

        var companyLimitExceeded = CompanyTransferCalculator.IsCompanyLimitExceeded(usage.CompaniesUsed, maxCompanies);
        var seatsAfter = CompanyTransferCalculator.ComputeSeatsAfter(usage.SeatsUsed, companySeats, newOwnerAddsSeat);
        var seatOverflow = CompanyTransferCalculator.IsSeatOverflow(seatsAfter, maxEmployees);

        return new TransferPreview(
            usage.CompaniesUsed, maxCompanies, companyLimitExceeded,
            seatsAfter, maxEmployees, seatOverflow, ownerWillChange);
    }

    private async Task<(int? MaxCompanies, int? MaxEmployees)> LimitsAsync(Guid accountId, CompanyKind kind)
    {
        if (kind == CompanyKind.Orders)
        {
            var orders = await ordersPlans.GetForAccountAsync(accountId);
            return (orders.MaxShops, orders.MaxSeats);
        }
        // ARCHITECTURE_CYCLE37.md §37.3.2: a «Дома» company has no company/seat limit; its limit is the number of PUBLISHED houses (checked in TransferAsync).
        if (kind == CompanyKind.Stays) return (null, null);
        var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(accountId);
        return (plan.AccountMaxCompanies, plan.AccountMaxEmployees);
    }

    /// <summary>
    /// §51.3 — the transfer itself, one transaction, ordered exactly as the architecture spells out.
    /// </summary>
    public async Task<TransferResult> TransferAsync(
        Guid companyId, Guid targetBillingAccountId, string? newOwnerUserId, bool confirmSeatOverflow, string changedByUserId,
        bool confirmRightsTransfer = false)
    {
        // ARCHITECTURE_CYCLE20.md §407.2, API_CONTRACT_CYCLE20.md §437.2 (Т20-09 п. 1) — checked FIRST,
        // before the company/account even get looked up: the contract's own ordering ("Проверка
        // confirmRightsTransfer — первой, до расчётов").
        if (!confirmRightsTransfer)
            return TransferResult.Fail(
                TransferFailureKind.RightsTransferNotConfirmed,
                "Подтвердите, что права на компанию переходят к принимающему абоненту.");

        var company = await db.Companies.FindAsync(companyId);
        if (company is null)
            return TransferResult.Fail(TransferFailureKind.CompanyNotFound, "Компания не найдена");

        var targetAccount = await db.BillingAccounts.FindAsync(targetBillingAccountId);
        if (targetAccount is null)
            return TransferResult.Fail(TransferFailureKind.TargetAccountNotFound, "Принимающий аккаунт не найден");

        if (company.BillingAccountId == targetBillingAccountId)
            return TransferResult.Fail(TransferFailureKind.SameAccount, "Компания уже в этом аккаунте");

        // ARCHITECTURE_CYCLE28.md §574.2 — after existence checks, before any write.
        if (ShowcaseMixingGuard.CheckTransfer(company.IsShowcase, targetAccount.IsShowcase) is { } mixing)
            return TransferResult.Fail(TransferFailureKind.ShowcaseMixing, mixing);

        var sourceBillingAccountId = company.BillingAccountId;

        // §52: two account locks in ascending Guid order first (dedlock avoidance for two concurrent
        // transfers), then — only if this transfer also changes the owner — the company-members lock.
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (sourceBillingAccountId.HasValue)
        {
            var (first, second) = sourceBillingAccountId.Value.CompareTo(targetBillingAccountId) <= 0
                ? (sourceBillingAccountId.Value, targetBillingAccountId)
                : (targetBillingAccountId, sourceBillingAccountId.Value);
            await AdvisoryLock.AcquireAsync(db, $"billing-account:{first}");
            await AdvisoryLock.AcquireAsync(db, $"billing-account:{second}");
        }
        else
        {
            await AdvisoryLock.AcquireAsync(db, $"billing-account:{targetBillingAccountId}");
        }

        AppUser? newOwner = null;
        var newOwnerAddsSeat = false;
        if (!string.IsNullOrEmpty(newOwnerUserId))
        {
            await AdvisoryLock.AcquireAsync(db, $"company-members:{companyId}");

            var (validatedOwner, failure) = await ValidateNewOwnerAsync(newOwnerUserId, targetBillingAccountId);
            if (failure is not null) return new TransferResult(false, failure);
            newOwner = validatedOwner;
            if (ShowcaseMixingGuard.CheckTransfer(company.IsShowcase, targetAccount.IsShowcase, newOwner!.IsShowcase) is { } ownerMixing)
                return TransferResult.Fail(TransferFailureKind.ShowcaseMixing, ownerMixing);

            var isAlreadyMember = await db.CompanyMembers.AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == newOwner!.Id);
            newOwnerAddsSeat = !isAlreadyMember;
        }
        else
        {
            // ARCHITECTURE_CYCLE20.md §407.2 (US-20-07, LG6) — mirrors PreviewAsync's own check, applied
            // again here (not just trusted from a prior preview call) because the caller may never have
            // called preview, and because state can change between the two calls.
            var currentOwnerFailure = await ValidateCurrentOwnerLinkedAsync(company, targetBillingAccountId);
            if (currentOwnerFailure is not null) return new TransferResult(false, currentOwnerFailure);
        }

        var preview = await ComputePreviewAsync(companyId, targetBillingAccountId, newOwnerAddsSeat, newOwner is not null, company.Kind);

        if (company.Kind == CompanyKind.Stays)
        {
            // The published houses of the moved company plus those already on the receiving account must fit the tariff of the receiving account.
            var movingHouses = await db.Houses.CountAsync(h => h.CompanyId == companyId && h.IsPublished && h.ArchivedAtUtc == null);
            var plan = await staysPlans.GetForAccountAsync(targetBillingAccountId);
            if (movingHouses > 0 && plan.MaxHouses is { } maxHouses)
            {
                var onTarget = await staysPlans.CountPublishedHousesAsync(targetBillingAccountId);
                if (onTarget + movingHouses > maxHouses)
                    return TransferResult.Fail(TransferFailureKind.CompanyLimitExceeded,
                        $"На принимающем аккаунте тариф «{plan.PlanName}» позволяет опубликовать {maxHouses} {Stays.StaysTexts.Plural(maxHouses, "дом", "дома", "домов")}, " +
                        $"а после переноса их будет {onTarget + movingHouses}. Снимите дома с публикации или смените тариф.");
            }
            if (movingHouses > 0 && !plan.HasActivePlan)
                return TransferResult.Fail(TransferFailureKind.CompanyLimitExceeded, "На принимающем аккаунте не выбран тариф «Дома» — выберите тариф, чтобы принять опубликованные дома.");
        }

        if (preview.CompanyLimitExceeded)
        {
            var (maxCompanies, _) = await LimitsAsync(targetBillingAccountId, company.Kind);
            var planName = company.Kind == CompanyKind.Orders
                ? (await ordersPlans.GetForAccountAsync(targetBillingAccountId)).PlanName
                : await db.AccountSubscriptions
                    .Where(s => s.BillingAccountId == targetBillingAccountId)
                    .Select(s => s.PlanConfig != null ? s.PlanConfig.Name : null)
                    .FirstOrDefaultAsync() ?? "Бесплатный";
            return TransferResult.Fail(
                TransferFailureKind.CompanyLimitExceeded,
                BillingTexts.TransferRejectedCompanyLimit(planName, preview.CompaniesUsedOnTarget, maxCompanies ?? 0));
        }

        if (preview.SeatOverflow && !confirmSeatOverflow)
        {
            return TransferResult.Fail(
                TransferFailureKind.SeatOverflowConfirmationRequired,
                $"После переноса будет занято {preview.SeatsAfter} из {preview.SeatLimit} мест на принимающем аккаунте. " +
                "Подтвердите перенос ещё раз, чтобы продолжить.");
        }

        // Step 6: the number belongs to the SOURCE account — a company leaving that account can no
        // longer be served by it (§43.6, §47.4). Strictly before step 7 (§51.3): the composite FK
        // (ChannelCompanyAssignment → Companies(Id, BillingAccountId)) now makes this order mandatory
        // at the DB level, not just by convention — if the assignment's delete and the company's
        // BillingAccountId update were sent in the same SaveChangesAsync batch, EF Core's statement
        // ordering for unrelated entities is not guaranteed to run the DELETE before the UPDATE, and
        // the composite FK would reject the update while the (now stale) assignment row still points
        // at the company's old BillingAccountId. A dedicated SaveChangesAsync here forces the DELETE
        // to commit (within the same transaction) strictly before the UPDATE is even sent.
        var assignment = await db.ChannelCompanyAssignments.FirstOrDefaultAsync(a => a.CompanyId == companyId);
        if (assignment is not null)
        {
            db.ChannelCompanyAssignments.Remove(assignment);
        }

        // TD-10 (ARCHITECTURE_CYCLE16.md §253): drop the company's undelivered notification queue on
        // every transfer, not only when it happened to have a channel assignment. Before this fix, a
        // company transferred while its ChannelCompanyAssignment row was absent (e.g. never assigned,
        // or already removed by something else) kept its Pending OutboundNotifications sitting in the
        // queue for the number that now belongs to a different company/account — the exact leak TD-10
        // names. Already-sent rows and the log itself are untouched; existing Cancelled reason reused.
        var pending = await db.OutboundNotifications
            .Where(n => n.CompanyId == companyId && n.Status == NotificationStatus.Pending)
            .ToListAsync();
        foreach (var row in pending)
        {
            row.Status = NotificationStatus.Cancelled;
            row.Reason = NotificationReason.BookingOrAssignmentCancelled;
        }
        if (assignment is not null || pending.Count > 0)
        {
            await db.SaveChangesAsync();
        }

        // Step 7: the payer changes. Pre-existing bug found and fixed while working this area (not one
        // of the assigned findings, but it made every transfer 500): Company.BillingAccountId is part
        // of the (Id, BillingAccountId) alternate key AppDbContext defines, and EF Core's change
        // tracker unconditionally refuses to mark a key-participating property Modified — via a plain
        // assignment OR via EntityEntry.ReloadAsync, which internally goes through the exact same
        // guarded code path ("The property 'Company.BillingAccountId' is part of a key and so cannot be
        // modified"). ExecuteUpdateAsync writes the column directly, bypassing the tracker entirely;
        // detaching the stale tracked entry and re-querying gives a FRESH tracked entity with the new
        // value already "clean" (no modified key property for a later SaveChangesAsync to trip over).
        await db.Companies.Where(c => c.Id == companyId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.BillingAccountId, targetBillingAccountId));
        db.Entry(company).State = EntityState.Detached;
        company = await db.Companies.FirstAsync(c => c.Id == companyId);

        // Step 8: owner change (if requested), through the SAME writer §50 uses — §59 grep 8.
        string? oldOwnerUserId = null;
        if (newOwner is not null)
        {
            oldOwnerUserId = await companyOwnerWriter.ChangeOwnerAsync(
                company, newOwner.Id, changedByUserId, withTransfer: true,
                // ARCHITECTURE_CYCLE20.md §407.2 (Т20-09 п. 1) — the confirmation fact recorded here too,
                // not only on the two SubscriptionChangeLog rows below.
                comment: "Перенос компании между биллинг-аккаунтами; подтверждено: права на компанию переходят к принимающему абоненту");
        }

        // Step 9: save, then IdentityRoleSync inside the same transaction.
        await db.SaveChangesAsync();
        if (newOwner is not null)
        {
            await IdentityRoleSync.SyncAsync(db, userManager, newOwner.Id);
            if (oldOwnerUserId != newOwner.Id)
                await IdentityRoleSync.SyncAsync(db, userManager, oldOwnerUserId!);
        }

        // Step 10: one SubscriptionChangeLog row per account touched (history of A, history of B).
        // N9, §49: the comment names people, not raw ids — "Мария Иванова (ООО «Ромашка») → аккаунт
        // Петра Сидорова", same sample shape §49 gives for other change kinds — and says whether the
        // owner changed, not just that a transfer happened.
        var now = DateTime.UtcNow;
        var ownerChanged = newOwner is not null;
        var targetOwnerName = await GetDisplayNameAsync(targetAccount.OwnerUserId);
        // §413 П-7's own SQL LIKE '%ответственный не менялся%' matches the un-changed branch — kept
        // verbatim; the confirmation note below is a separate, always-present trailing clause on BOTH
        // branches (ARCHITECTURE_CYCLE20.md §407.2, Т20-09 п. 1: "факт подтверждения пишется в обе
        // строки SubscriptionChangeLog").
        var ownershipSuffix = (ownerChanged ? $"; ответственный сменился на {targetOwnerName}" : "; ответственный не менялся")
            + "; подтверждено: права на компанию переходят к принимающему абоненту";

        // NB-8 (cycle-07 backend report): sourceOwnerName is computed once here and reused below for
        // the target-account log row's comment — the previous code re-issued FindAsync(sourceBillingAccountId)
        // a second time inside that string interpolation even though sourceAccount was already loaded
        // right above.
        string? sourceOwnerName = null;
        if (sourceBillingAccountId.HasValue)
        {
            var sourceAccount = await db.BillingAccounts.FindAsync(sourceBillingAccountId.Value);
            if (sourceAccount is not null)
            {
                sourceOwnerName = await GetDisplayNameAsync(sourceAccount.OwnerUserId);
                db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = sourceAccount.OwnerUserId,
                    BillingAccountId = sourceAccount.Id,
                    CompanyId = companyId,
                    ChangeKind = SubscriptionChangeKind.CompanyTransferred,
                    ChangedByUserId = changedByUserId,
                    ChangedAt = now,
                    Comment = $"Компания «{company.Name}» перенесена от {sourceOwnerName} к {targetOwnerName}{ownershipSuffix}",
                });
            }
        }

        db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
        {
            Id = Guid.NewGuid(),
            OwnerUserId = targetAccount.OwnerUserId,
            BillingAccountId = targetAccount.Id,
            CompanyId = companyId,
            ChangeKind = SubscriptionChangeKind.CompanyTransferred,
            ChangedByUserId = changedByUserId,
            ChangedAt = now,
            Comment = sourceBillingAccountId.HasValue
                ? $"Компания «{company.Name}» принята от {sourceOwnerName}{ownershipSuffix}"
                : $"Компания «{company.Name}» принята в аккаунт (у компании не было плательщика){ownershipSuffix}",
        });

        // Step 11: WithTransfer = true is already set inside CompanyOwnerWriter's log row above.
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        // §59: Information-level log for every company transfer — from, to, by whom, whether the owner
        // changed. Deliberately after the commit: this is an observability trace of a fact that already
        // happened, not part of the transactional outcome.
        logger.LogInformation(
            "Company {CompanyId} ({CompanyName}) transferred from account {SourceAccountId} to {TargetAccountId} by {ChangedByUserId}; owner changed: {OwnerChanged}",
            companyId, company.Name, sourceBillingAccountId, targetBillingAccountId, changedByUserId, ownerChanged);

        return TransferResult.Succeeded;
    }

    private async Task<string> GetDisplayNameAsync(string? userId)
    {
        if (userId is null) return "—";
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        return user is null ? "—" : $"{user.FirstName} {user.LastName}".Trim();
    }
}
