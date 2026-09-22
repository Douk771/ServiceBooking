namespace ServiceBooking.API.Services.Billing;

/// <summary>Cycle 7, stage 5 (ARCHITECTURE_CYCLE7.md §44.2, US-66, US-76) — the known capability keys
/// an admin can pick for an option in the catalog editor. Free-text entry of an unknown key stays
/// allowed on purpose (§44.2) — this list only drives the dropdown and the
/// <c>AdminOptionDto.capabilityKnown</c> warning flag, it is not a foreign key.</summary>
public static class OptionCapabilityCatalog
{
    public sealed record Capability(string Key, string Kind, string Name);

    // notifications.whatsapp is Numeric (Р6: the unit of payment is "how many numbers", not a flag).
    public static readonly IReadOnlyList<Capability> Known =
    [
        new(CapabilityKeys.NotificationsWhatsApp, "Numeric", "Номера для рассылок WhatsApp"),
        new(CapabilityKeys.Companies, "Numeric", "Дополнительные компании"),
        new(CapabilityKeys.Employees, "Numeric", "Дополнительные сотрудники"),
        new("analytics", "Boolean", "Аналитика"),
        new("online-payment", "Boolean", "Онлайн-оплата"),
    ];

    public static bool IsKnown(string? key) => key is not null && Known.Any(c => c.Key == key);
}
