using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §388.2 — settings of a shop (Company.Kind = Orders), 1:1 with the company, PK =
/// <see cref="CompanyId"/> (the CompanyNotificationSettings pattern). Created in the same transaction as the
/// shop; readers still treat a missing row as the defaults below.
/// </summary>
public class ShopSettings
{
    public Guid CompanyId { get; set; }

    public ShopCustomerMode CustomerMode { get; set; } = ShopCustomerMode.Anyone;
    public OrderAcceptanceMode AcceptanceMode { get; set; } = OrderAcceptanceMode.Manual;
    public bool AllowCustomerCancel { get; set; } = true;
    public bool TrackStock { get; set; }

    /// <summary>
    /// Counter of order changes of this shop for the cheap board poll (§397.1). Written ONLY by OrderEventLog,
    /// in the same transaction as the change it counts, so the board cannot miss a change.
    /// </summary>
    public long OrdersRevision { get; set; }

    // [legal L2] seller details — all optional in cycle 1; obligation is switched on in code
    // (SellerInfoRequirements) after legal-counsel's conclusion.
    public LegalEntityForm? SellerLegalForm { get; set; }
    public string? SellerLegalName { get; set; }
    public string? SellerInn { get; set; }
    public string? SellerOgrn { get; set; }
    public string? SellerLegalAddress { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
