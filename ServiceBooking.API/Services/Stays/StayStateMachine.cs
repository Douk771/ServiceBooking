using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

public enum StayAction { AttachProof, Expire, ConfirmPayment, RejectPayment, CancelByGuest, CancelByOwner }

/// <summary>ARCHITECTURE_CYCLE37.md §37.6.5 — the status table of SPEC §4.7. Pure.</summary>
public static class StayStateMachine
{
    public static bool IsTerminal(StayBookingStatus s) =>
        s is StayBookingStatus.ExpiredUnpaid or StayBookingStatus.PaymentRejected or StayBookingStatus.CancelledByGuest or StayBookingStatus.CancelledByOwner;

    /// <summary>Statuses whose occupancy row is not released.</summary>
    public static bool Occupies(StayBookingStatus s) => !IsTerminal(s);

    /// <summary>The status after the action, or null when the action is not allowed from <paramref name="from"/>.</summary>
    public static StayBookingStatus? Next(StayBookingStatus from, StayAction action) => (from, action) switch
    {
        (StayBookingStatus.Held, StayAction.AttachProof) => StayBookingStatus.AwaitingPaymentCheck,
        (StayBookingStatus.AwaitingPaymentCheck, StayAction.AttachProof) => StayBookingStatus.AwaitingPaymentCheck,
        (StayBookingStatus.Held, StayAction.Expire) => StayBookingStatus.ExpiredUnpaid,
        (StayBookingStatus.AwaitingPaymentCheck, StayAction.ConfirmPayment) => StayBookingStatus.Confirmed,
        (StayBookingStatus.AwaitingPaymentCheck, StayAction.RejectPayment) => StayBookingStatus.PaymentRejected,
        (StayBookingStatus.Held or StayBookingStatus.AwaitingPaymentCheck or StayBookingStatus.Confirmed, StayAction.CancelByGuest) => StayBookingStatus.CancelledByGuest,
        (StayBookingStatus.Held or StayBookingStatus.AwaitingPaymentCheck or StayBookingStatus.Confirmed, StayAction.CancelByOwner) => StayBookingStatus.CancelledByOwner,
        _ => null
    };

    /// <summary>Display status: "Completed" is Confirmed after the check-out moment.</summary>
    public static string DisplayStatus(StayBookingStatus status, DateTime nowUtc, DateOnly checkOut, TimeOnly checkOutTime, string timeZoneId) =>
        status == StayBookingStatus.Confirmed && nowUtc >= StayTime.ToUtc(timeZoneId, checkOut, checkOutTime)
            ? "Completed" : status.ToString();

    public static IReadOnlyList<string> GuestActions(StayBookingStatus status, DateTime nowUtc, DateTime? holdExpiresAtUtc, DateOnly checkIn, TimeOnly checkInTime, string tz)
    {
        var list = new List<string>();
        var holdOk = status != StayBookingStatus.Held || holdExpiresAtUtc > nowUtc;
        if (holdOk && (status is StayBookingStatus.Held or StayBookingStatus.AwaitingPaymentCheck)) list.Add("AttachProof");
        if (!IsTerminal(status) && nowUtc < StayTime.ToUtc(tz, checkIn, checkInTime) && holdOk) list.Add("Cancel");
        return list;
    }

    public static IReadOnlyList<string> StaffActions(StayBookingStatus status)
    {
        var list = new List<string>();
        if (status == StayBookingStatus.AwaitingPaymentCheck) { list.Add("ConfirmPayment"); list.Add("RejectPayment"); }
        if (!IsTerminal(status)) list.Add("Cancel");
        return list;
    }
}
