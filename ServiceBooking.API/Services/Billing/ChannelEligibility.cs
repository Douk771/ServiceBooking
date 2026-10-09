using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §457.4 — may this ACCOUNT buy / keep a notification channel at all? The number belongs to the account, so the answer is
/// "the 'Записи' tariff allows it, OR the account has a shop and the 'Заказы' tariff allows it" (and, since cycle 37/42, a «Дома» / «Бани» company and that line's tariff). For an account without shops this is exactly the
/// cycle-23 question (<c>EffectivePlan.AllowNotificationChannel</c>); the shop line is consulted only when the first answer is "no".
/// </summary>
public class ChannelEligibility(AppDbContext db, OrdersPlanResolver ordersPlans, Stays.StaysPlanResolver slotPlans)
{
    public async Task<bool> IsAllowedAsync(Guid? accountId, EffectivePlan servicesPlan, CancellationToken ct = default)
    {
        if (servicesPlan.AllowNotificationChannel) return true;
        if (accountId is not { } id) return false;
        var hasShop = await db.Companies.AsNoTracking().AnyAsync(c => c.Kind == CompanyKind.Orders && c.BillingAccountId == id, ct);
        if (hasShop && (await ordersPlans.GetForAccountAsync(id, ct)).AllowNotificationChannel) return true;
        // ARCHITECTURE_CYCLE37.md §37.3.2, ARCHITECTURE_CYCLE42.md §42.5.6: or the account has a company of a slot line ("Дома", «Бани») and the tariff of
        // that line in force allows the channel. The plan is read only for a line the account actually has.
        foreach (var vertical in new[] { Slots.SlotVerticals.Stays, Slots.SlotVerticals.Baths })
        {
            var hasLine = await db.Companies.AsNoTracking().AnyAsync(c => c.Kind == vertical.Kind && c.BillingAccountId == id, ct);
            if (hasLine && (await slotPlans.GetForAccountAsync(vertical, id, ct: ct)) is { HasActivePlan: true, AllowNotificationChannel: true }) return true;
        }
        return false;
    }
}
