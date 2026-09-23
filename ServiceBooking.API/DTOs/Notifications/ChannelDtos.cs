using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §19.5 / ARCHITECTURE_CYCLE7.md §47.3 — the shape every channel
/// response uses. PaymentState/PaidUntil keep their form but are computed from the account's
/// subscription-driven funding ranking, not the channel's own (historical) payment columns.</summary>
public record ChannelDto(
    Guid Id,
    // ARCHITECTURE_CYCLE9.md §104.3/§114.3 — additive. A pre-cycle-9 frontend simply never reads this
    // field; every field below it is UNCHANGED, both in name and position, so this cycle adds exactly
    // one field to the shape rather than restructuring it.
    NotificationTransport Transport,
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
/// beats a generic ProblemDetails blob. ARCHITECTURE_CYCLE9.md §104.2/§114.2 (US-119): <see cref="Transport"/>
/// is additive and optional — absent (null) means <see cref="NotificationTransport.WhatsApp"/>, so a
/// pre-cycle-9 caller that never sends it keeps requesting exactly what it always requested.</summary>
public record CreateChannelRequestDto(LegalEntityForm? LegalEntityForm, string? Inn, OfferAcceptedDto? OfferAccepted, NotificationTransport? Transport = null);

/// <summary>API_CONTRACT_CYCLE9.md §114.1 — GET /api/notification-channels/offer, reshaped from the
/// cycle-4/5 form (Available/Currency/IdleDays dropped: Available was always exactly
/// <c>PricePerMonth is not null</c>, so it's redundant rather than a second field to keep in sync;
/// Currency/IdleDays were never read by any client). <see cref="RiskText"/>/<see cref="RiskVersion"/> now
/// come from the <c>ChannelRiskNotice</c> legal document (§104.8) instead of the deleted
/// <c>NotificationRiskText</c> constant. <see cref="Transports"/> is new — what's offered per transport,
/// including MAX's pre-connection notice (§104.9).</summary>
public record ChannelOfferDto(decimal? PricePerMonth, bool AllowedByPlan, string RiskText, string RiskVersion, IReadOnlyList<TransportOfferDto> Transports);

/// <summary>API_CONTRACT_CYCLE9.md §114.1 — one entry per transport on the owner's channel offer.
/// <see cref="ConnectionNotice"/> is server-composed Russian text (§6 convention) the owner must see
/// BEFORE requesting that transport — never sent as a version/flag the frontend fills in its own
/// wording for.</summary>
public record TransportOfferDto(NotificationTransport Transport, string DisplayName, bool Available, string? ConnectionNotice);

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
