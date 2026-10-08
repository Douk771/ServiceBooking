using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Notifications;

/// <summary>What the catalog and the platform settings say about one messenger option (§40.7.1).</summary>
public readonly record struct TransportOptionState(bool Open, bool Sellable, decimal? PricePerMonth);

/// <summary>
/// One transport of one account (§40.3.3): how it is paid, whether its option is open/sellable, its first live number
/// (<see cref="Primary"/>), the surplus numbers (<see cref="Duplicates"/>), whether messages are routed to it and whether it
/// works right now.
/// </summary>
public sealed record TransportState(
    NotificationTransport Transport, TransportPayment Payment, TransportOptionState Option,
    NotificationChannel? Primary, IReadOnlyList<NotificationChannel> Duplicates, bool Routable, bool Working)
{
    public bool Paid => Payment.Paid;
}

/// <summary>Everything the messaging rules need to know about one billing account.</summary>
public sealed record AccountMessagingState(
    Guid AccountId, bool PlatformEnabled, TransportState WhatsApp, TransportState Max, IReadOnlyList<NotificationChannel> Channels)
{
    public TransportState For(NotificationTransport transport) => transport switch
    {
        NotificationTransport.WhatsApp => WhatsApp,
        NotificationTransport.Max => Max,
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
    };

    public IEnumerable<TransportState> Transports => [WhatsApp, Max];

    public bool AnyPaid => WhatsApp.Paid || Max.Paid;

    /// <summary>How many transports are paid (0…2) — the meaning of the legacy <c>numbersPaid</c> field (§40.3.4).</summary>
    public int PaidTransportCount => (WhatsApp.Paid ? 1 : 0) + (Max.Paid ? 1 : 0);
    public bool AnyRoutable => WhatsApp.Routable || Max.Routable;

    /// <summary>Funding rank of one channel (§40.3.2); a channel that is not live (replaced) has none and counts as not paid.</summary>
    public ChannelFundingState FundingOf(NotificationChannel channel)
    {
        var transport = For(channel.Transport);
        if (transport.Primary?.Id == channel.Id)
            return transport.Paid ? ChannelFundingState.Funded : ChannelFundingState.NotPaid;
        return transport.Duplicates.Any(d => d.Id == channel.Id) ? ChannelFundingState.Unfunded : ChannelFundingState.NotPaid;
    }

    /// <summary>The paid first number of a transport (the one messages go through), else <see langword="null"/>.</summary>
    public NotificationChannel? FundedChannel(NotificationTransport transport) =>
        For(transport) is { Paid: true, Primary: { } primary } ? primary : null;
}

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.3.3 — the ONE reader of "what is paid, open and working" for an account's messengers. Exactly four
/// queries for any set of accounts (channels; the two option rows; subscriptions; trial dates) plus the 60 s caches of the platform
/// settings and of the two catalog rows. Payment is decided by <see cref="ChannelOptionFunding"/>, availability by
/// <see cref="ChannelOptionAvailability"/>, ranking by <see cref="TransportFunding"/> — nothing here reads a tariff flag.
/// </summary>
public sealed class AccountMessagingReader(
    AppDbContext db, PlatformSettings platformSettings, IMemoryCache cache, LegalDocumentProvider legalDocuments)
{
    private const string CatalogCacheKey = "account-messaging:option-catalog";
    private static readonly TimeSpan CatalogCacheDuration = TimeSpan.FromSeconds(60);

    private sealed record CatalogRow(bool IsActive, decimal? PricePerMonth);

    private static readonly NotificationTransport[] Transports = [NotificationTransport.WhatsApp, NotificationTransport.Max];

    public static string OptionCode(NotificationTransport transport) => transport switch
    {
        NotificationTransport.WhatsApp => ChannelOptionCodes.WhatsApp,
        NotificationTransport.Max => ChannelOptionCodes.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, null),
    };

    public static NotificationTransport? TransportOf(string optionCode) => optionCode switch
    {
        ChannelOptionCodes.WhatsApp => NotificationTransport.WhatsApp,
        ChannelOptionCodes.Max => NotificationTransport.Max,
        _ => null,
    };

    public async Task<IReadOnlyDictionary<Guid, AccountMessagingState>> LoadAsync(
        IEnumerable<Guid> accountIds, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var ids = accountIds.Distinct().ToList();
        var result = new Dictionary<Guid, AccountMessagingState>();
        if (ids.Count == 0) return result;
        var now = nowUtc ?? DateTime.UtcNow;

        var channels = (await db.NotificationChannels.AsNoTracking()
                .Where(c => c.BillingAccountId != null && ids.Contains(c.BillingAccountId.Value))
                .ToListAsync(ct))
            .ToLookup(c => c.BillingAccountId!.Value);

        var optionRows = (await db.AccountSubscriptionOptions.AsNoTracking()
                .Where(o => ids.Contains(o.BillingAccountId) &&
                            (o.Option.Code == ChannelOptionCodes.WhatsApp || o.Option.Code == ChannelOptionCodes.Max))
                .Select(o => new
                {
                    o.BillingAccountId, o.Option.Code, o.EndsAtUtc, o.PaidUntilUtc, o.GrantedByTrial, o.ActivatedAtUtc, o.Quantity,
                })
                .ToListAsync(ct))
            .ToDictionary(o => (o.BillingAccountId, o.Code));

        var subscriptions = (await db.AccountSubscriptions.AsNoTracking()
                .Where(s => s.BillingAccountId != null && ids.Contains(s.BillingAccountId.Value))
                .ToListAsync(ct))
            .GroupBy(s => s.BillingAccountId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var trialEnds = await db.BillingAccounts.AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.TrialEndsAtUtc })
            .ToDictionaryAsync(a => a.Id, a => a.TrialEndsAtUtc, ct);

        var platformEnabled = await platformSettings.IsCustomerMessagingEnabledAsync(ct);
        var catalog = await LoadCatalogAsync(ct);
        var snapshot = legalDocuments.Current;
        var options = new Dictionary<NotificationTransport, TransportOptionState>();
        foreach (var transport in Transports)
        {
            var code = OptionCode(transport);
            var row = catalog.GetValueOrDefault(code);
            var open = await platformSettings.IsOptionOpenAsync(transport, ct);
            var sellable = ChannelOptionAvailability.Sellable(
                open, row?.IsActive ?? false, row?.PricePerMonth, LegalOptionGuards.IsPubliclySellable(code, snapshot));
            options[transport] = new TransportOptionState(open, sellable, row?.PricePerMonth);
        }

        foreach (var accountId in ids)
        {
            var subscription = subscriptions.GetValueOrDefault(accountId);
            var legacy = new LegacySubscriptionFacts(SubscriptionUsability.IsUsable(subscription, now), subscription?.PaidUntil);
            var accountTrialEnd = trialEnds.GetValueOrDefault(accountId);
            var accountChannels = channels[accountId].ToList();

            TransportState Build(NotificationTransport transport)
            {
                OptionRowFacts? facts = optionRows.TryGetValue((accountId, OptionCode(transport)), out var r)
                    ? new OptionRowFacts(r.EndsAtUtc, r.PaidUntilUtc, r.GrantedByTrial, r.ActivatedAtUtc, r.Quantity)
                    : null;
                return BuildTransport(transport, facts, accountTrialEnd, legacy, now, accountChannels, options[transport], platformEnabled);
            }

            result[accountId] = new AccountMessagingState(accountId, platformEnabled, Build(NotificationTransport.WhatsApp),
                Build(NotificationTransport.Max), accountChannels);
        }

        return result;
    }

    public async Task<AccountMessagingState?> ForAccountAsync(Guid accountId, DateTime? nowUtc = null, CancellationToken ct = default) =>
        (await LoadAsync([accountId], nowUtc, ct)).GetValueOrDefault(accountId);

    /// <summary>The single way to learn the messengers of a company: those of the company's billing account (§40.4.1).
    /// A company without an account has no messengers — the state is empty (nothing paid, nothing routable).</summary>
    public async Task<AccountMessagingState> ForCompanyAsync(Guid companyId, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var accountId = await db.Companies.AsNoTracking().Where(c => c.Id == companyId)
            .Select(c => c.BillingAccountId).FirstOrDefaultAsync(ct);
        if (accountId is { } id && await ForAccountAsync(id, nowUtc, ct) is { } state) return state;
        return await EmptyAsync(accountId ?? Guid.Empty, ct);
    }

    private async Task<AccountMessagingState> EmptyAsync(Guid accountId, CancellationToken ct)
    {
        var platformEnabled = await platformSettings.IsCustomerMessagingEnabledAsync(ct);
        TransportState Empty(NotificationTransport t) =>
            new(t, TransportPayment.None, new TransportOptionState(false, false, null), null, [], false, false);
        return new AccountMessagingState(accountId, platformEnabled, Empty(NotificationTransport.WhatsApp), Empty(NotificationTransport.Max), []);
    }

    /// <summary>
    /// The pure core of the reader (no database): one transport of one account from its option-row facts and its channels.
    /// Payment — <see cref="ChannelOptionFunding"/>; rank — <see cref="TransportFunding"/>; routable — §40.5.1 (paid ∧ first live number ∧
    /// not suspended ∧ the number was bound at least once); working — routable ∧ connected ∧ platform on.
    /// </summary>
    public static TransportState BuildTransport(
        NotificationTransport transport, OptionRowFacts? optionRow, DateTime? accountTrialEndsAtUtc, LegacySubscriptionFacts subscription,
        DateTime nowUtc, IEnumerable<NotificationChannel> accountChannels, TransportOptionState option, bool platformEnabled)
    {
        var payment = ChannelOptionFunding.Evaluate(optionRow, accountTrialEndsAtUtc, subscription, nowUtc);

        var live = accountChannels.Where(c => c.Transport == transport && c.State != ChannelState.Replaced)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToList();
        var primary = live.FirstOrDefault();
        var duplicates = live.Skip(1).ToList();

        var ranking = TransportFunding.Rank(live.Select(c => new TransportChannelFacts(c.Id, c.State, c.CreatedAt)), payment.Paid);
        var routable = primary is not null &&
            new NotificationRouting.TransportCandidate(
                transport, payment.Paid, ranking.GetValueOrDefault(primary.Id) == ChannelFundingState.Funded,
                primary.IsSuspendedByAdmin, primary.State).IsRoutable;
        var working = routable && primary!.State == ChannelState.Connected && platformEnabled;
        return new TransportState(transport, payment, option, primary, duplicates, routable, working);
    }

    private async Task<Dictionary<string, CatalogRow>> LoadCatalogAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CatalogCacheKey, out Dictionary<string, CatalogRow>? cached) && cached is not null) return cached;
        var rows = await db.SubscriptionOptions.AsNoTracking()
            .Where(o => o.Code == ChannelOptionCodes.WhatsApp || o.Code == ChannelOptionCodes.Max)
            .Select(o => new { o.Code, o.IsActive, o.PricePerMonth })
            .ToListAsync(ct);
        var catalog = rows.ToDictionary(r => r.Code, r => new CatalogRow(r.IsActive, r.PricePerMonth));
        cache.Set(CatalogCacheKey, catalog, CatalogCacheDuration);
        return catalog;
    }

    /// <summary>Drops the cached catalog rows — called when an admin edits the options catalog.</summary>
    public static void InvalidateCatalogCache(IMemoryCache cache) => cache.Remove(CatalogCacheKey);
}
