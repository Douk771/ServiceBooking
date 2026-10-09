using ServiceBooking.Core.Enums;

namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE37.md §37.2.2 — settings of a "Дома" company, 1:1 with <see cref="Company"/>, PK = CompanyId. Money is whole roubles.</summary>
public class StaysSettings
{
    public Guid CompanyId { get; set; }

    public TimeOnly CheckInTime { get; set; } = new(14, 0);
    public TimeOnly CheckOutTime { get; set; } = new(12, 0);
    public int MinNights { get; set; } = 1;
    public int MaxNights { get; set; } = 30;
    public int HorizonDays { get; set; } = 365;
    public bool AllowGapFill { get; set; }
    public bool AllowSameDayCheckIn { get; set; } = true;
    public int HoldMinutes { get; set; } = 30;
    public int PrepayPercent { get; set; } = 30;

    /// <summary>NEVER in a public DTO (Т37-04).</summary>
    public string? PaymentDetails { get; set; }
    public string? PaymentPurpose { get; set; }

    public StayCancellationPolicy CancellationPolicy { get; set; } = StayCancellationPolicy.Standard;
    public int DogFeeRub { get; set; }
    public int CotFeeRub { get; set; }

    public TimeOnly CheckInInfoSendTime { get; set; } = new(9, 0);
    public string? CheckInInfoText { get; set; }
    /// <summary>ЮР-4: false — only a link goes to a messenger.</summary>
    public bool CheckInInfoSendFullText { get; set; }
    public bool ArrivalReminderEnabled { get; set; } = true;
    /// <summary>ARCHITECTURE_CYCLE39.md §39.11: the time of the reminder the day before the check-in (08:00…22:00, step 30).</summary>
    public TimeOnly ArrivalReminderTime { get; set; } = new(18, 0);
    /// <summary>NULL = the default text (the old code of cycle 37, byte for byte). Never longer than 700 characters.</summary>
    public string? ArrivalReminderTemplate { get; set; }
    /// <summary>ЮР39-3: the template reaches a web-push only when this is on; off by default.</summary>
    public bool ArrivalReminderPushText { get; set; }
    /// <summary>ЮР39-2/US-39-11: accept orders of services without a stay. Off by default.</summary>
    public bool AcceptServiceOrdersWithoutStay { get; set; }
    /// <summary>ЮР-5: false — the guest's comment is hidden from the housekeeper.</summary>
    public bool HousekeeperSeesGuestComment { get; set; }
    public bool GuestWebPushEnabled { get; set; } = true;
    public bool GuestMessengerEnabled { get; set; }
    /// <summary>ARCHITECTURE_CYCLE42.md §42.2.3: reminder before a session N hours ahead (1..24); NULL = off.</summary>
    public int? ServiceReminderHours { get; set; }
    public bool StaffMaxEnabled { get; set; } = true;

    public StayProviderStatus? ProviderStatus { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderInn { get; set; }
    public string? ProviderOgrn { get; set; }
    public string? ProviderClaimsAddress { get; set; }

    /// <summary>Poll counter of the board; written only by StayBookingEventLog and HouseBlockWriter.</summary>
    public long BookingsRevision { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
