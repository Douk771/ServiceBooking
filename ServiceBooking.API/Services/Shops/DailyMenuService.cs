using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §452.3 — reads of a shop's daily menus. The storefront, <c>quote</c>, order creation and the cabinet all get the
/// menu of a date through here (one selection: the menu row of the date + its items, ≤ 1000 rows), so "what is sold on a date" cannot be
/// computed two ways.
/// </summary>
public class DailyMenuService(AppDbContext db)
{
    /// <summary>The menu of one date as <see cref="CatalogAvailability"/> reads it; <see cref="DailyMenuLookup.None"/> when the date has no menu.</summary>
    public async Task<DailyMenuLookup> LookupAsync(Guid companyId, DateOnly date, CancellationToken ct = default)
    {
        // A menu with no items is still a menu ("nothing is sold that day"): one query tells it apart from "no menu".
        var menu = await db.ShopDailyMenus.AsNoTracking().Include(m => m.Items)
            .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.Date == date, ct);
        return menu is null ? DailyMenuLookup.None : DailyMenuLookup.ForMenu(menu.Items.Select(i => i.ProductId));
    }
}
