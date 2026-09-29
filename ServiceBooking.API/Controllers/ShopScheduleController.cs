using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Orders;
using ServiceBooking.API.DTOs.Shops;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §449–§453, API_CONTRACT_CYCLE24.md §473–§476 — the TIME of a shop: working hours, special days, the pickup settings,
/// pause / the "stop" switch, the lightweight acceptance status for the orders screen and the slots for staff. Order of checks like every cabinet
/// route: token → the company exists and is a SHOP (404) → the role (403) → validation (400) → conflicts (409). Working hours, special days and
/// pickup settings are the owner's; pause and the status are for owner and staff (§460).
/// </summary>
[ApiController]
[Route("api/shops/{shopId:guid}")]
[Authorize]
public class ShopScheduleController(
    AppDbContext db, ShopAccessResolver access, ShopGateLoader gates, ShopManageMapper manageMapper, OrderActorResolver actorResolver,
    IOptions<OrdersOptions> options) : ControllerBase
{
    // ── Working hours ────────────────────────────────────────────────────────────────────────────────────────────

    [HttpGet("working-hours")]
    public async Task<ActionResult<WorkingHoursDto>> GetWorkingHours(Guid shopId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var json = await db.ShopSettings.AsNoTracking().Where(s => s.CompanyId == shopId).Select(s => s.WorkingHoursJson).FirstOrDefaultAsync(ct);
        return Ok(ShopScheduleMapper.ToDto(ShopScheduleRules.Parse(json)));
    }

    /// <summary>The whole week is replaced. Times are "HH:mm", 5-minute step; an interval with <c>end ≤ start</c> goes through midnight. Valid for NEW orders; existing ones are not touched.</summary>
    [HttpPut("working-hours")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<WorkingHoursDto>> PutWorkingHours(Guid shopId, WorkingHoursInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        var parsed = ShopScheduleRules.ParseWeek((input.Days ?? []).Select(d => (d.DayOfWeek ?? (DayOfWeek)99, (IReadOnlyList<TimeIntervalText>?)d.Intervals?.Select(i => new TimeIntervalText(i.Start, i.End)).ToList())).ToList());
        if (!parsed.Ok) return BadRequest(parsed.Error);

        var settings = await LoadOrCreateSettingsAsync(shopId, ct);
        settings.WorkingHoursJson = ShopScheduleRules.Serialize(parsed.Hours!);
        Touch(settings);
        await db.SaveChangesAsync(ct);
        return Ok(ShopScheduleMapper.ToDto(parsed.Hours));
    }

    // ── Special days (P1) ────────────────────────────────────────────────────────────────────────────────────────

    [HttpGet("special-days")]
    public async Task<ActionResult<List<SpecialDayDto>>> ListSpecialDays(Guid shopId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ViewShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var today = LocalToday(result.Shop!);
        var first = from ?? today;
        var last = to ?? today.AddDays(options.Value.SpecialDaysHorizonDays);
        if (last < first || last.DayNumber - first.DayNumber > options.Value.SpecialDaysHorizonDays + 366) return BadRequest(SpecialDayDateText);

        var rows = await db.ShopSpecialDays.AsNoTracking().Where(d => d.CompanyId == shopId && d.Date >= first && d.Date <= last).OrderBy(d => d.Date).ToListAsync(ct);
        return Ok(rows.Select(ShopScheduleMapper.ToDto).ToList());
    }

    /// <summary>
    /// A date with its own hours (or closed). Active orders (New / Accepted / Ready) for that date that fall outside the new hours make the request a 409
    /// <c>ScheduleConflictsWithOrders</c> with the list — unless <c>confirmConflicts</c> is true. The orders themselves are NOT changed.
    /// </summary>
    [HttpPut("special-days/{date}")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<SpecialDayDto>> PutSpecialDay(Guid shopId, DateOnly date, SpecialDayInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var shop = result.Shop!;
        if (!InHorizon(shop, date)) return BadRequest(SpecialDayDateText);

        IReadOnlyList<TimeInterval> intervals = [];
        if (!input.IsClosed)
        {
            var parsed = ShopScheduleRules.ParseDay(input.Intervals?.Select(i => new TimeIntervalText(i.Start, i.End)).ToList());
            if (!parsed.Ok) return BadRequest(parsed.Error);
            intervals = parsed.Intervals!;
        }
        var closed = input.IsClosed || intervals.Count == 0; // "open with no hours" is a day off, like a weekday without intervals

        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shopId, ct) ?? new ShopSettings { CompanyId = shopId };
        var zone = ShopGateLoader.ZoneOf(shop);
        var schedule = await gates.LoadScheduleAsync(shop, settings, zone, LocalToday(shop), ct);
        var specialDays = schedule.SpecialDays.ToDictionary(d => d.Key, d => d.Value);
        specialDays[date] = closed ? SpecialDayHours.Closed : new SpecialDayHours(false, intervals);
        var proposed = new PickupSchedule(schedule with { SpecialDays = specialDays }, ShopOrderingGate.PickupSettingsOf(settings));

        // The tail of an overnight interval must not reach the first interval of the next day, in either direction (previous day → this one, this one → next).
        if (!closed)
        {
            var previous = proposed.IntervalsFor(date.AddDays(-1));
            var next = proposed.IntervalsFor(date.AddDays(1));
            if (previous.Count > 0 && previous[^1].EndUtc > proposed.IntervalsFor(date)[0].StartUtc ||
                next.Count > 0 && proposed.IntervalsFor(date)[^1].EndUtc >= next[0].StartUtc)
                return BadRequest(ShopScheduleRules.TailOverlapsNextDay);
        }

        if (!input.ConfirmConflicts)
        {
            var conflicts = await ConflictingOrdersAsync(shop, date, proposed, ct);
            if (conflicts.Count > 0)
                return Conflict(new CatalogConflictDto(CatalogConflictCode.ScheduleConflictsWithOrders,
                    "На этот день уже есть заказы вне новых часов — свяжитесь с покупателями или отмените заказы", conflicts));
        }

        var row = await db.ShopSpecialDays.FirstOrDefaultAsync(d => d.CompanyId == shopId && d.Date == date, ct);
        if (row is null)
        {
            row = new ShopSpecialDay { CompanyId = shopId, Date = date };
            db.ShopSpecialDays.Add(row);
        }
        row.IsClosed = closed;
        row.IntervalsJson = closed ? null : ShopScheduleRules.SerializeIntervals(intervals);
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await db.SaveChangesAsync(ct);
        return Ok(ShopScheduleMapper.ToDto(row));
    }

    /// <summary>The date goes back to the weekly hours. Idempotent: no special day is also a 204.</summary>
    [HttpDelete("special-days/{date}")]
    [RequiresOwnerTerms]
    public async Task<IActionResult> DeleteSpecialDay(Guid shopId, DateOnly date, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        if (!InHorizon(result.Shop!, date)) return BadRequest(SpecialDayDateText);

        var row = await db.ShopSpecialDays.FirstOrDefaultAsync(d => d.CompanyId == shopId && d.Date == date, ct);
        if (row is not null)
        {
            db.ShopSpecialDays.Remove(row);
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    // ── Pickup settings ──────────────────────────────────────────────────────────────────────────────────────────

    [HttpPut("pickup-settings")]
    [RequiresOwnerTerms]
    public async Task<ActionResult<ShopManageDto>> PutPickupSettings(Guid shopId, PickupSettingsDto input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageShop, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;

        if (!input.AsapEnabled && !input.ScheduledEnabled) return BadRequest("Включите хотя бы один вариант времени получения");
        if (input.SlotStepMinutes is not (15 or 30 or 60)) return BadRequest("Шаг слотов — 15, 30 или 60 минут");
        if (input.PreorderDays is < 0 or > 14) return BadRequest("Предзаказ — от 0 до 14 дней вперёд");
        if (input.MinPrepMinutes is < 0 or > 180) return BadRequest("Время приготовления — от 0 до 180 минут");

        var settings = await LoadOrCreateSettingsAsync(shopId, ct);
        settings.AsapEnabled = input.AsapEnabled;
        settings.ScheduledEnabled = input.ScheduledEnabled;
        settings.SlotStepMinutes = input.SlotStepMinutes;
        settings.PreorderDays = input.PreorderDays;
        settings.MinPrepMinutes = input.MinPrepMinutes;
        Touch(settings);
        await db.SaveChangesAsync(ct);
        return Ok(await manageMapper.BuildAsync(result.Shop!, result.Role, ct));
    }

    // ── Pause / stop ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// "Принимаем / пауза / не принимаем" (owner and staff). Every change records who and when. The end of a pause is NOT scheduled anywhere: an expired
    /// <c>pausedUntilUtc</c> simply reads as "accepting", so the resume is instant and survives a restart. It does not touch the board revision (the state
    /// rides in every board answer).
    /// </summary>
    [HttpPut("acceptance")]
    public async Task<ActionResult<ShopAcceptanceDto>> PutAcceptance(Guid shopId, AcceptanceInput input, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageAcceptance, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var shop = result.Shop!;
        if (!Enum.IsDefined(input.Mode) || input.Pause is { } p && !Enum.IsDefined(p)) return BadRequest(ShopAcceptanceRules.PauseDurationRequired);

        var now = DateTime.UtcNow;
        var context = await gates.LoadAsync(shop, now, ct);
        var applied = ShopAcceptanceRules.Apply(input.Mode, input.Pause, now, context.Pickup);
        if (applied is null) return BadRequest(ShopAcceptanceRules.PauseDurationRequired);

        var settings = await LoadOrCreateSettingsAsync(shopId, ct);
        var actor = await actorResolver.ResolveStaffAsync(User);
        settings.OrdersStopped = applied.Value.Stopped;
        settings.PausedUntilUtc = applied.Value.PausedUntilUtc;
        settings.AcceptanceChangedAtUtc = now;
        settings.AcceptanceChangedByUserId = actor.UserId;
        settings.AcceptanceChangedByName = actor.NameSnapshot;
        settings.UpdatedAtUtc = now;
        settings.UpdatedByUserId = actor.UserId;
        await db.SaveChangesAsync(ct);
        return Ok(ShopScheduleMapper.ToDto(ShopAcceptanceRules.State(settings, now, context.Zone)));
    }

    /// <summary>The light status polled by the orders screen every 60 seconds: the acceptance verdict, open / closed, the pause and the month limit.</summary>
    [HttpGet("ordering-status")]
    [EnableRateLimiting("order-board")]
    public async Task<ActionResult<ShopOrderingStatusDto>> GetOrderingStatus(Guid shopId, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageAcceptance, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        var context = await gates.LoadAsync(result.Shop!, DateTime.UtcNow, ct);
        var gate = context.Gate;
        return Ok(new ShopOrderingStatusDto(
            gate.Accepting, gate.Code, gate.ReasonText, gate.OwnerText, ShopScheduleMapper.ToDto(gate.OpenState), ShopScheduleMapper.ToDto(gate.Acceptance),
            ShopScheduleMapper.ToDto(gate.Asap, context.Settings.AsapEnabled), gate.ScheduledAvailable, ShopScheduleMapper.ToDto(gate.OrderLimit),
            context.Schedule.HoursSet));
    }

    // ── Slots for staff ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The slots staff can move an order to: without the preparation time, the pause and the "order to a time" switch — a slot is fit while it has not ended.</summary>
    [HttpGet("pickup-slots")]
    public async Task<ActionResult<PickupSlotsDto>> GetStaffSlots(Guid shopId, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        var result = await access.ResolveAsync(shopId, User, ShopPermission.ManageOrders, asNoTracking: true, ct: ct);
        if (!result.Ok) return result.Error!;
        if (date is not { } day) return BadRequest("Укажите дату");

        var now = DateTime.UtcNow;
        var context = await gates.LoadAsync(result.Shop!, now, ct);
        var schedule = context.Pickup;
        var today = schedule.CurrentWorkingDay(now);
        var slots = schedule.SlotsForDate(day, now, forStaff: true);
        var open = schedule.OpenState(now);
        // "As soon as possible" for staff needs only an open shop; the estimate is still now + preparation.
        var asap = day == today
            ? new AsapOptionDto(open.IsOpen, open.IsOpen ? now.AddMinutes(context.Settings.MinPrepMinutes) : null,
                open.IsOpen ? $"≈ к {ShopTimeTexts.Clock(LocalMinutes(now.AddMinutes(context.Settings.MinPrepMinutes), context.Zone))}" : open.Text)
            : null;
        return Ok(new PickupSlotsDto(
            day, ShopTimeTexts.DateLabel(day, today), slots.Select(s => new PickupSlotDto(s.StartUtc, s.EndUtc, s.Label)).ToList(), asap,
            slots.Count > 0 ? null : schedule.SlotsReason(day, now, forStaff: true)));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    private const string SpecialDayDateText = "Дата — от сегодня до 90 дней вперёд";

    private static DateOnly LocalToday(Company shop) => ShopClock.BusinessDate(shop.TimeZoneId, DateTime.UtcNow);

    private bool InHorizon(Company shop, DateOnly date)
    {
        var today = LocalToday(shop);
        return date >= today && date <= today.AddDays(options.Value.SpecialDaysHorizonDays);
    }

    private static int LocalMinutes(DateTime utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        return local.Hour * 60 + local.Minute;
    }

    private async Task<ShopSettings> LoadOrCreateSettingsAsync(Guid shopId, CancellationToken ct)
    {
        var settings = await db.ShopSettings.FirstOrDefaultAsync(s => s.CompanyId == shopId, ct);
        if (settings is not null) return settings;
        settings = new ShopSettings { CompanyId = shopId };
        db.ShopSettings.Add(settings);
        return settings;
    }

    private void Touch(ShopSettings settings)
    {
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
    }

    /// <summary>Active orders of the date whose pickup does not fit the proposed hours (a slot must lie inside one interval; "as soon as possible" — its estimate).</summary>
    private async Task<List<ScheduleConflictOrderDto>> ConflictingOrdersAsync(Company shop, DateOnly date, PickupSchedule proposed, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CompanyId == shop.Id && o.PickupDate == date &&
                        (o.Status == OrderStatus.New || o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready))
            .OrderBy(o => o.PickupStartUtc).ThenBy(o => o.Number).ToListAsync(ct);
        var intervals = proposed.IntervalsFor(date);
        var now = DateTime.UtcNow;
        return orders
            .Where(o =>
            {
                var end = o.PickupEndUtc ?? o.PickupStartUtc;
                return !intervals.Any(i => o.PickupStartUtc >= i.StartUtc && end <= i.EndUtc);
            })
            .Select(o => new ScheduleConflictOrderDto(
                o.Id, o.Number, PickupSchedule.PickupText(o.PickupKind, o.PickupDate, o.PickupStartUtc, proposed.Schedule.Zone, now),
                OrderTexts.StatusText(o.Status), o.CustomerName, o.CustomerPhone))
            .ToList();
    }
}
