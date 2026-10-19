using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE43.md §43.5 — the two messenger options (<c>notifications.whatsapp</c>, <c>notifications.max</c>) are outside the tariff:
/// nothing reads or writes their <see cref="PlanOptionRule"/> rows. One filter for every query that must leave them out.
/// </summary>
public static class MessengerOptionFilters
{
    public static IQueryable<SubscriptionOption> WhereNotMessenger(this IQueryable<SubscriptionOption> q) =>
        q.Where(o => o.Code != ChannelOptionCodes.WhatsApp && o.Code != ChannelOptionCodes.Max);

    public static IQueryable<PlanOptionRule> WhereNotMessenger(this IQueryable<PlanOptionRule> q) =>
        q.Where(r => r.Option.Code != ChannelOptionCodes.WhatsApp && r.Option.Code != ChannelOptionCodes.Max);

    public static IQueryable<AccountSubscriptionOption> WhereNotMessenger(this IQueryable<AccountSubscriptionOption> q) =>
        q.Where(o => o.Option.Code != ChannelOptionCodes.WhatsApp && o.Option.Code != ChannelOptionCodes.Max);
}
