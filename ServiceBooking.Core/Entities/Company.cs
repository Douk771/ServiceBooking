using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

public class Company
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public bool AllowSelfBooking { get; set; } = true;
    public bool RequirePrepayment { get; set; } = false;
    // Owner's own opt-out from the public company directory (GET /api/companies), independent of the
    // tariff's AllowPublicListing flag — the company still exists and its own page is reachable by
    // slug, it just doesn't appear in the general listing.
    public bool ShowInPublicListing { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // US-65/Q5 (ARCHITECTURE_CYCLE6.md §45.7): how far ahead a client may book online. Always the
    // already-normalized value (ServiceBooking.API.Services.BookingHorizon.TryNormalize/Normalize) —
    // never raw user input. Staff (manual bookings, reschedule) are never subject to this. 90 here
    // duplicates BookingHorizon.Default as a literal (Core can't reference the API project) — kept in
    // sync by convention, same as this file already does for TimeZoneId's "Europe/Moscow" literal.
    public int BookingHorizonDays { get; set; } = 90;

    // Cycle 4 (ARCHITECTURE_CYCLE4.md §23.3, §34): city drives the derived time zone used for
    // reminder/visit-time math. Restrict on delete — a city referenced by a company can't be removed
    // from the directory out from under it. Required for new companies from the AddCompanyCityAndTimeZone
    // migration onward; existing companies are backfilled to Барнаул/Asia/Barnaul (§34.2).
    public int? CityId { get; set; }
    public City? City { get; set; }

    // IANA identifier. Derived from City.TimeZoneId by default, but the owner may override it — see
    // TimeZoneIsManual (US-30 p.3: a manual override survives a later "save city" that would otherwise
    // reset it back to the city's own zone).
    public string TimeZoneId { get; set; } = "Europe/Moscow";
    public bool TimeZoneIsManual { get; set; }

    // The account holder (creator) — the subscription/tariff is bound to this user, and every company
    // they own (their "branches") shares that one account-level plan.
    public string OwnerUserId { get; set; } = string.Empty;
    public AppUser Owner { get; set; } = null!;

    // Cycle 7 (ARCHITECTURE_CYCLE7.md §43.2, §43.4) — "who pays and by which rules this company
    // lives", independent of OwnerUserId ("who manages/sees it"). Stage 6 (§43.6, B5-13): the
    // column is now NOT NULL at the database level (AppDbContext's Fluent config calls
    // `.IsRequired()`) and every Company row also carries the alternate key (Id, BillingAccountId)
    // that ChannelCompanyAssignment's composite FK pins to. The CLR type stays `Guid?` deliberately
    // — call sites across the codebase already null-check it defensively, and the DB-level NOT NULL
    // is what actually matters (EF happily maps a NOT NULL column into a nullable property; it just
    // never observes null). Changing this to a non-nullable `Guid` would touch dozens of unrelated
    // call sites for no behavioural gain — see the migration's own remarks for the full reasoning.
    public Guid? BillingAccountId { get; set; }
    public BillingAccount? BillingAccount { get; set; }

    public ICollection<CompanyMember> Members { get; set; } = [];
    public ICollection<Service> Services { get; set; } = [];
    public ICollection<Booking> Bookings { get; set; } = [];

    // ARCHITECTURE_CYCLE10.md §102.2/§109.2 — showcase photos of the salon. Public storage class,
    // deliberately NOT a substitute for LogoUrl (a logo and a gallery photo are different things, §109.2).
    public ICollection<CompanyPhoto> Photos { get; set; } = [];

    // ARCHITECTURE_CYCLE19.md §383.3 — the five cycle-13 address-verification columns
    // (AddressVerifiedInputKey, AddressVerifiedAt, AddressPrecision, AddressLatitude, AddressLongitude)
    // are removed as CLR properties: the geocoder that filled them no longer exists. The columns
    // themselves stay in the database as EF shadow properties (AppDbContext's Company configuration) so
    // the migration this cycle ships is data-only — no DropColumn. Code has no way to read or write them
    // any more; physical removal is a later cycle's decision (§393), after production data is checked.

    // ARCHITECTURE_CYCLE15.md §252 — links the owner pasted in themselves, byte-for-byte (path/query/
    // fragment are never parsed or rewritten). Null = field not filled in → the storefront shows no
    // link at all for that service (0-bis П1); this is NOT auto-built from Address/CityId/AddressPoint
    // any more (cycle 13's US-132/133 generator is removed). Validated ONLY by
    // ServiceBooking.API.Services.Companies.MapLinkValidation, the single place in the product that
    // decides whether a link is acceptable.
    public string? YandexMapsUrl { get; set; }
    public string? TwoGisUrl { get; set; }

    // ARCHITECTURE_CYCLE15.md §252/§252.3 — how many hours before the visit a CLIENT (not staff) may
    // still reschedule it themselves via PATCH /api/bookings/{id}/reschedule. Always the already-
    // normalized value (ServiceBooking.API.Services.Bookings.ClientRescheduleWindow.Normalize) — never
    // raw user input. NOT a way to turn client self-reschedule off entirely; that's AllowSelfBooking.
    public int ClientRescheduleMinHours { get; set; } = 2;

    // ARCHITECTURE_CYCLE23.md §388.1 — product type of the company: a salon (online booking, ezbook.ru) or a
    // pickup-order shop (goods.ezbook.ru). Set ONCE by CompanyCreationService and never changed afterwards —
    // there is deliberately no DTO setter for it anywhere, the admin panel included. Every pre-existing row is
    // Services through the column default (no backfill).
    public CompanyKind Kind { get; set; } = CompanyKind.Services;
}
