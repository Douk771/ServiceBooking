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

    /// <summary>MAX equivalent of <see cref="RecipientHasNoWhatsApp"/> — the provider's
    /// <c>outgoingMessageStatus</c> webhook reported <c>noAccount</c> for a MAX send
    /// (ARCHITECTURE_CYCLE9.md §104.9/§104.9's researched table). Kept as its OWN member rather than
    /// reusing <see cref="RecipientHasNoWhatsApp"/> — the delivery log must say which messenger the
    /// client doesn't have, and <see cref="RecipientHasNoWhatsApp"/>'s own name would simply be wrong for
    /// a MAX channel. Append-only (§6): appended here, not inserted near its WhatsApp counterpart.</summary>
    RecipientNotInMax,

    /// <summary>ARCHITECTURE_CYCLE9.md §104.5 (US-125) — <c>NotificationDeliveryMode.PriorityChannel</c>
    /// selected a <c>priorityTransport</c> that is either not assigned to the company at all, or assigned
    /// but not currently usable (unfunded, disconnected/banned). Deliberately distinct from
    /// <see cref="NoUsableChannel"/>: that one means "no channel of any kind is usable"; this one means
    /// "a specific channel was named as priority and it, specifically, is not usable right now" — no
    /// silent fallback to a different transport (§3 SPEC, Q7в).</summary>
    PriorityChannelUnavailable,

    /// <summary>ARCHITECTURE_CYCLE9.md §105.8 (US-124) — the push service answered 404/410 Gone: the
    /// subscription itself is dead (browser revoked it, app uninstalled). The subscription row is
    /// deleted in the same pass this reason is recorded; no retry.</summary>
    PushSubscriptionGone,

    /// <summary>§105.8 — 401/403 from the push service, almost always a VAPID key-pair mismatch (R12).
    /// No retry; logged at Warning so an operator notices instead of the row silently sitting Failed.</summary>
    PushAuthRejected,

    /// <summary>§105.8 — 413 from the push service: the encrypted payload didn't fit. No retry, this is
    /// a code-side bug (the payload is built server-side), not a transient condition.</summary>
    PushPayloadTooLarge,

    /// <summary>§105.8 (Q17) — the queued row's own <c>ExpiresAtUtc</c> (min(CreatedAt + 1h, visit start))
    /// was reached before it could be sent; never sent to the push service at all.</summary>
    PushTtlExhausted,

    /// <summary>§105.6 — the company's <c>StaffPushEnabled</c> was turned off after this row was queued
    /// (or the setting was already off when the dispatch pass re-checked it); checked again at send time,
    /// not only at queue time.</summary>
    StaffPushDisabledByCompany,

    /// <summary>§105.6 (R4) — the intended recipient is no longer staff of the booking's company by the
    /// time the dispatch pass runs (<c>CompanyMembership.IsStaffAsync</c> re-checked at send time, never
    /// cached from queue time) — closes the "left the salon, still gets client names pushed" gap.</summary>
    MasterNoLongerInCompany,

    /// <summary>§105.5/R4 — the queued row's <c>SubscriptionId</c> now belongs to a DIFFERENT user than the
    /// one the row was queued for (shared device re-registered under another staff member's login between
    /// queue time and send time; <c>PushSubscriptionWriter.UpsertAsync</c> reassigns <c>UserId</c> on an
    /// existing endpoint but keeps its <c>Id</c>). Deliberately distinct from
    /// <see cref="PushSubscriptionGone"/>: the subscription itself is alive and must NOT be deleted — it is
    /// simply no longer this row's recipient's. No retry; the row that queued for the original recipient is
    /// simply held, and a future queueing pass (once that recipient registers a device again) sends fresh.</summary>
    PushSubscriptionReassigned,
}
