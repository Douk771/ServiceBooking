namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE37.md §37.2.6 — the "Дома" tariff of a billing account. No row = no plan (there is no free tier, Q2).</summary>
public class StaysSubscription
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

/// <summary>Seed ids of the "Дома" tariff line (§37.10.1).</summary>
public static class StaysPlans
{
    public static readonly Guid OneHouseSeedId = new("0c37f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b01");
    public static readonly Guid UpToThreeSeedId = new("0c37f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b02");
    public static readonly Guid UnlimitedSeedId = new("0c37f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b03");
    public static readonly Guid TrialSeedId = new("0c37f0e5-6a3d-4a5e-9b1f-2d4c7e8a9b04");
}

/// <summary>ARCHITECTURE_CYCLE42.md §42.2.2 — the «Бани» tariff of a billing account, same shape as <see cref="StaysSubscription"/>. No row = no plan.</summary>
public class BathsSubscription
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

/// <summary>Seed ids of the «Бани» tariff line (§42.2.5).</summary>
public static class BathsPlans
{
    public static readonly Guid OneBathSeedId = new("0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b01");
    public static readonly Guid UpToThreeSeedId = new("0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b02");
    public static readonly Guid UnlimitedSeedId = new("0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b03");
    public static readonly Guid TrialSeedId = new("0c42ba70-6a3d-4a5e-9b1f-2d4c7e8a9b04");
}
