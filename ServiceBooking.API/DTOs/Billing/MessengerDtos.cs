using ServiceBooking.API.DTOs.Common;
using ServiceBooking.API.Services.Billing;
using ServiceBooking.API.Services.Notifications.Funding;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Billing;

// Cycle 43 (API_CONTRACT_CYCLE43.md §43.20–§43.34, contracts/cycle43/openapi.yaml): the messenger options — price screen, account card block,
// quote/extend/disable and the owner's «Подписка» block. Enums go out as strings (global JsonStringEnumConverter).

public sealed record MessengerSaleStateDto(bool Sellable, MessengerSaleReason? Reason, string Text)
{
    public static MessengerSaleStateDto From(MessengerSaleState s) => new(s.Sellable, s.Reason, s.Text);
}

public sealed record MessengerPriceChangeDto(
    DateTime ChangedAt, string? ChangedByName, decimal? OldPricePerMonth, decimal? NewPricePerMonth, string OldPriceText, string NewPriceText,
    bool OldIsActive, bool NewIsActive, string? Comment, OptionPriceChangeSource Source, int? ActivePaidAccounts);

public sealed record AdminMessengerOptionDto(
    NotificationTransport Transport, string Label, Guid? OptionId, bool Open, bool IsActive, decimal? PricePerMonth, string PriceText,
    MessengerSaleStateDto Sale, int ActivePaidAccounts, IReadOnlyList<MessengerPriceChangeDto> History);

public sealed record AdminMessengerOptionsDto(IReadOnlyList<AdminMessengerOptionDto> Items, string PlanMatrixNote, string PriceHint);

/// <summary><c>PricePerMonth</c> is required in the body: an omitted field must NOT be read as "remove the price" (hence <see cref="Optional{T}"/>).</summary>
public sealed record MessengerPriceInput(Optional<decimal?> PricePerMonth, bool? IsActive);

public sealed record UpdateMessengerPriceInput(
    Optional<decimal?> PricePerMonth, bool? IsActive, string? Comment, decimal? ExpectedPricePerMonth, bool ExpectedIsActive);

public sealed record MessengerPricePreviewDto(
    NotificationTransport Transport, decimal? CurrentPricePerMonth, decimal? NewPricePerMonth, bool CurrentIsActive, bool NewIsActive,
    bool SellableBefore, bool SellableAfter, bool StartsSelling, bool StopsSelling, bool IsZeroPrice, int ActivePaidAccounts,
    IReadOnlyList<string> Consequences, string ConfirmTitle, string ConfirmButtonText);

public enum MessengerNumberState { None, AwaitingQr, Bound }

public sealed record MessengerNumberDto(MessengerNumberState State, string Text, string? PhoneMasked, Guid? ChannelId);

public sealed record AdminMessengerLineDto(
    NotificationTransport Transport, string Label, Guid? OptionId, MessengerLineStatus Status, string StatusText, decimal? PricePerMonth,
    string PriceText, decimal MonthlyPrice, bool CountsInTotal, DateTime? PaidUntil, bool IsTrial, DateTime? EndsAt, MessengerNumberDto Number,
    MessengerSaleStateDto Sale, bool CanExtend, string ExtendActionText, string? ExtendDisabledText, bool CanDisable,
    string? DisableNowConfirmText, string? DisablePeriodEndConfirmText);

public sealed record MessengerQuoteDto(
    NotificationTransport Transport, int Months, decimal PricePerMonth, decimal Amount, string AmountText, DateTime? CurrentPaidUntil,
    DateTime NewPaidUntil, string NewPaidUntilText, string ActionText);

public sealed record ExtendMessengerInput(int Months, string? Comment, decimal? ExpectedPricePerMonth);

public sealed record DisableMessengerInput(MessengerDisableWhen When, string? Comment);

public sealed record OwnerMessengerLineDto(
    NotificationTransport Transport, string Label, MessengerLineStatus Status, string StatusText, decimal? PricePerMonth, string? PriceText,
    decimal MonthlyPrice, DateTime? PaidUntil, bool IsTrial, string? Footnote, OwnerMessengerAction? Action, string? ActionText,
    Guid? TargetCompanyId, string? NoCompanyText);

public sealed record OwnerMessengersBlockDto(
    string Title, string Note, IReadOnlyList<OwnerMessengerLineDto> Lines, decimal MonthlyTotal, string MonthlyTotalText,
    string ConditionsUrl, string ConditionsLabel);
