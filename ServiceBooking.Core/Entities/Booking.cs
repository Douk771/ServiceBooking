using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

public class Booking
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ServiceId { get; set; }
    public string MasterId { get; set; } = string.Empty;
    public string? ClientId { get; set; }

    // For guest bookings
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public string? GuestEmail { get; set; }

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    // Snapshot of Service.Price at the moment the booking was created, so that later price changes
    // on the service don't retroactively change historical revenue/commission figures.
    public decimal Price { get; set; }

    // Snapshot of CompanyMember.CommissionPercent (for MasterId at CompanyId) at the moment the booking
    // was created, for the same reason Price is a snapshot: a later commission-rate change, or the
    // master's membership being removed entirely, must not retroactively change a closed period's
    // report. Without this, GetValueOrDefault against the CURRENT CompanyMembers table silently turns a
    // departed master's historical commission into 0%.
    public decimal CommissionPercent { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.NotRequired;
    public string? Notes { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Consent snapshot, US-37 (ARCHITECTURE.md §5.2). Filled by the SERVER, from the legal documents
    // snapshot in effect at creation time, ONLY on the guest booking path — never from the request body,
    // and never rewritten after creation. Staff manual bookings and authenticated-client bookings leave
    // these null: staff collected consent outside the product, and an authenticated client's consent
    // already lives in the ConsentRecord journal (ARCHITECTURE_CYCLE5.md §44.2, formerly UserConsent).
    // ConsentTermsVersion keeps its cycle-3 name on purpose (ARCHITECTURE_CYCLE5.md §44.5): it now holds
    // the TermsClient version, exactly what it always held — renaming the column would only cost exports
    // and admin queries for zero benefit.
    public string? ConsentPrivacyVersion { get; set; }
    public string? ConsentTermsVersion { get; set; }
    public DateTime? ConsentAcceptedAtUtc { get; set; }

    // Set by account deletion (US-39, ARCHITECTURE.md §5.2/§7.4) when this booking's ClientId is
    // anonymized — distinguishes "obscured on purpose" from a data-integrity bug (ClientId null with an
    // empty GuestName would otherwise look identical to corruption).
    public bool ClientDeleted { get; set; }

    // US-78, US-65 п. 7 (ARCHITECTURE_CYCLE5.md §44.5). BookedForOther/GuardianConfirmed* apply only to
    // the client/guest/embed self-booking paths (never a staff manual booking, §46 API_CONTRACT_CYCLE5.md) —
    // "false"/nulls is the default and requires no confirmation, matching today's behavior exactly.
    // BookingNoticeVersion is filled unconditionally by the server, from the snapshot in effect at
    // creation time — the ст. 18 notice (D5) is shown on every booking form, staff included, so it is
    // recorded on every booking, unlike the guest-only ConsentPrivacyVersion/ConsentTermsVersion pair above.
    public bool BookedForOther { get; set; }
    public DateTime? GuardianConfirmedAtUtc { get; set; }
    public string? GuardianConfirmationVersion { get; set; }
    public string? BookingNoticeVersion { get; set; }

    public Company Company { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public AppUser Master { get; set; } = null!;
    public AppUser? Client { get; set; }
}
