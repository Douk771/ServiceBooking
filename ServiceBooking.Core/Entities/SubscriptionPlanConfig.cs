using ServiceBooking.Core.Enums;

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
    // Whether accounts on this plan may buy the WhatsApp notification channel option (cycle 4, Q1: off
    // by default on every plan including new ones — a superadmin turns it on per plan deliberately, see
    // ARCHITECTURE_CYCLE4.md §33 and §38.5 p.1).
    public bool AllowNotificationChannel { get; set; } = false;
    // Client-note photo storage cap in megabytes; null = unlimited (US-24). Enforced against the sum of
    // ClientNotePhoto.SizeBytes for the company, not against this field's unit directly — see
    // PhotoQuota and ClientNotePhotosController.
    public int? PhotoQuotaMb { get; set; } = 100;
    // How long client-note photos survive before the background cleanup task (US-21) removes them.
    public PhotoRetention PhotoRetention { get; set; } = PhotoRetention.SixMonths;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public int NotifyDaysBefore { get; set; } = 7;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Cycle 7 additions (ARCHITECTURE_CYCLE7.md §43.4) for the public/admin pricing screen (§48).
    // Newline-separated bullet points shown on the public price card; PricingCatalogCache splits,
    // trims and caps this at 5 lines to build PublicPlanDto.highlights.
    public string? Highlights { get; set; }
    // Whether this plan may appear on the public price list (GET /api/pricing) — independent of
    // IsActive, which controls whether it can still be assigned to an account at all.
    public bool IsPublic { get; set; }
    public int SortOrder { get; set; }
    // The single system free tariff (П4/§64 п.5): exactly one row may have this set (partial unique
    // index in AppDbContext), its PricePerMonth must be 0, and it can be neither deleted nor
    // deactivated. Enforcement lives in AdminController — ValidateSystemFreeAsync/SetSystemFree plus
    // the partial unique index below — not "not implemented in this slice" (that was true through
    // cycle 6 only; corrected cycle 18, ARCHITECTURE_CYCLE18.md §332.1, CURRENT_STATE.md §0.2 п.6).
    public bool IsSystemFree { get; set; }

    // Cycle 18 (ARCHITECTURE_CYCLE18.md §332.1, US-18-01/US-18-02): the second system tariff —
    // "пробный период" (trial). Exactly one row may have this set (partial unique index below);
    // PricePerMonth must be 0; can be neither deleted nor deactivated while it has live subscribers;
    // can never coincide with IsSystemFree on the same row. Changed ONLY by
    // PUT /api/admin/plans/{id}/system-trial — this field is deliberately absent from AdminPlanInput,
    // same convention as IsSystemFree.
    public bool IsSystemTrial { get; set; }
}
