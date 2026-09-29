namespace ServiceBooking.API.Services.Shops;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §404 L2 — which seller-detail fields are obligatory. Cycle 1: none (legal-counsel has not
/// concluded), so every shop is "complete". The conclusion changes THIS class (and adds the "fill in the seller
/// details" refusal to ShopOrderingGate); the contract shape (<c>requiredFields</c>, <c>isComplete</c>) stays.
/// </summary>
public static class SellerInfoRequirements
{
    public static IReadOnlyList<string> RequiredFields() => [];

    public static bool IsComplete(
        string? legalForm, string? legalName, string? inn, string? ogrn, string? legalAddress)
    {
        var filled = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["legalForm"] = !string.IsNullOrWhiteSpace(legalForm),
            ["legalName"] = !string.IsNullOrWhiteSpace(legalName),
            ["inn"] = !string.IsNullOrWhiteSpace(inn),
            ["ogrn"] = !string.IsNullOrWhiteSpace(ogrn),
            ["legalAddress"] = !string.IsNullOrWhiteSpace(legalAddress),
        };
        return RequiredFields().All(f => filled.GetValueOrDefault(f));
    }
}
