using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.2.4 — the ONLY place of occupancy of a house. Written only by HouseOccupancyWriter.
/// PostgreSQL EXCLUDE constraint EX_HouseOccupancies_NoOverlap forbids two unreleased periods with a common night.
/// </summary>
public class HouseOccupancy
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid HouseId { get; set; }
    public DateOnly StartDate { get; set; }
    /// <summary>The check-out date: the period is [StartDate, EndDate).</summary>
    public DateOnly EndDate { get; set; }
    public OccupancySource Source { get; set; }
    public Guid? StayBookingId { get; set; }
    public Guid? HouseBlockId { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
