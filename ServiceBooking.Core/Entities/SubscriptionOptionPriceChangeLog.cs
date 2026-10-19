namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE43.md §43.2.1 — append-only journal of changes to the price or activity of a catalog option.
/// Written only through OptionPriceLog.Write. <see cref="Source"/> holds an OptionPriceChangeSource value.</summary>
public class SubscriptionOptionPriceChangeLog
{
    public Guid Id { get; set; }

    public Guid OptionId { get; set; }
    public SubscriptionOption Option { get; set; } = null!;

    // Snapshot of the option code at the moment of the change.
    public string OptionCode { get; set; } = string.Empty;

    // null = "price not set".
    public decimal? OldPricePerMonth { get; set; }
    public decimal? NewPricePerMonth { get; set; }
    public bool OldIsActive { get; set; }
    public bool NewIsActive { get; set; }

    public int Source { get; set; }

    // Snapshot of accounts with a currently valid PAID (not trial) option row; null for non-messenger options.
    public int? ActivePaidAccounts { get; set; }

    // No FK: like other "who" columns in journals, the value outlives the user.
    public string? ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    // Required on the messenger price screen, may be null on the legacy options API path.
    public string? Comment { get; set; }
}
