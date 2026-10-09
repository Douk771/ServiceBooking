using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Stays;

/// <summary>A service together with the company and the settings every calculation needs.</summary>
public sealed record ServiceScope(StayService Service, Company Company, StaysSettings Settings);

public sealed record ServiceSelection(DateOnly BusinessDate, int StartMinute, int Hours, IReadOnlyList<ItemSelectionInput> Items);

public sealed record ServiceProblem(ServiceRefusalCode Code, string Message);

public sealed record ResolvedItem(Guid ItemId, string Name, int UnitPriceRub, int Quantity, int MaxPerSession);

/// <summary>The verdict of the rules on ONE choice of time: the problems in the order of the contract and the money when it can be computed.</summary>
public sealed record ServiceEvaluation(
    DateOnly BusinessDate, int StartMinute, int Hours, DateTime StartUtc, DateTime EndUtc, IReadOnlyList<ServiceProblem> Problems, int[]? HourPrices,
    IReadOnlyList<ResolvedItem> Items, ServiceQuoteResult? Money)
{
    public bool Ok => Problems.Count == 0 && Money is not null && HourPrices is not null;
}

/// <summary>
/// ARCHITECTURE_CYCLE39.md §39.4 — the DATABASE side of «which starts are free»: loads the windows of the date (a manual date or the weekly template), the price rules and the
/// active sessions of the service, then hands them to the pure <see cref="ServiceSlotCalculator"/>. ONE path for what a guest is shown and for what the server accepts.
/// A session whose parent is a hold that has already expired counts as free for reading (the task will release it within 15 s); for writing the caller first releases
/// what it can and then counts everything that is still active (<c>includeExpiredHolds</c>).
/// </summary>
public class ServiceSlotService(AppDbContext db, IOptions<StaysOptions> options, IStaysClock clock)
{
    public int BusinessDayStart => options.Value.Services.BusinessDayStartMinute;

    // ── loading ──

    public async Task<ServiceScope?> FindPublicAsync(CompanyKind kind, Guid serviceId, CancellationToken ct)
    {
        var service = await db.StayServices.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId && s.IsPublished && s.ArchivedAtUtc == null, ct);
        return service is null ? null : await ScopeAsync(kind, service, ct);
    }

    public async Task<ServiceScope?> FindOfCompanyAsync(CompanyKind kind, Guid companyId, Guid serviceId, CancellationToken ct)
    {
        var service = await db.StayServices.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId && s.CompanyId == companyId, ct);
        return service is null ? null : await ScopeAsync(kind, service, ct);
    }

    public async Task<ServiceScope?> ScopeAsync(CompanyKind kind, StayService service, CancellationToken ct)
    {
        var company = await db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.Id == service.CompanyId && c.Kind == kind, ct);
        if (company is null) return null;
        var settings = await db.StaysSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == company.Id, ct) ?? new StaysSettings { CompanyId = company.Id };
        return new ServiceScope(service, company, settings);
    }

    public DateOnly TodayOf(Company company) => BusinessClock.TodayBusinessDate(company.TimeZoneId, clock.UtcNow, BusinessDayStart);

    /// <summary>The windows of every business date of [from, to]: a manual date wins over the weekly template.</summary>
    public async Task<Dictionary<DateOnly, List<WindowSpec>>> WindowsAsync(Guid serviceId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var weekly = (await db.StayServiceWeeklyWindows.AsNoTracking().Where(w => w.ServiceId == serviceId).ToListAsync(ct))
            .ToLookup(w => w.DayOfWeek, w => new WindowSpec(w.StartMinute, w.EndMinute));
        var overrides = await db.StayServiceDateOverrides.AsNoTracking().Where(o => o.ServiceId == serviceId && o.BusinessDate >= from && o.BusinessDate <= to)
            .ToDictionaryAsync(o => o.BusinessDate, ct);
        var result = new Dictionary<DateOnly, List<WindowSpec>>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (overrides.TryGetValue(d, out var o))
                result[d] = o.IsClosed ? [] : ServiceScheduleRules.Sorted(ServiceJson.ReadWindows(o.WindowsJson).Select(w => new WindowSpec(w.StartMinute, w.EndMinute)));
            else
                result[d] = ServiceScheduleRules.Sorted(weekly[BusinessClock.DayOfWeekIso(d)]);
        }
        return result;
    }

    public async Task<List<PriceRuleSpec>> RulesAsync(Guid serviceId, CancellationToken ct) =>
        (await db.StayServicePriceRules.AsNoTracking().Where(r => r.ServiceId == serviceId).ToListAsync(ct))
            .Select(r => new PriceRuleSpec(r.DaysMask, r.FromHour, r.ToHour, r.PriceRub)).ToList();

    /// <summary>The active sessions of the service that can touch the business dates [from, to].</summary>
    public async Task<List<OccupiedSpec>> OccupiedAsync(
        ServiceScope scope, DateOnly from, DateOnly to, bool includeExpiredHolds, CancellationToken ct)
    {
        var tz = scope.Company.TimeZoneId;
        var fromUtc = BusinessClock.ToUtc(tz, from, BusinessDayStart).AddHours(-24);
        var toUtc = BusinessClock.ToUtc(tz, to, BusinessDayStart).AddHours(48);
        var rows = await db.StayServiceSessions.AsNoTracking()
            .Where(s => s.ServiceId == scope.Service.Id && s.ReleasedAtUtc == null && s.StartUtc < toUtc && s.OccupiedUntilUtc > fromUtc)
            .Select(s => new { s.StartUtc, s.OccupiedUntilUtc, s.StayBookingId, s.StayServiceOrderId }).ToListAsync(ct);
        if (rows.Count == 0) return [];
        if (includeExpiredHolds) return rows.Select(r => new OccupiedSpec(r.StartUtc, r.OccupiedUntilUtc)).ToList();

        var now = clock.UtcNow;
        var bookingIds = rows.Where(r => r.StayBookingId != null).Select(r => r.StayBookingId!.Value).Distinct().ToList();
        var orderIds = rows.Where(r => r.StayServiceOrderId != null).Select(r => r.StayServiceOrderId!.Value).Distinct().ToList();
        var expiredBookings = bookingIds.Count == 0 ? [] : (await db.StayBookings.AsNoTracking()
            .Where(b => bookingIds.Contains(b.Id) && b.Status == StayBookingStatus.Held && b.HoldExpiresAtUtc <= now).Select(b => b.Id).ToListAsync(ct)).ToHashSet();
        var expiredOrders = orderIds.Count == 0 ? [] : (await db.StayServiceOrders.AsNoTracking()
            .Where(o => orderIds.Contains(o.Id) && o.Status == StayBookingStatus.Held && o.HoldExpiresAtUtc <= now).Select(o => o.Id).ToListAsync(ct)).ToHashSet();
        return rows
            .Where(r => !(r.StayBookingId is { } b && expiredBookings.Contains(b)) && !(r.StayServiceOrderId is { } o && expiredOrders.Contains(o)))
            .Select(r => new OccupiedSpec(r.StartUtc, r.OccupiedUntilUtc)).ToList();
    }

    public static StayRangeSpec StayRangeOf(string timeZoneId, DateOnly checkIn, TimeOnly checkInTime, DateOnly checkOut, TimeOnly checkOutTime) =>
        new(StayTime.ToUtc(timeZoneId, checkIn, checkInTime), StayTime.ToUtc(timeZoneId, checkOut, checkOutTime));

    public static StayRangeSpec StayRangeOf(StayBooking b) =>
        StayRangeOf(b.TimeZoneIdSnapshot, b.CheckInDate, b.CheckInTimeSnapshot, b.CheckOutDate, b.CheckOutTimeSnapshot);

    public ServiceSlotInput BuildInput(
        ServiceScope scope, DateOnly date, bool staff, StayRangeSpec? stay, IReadOnlyList<WindowSpec> windows, IReadOnlyList<PriceRuleSpec> rules,
        IReadOnlyList<OccupiedSpec> occupied)
    {
        var s = scope.Service;
        return new ServiceSlotInput(date, scope.Company.TimeZoneId, clock.UtcNow, scope.Settings.HorizonDays,
            new ServiceSlotRules(s.MinHours, s.MaxHours, s.StepMinutes, s.BufferMinutes, staff ? 0 : s.MinLeadMinutes), windows, rules, occupied, stay, BusinessDayStart);
    }

    public async Task<ServiceSlotInput> InputAsync(
        ServiceScope scope, DateOnly date, bool staff, StayRangeSpec? stay, bool includeExpiredHolds, IReadOnlyList<OccupiedSpec>? extraOccupied, CancellationToken ct)
    {
        var windows = (await WindowsAsync(scope.Service.Id, date, date, ct))[date];
        var rules = await RulesAsync(scope.Service.Id, ct);
        var occupied = await OccupiedAsync(scope, date, date, includeExpiredHolds, ct);
        if (extraOccupied is { Count: > 0 }) occupied = [.. occupied, .. extraOccupied];
        return BuildInput(scope, date, staff, stay, windows, rules, occupied);
    }

    // ── starts and availability ──

    public async Task<ServiceStartsDto> StartsAsync(ServiceScope scope, DateOnly date, bool staff, StayRangeSpec? stay, CancellationToken ct)
    {
        var input = await InputAsync(scope, date, staff, stay, includeExpiredHolds: false, extraOccupied: null, ct);
        var starts = ServiceSlotCalculator.Calculate(input);
        var s = scope.Service;
        return new ServiceStartsDto(s.Id, date, ServiceTimeFormat.BusinessDateLabel(date), s.MinHours, s.MaxHours,
            starts.Starts.Select(x => new StartDto(x.StartMinute, x.StartUtc, ServiceTimeFormat.StartLabel(date, x.StartMinute), x.MaxHours,
                Enumerable.Range(s.MinHours, Math.Max(0, x.MaxHours - s.MinHours + 1))
                    .Select(h => new HoursOptionDto(h, ServiceTimeFormat.GuestEnd(date, x.StartMinute, h))).ToList())).ToList(),
            starts.Reason, starts.Starts.Count == 0 ? ServiceTexts.NoStartsText : null);
    }

    /// <summary>The calendar of dates: <c>hasStarts</c> by date, four queries and one pure call per date (p95 &lt; 500 ms for 14 days).</summary>
    public async Task<List<AvailabilityDayDto>> AvailabilityAsync(
        ServiceScope scope, DateOnly from, int days, bool staff, StayRangeSpec? stay, CancellationToken ct)
    {
        var to = from.AddDays(days - 1);
        var windows = await WindowsAsync(scope.Service.Id, from, to, ct);
        var rules = await RulesAsync(scope.Service.Id, ct);
        var occupied = await OccupiedAsync(scope, from, to, includeExpiredHolds: false, ct);
        var result = new List<AvailabilityDayDto>(days);
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var has = ServiceSlotCalculator.Calculate(BuildInput(scope, d, staff, stay, windows[d], rules, occupied)).Starts.Count > 0;
            result.Add(new AvailabilityDayDto(d, ServiceTimeFormat.BusinessDateLabel(d), has));
        }
        return result;
    }

    // ── evaluation of one choice ──

    /// <summary>
    /// The rules on ONE choice. The slot problem (the first of <see cref="ServiceSlotCalculator.Diagnose"/>) comes first, then the problems of the positions. Money is
    /// computed whenever every hour has a price (the amounts are 0 only when the start is unavailable, the hours are out of range or an hour has no price).
    /// </summary>
    public async Task<ServiceEvaluation> EvaluateAsync(
        ServiceScope scope, ServiceSelection sel, bool staff, StayRangeSpec? stay, bool includeExpiredHolds, IReadOnlyList<OccupiedSpec>? extraOccupied,
        int? prepayPercent, CancellationToken ct)
    {
        var input = await InputAsync(scope, sel.BusinessDate, staff, stay, includeExpiredHolds, extraOccupied, ct);
        var service = await ItemsOfAsync(scope.Service.Id, ct);
        return Evaluate(scope, input, sel, service, prepayPercent);
    }

    public ServiceEvaluation Evaluate(ServiceScope scope, ServiceSlotInput input, ServiceSelection sel, IReadOnlyList<StayServiceItem> catalog, int? prepayPercent)
    {
        var svc = scope.Service;
        var startUtc = BusinessClock.ToUtc(scope.Company.TimeZoneId, sel.BusinessDate, sel.StartMinute);
        var endUtc = startUtc.AddHours(sel.Hours);
        var problems = new List<ServiceProblem>();

        var diagnose = ServiceSlotCalculator.Diagnose(input, sel.StartMinute, sel.Hours);
        if (diagnose is { } code) problems.Add(new ServiceProblem(code, SlotMessage(code, scope, input, sel)));

        var items = new List<ResolvedItem>();
        // The same position listed twice is ONE position with the summed quantity: «максимум на сеанс» is a limit of the position, not of one line of the request.
        foreach (var chosen in sel.Items.Where(i => i.Quantity > 0).GroupBy(i => i.ItemId).Select(g => new ItemSelectionInput(g.Key, g.Sum(x => x.Quantity))))
        {
            var item = catalog.FirstOrDefault(i => i.Id == chosen.ItemId);
            if (item is null || !item.IsActive) { problems.Add(new ServiceProblem(ServiceRefusalCode.ItemUnavailable, ServiceTexts.ItemUnavailable(item?.Name ?? "—"))); continue; }
            if (chosen.Quantity > item.MaxPerSession)
            {
                problems.Add(new ServiceProblem(ServiceRefusalCode.ItemQuantityExceeded, ServiceTexts.ItemQuantityExceeded(item.Name, item.MaxPerSession)));
                continue;
            }
            items.Add(new ResolvedItem(item.Id, item.Name, item.PriceRub, chosen.Quantity, item.MaxPerSession));
        }

        int[]? prices = null;
        if (sel.Hours is >= 1 and <= 12)
        {
            var raw = ServicePricing.HourPrices(input.PriceRules, sel.BusinessDate, sel.StartMinute, sel.Hours);
            if (ServicePricing.AllPriced(raw)) prices = raw.Select(p => p!.Value).ToArray();
        }
        var moneyShown = prices is not null
            && diagnose is null or ServiceRefusalCode.TooEarly or ServiceRefusalCode.OutsideStay or ServiceRefusalCode.SlotTaken;
        var money = moneyShown && prices is not null
            ? ServiceMoney.Quote(prices, items.Select(i => new ServiceItemQuantity(i.UnitPriceRub, i.Quantity)).ToList(), prepayPercent) : null;
        return new ServiceEvaluation(sel.BusinessDate, sel.StartMinute, sel.Hours, startUtc, endUtc, problems, prices, items, money);
    }

    private async Task<List<StayServiceItem>> ItemsOfAsync(Guid serviceId, CancellationToken ct) =>
        await db.StayServiceItems.AsNoTracking().Where(i => i.ServiceId == serviceId).ToListAsync(ct);

    public string SlotMessage(ServiceRefusalCode code, ServiceScope scope, ServiceSlotInput input, ServiceSelection sel) => code switch
    {
        ServiceRefusalCode.DateInPast => ServiceTexts.DateInPast,
        ServiceRefusalCode.BeyondHorizon => ServiceTexts.BeyondHorizon(BusinessClock.TodayBusinessDate(input.TimeZoneId, input.NowUtc, input.BusinessDayStartMinute).AddDays(input.HorizonDays - 1)),
        ServiceRefusalCode.HoursOutOfRange => ServiceTexts.HoursOutOfRange(scope.Service.MinHours, scope.Service.MaxHours),
        ServiceRefusalCode.OutsideStay when input.StayRange is { } stay => ServiceTexts.OutsideStay(LocalLabel(input.TimeZoneId, stay.FromUtc), LocalLabel(input.TimeZoneId, stay.ToUtc)),
        ServiceRefusalCode.TooEarly => ServiceTexts.TooEarly(input.Service.MinLeadMinutes),
        ServiceRefusalCode.NoPriceForHours => ServiceTexts.NoPriceForHours,
        ServiceRefusalCode.SlotTaken => ServiceTexts.SlotTaken,
        _ => ServiceTexts.StartUnavailable,
    };

    private static string LocalLabel(string tz, DateTime utc)
    {
        var local = StayTime.LocalDateTime(tz, utc);
        return $"{StayFormat.Date(DateOnly.FromDateTime(local))} {local:HH':'mm}";
    }

    // ── the guard against the lazily-expired holds (write path, under the lock of the service) ──

    /// <summary>
    /// ARCHITECTURE_CYCLE39.md §39.5.2 — active sessions of the service on the business date whose parent is a hold that has already run out. Returned as (orders, bookings)
    /// so the caller can release them: an order by the same conditional UPDATE as the task, a booking only under a NON-BLOCKING try-lock of its house.
    /// </summary>
    public async Task<(List<Guid> Orders, List<(Guid BookingId, Guid HouseId)> Bookings)> ExpiredHoldsAsync(ServiceScope scope, DateOnly date, CancellationToken ct)
    {
        var tz = scope.Company.TimeZoneId;
        var fromUtc = BusinessClock.ToUtc(tz, date, BusinessDayStart).AddHours(-24);
        var toUtc = BusinessClock.ToUtc(tz, date, BusinessDayStart).AddHours(48);
        var now = clock.UtcNow;
        var orders = await (from s in db.StayServiceSessions.AsNoTracking()
                            join o in db.StayServiceOrders.AsNoTracking() on s.StayServiceOrderId equals o.Id
                            where s.ServiceId == scope.Service.Id && s.ReleasedAtUtc == null && s.StartUtc < toUtc && s.OccupiedUntilUtc > fromUtc
                                  && o.Status == StayBookingStatus.Held && o.HoldExpiresAtUtc <= now
                            select o.Id).ToListAsync(ct);
        var bookings = await (from s in db.StayServiceSessions.AsNoTracking()
                              join b in db.StayBookings.AsNoTracking() on s.StayBookingId equals b.Id
                              where s.ServiceId == scope.Service.Id && s.ReleasedAtUtc == null && s.StartUtc < toUtc && s.OccupiedUntilUtc > fromUtc
                                    && b.Status == StayBookingStatus.Held && b.HoldExpiresAtUtc <= now
                              select new { b.Id, b.HouseId }).ToListAsync(ct);
        return (orders, bookings.Select(b => (b.Id, b.HouseId)).Distinct().ToList());
    }
}
