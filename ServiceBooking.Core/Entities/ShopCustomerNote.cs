namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE25.md §497.2 — a shop's private note about a customer, keyed by canonical phone.</summary>
public class ShopCustomerNote
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    /// <summary>Canonical phone (PhoneNormalizer), same form as Orders.CustomerPhone.</summary>
    public string Phone { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>No FK on purpose (like ShopSettings.AcceptanceChangedByUserId).</summary>
    public string? UpdatedByUserId { get; set; }
    public string UpdatedByName { get; set; } = string.Empty;
}
