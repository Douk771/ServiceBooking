using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE37.md §37.2.5 — an owner's block of dates; [StartDate, EndDate) with EndDate = the check-out date.</summary>
public class HouseBlock
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid HouseId { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public HouseBlockKind Kind { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
}

public class HouseBlockEvent
{
    public Guid Id { get; set; }
    public Guid HouseBlockId { get; set; }
    public Guid CompanyId { get; set; }
    public HouseBlockEventKind Kind { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string ActorUserId { get; set; } = string.Empty;
    public string ActorNameSnapshot { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
}
