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
    Migrated
}
