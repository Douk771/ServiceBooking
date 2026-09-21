namespace ServiceBooking.Core.Enums;

/// <summary>
/// T-24 (ARCHITECTURE_CYCLE5.md §52.3): the one disputed point of law (ч. 3 ст. 6 152-ФЗ — does passing a
/// recipient's phone/name to a third-party delivery provider need a separate consent, beyond the
/// contractual basis for the notification itself) resolved as a single configuration value, not a code
/// branch. Whichever position the lawyer eventually confirms, only this value changes — see §52.4 for the
/// cost of moving it in either direction.
///
/// Applies ONLY to whether <see cref="Notifications.NotificationGate"/> checks the grant when QUEUEING a
/// notification. It does NOT change whether the PdnConsent/ProviderDelivery purpose is offered, asked, or
/// recorded — that happens unconditionally, at every value of this flag (§52.3's "цель предъявляется и
/// записывается всегда").
/// </summary>
public enum ProviderDeliveryConsentMode
{
    /// <summary>Every recipient needs an explicit, current `ProviderDelivery` grant — including guests,
    /// who structurally cannot have one. Only meaningful if a lawyer confirms the strict reading extends
    /// to guests too (§52.4's "ужесточение").</summary>
    Strict,

    /// <summary>The default (ARCHITECTURE_CYCLE5.md §52.3.1): checked only for recipients WITH an
    /// account — nobody asked a guest for this consent, so a guest is treated as consenting on the
    /// contractual basis their booking is already made on. The only value under which the product's own
    /// published D4 text ("откажетесь — сообщений о записи не будет") is actually true.</summary>
    AccountsOnly,

    /// <summary>The purpose is never checked at queue time — reliance is on named disclosure of the
    /// delivery provider in D1/D5 alone. Legal only if D4's cause-1 wording is rewritten first (§52.4
    /// step 1) — switching to this value without that edit reintroduces the exact gap §52.3.1 found.</summary>
    Off
}
