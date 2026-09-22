namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE7.md §44.2 — the capability-key literals shared between the raw-SQL
/// <c>SeedBillingCatalog</c> migration and <see cref="SubscriptionResolver"/>'s option arithmetic
/// (§44.3 п.6). A prior pass seeded the catalog with the *option code* prefix ("extra-companies",
/// "extra-employees") as the capability key, because that is what the resolver happened to read —
/// backwards from §44.2/§54.3, where the prefix "extra-" belongs to <c>SubscriptionOption.Code</c>
/// (an option's own identity in the catalog), while the capability itself is named "companies"/
/// "employees" (the same axis the plan's own base limit uses). Centralizing the three literals here
/// means a future rename is a compile error at every call site instead of a silently orphaned string
/// in either the migration or the resolver.
/// </summary>
public static class CapabilityKeys
{
    public const string Employees = "employees";
    public const string Companies = "companies";

    /// <summary>Same literal as <see cref="SubscriptionResolver.WhatsAppOptionCode"/> — kept as its
    /// own constant here because §44.2 lists it as a capability key in its own right, not because the
    /// value differs.</summary>
    public const string NotificationsWhatsApp = SubscriptionResolver.WhatsAppOptionCode;
}
