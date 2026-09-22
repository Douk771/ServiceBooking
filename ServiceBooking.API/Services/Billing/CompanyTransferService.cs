using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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
/// Cycle 5 (ARCHITECTURE_CYCLE5.md §51) — moves a company to another billing account, optionally
/// changing its responsible owner in the same transaction (§51.0's two scenarios). Reuses
/// <see cref="CompanyOwnerWriter"/> for the owner-change branch so it can never drift from
/// <c>PUT /api/admin/companies/{id}/owner</c> (§50).
/// </summary>
public class CompanyTransferService(
    AppDbContext db, UserManager<AppUser> userManager, SubscriptionResolver subscriptionResolver,
    AccountUsageReader usageReader, CompanyOwnerWriter companyOwnerWriter)
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

        var newOwnerAddsSeat = false;
        if (!string.IsNullOrEmpty(newOwnerUserId))
        {
            var (newOwner, failure) = await ValidateNewOwnerAsync(newOwnerUserId, targetBillingAccountId);
            if (failure is not null) return new TransferPreviewResult(false, failure, null);
            var isAlreadyMember = await db.CompanyMembers.AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == newOwner!.Id);
            newOwnerAddsSeat = !isAlreadyMember;
        }

        var preview = await ComputePreviewAsync(companyId, targetBillingAccountId, newOwnerAddsSeat, ownerWillChange: !string.IsNullOrEmpty(newOwnerUserId));
        return TransferPreviewResult.Ok(preview);
    }

    private async Task<TransferPreview> ComputePreviewAsync(Guid companyId, Guid targetBillingAccountId, bool newOwnerAddsSeat, bool ownerWillChange)
    {
        var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(targetBillingAccountId);
        var usage = (await usageReader.GetAsync([targetBillingAccountId])).GetValueOrDefault(targetBillingAccountId)
                    ?? new AccountUsage(targetBillingAccountId, 0, 0);
        var companySeats = (await usageReader.GetCompanySeatsAsync([companyId])).GetValueOrDefault(companyId);

        var companyLimitExceeded = CompanyTransferCalculator.IsCompanyLimitExceeded(usage.CompaniesUsed, plan.AccountMaxCompanies);
        var seatsAfter = CompanyTransferCalculator.ComputeSeatsAfter(usage.SeatsUsed, companySeats, newOwnerAddsSeat);
        var seatOverflow = CompanyTransferCalculator.IsSeatOverflow(seatsAfter, plan.AccountMaxEmployees);

        return new TransferPreview(
            usage.CompaniesUsed, plan.AccountMaxCompanies, companyLimitExceeded,
            seatsAfter, plan.AccountMaxEmployees, seatOverflow, ownerWillChange);
    }

    /// <summary>
    /// §51.3 — the transfer itself, one transaction, ordered exactly as the architecture spells out.
    /// </summary>
    public async Task<TransferResult> TransferAsync(
        Guid companyId, Guid targetBillingAccountId, string? newOwnerUserId, bool confirmSeatOverflow, string changedByUserId)
    {
        var company = await db.Companies.FindAsync(companyId);
        if (company is null)
            return TransferResult.Fail(TransferFailureKind.CompanyNotFound, "Компания не найдена");

        var targetAccount = await db.BillingAccounts.FindAsync(targetBillingAccountId);
        if (targetAccount is null)
            return TransferResult.Fail(TransferFailureKind.TargetAccountNotFound, "Принимающий аккаунт не найден");

        if (company.BillingAccountId == targetBillingAccountId)
            return TransferResult.Fail(TransferFailureKind.SameAccount, "Компания уже в этом аккаунте");

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

            var isAlreadyMember = await db.CompanyMembers.AnyAsync(cm => cm.CompanyId == companyId && cm.UserId == newOwner!.Id);
            newOwnerAddsSeat = !isAlreadyMember;
        }

        var preview = await ComputePreviewAsync(companyId, targetBillingAccountId, newOwnerAddsSeat, newOwner is not null);

        if (preview.CompanyLimitExceeded)
        {
            var plan = await subscriptionResolver.GetEffectivePlanForAccountAsync(targetBillingAccountId);
            var planName = await db.AccountSubscriptions
                .Where(s => s.BillingAccountId == targetBillingAccountId)
                .Select(s => s.PlanConfig != null ? s.PlanConfig.Name : null)
                .FirstOrDefaultAsync() ?? "Бесплатный";
            return TransferResult.Fail(
                TransferFailureKind.CompanyLimitExceeded,
                BillingTexts.TransferRejectedCompanyLimit(planName, preview.CompaniesUsedOnTarget, plan.AccountMaxCompanies ?? 0));
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
        // (a later slice) will make this order mandatory at the DB level, but the order matters now
        // already, independent of that constraint.
        var assignment = await db.ChannelCompanyAssignments.FirstOrDefaultAsync(a => a.CompanyId == companyId);
        if (assignment is not null)
        {
            db.ChannelCompanyAssignments.Remove(assignment);
            var pending = await db.OutboundNotifications
                .Where(n => n.CompanyId == companyId && n.Status == NotificationStatus.Pending)
                .ToListAsync();
            foreach (var row in pending)
            {
                row.Status = NotificationStatus.Cancelled;
                row.Reason = NotificationReason.BookingOrAssignmentCancelled;
            }
        }

        // Step 7: the payer changes.
        company.BillingAccountId = targetBillingAccountId;

        // Step 8: owner change (if requested), through the SAME writer §50 uses — §59 grep 8.
        string? oldOwnerUserId = null;
        if (newOwner is not null)
        {
            oldOwnerUserId = await companyOwnerWriter.ChangeOwnerAsync(
                company, newOwner.Id, changedByUserId, withTransfer: true,
                comment: "Перенос компании между биллинг-аккаунтами");
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
        var now = DateTime.UtcNow;
        if (sourceBillingAccountId.HasValue)
        {
            var sourceAccount = await db.BillingAccounts.FindAsync(sourceBillingAccountId.Value);
            if (sourceAccount is not null)
            {
                db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = sourceAccount.OwnerUserId,
                    BillingAccountId = sourceAccount.Id,
                    CompanyId = companyId,
                    ChangeKind = SubscriptionChangeKind.CompanyTransferred,
                    ChangedByUserId = changedByUserId,
                    ChangedAt = now,
                    Comment = $"Компания перенесена в другой аккаунт ({targetBillingAccountId})",
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
                ? $"Компания принята из другого аккаунта ({sourceBillingAccountId})"
                : "Компания принята в аккаунт (у компании не было плательщика)",
        });

        // Step 11: WithTransfer = true is already set inside CompanyOwnerWriter's log row above.
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return TransferResult.Succeeded;
    }
}
