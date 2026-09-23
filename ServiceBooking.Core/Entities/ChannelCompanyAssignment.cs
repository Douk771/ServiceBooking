using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// Assigns a company to a channel (US-61). A company may be assigned to at most one channel PER
/// TRANSPORT — enforced by a unique index on (<see cref="CompanyId"/>, <see cref="Transport"/>)
/// (ARCHITECTURE_CYCLE9.md §104.3, widened from the cycle-4 "one channel at all" rule
/// (ARCHITECTURE_CYCLE4.md §23.1) — not by application-level checking, so the rule holds even under
/// concurrent requests.
/// </summary>
public class ChannelCompanyAssignment
{
    public Guid Id { get; set; }

    public Guid ChannelId { get; set; }
    public NotificationChannel Channel { get; set; } = null!;

    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    // ARCHITECTURE_CYCLE9.md §104.3 — a DENORMALIZED copy of Channel.Transport, pinned to the channel by
    // a composite FK on (ChannelId, BillingAccountId, Transport) -> NotificationChannels
    // (Id, BillingAccountId, Transport) (see AppDbContext). NotificationChannel.Transport never changes
    // after a channel is created (the transport is chosen at request time), so this copy cannot go
    // stale — and the composite FK makes it physically impossible to write anything else here. This is
    // what turns "a company may be assigned to at most one channel of each transport" into a schema
    // property (unique index on (CompanyId, Transport)) instead of a check-then-act in the controller.
    public NotificationTransport Transport { get; set; } = NotificationTransport.WhatsApp;

    // Cycle 7, stage 6 (ARCHITECTURE_CYCLE7.md §43.6) — the co-tenancy column: this assignment is
    // only valid while the company and the channel share this same billing account. Backed by two
    // composite FKs (see AppDbContext), which is what makes "assign a company to a number belonging
    // to a different account" physically impossible, and what forces CompanyTransferService to drop
    // the assignment before it can move the company to a different account.
    public Guid BillingAccountId { get; set; }

    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
    public string AssignedByUserId { get; set; } = string.Empty;
}
