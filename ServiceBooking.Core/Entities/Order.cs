using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE23.md §388.2 — a pickup order. Every rule the customer saw is snapshotted here.</summary>
public class Order
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }

    /// <summary>Short number within the shop's business day (OrderNumberAllocator).</summary>
    public int Number { get; set; }

    /// <summary>The shop's local date at creation time (by the shop's TimeZoneId).</summary>
    public DateOnly BusinessDate { get; set; }

    /// <summary>32 random bytes, base64url (43 chars). The access secret of the order page.</summary>
    public string PublicToken { get; set; } = string.Empty;

    public OrderStatus Status { get; set; } = OrderStatus.New;

    /// <summary>Optimistic-concurrency token (EF); +1 on every change — staff actions carry expectedVersion.</summary>
    public int Version { get; set; }

    public OrderActorKind CustomerKind { get; set; } = OrderActorKind.Guest;
    public string? CustomerUserId { get; set; }

    /// <summary>null after depersonalization.</summary>
    public string? CustomerName { get; set; }

    /// <summary>Canonical phone (PhoneNormalizer); null after depersonalization.</summary>
    public string? CustomerPhone { get; set; }

    /// <summary>Snapshot: the order was placed with a verified phone.</summary>
    public bool CustomerPhoneVerified { get; set; }

    public string? Comment { get; set; }

    // Rule snapshots — settings changes apply to NEW orders only (US-23-10).
    public OrderAcceptanceMode AcceptanceModeSnapshot { get; set; }
    public bool AllowCustomerCancelSnapshot { get; set; }
    public ShopCustomerMode CustomerModeSnapshot { get; set; }

    public decimal EstimatedTotal { get; set; }
    public decimal? FinalTotal { get; set; }
    public bool HasWeightItems { get; set; }
    public bool IsModifiedByShop { get; set; }

    /// <summary>Reason of rejection / cancellation by the shop.</summary>
    public string? StatusReason { get; set; }

    public Guid IdempotencyKey { get; set; }

    // A guest's consent snapshot, exactly like Booking (§398.3).
    public string? ConsentPrivacyVersion { get; set; }
    public string? ConsentTermsVersion { get; set; }
    public DateTime? ConsentAcceptedAtUtc { get; set; }

    /// <summary>[legal L4] version of the line under the button, when the text exists in the manifest.</summary>
    public string? CheckoutNoticeVersion { get; set; }

    /// <summary>Depersonalized on purpose (account deletion / destruction rule) — unlike a corrupted row.</summary>
    public bool PersonalDataErased { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? ReadyAtUtc { get; set; }

    /// <summary>The moment of the transition into ANY terminal status.</summary>
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<OrderEvent> Events { get; set; } = [];
}
