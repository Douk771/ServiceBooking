namespace ServiceBooking.Core.Entities;

/// <summary>
/// Cycle 18, Д6/Д14 (ARCHITECTURE_CYCLE18.md §332.5). Sole purpose of processing — verbatim from Д17
/// of SPEC.md — "учёт предоставленных пробных периодов и проверка соблюдения условия об однократном
/// предоставлении пробного периода одному Абоненту" (short form for the UI and logs: "проверка
/// однократности пробного периода"). Deliberately outlives the account: deleting an account removes
/// its <c>VerifiedPhones</c> row (§151.2 cycle 14), so this table cannot lean on VerifiedPhones — or
/// "delete account → register again" would grant a second trial silently.
///
/// No FK, no UserId, no BillingAccountId, no email — and none should ever be added: a link back to an
/// account would bring a cascade/scrub with it and defeat the whole point of this table.
///
/// Stores not the phone number but HMAC-SHA256 of the normalized number (<c>TrialPhoneKey</c>) — an
/// equality-only value, never reversible back to the number.
///
/// 🔴 Д14: this is NOT anonymized data — cryptographic transforms are not in the closed list of
/// anonymization methods in RKN order № 140 (19.06.2025), so every operator obligation applies:
/// legal basis is п. 7 ч. 1 ст. 6 152-ФЗ (NOT consent, Д15), composition is minimal, retention is 3
/// years FROM THE DATE OF GRANT (Д16), key requirements are §343.1. Never returned in any DTO — same
/// convention as <c>VerifiedPhone.ExternalAccountKey</c>.
/// 🔴 Д17: email is deliberately NOT part of the computed value — customer decision Р1. Do not
/// "improve" this in a later cycle without a new customer decision.
/// </summary>
public class TrialPhoneRegistration
{
    public Guid Id { get; set; }

    public string PhoneKeyHash { get; set; } = string.Empty;   // string(64), lowercase hex, UNIQUE

    // Д16: the DATE OF GRANT of the trial — and only that date — is what the 3-year clock counts
    // from. Not the date the account was deleted, not the trial's end date, not the date of the last
    // uniqueness check. An emergency regrant does NOT bump this (§334.5).
    public DateTime RegisteredAtUtc { get; set; }

    // К3 (§343.1): which key this value was computed on. Rotating the key means a new KeyId, and rows
    // with the old KeyId are destroyed by the retention rule — they can no longer be compared against
    // anything, i.e. would be held without purpose (ч. 7 ст. 5 152-ФЗ).
    public string KeyId { get; set; } = string.Empty;          // string(16), NOT NULL
}
