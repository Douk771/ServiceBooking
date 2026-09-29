namespace ServiceBooking.API.DTOs.Orders;

// ARCHITECTURE_CYCLE23.md §398.1, API_CONTRACT_CYCLE23.md §422.1 — the "orders" section of GET /api/profile/export.

public record ExportOrderSellerDto(string? LegalName, string? Inn);

public record ExportOrderItemDto(
    string Name, string Unit, decimal UnitPrice, int QuantityOrdered, int? QuantityActual, decimal LineTotal);

/// <summary>Only the events the customer may see (no staff names), as ready-made text.</summary>
public record ExportOrderEventDto(DateTime OccurredAtUtc, string Text);

// Source: "Account" — an order of the account; "GuestSamePhone" — a guest order on the same VERIFIED number (SubjectScope).
public record ExportOrderDto(
    string ShopName, string? ShopAddress, ExportOrderSellerDto? Seller, int Number, DateOnly BusinessDate, DateTime CreatedAtUtc,
    string Status, string StatusText, string? CustomerName, string? CustomerPhone, string? Comment,
    List<ExportOrderItemDto> Items, decimal Total, string? Reason, string Source, List<ExportOrderEventDto> Events,
    // Cycle 24 (API_CONTRACT_CYCLE24.md §488): the pickup, the messenger choice with its consent snapshot [legal L9], and the browsers subscribed to the order.
    ExportOrderPickupDto? Pickup = null, bool NotifyByMessenger = false, string? MessengerConsentVersion = null,
    DateTime? MessengerConsentAtUtc = null, ExportOrderPushDto? WebPushSubscriptions = null);

public record ExportOrderPickupDto(string Kind, DateOnly Date, string Text);

/// <summary>How many browsers were subscribed to the order and since when — never the endpoint or the keys.</summary>
public record ExportOrderPushDto(int Count, List<DateTime> CreatedAtUtc);
