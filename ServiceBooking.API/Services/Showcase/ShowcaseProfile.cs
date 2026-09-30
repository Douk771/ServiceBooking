namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.2 — the volume and flags of one showcase build. Only <see cref="Prod"/> exists in pass A of cycle 28; the "demo" profile
/// (three demo roles, reviews, notes, change history — US-28-10/13) belongs to pass B and will be a second static member here. <see cref="Name"/> is part
/// of every generated id (<see cref="ShowcaseIds"/>).
/// </summary>
public sealed record ShowcaseProfile(
    string Name,
    int RegisteredClients,
    int GuestClients,
    int PastDays,
    int ScheduleDaysAhead,
    double PastOccupancy)
{
    /// <summary>The production showcase: 9 example companies, ~120 registered and ~300 guest clients, 60 days of history and a schedule 44 days ahead
    /// (booking horizon 30 days, so the showcase stays complete for at least 14 days without a re-seed, §575.3).</summary>
    public static readonly ShowcaseProfile Prod = new("prod", RegisteredClients: 120, GuestClients: 300, PastDays: 60, ScheduleDaysAhead: 44, PastOccupancy: 1.05);
}
