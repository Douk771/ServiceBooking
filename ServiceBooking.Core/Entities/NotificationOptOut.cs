using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// One table by canonical phone number for the whole platform (ARCHITECTURE_CYCLE4.md §23.3) — not a
/// flag on <see cref="AppUser"/> plus a separate table for guests, which would give the same number two
/// sources of truth that can drift (opt out as a guest, register later, the opt-out "disappears"). The
/// deliberate consequence: opting out belongs to the NUMBER being messaged, not the person, so changing
/// your phone number does not carry the opt-out with you.
/// </summary>
public class NotificationOptOut
{
    public Guid Id { get; set; }

    public string Phone { get; set; } = string.Empty; // canonical, unique
    public string? UserId { get; set; } // who clicked, if known — informational only

    public DateTime OptedOutAtUtc { get; set; } = DateTime.UtcNow;
    public OptOutSource Source { get; set; }
}
