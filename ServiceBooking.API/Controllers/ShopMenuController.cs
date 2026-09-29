using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §452.3, API_CONTRACT_CYCLE24.md §482.3 (US-24-11) — the menu of a DATE. Where a menu exists, ONLY its products are sold that day;
/// where it does not, the weekday mask of each product decides. Owner and staff edit it (<see cref="ShopPermission.ManageAvailability"/>). The calendar
/// covers today … today + max(pre-order days, 7); past dates are not edited. A menu never changes orders that already exist. A date without a menu is
/// PRE-FILLED (Q-24-6) with the products the weekday rule allows, so the owner starts from what the customers would get anyway.
/// </summary>
[ApiController]
[Route("api/shops/{shopId:guid}")]
[Authorize]
public class ShopMenuController(
    AppDbContext db, ShopAccessResolver access, ShopGateLoader gates, IOptions<OrdersOptions> options) : ControllerBase
{
    public const string ProductNotFound = "Товар не найден";
    public const string ProductRepeats = "Товар в меню повторяется";
    public const string NoMenuOnDate = "На эту дату меню нет";
    public const int MaxProductsInMenu = 1000;

    [HttpGet("daily-menus")]
    public async Task<ActionResult<DailyMenuCalendarDto>> GetCalendar(Guid shopId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageAvailability, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var horizon = await HorizonAsync(result.Shop!, ct);
        var first = from ?? horizon.Today;
        var last = to ?? horizon.Last;
        if (!horizon.Contains(first) || !horizon.Contains(last) || last < first) return BadRequest(horizon.RangeText);

        var menus = await db.ShopDailyMenus.AsNoTracking().Where(m => m.CompanyId == shopId && m.Date >= first && m.Date <= last)
            .Select(m => new { m.Date, Count = m.Items.Count }).ToDictionaryAsync(m => m.Date, m => m.Count, ct);
        var masks = await db.Products.AsNoTracking().Where(p => p.CompanyId == shopId && p.DeletedAtUtc == null).Select(p => p.AvailableWeekdaysMask).ToListAsync(ct);

        var days = new List<DailyMenuDayDto>();
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var hasMenu = menus.TryGetValue(day, out var count);
            days.Add(new DailyMenuDayDto(day, ShopTimeTexts.DateLabel(day, horizon.Today), hasMenu,
                hasMenu ? count : masks.Count(m => WeekdayMask.Allows(m, day))));
        }
        return Ok(new DailyMenuCalendarDto(first, last, days));
    }

    [HttpGet("daily-menus/{date}")]
    public async Task<ActionResult<DailyMenuDto>> GetMenu(Guid shopId, DateOnly date, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageAvailability, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var horizon = await HorizonAsync(result.Shop!, ct);
        if (!horizon.Contains(date)) return BadRequest(horizon.DateText);
        return Ok(await BuildDtoAsync(shopId, date, horizon.Today, ct));
    }

    /// <summary>The full list of products of the day (replacing the menu). Only the shop's live products; no repeats; up to 1000.</summary>
    [HttpPut("daily-menus/{date}")]
    public async Task<ActionResult<DailyMenuDto>> PutMenu(Guid shopId, DateOnly date, DailyMenuInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageAvailability, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var horizon = await HorizonAsync(result.Shop!, ct);
        if (!horizon.Contains(date)) return BadRequest(horizon.DateText);

        var ids = input.ProductIds ?? [];
        if (ids.Count > MaxProductsInMenu) return BadRequest(ProductNotFound);
        if (ids.Distinct().Count() != ids.Count) return BadRequest(ProductRepeats);
        var known = await db.Products.AsNoTracking().Where(p => p.CompanyId == shopId && p.DeletedAtUtc == null && ids.Contains(p.Id)).CountAsync(ct);
        if (known != ids.Count) return BadRequest(ProductNotFound);

        await SaveMenuAsync(shopId, date, ids, ct);
        return Ok(await BuildDtoAsync(shopId, date, horizon.Today, ct));
    }

    /// <summary>The date goes back to the weekday rule. Idempotent.</summary>
    [HttpDelete("daily-menus/{date}")]
    public async Task<IActionResult> DeleteMenu(Guid shopId, DateOnly date, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageAvailability, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var horizon = await HorizonAsync(result.Shop!, ct);
        if (!horizon.Contains(date)) return BadRequest(horizon.DateText);

        var menu = await db.ShopDailyMenus.FirstOrDefaultAsync(m => m.CompanyId == shopId && m.Date == date, ct);
        if (menu is not null)
        {
            db.ShopDailyMenus.Remove(menu); // its items go with it (cascade)
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    /// <summary>P1: copy the products of another date's menu. Products deleted since are left out. The source date needs a menu.</summary>
    [HttpPost("daily-menus/{date}/copy")]
    public async Task<ActionResult<DailyMenuDto>> CopyMenu(Guid shopId, DateOnly date, DailyMenuCopyInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageAvailability, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var horizon = await HorizonAsync(result.Shop!, ct);
        if (!horizon.Contains(date)) return BadRequest(horizon.DateText);
        if (input.SourceDate is not { } source) return BadRequest(NoMenuOnDate);

        var sourceProducts = await db.ShopDailyMenus.AsNoTracking().Where(m => m.CompanyId == shopId && m.Date == source)
            .SelectMany(m => m.Items.Select(i => (Guid?)i.ProductId)).ToListAsync(ct);
        var sourceExists = sourceProducts.Count > 0 || await db.ShopDailyMenus.AsNoTracking().AnyAsync(m => m.CompanyId == shopId && m.Date == source, ct);
        if (!sourceExists) return BadRequest(NoMenuOnDate);

        var ids = sourceProducts.Select(i => i!.Value).ToList();
        var live = await db.Products.AsNoTracking().Where(p => p.CompanyId == shopId && p.DeletedAtUtc == null && ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        await SaveMenuAsync(shopId, date, live, ct);
        return Ok(await BuildDtoAsync(shopId, date, horizon.Today, ct));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed record Horizon(DateOnly Today, DateOnly Last, int Days)
    {
        public bool Contains(DateOnly date) => date >= Today && date <= Last;
        public string DateText => $"Дата — от сегодня до {Days} дней вперёд";
        public string RangeText => DateText;
    }

    /// <summary>today = the shop's current working day; last = today + max(pre-order days, 7) (§452.3).</summary>
    private async Task<Horizon> HorizonAsync(Company shop, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var context = await gates.LoadAsync(shop, now, ct);
        var days = Math.Max(context.Settings.PreorderDays, options.Value.DailyMenuMinHorizonDays);
        var today = context.Pickup.CurrentWorkingDay(now);
        return new Horizon(today, today.AddDays(days), days);
    }

    private async Task SaveMenuAsync(Guid shopId, DateOnly date, IReadOnlyList<Guid> productIds, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // One writer per shop: two people saving the same date at once must not both insert the row (the unique index would turn the loser into a 500).
        await AdvisoryLock.AcquireAsync(db, $"shop-menu:{shopId}");
        var menu = await db.ShopDailyMenus.Include(m => m.Items).FirstOrDefaultAsync(m => m.CompanyId == shopId && m.Date == date, ct);
        if (menu is null)
        {
            menu = new ShopDailyMenu { Id = Guid.NewGuid(), CompanyId = shopId, Date = date };
            db.ShopDailyMenus.Add(menu);
        }
        else
        {
            db.ShopDailyMenuItems.RemoveRange(menu.Items);
        }
        menu.UpdatedAtUtc = DateTime.UtcNow;
        menu.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        foreach (var productId in productIds) db.ShopDailyMenuItems.Add(new ShopDailyMenuItem { DailyMenuId = menu.Id, ProductId = productId });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task<DailyMenuDto> BuildDtoAsync(Guid shopId, DateOnly date, DateOnly today, CancellationToken ct)
    {
        var menuProducts = await db.ShopDailyMenus.AsNoTracking().Where(m => m.CompanyId == shopId && m.Date == date)
            .Select(m => new { Ids = m.Items.Select(i => i.ProductId).ToList() }).FirstOrDefaultAsync(ct);
        var exists = menuProducts is not null;
        var inMenu = menuProducts?.Ids.ToHashSet() ?? [];

        var categories = await db.ProductCategories.AsNoTracking().Where(c => c.CompanyId == shopId).ToDictionaryAsync(c => c.Id, ct);
        var products = (await db.Products.AsNoTracking().Where(p => p.CompanyId == shopId && p.DeletedAtUtc == null).ToListAsync(ct))
            .OrderBy(p => p.CategoryId is { } c && categories.TryGetValue(c, out var cat) ? cat.Position : int.MaxValue)
            .ThenBy(p => p.Position).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id).ToList();

        var rows = products.Select(p =>
        {
            var allowed = WeekdayMask.Allows(p.AvailableWeekdaysMask, date);
            // No menu on the date → pre-filled by the weekday rule; a menu → exactly its products.
            return new DailyMenuProductDto(
                p.Id, p.Name, p.CategoryId is { } cid && categories.TryGetValue(cid, out var category) ? category.Name : null,
                p.IsPublished, exists ? inMenu.Contains(p.Id) : allowed, allowed);
        }).ToList();
        return new DailyMenuDto(date, ShopTimeTexts.DateLabel(date, today), exists, rows.Where(r => r.InMenu).Select(r => r.ProductId).ToList(), rows);
    }
}
