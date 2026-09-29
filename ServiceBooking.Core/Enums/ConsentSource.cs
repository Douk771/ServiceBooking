namespace ServiceBooking.Core.Enums;

/// <summary>
/// Where a ConsentRecord row came from (ARCHITECTURE_CYCLE5.md §44.2, API_CONTRACT_CYCLE5.md §38.3).
/// `Migrated` is the one member that is never written on a production deploy — the M1 migration is the
/// only writer, and the base is wiped before the first real salon (§44.8) — kept only so dev/test
/// databases that already had UserConsent rows can carry that history forward instead of losing it.
/// </summary>
public enum ConsentSource
{
    Registration,
    ReAcceptance,
    Profile,
    CompanyCreation,
    ChannelRequest,
    ChannelLink,
    PhotoForm,
    HealthForm,
    Booking,
    Migrated,

    // ARCHITECTURE_CYCLE13.md §220.3 (LEGAL_REVIEW.md §16.4) — the "understood, save the address"
    // confirmation on the public-address-notice screen (POST /api/companies/address/notice). Append-only
    // member, no migration: the underlying column is a plain int (see ConsentRecord.Source).
    AddressForm,

    // ARCHITECTURE_CYCLE20.md §402.2 (US-20-01, LG1) — staff marking "written health-data consent
    // obtained" after the client signs the paper form. This is the ONLY source that opens the health
    // field (§402.3); the electronic HealthDataConsent uiText/salon form no longer does. Append-only
    // member, no migration.
    PaperForm
}
