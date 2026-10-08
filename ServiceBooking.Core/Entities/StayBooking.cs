using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE37.md §37.2.4. Prices, rules, requisites and provider are SNAPSHOTS taken at booking time.</summary>
public class StayBooking
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid HouseId { get; set; }
    public string PublicToken { get; set; } = string.Empty;
    public StayBookingStatus Status { get; set; }
    public int Version { get; set; }
    public bool IsManual { get; set; }

    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
    public int Nights { get; set; }
    public int Adults { get; set; }
    public int Children { get; set; }
    public int Dogs { get; set; }
    public bool NeedCot { get; set; }
    public int ExtraBeds { get; set; }
    public TimeOnly? ArrivalTime { get; set; }

    public StayActorKind GuestKind { get; set; }
    public string? GuestUserId { get; set; }
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public string? Comment { get; set; }

    public DateTime? HoldExpiresAtUtc { get; set; }
    public int TotalRub { get; set; }
    public int PrepayRub { get; set; }
    public int DueAtCheckInRub { get; set; }
    public int PrepayPercentSnapshot { get; set; }
    public string NightPricesJson { get; set; } = "[]";
    public StayCancellationPolicy CancellationPolicySnapshot { get; set; }
    public TimeOnly CheckInTimeSnapshot { get; set; }
    public TimeOnly CheckOutTimeSnapshot { get; set; }
    public string TimeZoneIdSnapshot { get; set; } = string.Empty;
    public string? PaymentDetailsSnapshot { get; set; }
    public string? PaymentPurposeSnapshot { get; set; }
    public string? ProviderSnapshotJson { get; set; }

    public string? ConsentPrivacyVersion { get; set; }
    public string? ConsentTermsVersion { get; set; }
    public DateTime? ConsentAcceptedAtUtc { get; set; }
    public string? BookingNoticeVersion { get; set; }
    public string? BookingTermsVersion { get; set; }
    public string? CancellationTermsVersion { get; set; }
    public bool NotifyByMessenger { get; set; }
    public string? MessengerConsentVersion { get; set; }
    public DateTime? MessengerConsentAtUtc { get; set; }

    public Guid IdempotencyKey { get; set; }
    public string? StatusReason { get; set; }

    public DateTime? PaymentConfirmedAtUtc { get; set; }
    public string? PaymentConfirmedByUserId { get; set; }
    public string? PaymentConfirmedByNameSnapshot { get; set; }
    public DateTime? TerminalAtUtc { get; set; }

    public DateTime? HoldReminderQueuedAtUtc { get; set; }
    public DateTime? ArrivalReminderQueuedAtUtc { get; set; }
    /// <summary>ARCHITECTURE_CYCLE39.md §39.11.6: the snapshot of the reminder text for the booking page (Р39-17). Contains the guest's name: erased on depersonalisation (Т39-11).</summary>
    public string? ArrivalReminderPageText { get; set; }
    public DateTime? ArrivalReminderSentAtUtc { get; set; }
    public DateTime? CheckInInfoReleasedAtUtc { get; set; }
    public DateTime? PaymentProofsPurgedAtUtc { get; set; }
    public bool PersonalDataErased { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public House House { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public List<StayBookingCharge> Charges { get; set; } = [];
    public List<StayPaymentProof> PaymentProofs { get; set; } = [];
}

/// <summary>A line of the amount (A7). Σ AmountRub = TotalRub; the prepayment is computed from the PrepayEligible lines.</summary>
public class StayBookingCharge
{
    public Guid Id { get; set; }
    public Guid StayBookingId { get; set; }
    public int Position { get; set; }
    public StayChargeKind Kind { get; set; }
    /// <summary>ARCHITECTURE_CYCLE39.md §39.2.2: the session this line belongs to (kinds ServiceSlot / ServiceItem).</summary>
    public Guid? ServiceSessionId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int UnitPriceRub { get; set; }
    public int NightsCount { get; set; }
    public int AmountRub { get; set; }
    public bool PrepayEligible { get; set; }
}

/// <summary>Append-only journal; the only writer is StayBookingEventLog.</summary>
public class StayBookingEvent
{
    public Guid Id { get; set; }
    public Guid StayBookingId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? ServiceSessionId { get; set; }
    public StayBookingEventKind Kind { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public StayActorKind ActorKind { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorNameSnapshot { get; set; }
    public StayBookingStatus? FromStatus { get; set; }
    public StayBookingStatus? ToStatus { get; set; }
    public string? Reason { get; set; }
    public string? DetailsJson { get; set; }
}

/// <summary>A payment proof file (ЮР-6: deleted by retention, the fact of payment stays in the booking). The client's file name is NOT stored.</summary>
public class StayPaymentProof
{
    public Guid Id { get; set; }
    /// <summary>Exactly one of <see cref="StayBookingId"/> / <see cref="StayServiceOrderId"/> is set (CK_StayPaymentProofs_OneOwner).</summary>
    public Guid? StayBookingId { get; set; }
    public Guid? StayServiceOrderId { get; set; }
    public Guid CompanyId { get; set; }
    public string? StorageKey { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? PurgedAtUtc { get; set; }
}
