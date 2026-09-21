namespace ServiceBooking.Core.Entities;

/// <summary>
/// Reference list of Russian cities/towns, ~300 rows seeded by migration (ARCHITECTURE_CYCLE4.md §34.2).
/// Drives <see cref="Company.CityId"/> and the time zone shown/derived for a company. Search is a plain
/// <c>LIKE</c> against <see cref="SearchName"/> — no full-text index, no trigram extension: at this row
/// count a sequential scan is cheaper than any indexed alternative, and that is a deliberate choice, not
/// an oversight (do not "optimize" this without re-reading §34.2).
/// </summary>
public class City
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;

    // IANA identifier, e.g. "Asia/Barnaul". .NET 6+ resolves IANA ids on both Linux and Windows — see
    // DeploymentSafetyChecks.ValidateTimeZoneDatabase for the fail-fast that guards the runtime image
    // actually having the zone data installed.
    public string TimeZoneId { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    // Normalized for search: lowercase, ё→е, no hyphens/spaces (§34.2).
    public string SearchName { get; set; } = string.Empty;
}
