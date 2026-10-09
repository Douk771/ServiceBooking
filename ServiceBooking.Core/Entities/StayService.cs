using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

// ARCHITECTURE_CYCLE39.md §39.2 — time-slot services of a «Дома» company (bath, hot tub, furaco). The time of day of a service is MINUTES from 00:00 of the
// business date (B ≤ minute ≤ B + 1440, B = Stays:Services:BusinessDayStartMinute = 360), never a TimeOnly: «02:00 of the next day» is 1560.

/// <summary>A service of a company. <c>StandalonePrepayPercent</c> NULL = without prepayment; a published service is never archived (CK_StayServices_PublishedNotArchived).</summary>
public class StayService
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int MinHours { get; set; } = 2;
    public int MaxHours { get; set; } = 6;
    public int StepMinutes { get; set; } = 60;
    public int BufferMinutes { get; set; } = 30;
    public bool ShowBufferToGuests { get; set; }
    public int MinLeadMinutes { get; set; } = 60;
    public int? StandalonePrepayPercent { get; set; }
    public StayServiceCancellationPolicy CancellationPolicy { get; set; } = StayServiceCancellationPolicy.NoDeductions;
    public int CancellationBoundaryHours { get; set; } = 12;
    /// <summary>ARCHITECTURE_CYCLE42.md §42.2.3: "up to N people" (1..30); NULL for «Дома».</summary>
    public int? Capacity { get; set; }
    public bool AvailableForHouseBookings { get; set; } = true;
    public bool IsPublished { get; set; }
    public int Position { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class StayServicePhoto
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public Guid CompanyId { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public int Position { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A window of the weekly template. <c>DayOfWeek</c> is ISO (1 = Monday … 7 = Sunday) and means the weekday of the BUSINESS date.</summary>
public class StayServiceWeeklyWindow
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public int DayOfWeek { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}

/// <summary>A manual date: closed or its own windows; removing the row returns the day to the template.</summary>
public class StayServiceDateOverride
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public bool IsClosed { get; set; }
    /// <summary>[{ "startMinute": 1080, "endMinute": 1560 }]; empty when <see cref="IsClosed"/>.</summary>
    public string WindowsJson { get; set; } = "[]";
    public string? Comment { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}

/// <summary>Append-only journal of the schedule; the only writer is ServiceScheduleWriter (together with the board revision).</summary>
public class StayServiceScheduleEvent
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public Guid CompanyId { get; set; }
    public StayServiceScheduleEventKind Kind { get; set; }
    public DateOnly? BusinessDate { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string? ActorUserId { get; set; }
    public string? ActorNameSnapshot { get; set; }
    public string BeforeJson { get; set; } = "null";
    public string AfterJson { get; set; } = "null";
}

/// <summary>A price of an hour. <c>DaysMask</c>: bit 0 = Monday … bit 6 = Sunday (the weekday of the business date); hours are 6…30.</summary>
public class StayServicePriceRule
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public int DaysMask { get; set; }
    public int FromHour { get; set; }
    public int ToHour { get; set; }
    public int PriceRub { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>An option sold with a session (a broom, tea). Deleting a row is always allowed: a session keeps a snapshot.</summary>
public class StayServiceItem
{
    public Guid Id { get; set; }
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PriceRub { get; set; }
    public int MaxPerSession { get; set; } = 10;
    public bool IsActive { get; set; } = true;
    public int Position { get; set; }
}

/// <summary>
/// THE occupancy of a service (one table, one EXCLUDE constraint EX_StayServiceSessions_NoOverlap) plus the snapshot of the price. Exactly one parent: a booking of a
/// house OR a stand-alone order. Written only by ServiceSessionWriter.
/// </summary>
public class StayServiceSession
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid? StayBookingId { get; set; }
    public Guid? StayServiceOrderId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public int StartMinute { get; set; }
    public int Hours { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public int BufferMinutesSnapshot { get; set; }
    /// <summary>EndUtc + the buffer: the right border of the occupancy. A stored column, not a generated one (timestamptz + interval is not IMMUTABLE).</summary>
    public DateTime OccupiedUntilUtc { get; set; }
    public StayServiceSessionState State { get; set; } = StayServiceSessionState.Active;
    public DateTime? ReleasedAtUtc { get; set; }
    public string ServiceNameSnapshot { get; set; } = string.Empty;
    /// <summary>[{ "startMinute": 1380, "priceRub": 2000 }, …].</summary>
    public string HourPricesJson { get; set; } = "[]";
    /// <summary>[{ "itemId", "name", "unitPriceRub", "quantity", "amountRub" }] — only the positions with a quantity above zero.</summary>
    public string ItemsJson { get; set; } = "[]";
    public int ServiceAmountRub { get; set; }
    public int ItemsAmountRub { get; set; }
    public int TotalRub { get; set; }
    public StayActorKind AddedByKind { get; set; }
    public string? AddedByUserId { get; set; }
    public string? AddedByNameSnapshot { get; set; }
    /// <summary>ЮР39-6: mandatory when the staff added the session.</summary>
    public StayServiceRequestBasis? RequestBasis { get; set; }
    /// <summary>Version of StayServiceAddNotice the guest saw when choosing (Т39-05).</summary>
    public string? AddNoticeVersion { get; set; }
    public Guid? IdempotencyKey { get; set; }
    public int Version { get; set; } = 1;
    public string? StatusReason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The envelope of a stand-alone session: the shape of <see cref="StayBooking"/> without the house. The status is the SAME <see cref="StayBookingStatus"/>, the
/// transitions are the same StayStateMachine. Prices, requisites and the provider are SNAPSHOTS.
/// </summary>
public class StayServiceOrder
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ServiceId { get; set; }
    public string PublicToken { get; set; } = string.Empty;
    public Guid IdempotencyKey { get; set; }

    public StayBookingStatus Status { get; set; }
    public int Version { get; set; } = 1;
    public bool IsManual { get; set; }
    public DateTime? HoldExpiresAtUtc { get; set; }
    public DateTime? TerminalAtUtc { get; set; }
    public string? StatusReason { get; set; }

    public StayActorKind GuestKind { get; set; }
    public string? GuestUserId { get; set; }
    public string? GuestName { get; set; }
    public string? GuestPhone { get; set; }
    public string? Comment { get; set; }
    public StayServiceRequestBasis? RequestBasis { get; set; }

    /// <summary>ARCHITECTURE_CYCLE42.md §42.2.3: snapshot at creation (1..30), never changes.</summary>
    public int? GuestsCount { get; set; }
    /// <summary>ARCHITECTURE_CYCLE42.md §42.2.3: when the pre-session reminder was queued (once-only; set even with no channel).</summary>
    public DateTime? SessionReminderAtUtc { get; set; }

    public int ServiceAmountRub { get; set; }
    public int ItemsAmountRub { get; set; }
    public int TotalRub { get; set; }
    public int PrepayPercentSnapshot { get; set; }
    public int PrepayRub { get; set; }
    public int DueOnSiteRub { get; set; }

    public StayServiceCancellationPolicy CancellationPolicySnapshot { get; set; }
    public int CancellationBoundaryHoursSnapshot { get; set; }
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

    public DateTime? PaymentConfirmedAtUtc { get; set; }
    public string? PaymentConfirmedByUserId { get; set; }
    public string? PaymentConfirmedByNameSnapshot { get; set; }
    public DateTime? PaymentProofsPurgedAtUtc { get; set; }

    public DateTime? HoldReminderQueuedAtUtc { get; set; }
    public bool PersonalDataErased { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<StayPaymentProof> PaymentProofs { get; set; } = [];
}

/// <summary>Append-only journal of an order; the only writer is StayServiceOrderEventLog.</summary>
public class StayServiceOrderEvent
{
    public Guid Id { get; set; }
    public Guid StayServiceOrderId { get; set; }
    public Guid CompanyId { get; set; }
    public StayServiceOrderEventKind Kind { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public StayActorKind ActorKind { get; set; }
    public string? ActorUserId { get; set; }
    public string? ActorNameSnapshot { get; set; }
    public StayBookingStatus? FromStatus { get; set; }
    public StayBookingStatus? ToStatus { get; set; }
    public string? Reason { get; set; }
    public string? DetailsJson { get; set; }
}

/// <summary>ARCHITECTURE_CYCLE42.md §42.2.2 (Т42-05): append-only journal of "this is not alcohol or tobacco" confirmations. Written only by ServiceItemWriter.</summary>
public class StayServiceItemConfirmation
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ServiceId { get; set; }
    /// <summary>NULL — the position was deleted.</summary>
    public Guid? ItemId { get; set; }
    public string ItemNameSnapshot { get; set; } = string.Empty;
    public string MarkersHit { get; set; } = string.Empty;
    public string NoticeKey { get; set; } = string.Empty;
    public string NoticeVersion { get; set; } = string.Empty;
    public string ConfirmedByUserId { get; set; } = string.Empty;
    public string ConfirmedByNameSnapshot { get; set; } = string.Empty;
    public DateTime ConfirmedAtUtc { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
}

/// <summary>History of the arrival-reminder setting (US-39-20, Т39-12/13): append-only, kept while the company exists.</summary>
public class StaysReminderTemplateChange
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ChangedByUserId { get; set; }
    public string ChangedByNameSnapshot { get; set; } = string.Empty;
    public TimeOnly PreviousTime { get; set; }
    public TimeOnly NewTime { get; set; }
    public string? PreviousTemplate { get; set; }
    public string? NewTemplate { get; set; }
    public bool PreviousPushText { get; set; }
    public bool NewPushText { get; set; }
    /// <summary>Version of StayReminderTemplateOwnerNotice or <c>fallback:&lt;sha256&gt;</c>.</summary>
    public string OwnerNoticeVersion { get; set; } = string.Empty;
    public string? PushNoticeVersion { get; set; }
    public bool CodeMarkersConfirmed { get; set; }
    public string? CodeMarkersHit { get; set; }
}
