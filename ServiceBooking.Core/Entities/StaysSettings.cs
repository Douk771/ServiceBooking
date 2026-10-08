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
    /// <summary>ЮР-5: false — the guest's comment is hidden from the housekeeper.</summary>
    public bool HousekeeperSeesGuestComment { get; set; }
    public bool GuestWebPushEnabled { get; set; } = true;
    public bool GuestMessengerEnabled { get; set; }
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
