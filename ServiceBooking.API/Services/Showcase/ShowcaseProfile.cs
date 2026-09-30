namespace ServiceBooking.API.Services.Showcase;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.2 — the volume and flags of one showcase build. <see cref="Prod"/> is the production showcase; <see cref="Demo"/> (pass B,
/// §575.3, §579.4) is the same set plus the three demo roles and the richer data of US-28-13: reviews and ratings, client notes and a longer change history of
/// bookings. <see cref="Name"/> is part of every generated id (<see cref="ShowcaseIds"/>), so the two profiles never share an id.
/// The flags default to "off": a profile that does not set them produces exactly what it produced before they existed (the production showcase is byte-for-byte
/// the same as in pass A).
/// </summary>
public sealed record ShowcaseProfile(
    string Name,
    int RegisteredClients,
    int GuestClients,
    int PastDays,
    int ScheduleDaysAhead,
    double PastOccupancy)
{
    /// <summary>Three ready accounts for <c>POST /api/demo/login</c>: the owner of the flagship salon (on the public "Салон" tariff), its master and a client
    /// with visits in several companies (§579.4).</summary>
    public bool DemoRoles { get; init; }

    /// <summary>Reviews (and therefore ratings) on a share of completed visits of registered clients.</summary>
    public bool Reviews { get; init; }

    /// <summary>Neutral notes of masters about clients (never health data, never photos: US-28-04).</summary>
    public bool ClientNotes { get; init; }

    /// <summary>ARCHITECTURE_CYCLE35.md §35.9 — the five demo shops of «Заказы» with their people, catalogs, ~5 thousand orders and the live board timeline. Built by
    /// <c>ShowcaseShopsDataset</c> AFTER everything else, so the flag changes nothing in the salon part. Off for <see cref="Prod"/>.</summary>
    public bool Shops { get; init; }

    /// <summary>A longer change history of bookings: more moves, some of them twice.</summary>
    public bool RichHistory { get; init; }

    /// <summary>Share of bookings that were moved once (the journal gets a <c>Rescheduled</c> event).</summary>
    public double RescheduleChance { get; init; } = 0.05;

    /// <summary>The production showcase: 9 example companies, ~120 registered and ~300 guest clients, 60 days of history and a schedule 44 days ahead
    /// (booking horizon 30 days, so the showcase stays complete for at least 14 days without a re-seed, §575.3).</summary>
    public static readonly ShowcaseProfile Prod = new("prod", RegisteredClients: 120, GuestClients: 300, PastDays: 60, ScheduleDaysAhead: 44, PastOccupancy: 1.05);

    /// <summary>The demo stand's profile (§575.3, §579.4, US-28-13): the production set, the three roles, reviews, notes and a richer history.</summary>
    public static readonly ShowcaseProfile Demo = new("demo", RegisteredClients: 120, GuestClients: 300, PastDays: 60, ScheduleDaysAhead: 44, PastOccupancy: 1.05)
    {
        DemoRoles = true,
        Reviews = true,
        ClientNotes = true,
        RichHistory = true,
        RescheduleChance = 0.18,
        Shops = true,
    };
}
