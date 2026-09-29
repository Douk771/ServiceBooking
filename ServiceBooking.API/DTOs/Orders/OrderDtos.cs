using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.DTOs.Orders;

// ARCHITECTURE_CYCLE23.md §402, API_CONTRACT_CYCLE23.md §412–§420, contracts/cycle23/openapi.yaml (the source of truth).

/// <summary>Closed table of the checkout refusals (409 OrderRefusalDto.code). Serialized by name; append-only.</summary>
public enum OrderRefusalCode
{
    ShopNotAcceptingOrders,
    LoginRequired,
    PhoneVerificationRequired,
    PhoneVerificationUnavailable,
    EmptyCart,
    TooManyLines,
    PriceChanged,
    ItemsUnavailable
}

/// <summary>Closed table of the codes of an action over an existing order (409 OrderConflictDto.code). Append-only.</summary>
public enum OrderConflictCode
{
    VersionMismatch,
    InvalidTransition,
    CancelNotAllowed,
    AlreadyReady,
    InsufficientStock,
    LastItemCannotBeRemoved,
    InvalidQuantity,
    ProductUnavailable
}

/// <summary>Closed table of the codes of catalog/slug conflicts (409 CatalogConflictDto.code). Append-only.</summary>
public enum CatalogConflictCode
{
    SlugTaken,
    SlugReserved,
    SlugInvalid,
    CategoryNotEmpty,
    UnitChangeNotAllowed,
    ProductLimitReached,
    CategoryLimitReached,
    PhoneVerificationUnavailable
}

public record CatalogConflictDto(CatalogConflictCode Code, string Message);

// ── Cart and checkout ────────────────────────────────────────────────────────────────────────────────────────────

public record CartLineInput(Guid ProductId, int Quantity);

public record QuoteInput(List<CartLineInput>? Items);

public record OrderProblemDto(
    Guid ProductId, string Name, OrderProblemReason Reason, string Message, decimal? CurrentUnitPrice = null,
    int? AvailableQuantity = null);

public record QuoteLineDto(
    Guid ProductId, string Name, ProductUnit Unit, decimal UnitPrice, string? PortionText, int Quantity, decimal LineTotal,
    bool IsApproximate, OrderProblemDto? Problem);

public record QuoteDto(
    List<QuoteLineDto> Lines, decimal Total, bool IsApproximate, bool HasProblems, bool AcceptingOrders,
    string? NotAcceptingReason);

public record OrderLineInput(Guid ProductId, int Quantity, decimal ExpectedUnitPrice);

public record CreateOrderInput(
    Guid IdempotencyKey, List<OrderLineInput>? Items, string? CustomerName, string? CustomerPhone, string? Comment,
    string? CaptchaToken);

public record OrderRefusalDto(OrderRefusalCode Code, string Message, List<OrderProblemDto>? Problems = null);

public record CreateOrderResponse(PublicOrderDto Order, string OrderUrl);

// ── Order as the customer sees it ────────────────────────────────────────────────────────────────────────────────

public record PublicOrderItemDto(
    string Name, ProductUnit Unit, decimal UnitPrice, string? PortionText, int QuantityOrdered, int? QuantityActual,
    decimal LineTotal, bool IsApproximate);

public record OrderTimelineStepDto(OrderStatus Status, string Title, bool Reached, DateTime? ReachedAtUtc);

public record OrderChangeLineDto(string Name, string? Before, string? After, string Text);

public record OrderShopChangeDto(
    DateTime OccurredAtUtc, string? Comment, List<OrderChangeLineDto> Changes, decimal TotalBefore, decimal TotalAfter);

public record OrderShopInfoDto(
    string Name, string Slug, string PublicUrl, string? LogoUrl, string? Address, string? CityName, string? Phone,
    string? YandexMapsUrl, string? TwoGisUrl);

public record PublicOrderDto(
    string Token, int Number, DateOnly BusinessDate, DateTime CreatedAtUtc, OrderStatus Status, string StatusText,
    List<OrderTimelineStepDto> Timeline, List<PublicOrderItemDto> Items, decimal Total, bool TotalIsApproximate,
    string? Comment, string? CustomerName, string? CustomerPhoneMasked, string? Reason, bool CanCancel,
    OrderShopInfoDto Shop, List<OrderShopChangeDto> ShopChanges, int Version, bool IsGuest);

public record MyOrderSummaryDto(
    string OrderUrl, string Token, int Number, DateOnly BusinessDate, string ShopName, OrderStatus Status, string StatusText,
    decimal Total, bool TotalIsApproximate, DateTime CreatedAtUtc, bool IsActive);

// ── Order as staff sees it ───────────────────────────────────────────────────────────────────────────────────────

public record StaffOrderItemDto(
    Guid Id, Guid? ProductId, string Name, ProductUnit Unit, decimal UnitPrice, string? PortionText, int? WeightStepGrams,
    int QuantityOrdered, int? QuantityActual, decimal LineTotal, bool IsApproximate);

public record StaffOrderCardDto(
    Guid Id, int Number, DateOnly BusinessDate, DateTime CreatedAtUtc, OrderStatus Status, string StatusText,
    string? CustomerName, string? CustomerPhone, OrderActorKind CustomerKind, bool CustomerPhoneVerified, string? Comment,
    List<StaffOrderItemDto> Items, decimal Total, bool TotalIsApproximate, bool IsModified, bool HasWeightItems,
    string? Reason, DateTime? CompletedAtUtc, int Version, List<OrderAction> AvailableActions);

public record OrderEventDto(
    OrderEventKind Kind, DateTime OccurredAtUtc, OrderActorKind ActorKind, string? ActorName, OrderStatus? FromStatus,
    OrderStatus? ToStatus, string? Reason, string? Comment, List<OrderChangeLineDto>? Changes, decimal? TotalBefore,
    decimal? TotalAfter, string Text);

/// <summary>The card plus the journal (allOf StaffOrderCardDto + events in the contract).</summary>
public record StaffOrderDto(
    Guid Id, int Number, DateOnly BusinessDate, DateTime CreatedAtUtc, OrderStatus Status, string StatusText,
    string? CustomerName, string? CustomerPhone, OrderActorKind CustomerKind, bool CustomerPhoneVerified, string? Comment,
    List<StaffOrderItemDto> Items, decimal Total, bool TotalIsApproximate, bool IsModified, bool HasWeightItems,
    string? Reason, DateTime? CompletedAtUtc, int Version, List<OrderAction> AvailableActions, List<OrderEventDto> Events)
    : StaffOrderCardDto(Id, Number, BusinessDate, CreatedAtUtc, Status, StatusText, CustomerName, CustomerPhone, CustomerKind,
        CustomerPhoneVerified, Comment, Items, Total, TotalIsApproximate, IsModified, HasWeightItems, Reason, CompletedAtUtc,
        Version, AvailableActions);

public record OrderBoardDto(
    long Revision, bool Changed, DateOnly BusinessDate, DateTime ServerTimeUtc, List<StaffOrderCardDto>? NewOrders,
    List<StaffOrderCardDto>? Accepted, List<StaffOrderCardDto>? Ready, List<StaffOrderCardDto>? CompletedToday);

// ── Staff actions ────────────────────────────────────────────────────────────────────────────────────────────────

public record VersionInput(int ExpectedVersion);

public record ReasonInput(int ExpectedVersion, string? Reason);

public record ActualQuantityInput(Guid ItemId, int Quantity);

public record IssueQuoteInput(List<ActualQuantityInput>? ActualQuantities);

public record IssueInput(int ExpectedVersion, List<ActualQuantityInput>? ActualQuantities);

public record IssueQuoteDto(List<StaffOrderItemDto> Lines, decimal FinalTotal);

public record EditOrderLineInput(Guid? ItemId, Guid? ProductId, int Quantity);

public record EditOrderInput(int ExpectedVersion, List<EditOrderLineInput>? Items, string? CommentForCustomer);

public record OrderConflictDto(
    OrderConflictCode Code, string Message, StaffOrderDto? Order = null, PublicOrderDto? PublicOrder = null,
    List<OrderProblemDto>? Problems = null);
