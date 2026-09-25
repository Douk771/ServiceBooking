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
    // Cycle 7 (ARCHITECTURE_CYCLE7.md §53.1) — DEPRECATED, kept only because the response's FORM never
    // changes (§41 п. 4): this used to mean "seat cap for THIS company" and now means the account's
    // SUMMED seat cap across every company it owns (§46). A consumer still comparing
    // members.length >= maxEmployees for one company will undercount and show an active "add" button
    // that then 402s — use AccountSeatsUsed/AccountSeatsLimit/CanAddEmployee instead, which are computed
    // by the server with the exact same rule the 402 uses.
    // Marked `deprecated: true` in the OpenAPI contract (not a C# [Obsolete] — that would turn every
    // remaining read of it, including this DTO's own construction, into a build error under
    // `-warnaserror`). Use AccountSeatsUsed/AccountSeatsLimit/CanAddEmployee instead.
    int? MaxEmployees,
    // How many CompanyMembers rows THIS company has (local figure). 0 on the fully anonymous
    // GET /api/companies and GET /api/companies/{slug} (§46.2 — no AccountUsageReader call at all on
    // those paths); computed for every caller that manages the company.
    int EmployeeCount,
    // Account-wide seat usage/limit and whether AddMember would currently succeed — computed by
    // AccountUsageReader with the exact same rule CompaniesController.AddMember's 402 enforces (§46.4).
    // null on every field means "the caller doesn't manage this company, not computed" (§46.2) — NOT
    // "unlimited"; that's AccountSeatsLimit == null while AccountSeatsUsed has a value.
    int? AccountSeatsUsed,
    int? AccountSeatsLimit,
    bool? CanAddEmployee,
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
    int BookingHorizonDays,
    // ARCHITECTURE_CYCLE10.md §109.3/API_CONTRACT_CYCLE10.md §129 — additive, appended at the end with
    // defaults so existing positional CompanyDto(...) call sites keep compiling (§115 convention).
    // Null when there is no cover photo (CompanyPhoto with Position == 0 doesn't exist for this company).
    string? CoverPhotoUrl = null,
    string? CoverThumbnailUrl = null,
    // null = "this endpoint doesn't return the list" (catalog/list endpoints — batched cover-only lookup
    // is cheap, a full photo list per company in a 100-company page is not); [] = "no photos yet".
    // Filled ONLY on GET /api/companies/{slug} (the public page), per §109.3's zero-extra-requests rule.
    List<CompanyPhotoDto>? Photos = null,
    // ARCHITECTURE_CYCLE13.md §208/API_CONTRACT_CYCLE13.md §232 — additive, appended at the end with
    // defaults so every existing positional CompanyDto(...) call site keeps compiling (§115 convention).
    // null = "not computed for this caller" (anonymous/client reads, or an endpoint that doesn't bother) —
    // NEVER "unverified"; the frontend must read addressVerification.status, not its mere presence.
    CompanyAddressVerificationDto? AddressVerification = null,
    // null = no point, or the point exists but may not be exposed under the current licence
    // (AddressVerification:StoreResults=false — the shipped default). Filled ONLY on
    // GET /api/companies/{slug}, same "one endpoint, zero extra requests" rule as Photos above.
    GeoPointDto? AddressPoint = null,
    // ARCHITECTURE_CYCLE15.md §252/§282, API_CONTRACT_CYCLE15.md §282 — additive, appended at the end
    // with defaults (§115 convention). Public on EVERY caller including anonymous: these are the
    // owner's own storefront-facing settings, not computed per-viewer. null = field not filled in ->
    // the frontend shows no link for that service at all (0-bis П1) — never "not computed". Stored and
    // returned byte-for-byte; never built/derived from Address/CityId/AddressPoint.
    string? YandexMapsUrl = null,
    string? TwoGisUrl = null,
    // ARCHITECTURE_CYCLE15.md §252.3/§282 — always the already-normalized value (never a raw/garbage
    // stored int), default 2. Public on every caller so the client UI can explain the rule before the
    // server ever applies it.
    int ClientRescheduleMinHours = 2
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
// ARCHITECTURE_CYCLE10.md §103.5: ProvidesServices is additive. Always true for an anonymous/client
// caller (otherwise the element wouldn't have made it into the response at all) — no leak there.
public record MasterPublicDto(
    string UserId,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    string? Bio,
    bool ProvidesServices = true
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
    int? BookingHorizonDays = null,
    // ARCHITECTURE_CYCLE15.md §283 — three-state semantics, NOT the plain "omitted/null = don't touch"
    // convention above: omitted/null = don't touch; "" or whitespace-only = CLEAR the field (NULL in
    // DB); any other string = validate (MapLinkValidation) and store byte-for-byte. See
    // CompaniesController.Update for where that distinction actually happens.
    string? YandexMapsUrl = null,
    string? TwoGisUrl = null,
    // Omitted/null → don't touch; outside [0, 168] → 400 via ClientRescheduleWindow.TryNormalize. 0 IS
    // a legitimate explicit value here ("до самого начала визита"), unlike BookingHorizonDays's 0.
    int? ClientRescheduleMinHours = null
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
