using ServiceBooking.API.DTOs.Common;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Companies;

public record CompanyDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? LogoUrl,
    string? Address,
    string? Phone,
    string? Email,
    bool AllowSelfBooking,
    bool RequirePrepayment,
    // True when online self-service booking is available: requires AllowSelfBooking plus an active
    // paid (non-Free, non-expired) subscription. On the Free plan online booking is blocked for BOTH
    // guests and authenticated clients (only staff manual bookings work) — see BookingsController.Create.
    bool OnlineBookingEnabled,
    // True when the owner's effective plan includes analytics — mirrors the gate ReportsController
    // applies to GET /api/reports/masters, so the owner-facing UI can hide the Reports tab instead of
    // letting the user open it and silently get a 402.
    bool AllowAnalytics,
    // True when the owner's effective plan includes mailing — mirrors MailingController's 402 gate,
    // used the same way as AllowAnalytics (hide the Mailing tab instead of silently failing).
    bool AllowMailing,
    // Owner's own opt-out from the public directory — independent of the tariff's AllowPublicListing.
    bool ShowInPublicListing,
    // Computed: true only when both the owner wants to be listed AND the tariff allows it — mirrors
    // OnlineBookingEnabled's pattern. This is what GET /api/companies actually filters on.
    bool PublicListingEnabled,
    // Computed: true only when the owner turned on RequirePrepayment AND the tariff allows online
    // payment — see BookingsController.Create, which uses this exact combination.
    bool PrepaymentEnabled,
    // Raw tariff capability flags (unlike the *Enabled fields above, NOT combined with the owner's own
    // toggle) — the owner-facing settings UI needs these to grey out a toggle the tariff doesn't support
    // at all, regardless of whether the owner currently has it on or off.
    bool PlanAllowsOnlineBooking,
    bool PlanAllowsOnlinePayment,
    bool PlanAllowsPublicListing,
    // Seat cap for CompanyMembers rows (owner included — see CompaniesController.AddMember's seat-limit
    // check, which counts ALL members the same way). Null means unlimited.
    int? MaxEmployees,
    // QA cycle C regression fix: company-wide average rating and review count, computed by the
    // database over ALL of the company's reviews (CompaniesController.GetReviewAggregateAsync /
    // GetReviewAggregatesAsync) — NOT derived from a single page of GET /api/companies/{id}/reviews,
    // which is paginated (§11) and would make the number visibly shift as a caller pages through
    // reviews. Null/0 when the company has no reviews yet.
    double? AverageRating,
    int ReviewCount,
    // Cycle 4 (ARCHITECTURE_CYCLE4.md §31.4): five additive fields. CityId is null only for companies
    // created before the AddCompanyCityAndTimeZone migration that somehow slipped the backfill — the
    // frontend treats a null city the same as "not set yet" and prompts the owner to pick one.
    int? CityId,
    string? CityName,
    string? CityRegion,
    string TimeZoneId,
    bool TimeZoneIsManual,
    int UtcOffsetMinutes,
    // US-65/Q5 (ARCHITECTURE_CYCLE6.md §45.7): always the already-normalized value (never 0/null) —
    // the calendar and the /embed/:slug widget read the horizon from here for free.
    int BookingHorizonDays
);

// ARCHITECTURE_CYCLE5.md §42.1 — the acceptance of TermsOwner (D3) that gates company creation. Checked
// by hand in the controller (not [Required]), same reasoning as RegisterDto.Legal: a domain-specific
// text beats a generic ProblemDetails blob.
public record OwnerTermsDto(string? Version);

public record CreateCompanyDto(
    string Name,
    string Slug,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    // Required (API_CONTRACT_CYCLE4.md §31.2, breaking change) — a company without a city has no
    // derivable time zone, and reminder timing needs one. Nullable in the DTO (rather than a plain
    // `int`) so a request that omits it entirely gets the specific "Укажите город салона" message
    // instead of a generic model-binding 400.
    int? CityId,
    // Optional override — see CompanyTimeZoneResolver.ForNewCompany.
    string? TimeZoneId,
    bool AllowSelfBooking = true,
    bool ShowInPublicListing = true,
    // ARCHITECTURE_CYCLE5.md §42.1, API_CONTRACT_CYCLE5.md §42.1 (BREAKING № 3) — appended at the end
    // with a default so every existing positional CreateCompanyDto(...) call keeps compiling; the
    // controller answers 400 at runtime if it's actually missing, same pattern as RegisterDto.Legal.
    OwnerTermsDto? OwnerTerms = null
);

// API_CONTRACT_CYCLE5.md §42.1 — replaces the bare CompanyDto response. A fresh token is not an
// optimization: claims are baked in at issuance, so without one the very next owner-action request would
// still carry the OLD (missing) "lco" claim and immediately 451 on a company the owner just created.
public record CreateCompanyResponseDto(CompanyDto Company, string Token);

// Public-facing master info for the booking flow
public record MasterPublicDto(
    string UserId,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    string? Bio
);

public record UpdateCompanyDto(
    string? Name,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    bool? AllowSelfBooking,
    bool? RequirePrepayment,
    bool? ShowInPublicListing,
    // If provided (non-null CityId), changes the company's city. Plain nullable, matching every other
    // field's "omitted or null → don't touch" convention — unlike TimeZoneId below, the contract does
    // not define a distinct meaning for an explicit `cityId: null`.
    int? CityId = null,
    // Optional<T> because the three cases in API_CONTRACT_CYCLE4.md §31.3 need to be told apart: field
    // omitted (leave the zone as-is), field explicitly null (revert to the city's own zone), field set
    // to a value (manual override) — see CompanyTimeZoneResolver.ForUpdate.
    Optional<string?> TimeZoneId = default,
    // US-65/Q5 (ARCHITECTURE_CYCLE6.md §45.7): omitted/null → don't touch, same convention as every
    // other plain-nullable field above; 0 → explicit reset to BookingHorizon.Default (90); anything
    // outside [1, 365] → 400 via BookingHorizon.TryNormalize, checked in CompaniesController.Update.
    int? BookingHorizonDays = null
);

// US-24 p.4 / US-19 p.7 — GET /api/companies/{id}/photo-usage.
public record CompanyPhotoUsageDto(
    Guid CompanyId,
    long UsedBytes,
    int PhotoCount,
    int? QuotaMb,       // null = unlimited
    double? PercentUsed, // null when QuotaMb is null
    PhotoRetention Retention
);
