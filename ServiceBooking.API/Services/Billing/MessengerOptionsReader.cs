using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.3 — the reader behind the price screen: the two messenger options with their sale state, the number of accounts
/// with a valid PAID period and the journal of price changes. The catalog is always read from the database (never from a cache) so that the
/// screen and the decisions of the write path agree.
/// </summary>
public sealed class MessengerOptionsReader(
    AppDbContext db, PlatformSettings platformSettings, LegalDocumentProvider legalDocuments, AccountMessagingReader messagingReader)
{
    public const int HistoryLimit = 50;

    public static readonly NotificationTransport[] Transports = [NotificationTransport.WhatsApp, NotificationTransport.Max];

    public async Task<AdminMessengerOptionsDto> BuildAsync(CancellationToken ct)
    {
        var items = new List<AdminMessengerOptionDto>(Transports.Length);
        foreach (var transport in Transports) items.Add(await BuildOneAsync(transport, ct));
        return new AdminMessengerOptionsDto(items, MessengerTexts.PlanMatrixNote, MessengerTexts.PriceHint);
    }

    public async Task<AdminMessengerOptionDto> BuildOneAsync(NotificationTransport transport, CancellationToken ct)
    {
        var code = AccountMessagingReader.OptionCode(transport);
        var option = await db.SubscriptionOptions.AsNoTracking().FirstOrDefaultAsync(o => o.Code == code, ct);
        var open = await platformSettings.IsOptionOpenAsync(transport, ct);
        var sale = ChannelOptionAvailability.Explain(
            open, option?.IsActive ?? false, option?.PricePerMonth, TermsOwnerPublished(code));
        var activePaid = option is null ? 0 : await CountActivePaidAccountsAsync(transport, option.Id, DateTime.UtcNow, ct);
        var history = option is null ? [] : await LoadHistoryAsync(option.Id, ct);

        return new AdminMessengerOptionDto(
            transport, MessengerTexts.DisplayName(transport), option?.Id, open, option?.IsActive ?? false, option?.PricePerMonth,
            MessengerTexts.PriceHistoryText(option?.PricePerMonth),
            MessengerSaleStateDto.From(sale), activePaid, history);
    }

    public bool TermsOwnerPublished(string optionCode) => LegalOptionGuards.IsPubliclySellable(optionCode, legalDocuments.Current);

    /// <summary>§43.3.4 — accounts whose option row is valid now, is not a trial and is PAID by <see cref="ChannelOptionFunding"/>.</summary>
    public async Task<int> CountActivePaidAccountsAsync(NotificationTransport transport, Guid optionId, DateTime nowUtc, CancellationToken ct)
    {
        var accountIds = await db.AccountSubscriptionOptions.AsNoTracking()
            .Where(o => o.OptionId == optionId && !o.GrantedByTrial && (o.EndsAtUtc == null || o.EndsAtUtc > nowUtc))
            .Select(o => o.BillingAccountId).Distinct().ToListAsync(ct);
        if (accountIds.Count == 0) return 0;

        var states = await messagingReader.LoadAsync(accountIds, nowUtc, ct);
        return states.Values.Count(s => s.For(transport).Payment is { Paid: true, IsTrial: false });
    }

    private async Task<IReadOnlyList<MessengerPriceChangeDto>> LoadHistoryAsync(Guid optionId, CancellationToken ct)
    {
        var rows = await db.SubscriptionOptionPriceChangeLogs.AsNoTracking()
            .Where(l => l.OptionId == optionId)
            .OrderByDescending(l => l.ChangedAtUtc).ThenByDescending(l => l.Id)
            .Take(HistoryLimit).ToListAsync(ct);
        var userIds = rows.Select(r => r.ChangedByUserId).Where(id => id != null).Distinct().ToList();
        var names = userIds.Count == 0
            ? new Dictionary<string, string>()
            : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => ($"{u.FirstName} {u.LastName}").Trim(), ct);

        return rows.Select(r => new MessengerPriceChangeDto(
            r.ChangedAtUtc, r.ChangedByUserId is { } id && names.TryGetValue(id, out var name) && name.Length > 0 ? name : null,
            r.OldPricePerMonth, r.NewPricePerMonth, MessengerTexts.PriceHistoryText(r.OldPricePerMonth), MessengerTexts.PriceHistoryText(r.NewPricePerMonth),
            r.OldIsActive, r.NewIsActive, r.Comment, (OptionPriceChangeSource)r.Source, r.ActivePaidAccounts)).ToList();
    }
}
