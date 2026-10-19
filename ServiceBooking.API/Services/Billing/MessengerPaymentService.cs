using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>Outcome of a messenger money operation: either a refusal (HTTP status + the plain Russian text of the body) or the value.</summary>
public sealed record MessengerResult<T>(int? Status, string? Text, T? Value)
{
    public bool Ok => Status is null;
    public static MessengerResult<T> Fail(int status, string text) => new(status, text, default);
    public static MessengerResult<T> Success(T value) => new(null, null, value);
}

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.6 — the ONLY writer of the money of a messenger of an account: connect/extend (from the account card and from
/// «Подтвердить оплату»), the quote before it and the switch-off. Every write runs in a transaction under the lock <c>billing-account:{id}</c> — the same
/// lock the subscription form and the owner's requests take — and leaves one <see cref="ChannelOptionChangeLog"/> row and one
/// <see cref="SubscriptionChangeLog"/> row (C40-7). The decisions are the pure rules of <see cref="MessengerChargeRules"/> and <see cref="MessengerLineRule"/>.
/// </summary>
public sealed class MessengerPaymentService(
    AppDbContext db, PlatformSettings platformSettings, LegalDocumentProvider legalDocuments, AccountMessagingReader messagingReader,
    ILogger<MessengerPaymentService> logger)
{
    public async Task<MessengerResult<MessengerQuoteDto>> QuoteAsync(Guid accountId, NotificationTransport t, int months, CancellationToken ct)
    {
        if (months is < MessengerChargeRules.MinMonths or > MessengerChargeRules.MaxMonths) return MessengerResult<MessengerQuoteDto>.Fail(400, MessengerTexts.MonthsRange);

        var sold = await EnsureSellableAsync(t, ct);
        if (!sold.Ok) return MessengerResult<MessengerQuoteDto>.Fail(sold.Status!.Value, sold.Text!);
        var price = sold.Value!.PricePerMonth!.Value;

        var now = DateTime.UtcNow;
        var line = (await StateAsync(accountId, now, ct)).For(t).Line;
        var calc = MessengerChargeRules.Quote(t, line.Status, line.Payment.PaidUntil, months, price, now);
        return MessengerResult<MessengerQuoteDto>.Success(new MessengerQuoteDto(
            t, months, price, calc.Amount, calc.AmountText, line.Payment.PaidUntil, calc.NewPaidUntil, calc.NewPaidUntilText, calc.ActionText));
    }

    /// <summary>Connects or extends: the new end is <c>max(now, current end) + months</c>; a trial row is bought out; a scheduled switch-off is lifted.
    /// <paramref name="channelId"/> is set on the «Подтвердить оплату» path of a number card (its journals get the number).</summary>
    public async Task<MessengerResult<bool>> ExtendAsync(
        Guid accountId, NotificationTransport t, int months, string? comment, decimal? expectedPricePerMonth, string adminUserId, Guid? channelId,
        CancellationToken ct)
    {
        if (months is < MessengerChargeRules.MinMonths or > MessengerChargeRules.MaxMonths) return MessengerResult<bool>.Fail(400, MessengerTexts.MonthsRange);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"billing-account:{accountId}");

        var sold = await EnsureSellableAsync(t, ct);
        if (!sold.Ok) return MessengerResult<bool>.Fail(sold.Status!.Value, sold.Text!);
        var option = sold.Value!;
        var price = option.PricePerMonth!.Value;
        if (expectedPricePerMonth is { } expected && expected != price)
            return MessengerResult<bool>.Fail(409, MessengerTexts.PriceChangedConflict(t, price));

        var now = DateTime.UtcNow;
        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return MessengerResult<bool>.Fail(404, string.Empty);

        var before = (await StateAsync(accountId, now, ct)).For(t).Line;
        var newPaidUntil = MessengerChargeRules.NewPaidUntil(before.Payment.PaidUntil, months, now);
        var renew = MessengerChargeRules.IsRenewal(before.Status);
        var summaryBefore = MessengerTexts.OptionsSummary(t, before.Status, before.IsTrial, before.PaidUntil);

        var row = await db.AccountSubscriptionOptions.FirstOrDefaultAsync(o => o.BillingAccountId == accountId && o.OptionId == option.Id, ct);
        var (oldPaidUntil, oldEndsAt) = (row?.PaidUntilUtc, row?.EndsAtUtc);
        if (row is null)
        {
            db.AccountSubscriptionOptions.Add(new AccountSubscriptionOption
            {
                Id = Guid.NewGuid(), BillingAccountId = accountId, OptionId = option.Id, Quantity = 1, PaidUntilUtc = newPaidUntil,
                ActivatedAtUtc = now, ActivatedByUserId = adminUserId,
            });
        }
        else
        {
            row.Quantity = Math.Max(row.Quantity, 1);
            row.PaidUntilUtc = newPaidUntil;
            row.EndsAtUtc = null;
            row.ActivatedAtUtc = now;
            row.ActivatedByUserId = adminUserId;
            row.GrantedByTrial = false; // a bought-out row is an ordinary one (the invariant of AccountSubscriptionOption.GrantedByTrial)
            row.RequestedQuantity = null;
            row.RequestedAtUtc = null;
            row.RequestedByUserId = null;
        }

        var amount = months * price;
        ChannelOptionLog.Write(db, accountId, option.Code, channelId is null ? ChannelOptionChangeSource.AdminBillingAccount : ChannelOptionChangeSource.AdminChannelCard,
            oldPaidUntil, newPaidUntil, oldEndsAt, null, adminUserId, now, channelId, comment, price, months, amount);

        var sub = await db.AccountSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.BillingAccountId == accountId, ct);
        db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
        {
            Id = Guid.NewGuid(), OwnerUserId = account.OwnerUserId, BillingAccountId = accountId, ChangedByUserId = adminUserId, ChangedAt = now,
            OldPlanConfigId = sub?.PlanConfigId, NewPlanConfigId = sub?.PlanConfigId,
            OldPaidUntil = before.PaidUntil, NewPaidUntil = newPaidUntil,
            OldIsActive = sub?.IsActive ?? false, NewIsActive = sub?.IsActive ?? false,
            ChangeKind = SubscriptionChangeKind.Options,
            OldOptionsSummary = summaryBefore,
            NewOptionsSummary = MessengerTexts.OptionsSummary(t, MessengerLineStatus.Paid, false, newPaidUntil),
            Comment = MessengerTexts.ExtendHistoryComment(t, renew, months, price, comment),
            Line = CompanyKind.Services,
        });
        if (channelId is { } numberId)
        {
            db.ChannelPaymentLogs.Add(new ChannelPaymentLog
            {
                Id = Guid.NewGuid(), ChannelId = numberId, ChangedByUserId = adminUserId, OldPaidUntil = oldPaidUntil, NewPaidUntil = newPaidUntil, Amount = amount,
                Comment = string.IsNullOrWhiteSpace(comment) ? $"payment confirmed: {months} мес." : $"payment confirmed: {comment}", ChangedAtUtc = now,
            });
        }
        account.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Messenger {Transport} extended for account {AccountId} by {UserId}: {Months} × {Price}", t, accountId, adminUserId, months, price);
        return MessengerResult<bool>.Success(true);
    }

    /// <summary>Switches a paid (or trial) line off: now, or at the end of the paid period. Nothing is refunded.</summary>
    public async Task<MessengerResult<bool>> DisableAsync(
        Guid accountId, NotificationTransport t, MessengerDisableWhen when, string comment, string adminUserId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await AdvisoryLock.AcquireAsync(db, $"billing-account:{accountId}");

        var now = DateTime.UtcNow;
        var account = await db.BillingAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return MessengerResult<bool>.Fail(404, string.Empty);

        var transport = (await StateAsync(accountId, now, ct)).For(t);
        var line = transport.Line;
        var decision = MessengerChargeRules.Disable(
            t, line.Status, transport.Row is not null, line.Payment.PaidUntil, AdminMessengerLinesBuilder.IsTrialWithoutOwnDate(transport), line.EndsAt, when, now);
        if (decision.Error is { } error) return MessengerResult<bool>.Fail(409, error);
        var endsAt = decision.NewEndsAtUtc!.Value;

        var code = AccountMessagingReader.OptionCode(t);
        var row = await db.AccountSubscriptionOptions.Include(o => o.Option)
            .FirstAsync(o => o.BillingAccountId == accountId && o.Option.Code == code, ct);
        var oldEndsAt = row.EndsAtUtc;
        row.EndsAtUtc = endsAt;

        ChannelOptionLog.Write(db, accountId, code, ChannelOptionChangeSource.AdminOptionEnded,
            row.PaidUntilUtc, row.PaidUntilUtc, oldEndsAt, endsAt, adminUserId, now, comment: comment);

        var sub = await db.AccountSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.BillingAccountId == accountId, ct);
        db.SubscriptionChangeLogs.Add(new SubscriptionChangeLog
        {
            Id = Guid.NewGuid(), OwnerUserId = account.OwnerUserId, BillingAccountId = accountId, ChangedByUserId = adminUserId, ChangedAt = now,
            OldPlanConfigId = sub?.PlanConfigId, NewPlanConfigId = sub?.PlanConfigId,
            OldPaidUntil = line.PaidUntil, NewPaidUntil = line.PaidUntil,
            OldIsActive = sub?.IsActive ?? false, NewIsActive = sub?.IsActive ?? false,
            ChangeKind = SubscriptionChangeKind.Options,
            OldOptionsSummary = MessengerTexts.OptionsSummary(t, line.Status, line.IsTrial, line.PaidUntil),
            NewOptionsSummary = MessengerTexts.OptionsSummaryAfterDisable(t, when == MessengerDisableWhen.Now, endsAt),
            Comment = MessengerTexts.DisableHistoryComment(t, endsAt, comment),
            Line = CompanyKind.Services,
        });
        account.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation("Messenger {Transport} disabled for account {AccountId} by {UserId}: {When}", t, accountId, adminUserId, when);
        return MessengerResult<bool>.Success(true);
    }

    /// <summary>The catalog row of the option read from the database (never from a cache) and the check that it is for sale: 409 with the reason's text otherwise.</summary>
    private async Task<MessengerResult<SubscriptionOption>> EnsureSellableAsync(NotificationTransport t, CancellationToken ct)
    {
        var code = AccountMessagingReader.OptionCode(t);
        var option = await db.SubscriptionOptions.AsNoTracking().FirstOrDefaultAsync(o => o.Code == code, ct);
        if (option is null) return MessengerResult<SubscriptionOption>.Fail(409, MessengerTexts.CatalogMissing(t));

        var sale = ChannelOptionAvailability.Explain(
            await platformSettings.IsOptionOpenAsync(t, ct), option.IsActive, option.PricePerMonth,
            LegalOptionGuards.IsPubliclySellable(code, legalDocuments.Current));
        return sale.Reason is { } reason
            ? MessengerResult<SubscriptionOption>.Fail(409, MessengerTexts.NotSellableConflict(t, reason))
            : MessengerResult<SubscriptionOption>.Success(option);
    }

    private async Task<AccountMessagingState> StateAsync(Guid accountId, DateTime now, CancellationToken ct) =>
        await messagingReader.ForAccountAsync(accountId, now, ct) ?? throw new InvalidOperationException("Messaging state of an existing account is missing.");
}
