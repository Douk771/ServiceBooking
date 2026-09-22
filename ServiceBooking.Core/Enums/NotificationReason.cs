namespace ServiceBooking.Core.Enums;

/// <summary>
/// Machine-readable reason an <see cref="Entities.OutboundNotification"/> ended up in its terminal (or
/// held) state — the Russian text shown in the delivery log (US-32 p.2) is assembled server-side from
/// this code, exactly like <see cref="ChannelStateReason"/> (ARCHITECTURE_CYCLE4.md §23.4).
/// </summary>
public enum NotificationReason
{
    /// <summary>Sent successfully.</summary>
    Delivered,

    /// <summary>Provider rejected permanently — most commonly "recipient has no WhatsApp" (noAccount).</summary>
    RecipientHasNoWhatsApp,

    /// <summary>Provider rejected for another permanent, non-retryable reason.</summary>
    RejectedByProvider,

    /// <summary>Retries exhausted (<c>AttemptCount</c> reached <c>Notifications:Dispatch:MaxAttempts</c>).</summary>
    RetriesExhausted,

    /// <summary>The visit this notification was about already started before it could be sent.</summary>
    VisitAlreadyStarted,

    /// <summary>Recipient is on the opt-out list (<see cref="Entities.NotificationOptOut"/>).</summary>
    RecipientOptedOut,

    /// <summary>Company's tariff does not include the notification channel option.</summary>
    NotOnPaidPlan,

    /// <summary>Company has no channel assigned, or the assigned channel is not paid/active.</summary>
    NoUsableChannel,

    /// <summary>This notification type is switched off in the company's settings.</summary>
    TypeDisabledByCompany,

    /// <summary>Too close to the visit (below <c>CompanyNotificationSettings.MinLeadMinutes</c>).</summary>
    BelowMinimumLeadTime,

    /// <summary>Booking (or the channel assignment) was cancelled after the notification was queued.</summary>
    BookingOrAssignmentCancelled,

    /// <summary>T-24 (ARCHITECTURE_CYCLE5.md §52.3): the recipient does not have a current
    /// `PdnConsent`/`ProviderDelivery` grant, and <c>Notifications:ProviderDeliveryConsent</c> requires
    /// one for them. Appended at the end — this enum is append-only (CURRENT_STATE.md §6: positions are
    /// not read from SQL/bitmasks here, but the convention is kept for the same reason ChannelStateReason
    /// keeps it: a reordering would silently relabel every already-persisted row).</summary>
    NoProviderDeliveryConsent,
}
