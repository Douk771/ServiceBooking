using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.API.Services.Companies;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.PublicSites;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §393.3, §395, §411–§413 (cycle 24: §477–§478) — the public storefront of a shop by its address (<c>goods.ezbook.ru/&lt;slug&gt;</c>):
/// the catalog for a pickup DATE, the slots, the cart check and the checkout. A slug that does not exist or belongs to a salon is a bare 404.
/// </summary>
[ApiController]
[Route("api/storefront/{slug}")]
public class StorefrontController(
    AppDbContext db, StockLedger stockLedger, PublicSiteLinks links, OrderCreationService orderCreation, ShopGateLoader gates,
    DailyMenuService menus, ShopChannelReader shopChannels, CustomerOrderNotificationsBuilder notificationsBuilder,
    ServiceBooking.API.Services.Notifications.CustomerMessagingOfferService messagingOffer) : ControllerBase
{
    /// <summary>
    /// The storefront in a handful of indexed reads (§393.3): the shop with its city; the settings, special days, tariff and month counter through
    /// <see cref="ShopGateLoader"/>; the daily menu of the date; the visible categories and the published products; and — only when stock tracking is
    /// on — the whole reserve in ONE GROUP BY. No N+1, read-only. Products that are not sold on the date are not sent at all.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting("storefront")]
    public async Task<ActionResult<StorefrontDto>> Get(string slug, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        var normalized = SlugPolicy.Normalize(slug);
        var shop = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == normalized && c.Kind == CompanyKind.Orders, ct);
        if (shop is null) return NotFound();

        var now = DateTime.UtcNow;
        var context = await gates.LoadAsync(shop, now, ct);
        var settings = context.Settings;
        var gate = context.Gate;
        var cityName = shop.CityId is null ? null : await db.Cities.AsNoTracking().Where(c => c.Id == shop.CityId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        // The date the assortment is for: the current working day, or a date the customer can really order for; anything else falls back to "today"
        // with a notice (the storefront never answers 400 for a date the customer typed).
        var today = context.Pickup.CurrentWorkingDay(now);
        var pickupDate = today;
        string? dateNotice = null;
        if (date is { } requested && requested != today)
        {
            if (gate.Dates.Any(d => d.Date == requested)) pickupDate = requested;
            else dateNotice = $"На {ShopTimeTexts.DayMonth(requested)} заказать нельзя — показан ассортимент на сегодня";
        }

        var webPushOffered = settings.CustomerWebPushEnabled && notificationsBuilder.PlatformPushEnabled;
        var messengerOffer = await messagingOffer.ForCompanyAsync(shop, ct: ct);
        var messengerOffered = messengerOffer.Offered;
        var pickupSettings = ShopOrderingGate.PickupSettingsOf(settings);

        var photos = shop.IsActive ? await CompanyPhotoQueries.OrderedAsync(db, shop.Id, ct) : [];

        StorefrontDto Build(List<StorefrontCategoryDto> categories) => new(
            shop.Slug, shop.Name, links.CompanyPageUrl(shop), shop.LogoUrl, shop.Description, shop.Address, cityName, shop.Phone,
            shop.YandexMapsUrl, shop.TwoGisUrl, IsAvailable: shop.IsActive, gate.Accepting, gate.ReasonText, settings.CustomerMode,
            settings.AllowCustomerCancel, ShopManageMapper.ToPublicSeller(settings), categories,
            gate.Code, pickupDate, dateNotice, ShopScheduleMapper.ToDto(gate.OpenState),
            new StorefrontWorkingHoursDto(PickupSchedule.SummaryLines(context.Schedule.Weekly)
                .Select(l => new WorkingHoursSummaryLineDto(l.DayLabel, l.Text)).ToList()),
            ShopScheduleMapper.ToPickupOptions(gate, pickupSettings), new StorefrontCustomerNotificationsDto(webPushOffered, messengerOffered, messengerOffer.Transports, messengerOffer.CheckboxLabel),
            string.IsNullOrWhiteSpace(shop.Email) ? null : shop.Email, photos);

        // A blocked shop: the page exists but is empty ("Магазин недоступен").
        if (!shop.IsActive) return Ok(Build([]));

        var menu = await menus.LookupAsync(shop.Id, pickupDate, ct);
        var categories = await db.ProductCategories.AsNoTracking()
            .Where(c => c.CompanyId == shop.Id && !c.IsHidden).OrderBy(c => c.Position).ThenBy(c => c.CreatedAtUtc).ThenBy(c => c.Id)
            .ToListAsync(ct);
        var visibleIds = categories.Select(c => c.Id).ToList();
        // Not published, deleted and hidden-category products are never sent. Sold-out / out-of-stock ones ARE, greyed (available = false).
        var products = await db.Products.AsNoTracking()
            .Where(p => p.CompanyId == shop.Id && p.DeletedAtUtc == null && p.IsPublished &&
                        (p.CategoryId == null || visibleIds.Contains(p.CategoryId.Value)))
            .OrderBy(p => p.Position).ThenBy(p => p.CreatedAtUtc).ThenBy(p => p.Id)
            .ToListAsync(ct);
        var reserved = settings.TrackStock ? await stockLedger.GetReservedAsync(shop.Id, ct: ct) : new Dictionary<Guid, int>();

        // null = not sold on this date: left out of the answer entirely.
        StorefrontProductDto? Map(Product p, ProductCategory? category)
        {
            var verdict = CatalogAvailability.Evaluate(
                p, category, settings.TrackStock, StockLedger.Free(p.StockOnHand, reserved.GetValueOrDefault(p.Id)), gate.Accepting, pickupDate, menu);
            if (verdict == ProductAvailability.NotOnThisDate) return null;
            var reason = verdict switch
            {
                ProductAvailability.SoldOut => StorefrontUnavailableReason.SoldOut,
                ProductAvailability.InsufficientStock => StorefrontUnavailableReason.InsufficientStock,
                ProductAvailability.ShopNotAccepting => StorefrontUnavailableReason.ShopNotAccepting,
                _ => (StorefrontUnavailableReason?)null
            };
            return new StorefrontProductDto(
                p.Id, p.Name, p.Description, p.ImageUrl, p.ThumbnailUrl, p.Unit, p.Price, p.PortionText, p.WeightStepGrams,
                OrderQuantityRules.MinQuantity(p.Unit, p.WeightStepGrams, p.MinQuantityGrams), OrderQuantityRules.MaxQuantity(p.Unit),
                new FoodInfoDto(p.CompositionAndAllergens), verdict == ProductAvailability.Available, reason);
        }

        var byCategory = products.ToLookup(p => p.CategoryId);
        var result = new List<StorefrontCategoryDto>();
        foreach (var category in categories)
        {
            var items = byCategory[category.Id].Select(p => Map(p, category)).OfType<StorefrontProductDto>().ToList();
            if (items.Count > 0) result.Add(new StorefrontCategoryDto(category.Id, category.Name, items));
        }
        var uncategorized = byCategory[null].Select(p => Map(p, null)).OfType<StorefrontProductDto>().ToList();
        if (uncategorized.Count > 0) result.Add(new StorefrontCategoryDto(null, "Другое", uncategorized));

        return Ok(Build(result));
    }

    /// <summary>
    /// The slots of a date (and "as soon as possible" for the current working day) — §477.2. A date the customer cannot order for is a 200 with no
    /// slots and the reason; a missing date is the only 400. Everything is the server's: the frontend prints.
    /// </summary>
    [HttpGet("pickup-slots")]
    [EnableRateLimiting("storefront")]
    public async Task<ActionResult<PickupSlotsDto>> GetPickupSlots(string slug, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        if (date is not { } day) return BadRequest("Укажите дату");
        var normalized = SlugPolicy.Normalize(slug);
        var shop = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == normalized && c.Kind == CompanyKind.Orders, ct);
        if (shop is null) return NotFound();

        var now = DateTime.UtcNow;
        var context = await gates.LoadAsync(shop, now, ct);
        var schedule = context.Pickup;
        var today = schedule.CurrentWorkingDay(now);
        var slots = schedule.SlotsForDate(day, now, forStaff: false);
        var reason = slots.Count > 0 ? null : schedule.SlotsReason(day, now, forStaff: false);
        var asap = day == today ? ShopScheduleMapper.ToDto(context.Gate.Asap, context.Settings.AsapEnabled) : null;
        return Ok(new PickupSlotsDto(
            day, ShopTimeTexts.DateLabel(day, today), slots.Select(s => new PickupSlotDto(s.StartUtc, s.EndUtc, s.Label)).ToList(), asap, reason));
    }

    [HttpPost("quote")]
    [EnableRateLimiting("storefront")]
    public async Task<ActionResult<QuoteDto>> Quote(string slug, QuoteInput input, CancellationToken ct)
    {
        var (error, quote) = await orderCreation.QuoteAsync(slug, input, ct);
        return error is not null ? error : Ok(quote);
    }

    /// <summary>201 with a new order; 200 with the existing one when the idempotency key was already used (§413.1 step 5).</summary>
    [HttpPost("orders")]
    [EnableRateLimiting("order-create")]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(string slug, CreateOrderInput input, CancellationToken ct)
    {
        var result = await orderCreation.CreateAsync(slug, input, User, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        if (result.Error is not null) return result.Error;
        return result.Created ? StatusCode(StatusCodes.Status201Created, result.Response) : Ok(result.Response);
    }
}
