namespace ServiceBooking.Core.Enums;

/// <summary>
/// The legal nature of what happened, recorded on ConsentRecord as its own column rather than derived
/// from DocumentKey by a switch (ARCHITECTURE_CYCLE5.md §44.2 p.3): a policy is ACKNOWLEDGED, an offer
/// is ACCEPTED, a consent (Art. 9/10) is CONSENTED to, and someone's authority to act for another person
/// is CONFIRMED. Every ConsentRecord row carries exactly one of these regardless of which DocumentKey it
/// references — a lawyer or an export reader needs this distinction, and burying it in a keyed switch
/// that nobody remembers to update is exactly the failure this field exists to avoid.
/// </summary>
public enum ConsentAct
{
    Acknowledged,
    Accepted,
    Consented,
    Confirmed
}
