namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §448.2, §459.1 — the subscription of the "Заказы" tariff line of an account, 1:1 with it. Kept apart from
/// <see cref="AccountSubscription"/> on purpose: that table is read directly in ~30 places as "THE subscription of the account"
/// and stays the "Записи" line, bit for bit. No row = the free tier of the line (the system free tariff with <c>Line = Orders</c>).
/// </summary>
public class OrdersSubscription
{
    public Guid Id { get; set; }
    public Guid BillingAccountId { get; set; }
    public BillingAccount BillingAccount { get; set; } = null!;

    public Guid? PlanConfigId { get; set; }
    public SubscriptionPlanConfig? PlanConfig { get; set; }

    public DateTime? PaidUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
