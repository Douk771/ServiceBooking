namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE40.md §40.2.3 — append-only journal of changes to the two channel options of a billing account.
/// Written only through ChannelOptionLog.Write. <see cref="Source"/> holds a ChannelOptionChangeSource value.</summary>
public class ChannelOptionChangeLog
{
    public Guid Id { get; set; }

    public Guid BillingAccountId { get; set; }
    public BillingAccount BillingAccount { get; set; } = null!;

    public string OptionCode { get; set; } = string.Empty;
    public int Source { get; set; }

    public DateTime? OldPaidUntilUtc { get; set; }
    public DateTime? NewPaidUntilUtc { get; set; }
    public DateTime? OldEndsAtUtc { get; set; }
    public DateTime? NewEndsAtUtc { get; set; }

    // No FK: like other "who" columns in journals, the value outlives the user.
    public string? ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    // No FK: the channel may be deleted, the record stays.
    public Guid? ChannelId { get; set; }
    public string? Comment { get; set; }
}
