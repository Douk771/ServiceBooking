namespace ServiceBooking.API.Services.Stays;

public enum NotAcceptingReason { CompanyBlocked, NoPlan, OverHouseLimit, NoPaymentDetails, NoProviderInfo, HouseNotPublished }

public sealed record StayProviderFacts(Core.Enums.StayProviderStatus? Status, string? Name, string? Inn, string? Ogrn, string? ClaimsAddress);

public readonly record struct GateResult(bool Accepting, NotAcceptingReason? ReasonCode, string? ReasonText)
{
    public static GateResult Ok => new(true, null, null);
}

/// <summary>ARCHITECTURE_CYCLE37.md §37.10.3 — "does the company accept bookings". One pure function, not AllowOnlineBooking.</summary>
public static class StaysBookingGate
{
    public static GateResult Evaluate(
        bool companyActive, bool hasActivePlan, int accountPublishedHouses, int? maxHouses,
        int prepayPercent, string? paymentDetails, StayProviderFacts provider)
    {
        if (!companyActive) return Fail(NotAcceptingReason.CompanyBlocked, "Компания заблокирована администратором");
        if (!hasActivePlan) return Fail(NotAcceptingReason.NoPlan, "Выберите тариф, чтобы принимать брони: пробный период закончился или тариф не выбран");
        if (maxHouses is { } max && accountPublishedHouses > max)
            return Fail(NotAcceptingReason.OverHouseLimit, $"Опубликовано {accountPublishedHouses} домов при лимите {max}: гости не могут бронировать. Снимите лишние дома с публикации или смените тариф");
        if (prepayPercent > 0 && string.IsNullOrWhiteSpace(paymentDetails))
            return Fail(NotAcceptingReason.NoPaymentDetails, "Заполните реквизиты для оплаты — без них гости не могут бронировать с предоплатой");
        // ЮР39-2 (ARCHITECTURE_CYCLE39.md §39.7.6): the executor's details are mandatory ALWAYS, whatever the prepayment (closes §37.19 п. 5).
        if (!ProviderComplete(provider))
            return Fail(NotAcceptingReason.NoProviderInfo, "Заполните сведения об исполнителе — без них гости не могут бронировать");
        return GateResult.Ok;
    }

    public static bool ProviderComplete(StayProviderFacts p)
    {
        if (p.Status is null || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Inn) || string.IsNullOrWhiteSpace(p.ClaimsAddress)) return false;
        var needsOgrn = p.Status is Core.Enums.StayProviderStatus.Organization or Core.Enums.StayProviderStatus.IndividualEntrepreneur;
        return !needsOgrn || !string.IsNullOrWhiteSpace(p.Ogrn);
    }

    private static GateResult Fail(NotAcceptingReason r, string text) => new(false, r, text);
}
