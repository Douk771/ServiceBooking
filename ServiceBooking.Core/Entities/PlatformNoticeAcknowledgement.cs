namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §404.1 (US-20-03) — one "I've read it" click, append-only. Idempotent by the
/// unique (NoticeId, UserId) index configured in AppDbContext: a repeat click must not create a second
/// row or move AcknowledgedAtUtc.
/// </summary>
public class PlatformNoticeAcknowledgement
{
    public Guid Id { get; set; }

    public Guid NoticeId { get; set; }
    public PlatformNotice Notice { get; set; } = null!;

    // No FK on purpose — same reasoning as ConsentRecord/GuestDataGateEvent: this row is evidence that a
    // specific account was notified and must survive that account's deletion up to the retention limit.
    public string UserId { get; set; } = string.Empty;

    // The billing account the acknowledgement was made "as" — only meaningful for owner-facing kinds
    // (AllOwners/OwnersOnPlans/BillingAccount); null for AllClients.
    public Guid? BillingAccountId { get; set; }

    public DateTime AcknowledgedAtUtc { get; set; } = DateTime.UtcNow;
}
