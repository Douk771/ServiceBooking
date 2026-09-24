using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.PhoneVerification;

/// <summary>
/// The status vocabulary the HTTP API exposes (API_CONTRACT_CYCLE14.md §164) — a STRICT superset of
/// <see cref="PhoneVerificationStatus"/> that adds <c>Expired</c>. Deliberately its own DTO-only enum,
/// not a member added to the persisted <see cref="PhoneVerificationStatus"/>: §145.2 is explicit that
/// "Expired" is computed, never written to a row — giving it a real, persistable enum member would make
/// that guarantee a matter of discipline instead of a matter of the type system.
/// </summary>
public enum PhoneVerificationDisplayStatus { Pending, Linked, Verified, Rejected, Cancelled, Expired }

/// <summary>GET /phone-verification/config (API_CONTRACT_CYCLE14.md §162, anonymous).</summary>
public record PhoneVerificationConfigDto(
    bool Enabled, bool Healthy, IReadOnlyList<PhoneVerificationMethod> Methods, int SessionTtlSeconds, int PollIntervalSeconds);

/// <summary>POST /phone-verification/sessions request body (§163). <c>Phone</c> is required for an
/// anonymous caller, optional for an authenticated one (falls back to the account's current number).</summary>
public record StartPhoneVerificationRequestDto(string? Phone);

/// <summary>POST /phone-verification/sessions 201 response (§163).</summary>
public record PhoneVerificationSessionCreatedDto(
    Guid SessionId, string StatusToken, PhoneVerificationMethod Method, string DeepLink, string? WebLink, string? QrPngBase64,
    string PhoneMasked, DateTime ExpiresAtUtc, int TtlSeconds);

/// <summary>GET /phone-verification/sessions/{id} 200 response (§164).</summary>
public record PhoneVerificationSessionStatusDto(
    Guid SessionId, PhoneVerificationDisplayStatus Status, PhoneVerificationFailureReason? FailureReason,
    string? Message, string PhoneMasked, DateTime ExpiresAtUtc, DateTime? VerifiedAtUtc);

/// <summary>Presented back to the platform at <c>POST /api/auth/register</c> (§168) and
/// <c>POST /api/profile/change-phone</c> (§169) to redeem a completed session.</summary>
public record PhoneVerificationRefDto(Guid SessionId, string StatusToken);

/// <summary>GET /api/admin/phone-verification/diagnostics (§172, SuperAdmin-only).</summary>
public record PhoneVerificationDiagnosticsDto(
    bool Enabled, string Provider, IReadOnlyList<PhoneVerificationMethod> Methods, bool WebhookSubscribed,
    DateTime? LastSubscriptionAttemptAtUtc, string? LastSubscriptionError, PhoneVerificationDailyCountsDto Last24h);

public record PhoneVerificationDailyCountsDto(
    int Started, int Verified, int Rejected, IReadOnlyDictionary<string, int> RejectedByReason);
