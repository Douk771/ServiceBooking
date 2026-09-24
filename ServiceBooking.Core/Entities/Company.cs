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

    // ARCHITECTURE_CYCLE13.md §202 (LEGAL_REVIEW.md §16.2/§16.5) — address verification. Five additive,
    // nullable columns; deliberately hold ONLY OUR OWN data (the owner's own address text, the fact/date/
    // precision of our own check) — never the geocoder's normalized string, never a locality/object id
    // from its response. The standard Yandex Geocoder licence does not permit storing that "result"; it
    // permits only a short-lived cache (§209.2) and our own derived facts (Q-L10).
    //
    // AddressVerifiedInputKey is the normalized (AddressNormalization.Key) form of the OWNER'S OWN
    // address text at the moment it was verified — never candidate.formattedAddress. The name is
    // deliberately "…InputKey", not "…Value": a reader who stores the geocoder's own wording here would
    // be doing so against the name, not by a plausible reading of it (§202's own remarks on the rename).
    public string? AddressVerifiedInputKey { get; set; }
    public DateTime? AddressVerifiedAt { get; set; }
    public AddressPrecision? AddressPrecision { get; set; }

    // Coordinates: written ONLY when AddressVerification:StoreResults is true, which is itself only
    // legitimate under Yandex's extended ("with result storage") licence (§206, §209.2). Under the
    // standard licence (the shipped default) these two columns stay null forever — that is the normal,
    // fully-functional state of the product (P3), not a degraded one.
    public double? AddressLatitude { get; set; }
    public double? AddressLongitude { get; set; }
}
