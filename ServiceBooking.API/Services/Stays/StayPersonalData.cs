using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Stays;

/// <summary>
/// ARCHITECTURE_CYCLE37.md §37.13.1 — depersonalisation of a booking (account deletion, retention). Dates, amounts, status and the occupancy stay, so the owner's
/// calendar does not break; the fact of payment (who confirmed, when, how much) stays too (ЮР-6). Active bookings are NOT cancelled.
/// </summary>
public static class StayPersonalData
{
    public const string DeletedUserName = "Удалённый пользователь";

    public static void Erase(StayBooking b)
    {
        b.GuestUserId = null;
        b.GuestName = null;
        b.GuestPhone = null;
        b.Comment = null;
        b.ArrivalTime = null;
        b.NotifyByMessenger = false;
        b.PersonalDataErased = true;
    }

    /// <summary>The name of the guest in the journal becomes the same placeholder as for a deleted account.</summary>
    public static void TombstoneGuestEvent(StayBookingEvent e)
    {
        if (e.ActorKind is StayActorKind.Guest or StayActorKind.Customer)
        {
            e.ActorUserId = null;
            e.ActorNameSnapshot = DeletedUserName;
        }
    }
}
