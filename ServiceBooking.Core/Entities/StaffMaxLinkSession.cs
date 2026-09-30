namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE25.md §497.2 — a one-time staff link session; only SHA-256 of the payload is stored.</summary>
public class StaffMaxLinkSession
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public AppUser? User { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
