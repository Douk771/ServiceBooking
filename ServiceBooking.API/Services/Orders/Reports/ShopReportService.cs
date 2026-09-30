using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Reports;
using ServiceBooking.API.Services.Shops;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Orders.Reports;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §500–§502, API_CONTRACT_CYCLE25.md §522, §526–§527 — validates a history / summary request, resolves the period from the
/// shop's WORKING day (T-25-04) and assembles the answer. The queries are <see cref="OrderReportQueries"/>; every text is built here or in the pure
/// helpers, never by the frontend. A validation failure is returned as the Russian 400 text.
/// </summary>
public sealed class ShopReportService(
    AppDbContext db, OrderReportQueries queries, ShopGateLoader gates, ServiceBooking.API.Services.Notifications.INotificationClock clock,
    IConfiguration configuration)
{
    public const string AmountNegativeText = "Сумма не может быть отрицательной";
    public const string AmountRangeText = "Сумма «от» больше суммы «до»";
    public const string NumberRangeText = "Номер заказа — число от 1 до 9999";
    public const string PageText = "Номер страницы — от 1";
    public const string UnknownSortText = "Неизвестный вид сортировки";
    public const string EmptyPeriodText = "За выбранный период заказов нет";
    public const string EmptyFilteredText = "По этим условиям заказов нет";

    public int HistoryPageSize => Math.Max(1, configuration.GetValue("Orders:HistoryPageSize", 50));

    /// <summary>The pickup context of the shop — its zone and current working day (T-25-04).</summary>
    public async Task<OrderPickupContext> ContextAsync(Company shop, CancellationToken ct)
    {
        var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.CompanyId == shop.Id, ct) ?? new ShopSettings { CompanyId = shop.Id };
        return await gates.PickupContextAsync(shop, settings, clock.UtcNow, ct);
    }

    public static bool TryResolvePeriod(
        ReportPeriodPreset? preset, DateOnly? from, DateOnly? to, ReportPeriodPreset defaultPreset, DateOnly workingDay,
        out ReportPeriod period, out string? error)
    {
        if (preset is { } p && !Enum.IsDefined(p)) { period = null!; error = ReportPeriod.CustomBoundsRequired; return false; }
        return ReportPeriod.TryResolve(preset ?? defaultPreset, from, to, workingDay, out period, out error);
    }

    // ── history ─────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<(string? Error, OrderHistoryPageDto? Page)> HistoryAsync(Company shop, OrderHistoryQuery query, CancellationToken ct)
    {
        var context = await ContextAsync(shop, ct);
        if (!TryResolvePeriod(query.Period, query.From, query.To, ReportPeriodPreset.Last7Days, context.WorkingDay, out var period, out var error))
            return (error, null);

        var customerText = query.Customer is { Length: > CustomerSearchTerm.MaxLength } long_ ? long_[..CustomerSearchTerm.MaxLength] : query.Customer;
        var customer = CustomerSearchTerm.Parse(customerText);
        if (customer.Kind == CustomerSearchKind.TooShort) return (CustomerSearchTerm.TooShortText, null);
        if (query.AmountFrom is < 0 || query.AmountTo is < 0) return (AmountNegativeText, null);
        if (query.AmountFrom is { } af && query.AmountTo is { } at && af > at) return (AmountRangeText, null);
        if (query.Number is < 1 or > 9999) return (NumberRangeText, null);
        var page = query.Page ?? 1;
        if (page < 1) return (PageText, null);
        var statuses = (query.Statuses ?? []).Where(Enum.IsDefined).Distinct().ToList();
        var sort = query.Sort is { } s && Enum.IsDefined(s) ? s : OrderHistorySort.PickupDesc;

        var criteria = new HistoryCriteria(shop.Id, period, statuses, customer, query.AmountFrom, query.AmountTo, query.Number, sort, page, HistoryPageSize);
        var result = await queries.HistoryAsync(criteria, ct);

        var hasFilters = statuses.Count > 0 || customer.Kind != CustomerSearchKind.None || query.AmountFrom is not null || query.AmountTo is not null ||
                         query.Number is not null;
        return (null, new OrderHistoryPageDto(
            ReportPeriodDto.Of(period), result.Rows.Select(r => ToRowDto(r, context.Zone, includePhone: true)).ToList(), page, criteria.PageSize,
            result.TotalCount, result.IssuedCount, result.IssuedAmount, SummaryText(result.TotalCount, result.IssuedAmount),
            result.TotalCount == 0 ? (hasFilters ? EmptyFilteredText : EmptyPeriodText) : null));
    }

    /// <summary>"Найдено 128 заказов, выдано на 54 300 ₽".</summary>
    public static string SummaryText(int totalCount, decimal issuedAmount) =>
        $"Найдено {totalCount} {ShopTimeTexts.Plural(totalCount, "заказ", "заказа", "заказов")}, выдано на {OrderTexts.Money(issuedAmount)}";

    /// <summary>The row of the list: the phone only masked, and nothing personal at all for an erased order.</summary>
    public static OrderHistoryRowDto ToRowDto(HistoryRow r, TimeZoneInfo zone, bool includePhone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(r.PickupStartUtc.Kind == DateTimeKind.Utc ? r.PickupStartUtc : DateTime.SpecifyKind(r.PickupStartUtc, DateTimeKind.Utc), zone);
        var clockText = ShopTimeTexts.Clock(local.Hour * 60 + local.Minute);
        var pickupText = $"{ShopTimeTexts.DayMonth(r.PickupDate)}, {(r.PickupKind == PickupKind.Asap ? "≈ " : "к ")}{clockText}";
        var erased = r.PersonalDataErased;
        return new OrderHistoryRowDto(
            r.OrderId, r.Number, r.PickupDate, DateTime.SpecifyKind(r.PickupStartUtc, DateTimeKind.Utc), pickupText, r.Status, OrderTexts.StatusText(r.Status),
            erased ? null : r.CustomerName,
            erased || !includePhone || string.IsNullOrEmpty(r.CustomerPhone) ? null : OrderPhoneMask.Mask(r.CustomerPhone),
            r.ItemCount, r.Total, r.HasWeightItems && r.Status != OrderStatus.Issued, erased);
    }

    // ── summary ─────────────────────────────────────────────────────────────────────────────────────────────

    public async Task<(string? Error, ShopSummaryDto? Summary)> SummaryAsync(
        Company shop, ReportPeriodPreset? preset, DateOnly? from, DateOnly? to, SummaryTopSort? topSort, bool compare, CancellationToken ct)
    {
        if (topSort is { } t && !Enum.IsDefined(t)) return (UnknownSortText, null);
        var context = await ContextAsync(shop, ct);
        if (!TryResolvePeriod(preset, from, to, ReportPeriodPreset.Today, context.WorkingDay, out var period, out var error)) return (error, null);
        var sort = topSort ?? SummaryTopSort.Amount;

        var aggregates = await queries.StatusAggregatesAsync(queries.InPeriod(shop.Id, period.From, period.To), ct);
        var numbers = Numbers.Of(aggregates);
        var top = await queries.TopProductsAsync(shop.Id, period, sort, 10, ct);

        SummaryShareDto Share(OrderStatus status, string label)
        {
            var count = aggregates.FirstOrDefault(a => a.Status == status)?.Count ?? 0;
            var (share, text) = ShopSummaryMath.Share(count, numbers.Terminal);
            return new SummaryShareDto(status, label, count, share, text);
        }

        var average = ShopSummaryMath.AverageCheck(numbers.IssuedAmount, numbers.IssuedCount);
        List<SummaryDayDto>? days = null;
        if (period.Days > 1)
        {
            var perDay = (await queries.DaysAsync(shop.Id, period, ct)).ToDictionary(d => d.Date);
            days = Enumerable.Range(0, period.Days).Select(i => period.From.AddDays(i)).Select(d =>
            {
                var a = perDay.GetValueOrDefault(d);
                return new SummaryDayDto(d, ShopTimeTexts.DateShort(d), a?.Orders ?? 0, a?.IssuedCount ?? 0, a?.IssuedAmount ?? 0m);
            }).ToList();
        }

        SummaryComparisonDto? previous = null;
        if (compare)
        {
            var prevPeriod = period.Previous();
            var prev = Numbers.Of(await queries.StatusAggregatesAsync(queries.InPeriod(shop.Id, prevPeriod.From, prevPeriod.To), ct));
            var prevAverage = ShopSummaryMath.AverageCheck(prev.IssuedAmount, prev.IssuedCount);
            previous = new SummaryComparisonDto(
                ReportPeriodDto.Of(prevPeriod), prev.Total, prev.IssuedCount, prev.IssuedAmount, prevAverage,
                ShopSummaryMath.Delta(numbers.Total, prev.Total), ShopSummaryMath.Delta(numbers.IssuedCount, prev.IssuedCount),
                ShopSummaryMath.Delta(numbers.IssuedAmount, prev.IssuedAmount),
                average is { } a1 && prevAverage is { } a2 ? ShopSummaryMath.Delta(a1, a2) : ShopSummaryMath.NoValueText);
        }

        return (null, new ShopSummaryDto(
            ReportPeriodDto.Of(period), numbers.Total, numbers.InProgress, ShopSummaryMath.InProgressText(numbers.InProgress), numbers.IssuedCount,
            numbers.IssuedAmount, ShopSummaryMath.PaymentNote, average, ShopSummaryMath.AverageCheckText(average), numbers.Terminal,
            [Share(OrderStatus.Rejected, "Отклонён магазином"), Share(OrderStatus.CancelledByShop, "Отменён магазином"),
             Share(OrderStatus.CancelledByCustomer, "Отменён покупателем")],
            Share(OrderStatus.NotPickedUp, "Не забран"),
            new SummaryTopDto(sort, top.Select(r => new SummaryTopItemDto(
                r.ProductId, r.Name, r.Unit, r.Quantity, OrderTexts.QuantityCompact(r.Unit, r.Quantity), r.Amount, r.IsDeleted)).ToList()),
            days, previous));
    }

    /// <summary>The counts of the summary, derived from ONE status aggregate.</summary>
    private sealed record Numbers(int Total, int InProgress, int IssuedCount, decimal IssuedAmount, int Terminal)
    {
        public static Numbers Of(IReadOnlyList<StatusAggregate> aggregates)
        {
            int Count(params OrderStatus[] statuses) => aggregates.Where(a => statuses.Contains(a.Status)).Sum(a => a.Count);
            var total = aggregates.Sum(a => a.Count);
            var inProgress = Count(OrderStatus.New, OrderStatus.Accepted, OrderStatus.Ready);
            var issued = aggregates.FirstOrDefault(a => a.Status == OrderStatus.Issued);
            return new Numbers(total, inProgress, issued?.Count ?? 0, issued?.Amount ?? 0m, total - inProgress);
        }
    }
}
