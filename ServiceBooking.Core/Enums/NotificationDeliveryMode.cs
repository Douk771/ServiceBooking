namespace ServiceBooking.Core.Enums;

/// <summary>
/// How a company's notification event picks which connected transport(s) to send through
/// (ARCHITECTURE_CYCLE9.md §104.5, US-125, API_CONTRACT_CYCLE9.md §114.4). An enum rather than a
/// <c>bool</c> — §6 requires "a rule with three or more outcomes is an enum", and this one already has
/// two now and a plausible third later ("priority with auto-fallback", Q7в) that this cycle deliberately
/// does not build (§109); a boolean flag reinterpreted for a third case is exactly the kind of drift the
/// project has already been burned by once (<c>allowWithoutSchedule</c>).
///
/// <see cref="PriorityChannel"/> is member 0 — the default for every company that predates this cycle
/// (<c>CompanyNotificationSettings.DeliveryMode</c>'s own <c>defaultValue: 0</c>), paired with
/// <c>PriorityTransport = NotificationTransport.WhatsApp</c> (also 0), which is exactly today's
/// single-transport behavior. No backfill migration needed by construction.
/// </summary>
public enum NotificationDeliveryMode
{
    /// <summary>Exactly one target: the channel whose transport matches
    /// <c>CompanyNotificationSettings.PriorityTransport</c>. If that channel isn't assigned or isn't
    /// currently usable, the result is ZERO targets (<see cref="NotificationReason.PriorityChannelUnavailable"/>)
    /// — never a silent fallback to a different transport (§3 SPEC, Q7в).</summary>
    PriorityChannel = 0,

    /// <summary>One target per usable connected transport the company has assigned — a client may
    /// receive the same message twice, once per messenger, and the owner is warned about this before
    /// turning the mode on (API_CONTRACT_CYCLE9.md §114.4).</summary>
    AllChannels = 1,
}
