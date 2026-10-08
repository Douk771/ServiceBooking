using ServiceBooking.API.DTOs.Stays;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// API_CONTRACT_CYCLE39.md §39.22.4 — the quote of ONE choice of time as the contract's DTO. Pure: the same builder serves the public quote, the quote by a booking link, the
/// staff's quote and the 409 <c>PriceChanged</c> (which carries the new quote). A problem is not an error (always 200): the amounts are 0 when the evaluation could not price the time.
/// </summary>
public static class ServiceQuoteBuilder
{
    /// <param name="prepayPercent">The percent of a stand-alone order (null — none, and always null for a session added to a booking: it is paid on site).</param>
    public static ServiceQuoteDto Build(
        ServiceScope scope, ServiceEvaluation e, int? prepayPercent, GateResult gate, int holdMinutes)
    {
        var svc = scope.Service;
        var tz = scope.Company.TimeZoneId;
        var timeShown = e.Hours is >= 1 and <= 12 && e.StartMinute is >= 0 and <= 2880;
        var time = timeShown
            ? new ServiceTimeDto(e.BusinessDate, e.StartMinute, e.StartMinute + 60 * e.Hours, e.Hours, e.StartUtc, e.EndUtc, ServiceTimeFormat.Guest(e.BusinessDate, e.StartMinute, e.Hours))
            : null;
        var hourPrices = e.HourPrices is null ? [] : HourPriceDtos(e.BusinessDate, e.StartMinute, e.HourPrices);
        var m = e.Money;
        var lines = m is null ? [] : Lines(svc.Name, e.Hours, m.ServiceAmountRub, e.Items);
        var withPrepay = prepayPercent is > 0;
        var summary = withPrepay ? ServiceTexts.CancellationSummary(svc.CancellationPolicy, svc.CancellationBoundaryHours) : null;
        _ = tz;
        return new ServiceQuoteDto(
            e.Ok, e.Problems.Select(p => new ServiceProblemDto(p.Code, p.Message)).ToList(), time, hourPrices, lines,
            m?.ServiceAmountRub ?? 0, m?.ItemsAmountRub ?? 0, m?.TotalRub ?? 0, withPrepay ? prepayPercent : null, withPrepay ? m?.PrepayRub ?? 0 : 0,
            m?.DueOnSiteRub ?? 0, withPrepay ? holdMinutes : null, summary, withPrepay ? null : ServiceTexts.PayOnSite, gate.Accepting,
            gate.Accepting ? null : ServiceTexts.NotAcceptingGuest);
    }

    public static List<HourPriceDto> HourPriceDtos(DateOnly businessDate, int startMinute, IReadOnlyList<int> prices) =>
        prices.Select((p, k) => new HourPriceDto(startMinute + 60 * k, ServiceTimeFormat.GuestMoment(businessDate, startMinute + 60 * k), p)).ToList();

    public static List<ServiceChargeLineDto> Lines(string serviceName, int hours, int serviceAmountRub, IEnumerable<ResolvedItem> items)
    {
        var lines = new List<ServiceChargeLineDto>
        {
            new(ServiceChargeKind.Service, $"{serviceName} · {hours} ч", 1, serviceAmountRub, serviceAmountRub)
        };
        lines.AddRange(items.Where(i => i.Quantity > 0).Select(i =>
            new ServiceChargeLineDto(ServiceChargeKind.Item, $"{i.Name} × {i.Quantity}", i.Quantity, i.UnitPriceRub, i.UnitPriceRub * i.Quantity)));
        return lines;
    }
}
