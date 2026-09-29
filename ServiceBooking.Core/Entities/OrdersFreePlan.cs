namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §448.3 — the fixed id of the system free tariff of the "Заказы" line, seeded by the cycle-24 migration.
/// The administrator edits its numbers in the admin panel without a deploy; the id is what code and the migration agree on.
/// </summary>
public static class OrdersFreePlan
{
    public static readonly Guid SeedId = new("0c24f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b01");
    public const string Name = "Заказы · Бесплатно";
}
