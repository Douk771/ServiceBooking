using ServiceBooking.API.Services.Billing;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Notifications;

/// <summary>API_CONTRACT_CYCLE4.md §19.5 / ARCHITECTURE_CYCLE5.md §47.3 — the shape every channel
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
    // Cycle 5 (ARCHITECTURE_CYCLE5.md §47.1) — new fields, additive. FundingState is PaymentState's
    // finer-grained cause (Funded/Unfunded/NotPaid vs PaymentState's Paid/NotPaid/Suspended);
    // FundingText is the server-composed sentence naming the cause, the working number, and the fix.
    ChannelFundingState FundingState,
    string FundingText);

public record ChannelCompanyDto(Guid CompanyId, string CompanyName, bool IsActive);

public record ChannelListDto(IReadOnlyList<ChannelDto> Channels);

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
