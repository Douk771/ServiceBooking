using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §19.5 / ARCHITECTURE_CYCLE7.md §47.3 — the shape every channel
/// response uses. PaymentState/PaidUntil keep their form but are computed from the account's
/// subscription-driven funding ranking, not the channel's own (historical) payment columns.</summary>
public record ChannelDto(
    Guid Id,
    ChannelState State,
    string StateText,
    string? PhoneMasked,
    ChannelPaymentStatus PaymentState,
    // Deprecated (§47.3): always null from here on — PaidFromUtc is a historical column, no longer
    // read by business logic (same precedent as NotificationChannel.ContactEmail).
    DateTime? PaidFrom,
    DateTime? PaidUntil,
    DateTime? RequestedAt,
    DateTime? ConnectedAt,
    DateTime? RiskAcceptedAt,
    DateTime? IdleSince,
    DateTime? IdleDeadline,
    Guid? ReplacedByChannelId,
    IReadOnlyList<ChannelCompanyDto> Companies,
    bool CanConnect,
    bool CanReplace,
    // Cycle 7 (ARCHITECTURE_CYCLE7.md §47.1) — new fields, additive. FundingState is PaymentState's
    // finer-grained cause (Funded/Unfunded/NotPaid vs PaymentState's Paid/NotPaid/Suspended);
    // FundingText is the server-composed sentence naming the cause, the working number, and the fix.
    ChannelFundingState FundingState,
    string FundingText,
    // T5-B4 (API_CONTRACT_CYCLE5.md §50.2) — owner-only in practice: this DTO is never returned to
    // anyone but the channel's own owner (LoadOwnedChannelAsync) or SuperAdmin's admin view.
    string? Inn = null,
    LegalEntityForm? LegalEntityForm = null);

public record ChannelCompanyDto(Guid CompanyId, string CompanyName, bool IsActive);

public record ChannelListDto(IReadOnlyList<ChannelDto> Channels);

/// <summary>ARCHITECTURE_CYCLE5.md §43.2, §54 row 13 — the "TermsOwner accepted as an offer" acknowledgement
/// at channel-request time. Same document as company creation's OwnerTermsDto, different Purpose
/// (ChannelOffer) on the journal row it produces.</summary>
public record OfferAcceptedDto(string? Version);

/// <summary>T5-B4 (API_CONTRACT_CYCLE5.md §50.1, BREAKING № 7). All three fields are checked by hand in
/// the controller, not [Required] — RegisterDto.Legal's own note explains why a domain-specific 400 text
/// beats a generic ProblemDetails blob.</summary>
public record CreateChannelRequestDto(LegalEntityForm? LegalEntityForm, string? Inn, OfferAcceptedDto? OfferAccepted);

/// <summary>API_CONTRACT_CYCLE4.md §21 — GET /api/notification-channels/offer.</summary>
public record ChannelOfferDto(bool Available, decimal? PricePerMonth, string Currency, int IdleDays, bool PlanAllows, string RiskTextVersion);

/// <summary>API_CONTRACT_CYCLE4.md §23 — POST .../accept-risk.</summary>
public record AcceptRiskDto(string Version);

/// <summary>API_CONTRACT_CYCLE4.md §24.1 — POST .../connect.</summary>
public record ConnectResponseDto(ChannelState State, int RefreshAfterSeconds);

/// <summary>API_CONTRACT_CYCLE4.md §24.2 — GET .../qr.</summary>
public record QrResponseDto(ChannelState State, string? QrBase64, int RefreshAfterSeconds, int ExpiresInSeconds);

/// <summary>API_CONTRACT_CYCLE4.md §24.3 — POST .../test-message.</summary>
public record TestMessageResponseDto(bool Delivered, string Message);

/// <summary>API_CONTRACT_CYCLE4.md §25.1 — POST .../companies.</summary>
public record AssignCompanyDto(Guid CompanyId, bool WarningAcknowledged);

/// <summary>API_CONTRACT_CYCLE4.md §27 — POST .../replace (B8, US-63).</summary>
public record ReplaceChannelResponseDto(Guid NewChannelId, DateTime? PaidUntil, int CompaniesMoved);
