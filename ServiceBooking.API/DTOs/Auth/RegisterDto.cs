using System.ComponentModel.DataAnnotations;
using ServiceBooking.API.DTOs.PhoneVerification;

namespace ServiceBooking.API.DTOs.Auth;

// CYCLE5-BREAKING (ARCHITECTURE_CYCLE5.md §46.2, API_CONTRACT_CYCLE5.md §40.1): `acceptedLegal` is gone.
// Registration blocks on exactly two things — acknowledging the Privacy policy and accepting the
// TermsClient agreement — recorded with the version the caller says they saw, verified server-side
// against the live snapshot. PdnConsent is deliberately NOT here: it is a separate, non-blocking call
// (POST /api/profile/consents, §46.2) — this endpoint must not even be ABLE to accept it, by shape.
//
// Deliberately NOT [Required] on either field/on Legal itself — same reasoning cycle 3's AcceptedLegal
// bool documented here before it (CURRENT_STATE.md §6, "осознанные 4xx... plain text"): automatic
// ModelState validation would answer a missing/incomplete `legal` with a generic ProblemDetails blob
// instead of the contract's specific text, and — code review finding — silently break the frontend's
// substring-matching error mapper (utils/authError.ts), which reads the domain string, not
// ProblemDetails.errors. Checked by hand in AuthController instead.
public record RegisterLegalDto(
    string? PrivacyAcknowledgedVersion,
    string? TermsAcceptedVersion
);

// Registration is phone-primary: the phone number is the account identifier (stored as UserName, which
// carries Identity's unique index). Email is optional. [EmailAddress] treats null as valid, so it only
// validates the format when an email is actually supplied.
// ARCHITECTURE_CYCLE14.md §148.1, API_CONTRACT_CYCLE14.md §168: PhoneVerification is ADDITIVE and
// OPTIONAL — omitting it entirely (existing frontend, deploy/ci/smoke.sh) behaves exactly as before this
// cycle (US-14-07). When present it must name a session already Verified for THIS phone before the
// account is created — see AuthController.Register's own comment for the exact check.
public record RegisterDto(
    [Required] string FirstName,
    [Required] string LastName,
    [Required] string Phone,
    [Required, MinLength(8)] string Password,
    [EmailAddress] string? Email,
    RegisterLegalDto? Legal,
    PhoneVerificationRefDto? PhoneVerification = null
);
