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
    int? MaxEmployees
);

public record CreateCompanyDto(
    string Name,
    string Slug,
    string? Description,
    string? Address,
    string? Phone,
    string? Email,
    bool AllowSelfBooking = true,
    bool ShowInPublicListing = true
);

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
    bool? ShowInPublicListing
);
