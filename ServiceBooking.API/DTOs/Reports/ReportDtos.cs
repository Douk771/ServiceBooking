using ServiceBooking.API.Services.Orders.Reports;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Reports;

// API_CONTRACT_CYCLE25.md §522–§530. The shape is contracts/cycle25/openapi.yaml; every text a person reads is built by the server.

public enum OrderHistorySort
{
    PickupDesc,
    PickupAsc
}

public enum SummaryTopSort
{
    Amount,
    Quantity
}

public sealed record ReportPeriodDto(ReportPeriodPreset Preset, DateOnly From, DateOnly To, string Label, int Days)
{
    public static ReportPeriodDto Of(ReportPeriod p) => new(p.Preset, p.From, p.To, p.Label, p.Days);
}

// ── history ─────────────────────────────────────────────────────────────────────────────────────────────

public sealed record OrderHistoryQuery(
    ReportPeriodPreset? Period, DateOnly? From, DateOnly? To, List<OrderStatus>? Statuses, string? Customer, decimal? AmountFrom,
    decimal? AmountTo, int? Number, OrderHistorySort? Sort, int? Page);

public sealed record OrderHistoryRowDto(
    Guid OrderId, int Number, DateOnly PickupDate, DateTime PickupStartUtc, string PickupText, OrderStatus Status, string StatusText,
    string? CustomerName, string? CustomerPhoneMasked, int ItemCount, decimal Total, bool TotalIsApproximate, bool PersonalDataErased);

public sealed record OrderHistoryPageDto(
    ReportPeriodDto Period, List<OrderHistoryRowDto> Items, int Page, int PageSize, int TotalCount, int IssuedCount, decimal IssuedAmount,
    string SummaryText, string? EmptyText);

// ── summary ─────────────────────────────────────────────────────────────────────────────────────────────

public sealed record SummaryShareDto(OrderStatus Status, string Label, int Count, decimal? Share, string ShareText);

public sealed record SummaryTopItemDto(
    Guid? ProductId, string Name, ProductUnit Unit, int Quantity, string QuantityText, decimal Amount, bool IsDeleted);

public sealed record SummaryTopDto(SummaryTopSort Sort, List<SummaryTopItemDto> Items);

public sealed record SummaryDayDto(DateOnly Date, string Label, int Orders, int IssuedCount, decimal IssuedAmount);

public sealed record SummaryComparisonDto(
    ReportPeriodDto Period, int OrdersTotal, int IssuedCount, decimal IssuedAmount, decimal? AverageCheck, string? OrdersDeltaText,
    string? IssuedCountDeltaText, string? IssuedAmountDeltaText, string? AverageCheckDeltaText);

public sealed record ShopSummaryDto(
    ReportPeriodDto Period, int OrdersTotal, int InProgress, string InProgressText, int IssuedCount, decimal IssuedAmount, string PaymentNote,
    decimal? AverageCheck, string AverageCheckText, int TerminalCount, List<SummaryShareDto> Cancellations, SummaryShareDto NotPickedUp,
    SummaryTopDto Top, List<SummaryDayDto>? Days, SummaryComparisonDto? Previous);

// ── pick list ───────────────────────────────────────────────────────────────────────────────────────────

public sealed record PickListSlotOptionDto(string From, string To, string Label);

public sealed record PickListProductDto(
    Guid? ProductId, string Name, string? CategoryName, ProductUnit Unit, int TotalQuantity, string QuantityText, int OrderCount,
    string? WeightBreakdownText, bool HasUnaccepted);

public sealed record PickListLineDto(string Name, ProductUnit Unit, int Quantity, string QuantityText, string? PortionText);

public sealed record PickListOrderDto(
    Guid OrderId, int Number, OrderStatus Status, bool IsUnaccepted, string PickupText, string? Comment, List<PickListLineDto> Lines);

public sealed record PickListTimeGroupDto(string? From, string? To, string Label, List<PickListOrderDto> Orders);

public sealed record PickListDto(
    string ShopName, DateOnly Date, string DateLabel, string? From, string? To, string IntervalLabel, bool IncludeNew, DateTime GeneratedAtUtc,
    string GeneratedAtText, int OrderCount, List<PickListSlotOptionDto> Slots, List<PickListProductDto> ByProduct, List<PickListTimeGroupDto> ByTime,
    string? EmptyText);

// ── customer ────────────────────────────────────────────────────────────────────────────────────────────

public sealed record ShopCustomerNoteDto(string Text, DateTime UpdatedAtUtc, string UpdatedByName, string UpdatedText);

public sealed record ShopCustomerNoteStateDto(ShopCustomerNoteDto? Note);

public sealed record ShopCustomerNoteInput(string? Text);

public sealed record ShopCustomerOrdersPageDto(List<OrderHistoryRowDto> Items, int Page, int PageSize, int TotalCount);

public sealed record ShopCustomerCardDto(
    Guid CustomerRef, string? Name, string Phone, string PhoneDisplay, bool PhoneVerified, int OrdersTotal, int IssuedCount, decimal IssuedAmount,
    int CancelledByCustomer, int NotPickedUp, DateOnly? FirstOrderDate, DateOnly? LastOrderDate, string StatsText, ShopCustomerOrdersPageDto Orders,
    ShopCustomerNoteDto? Note);
