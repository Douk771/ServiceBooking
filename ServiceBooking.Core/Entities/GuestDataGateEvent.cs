using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE20.md §406.2 (US-20-05, Т20-06) — a permanent, one-year record that TD-03's
/// guest-data gate fired, replacing the ~14-day-lived application log line that used to be the only
/// trace. Deliberately minimal: no IP, no User-Agent, no phone in any form, no counts, no company id
/// (LEGAL_REVIEW_CYCLE16.md §6.4) — the schema itself is what enforces that, not application discipline.
/// Written exclusively by <c>Services.Subjects.GuestDataGateJournal</c> — the single writer named in
/// §406.2, replacing the four inline "guest-data gate applied" log calls in ProfileController.
/// </summary>
public class GuestDataGateEvent
{
    public long Id { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    // No FK on purpose (same rule as ConsentRecord.RecordedByUserId): the gate can fire ON the very
    // account-deletion request that then removes this user, and the journal must outlive the account.
    public string UserId { get; set; } = string.Empty;

    public GuestDataGateOperation Operation { get; set; }
    public GuestDataGateOutcome Outcome { get; set; } = GuestDataGateOutcome.Applied;

    // Correlates a journal row with the request's trace id for incident investigation — never anything
    // that identifies the subject's phone or the data that was hidden.
    public string? TraceId { get; set; }
}
