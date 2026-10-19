using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Billing;

/// <summary>API_CONTRACT_CYCLE43.md §43.20 — the <c>{transport}</c> path segment: <c>WhatsApp</c> or <c>Max</c> by name, any case. Numbers and
/// unknown names are not a transport (the route answers 404).</summary>
public static class MessengerTransportRoute
{
    public static bool TryParse(string? value, out NotificationTransport transport)
    {
        transport = default;
        if (string.IsNullOrWhiteSpace(value) || !Enum.TryParse(value.Trim(), ignoreCase: true, out NotificationTransport parsed) ||
            !Enum.IsDefined(parsed) || !string.Equals(Enum.GetName(parsed), value.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        transport = parsed;
        return true;
    }
}
