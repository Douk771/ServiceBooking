namespace ServiceBooking.Core.Entities;
public class SubscriptionPlanConfig
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }
    public int? MaxEmployees { get; set; }
    public int? MaxCompanies { get; set; }
    public bool AllowOnlineBooking { get; set; } = true;
    public bool AllowMailing { get; set; } = false;
    public bool AllowAnalytics { get; set; } = false;
    // Whether companies on this plan may appear in the public directory (GET /api/companies) at all —
    // defaults to true so the existing baseline Free plan doesn't suddenly de-list every unsubscribed
    // company. Still gated per-company by Company.ShowInPublicListing (the owner's own opt-out).
    public bool AllowPublicListing { get; set; } = true;
    // Whether the company may require online prepayment for self-booked visits (Company.RequirePrepayment).
    // A paid-tier feature like AllowMailing/AllowAnalytics — off on the Free baseline.
    public bool AllowOnlinePayment { get; set; } = false;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int NotifyDaysBefore { get; set; } = 7;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
