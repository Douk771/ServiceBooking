using ServiceBooking.API.Services.Orders;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §392.3 — "does this shop take orders right now" as ONE pure function. Cycle 1: the
/// company is a shop and is active (an administrator's block makes the shop unavailable). Deliberately NOT
/// AllowOnlineBooking — there are no shop tariffs in cycle 1. Cycle 2 adds working hours, pause and the tariff
/// capability HERE; the storefront, quote, order creation and the manage DTO already read only this result.
/// </summary>
public static class ShopOrderingGate
{
    public sealed record Result(bool Accepting, string? ReasonText);

    public static Result Evaluate(Company company, ShopSettings? settings, DateTime nowUtc)
    {
        if (company.Kind != CompanyKind.Orders || !company.IsActive)
            return new Result(false, OrderTexts.ShopNotAvailable);
        return new Result(true, null);
    }
}
