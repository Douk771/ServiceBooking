using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>Snapshot of a template's PREVIOUS text every time it changes (US-59 p.8) — lets an owner see
/// what a message used to say, independent of <see cref="NotificationTemplate"/>'s current row.</summary>
public class NotificationTemplateHistory
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }
    public NotificationType Type { get; set; }
    public string PreviousBody { get; set; } = string.Empty;
    public string ChangedByUserId { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;

    // T5-B12 (ARCHITECTURE_CYCLE5.md §44.6, US-69/US-70). NewBody is the text that was actually being
    // confirmed by THIS row's acknowledgement — PreviousBody alone can't answer "what did they confirm
    // responsibility for", since the newest revision has no LATER row whose PreviousBody would show it.
    // Deliberate duplication (NewBody[n] == PreviousBody[n+1]): this is a journal, not a normalized model.
    public string NewBody { get; set; } = string.Empty;
    public string? AcknowledgedByUserId { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public string? WarningVersion { get; set; }
    // Comma-separated, NULL when nothing fired — the AdMarkersHit companion to ConsentRecord's
    // "confirmedDespiteMarkers" evidence (§51.2): proves what the server actually detected, not just
    // that the owner clicked "confirm".
    public string? AdMarkersHit { get; set; }
}
