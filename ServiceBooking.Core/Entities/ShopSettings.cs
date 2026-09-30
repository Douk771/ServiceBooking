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

    // ── Cycle 24 (ARCHITECTURE_CYCLE24.md §448.1). Every NOT NULL column has a DB default: OrderEventLog's upsert
    // INSERTs only the cycle-23 columns and relies on them. ──

    /// <summary>Weekly working hours as canonical JSON (§449.1). null = "hours not set" → the shop takes no orders.</summary>
    public string? WorkingHoursJson { get; set; }

    /// <summary>"Не принимаем, пока не включу".</summary>
    public bool OrdersStopped { get; set; }

    /// <summary>Pause until this moment; a moment in the past means "no pause" (the resume is computed, no background task).</summary>
    public DateTime? PausedUntilUtc { get; set; }

    public DateTime? AcceptanceChangedAtUtc { get; set; }
    public string? AcceptanceChangedByUserId { get; set; }

    /// <summary>Name snapshot; "Удалённый пользователь" after the account is deleted.</summary>
    public string? AcceptanceChangedByName { get; set; }

    public bool AsapEnabled { get; set; } = true;
    public bool ScheduledEnabled { get; set; }

    /// <summary>15 / 30 / 60.</summary>
    public int SlotStepMinutes { get; set; } = 15;

    /// <summary>0–14 days ahead.</summary>
    public int PreorderDays { get; set; }

    /// <summary>0–180 minutes.</summary>
    public int MinPrepMinutes { get; set; } = 15;

    public bool CustomerWebPushEnabled { get; set; } = true;
    public bool CustomerMessengerEnabled { get; set; }

    // ── Cycle 25 (ARCHITECTURE_CYCLE25.md §497.1). DB default true is mandatory (OrderEventLog's upsert). ──

    /// <summary>"Сообщения сотрудникам в MAX" — independent of the push flag.</summary>
    public bool StaffMaxEnabled { get; set; } = true;

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
