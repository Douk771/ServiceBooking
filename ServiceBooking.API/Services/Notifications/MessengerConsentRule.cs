namespace ServiceBooking.API.Services.Notifications;

/// <summary>Result of <see cref="MessengerConsentRule.Evaluate"/> (§40.11.2).</summary>
public enum MessengerConsentDecision
{
    Allowed,
    Declined,
    NoConsent,
}

/// <summary>Who filled the "notify me in a messenger" mark. <see cref="Customer"/> = a signed-in account
/// (the only source whose opt-in is written to the consent ledger, Т40-L-07); <see cref="Guest"/> = anonymous;
/// <see cref="Staff"/> = an employee creating the record on the client's behalf (Р40-Ю2).</summary>
public enum MessengerConsentOrigin
{
    Guest,
    Customer,
    Staff,
}

/// <summary>What is stored on the record for a given origin and incoming mark.</summary>
public readonly record struct StoredMessengerConsent(bool? NotifyByMessenger, bool ConsentByStaff);

/// <summary>
/// ARCHITECTURE_CYCLE40.md §40.11 — the one place that decides the client's consent to messenger messages. Pure.
/// The unsubscribe is NOT here: the gate checks it earlier and it wins over any mark (Q-40-5).
/// Vectors: <c>consent</c>.
/// </summary>
public static class MessengerConsentRule
{
    /// <summary><c>false</c> → Declined; <c>true</c> → Allowed; <c>null</c> → Allowed only for a recipient that has an
    /// account with a current <c>PdnConsent/ProviderDelivery</c>, otherwise NoConsent.</summary>
    public static MessengerConsentDecision Evaluate(
        bool? notifyByMessenger, bool recipientIsAccount, bool accountHasProviderDeliveryConsent) => notifyByMessenger switch
    {
        false => MessengerConsentDecision.Declined,
        true => MessengerConsentDecision.Allowed,
        null => recipientIsAccount && accountHasProviderDeliveryConsent
            ? MessengerConsentDecision.Allowed
            : MessengerConsentDecision.NoConsent,
    };

    /// <summary>§40.11.1: staff's <c>true</c> = "the client agreed" (stored with the by-staff flag); staff's
    /// <c>false</c>/<c>null</c> = no mark → stored as <c>null</c>. Guest and customer marks are stored as sent.</summary>
    public static StoredMessengerConsent Store(MessengerConsentOrigin origin, bool? notifyByMessenger) => origin switch
    {
        MessengerConsentOrigin.Staff => notifyByMessenger == true ? new(true, true) : new(null, false),
        _ => new(notifyByMessenger, false),
    };

    /// <summary>§40.11.3: only a signed-in customer's <c>true</c> mark writes <c>PdnConsent/ProviderDelivery</c>, and only
    /// when there is no current grant already.</summary>
    public static bool WritesConsentLedger(
        MessengerConsentOrigin origin, bool? notifyByMessenger, bool accountHasProviderDeliveryConsent) =>
        origin == MessengerConsentOrigin.Customer && notifyByMessenger == true && !accountHasProviderDeliveryConsent;
}
