using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Orders;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §398.2, §398.5 — the ONE definition of "depersonalize an order", shared by the account deletion and by the
/// retention rule <c>order-personalization</c>. The shop's books stay intact: the number, the lines, the totals and the status remain;
/// only what identifies the customer goes (name, phone, comment, the account link) and the order is marked <c>PersonalDataErased</c>
/// — a deliberate erasure, distinguishable from a corrupted row (the Booking.ClientDeleted precedent).
/// </summary>
public static class OrderPersonalData
{
    /// <summary>The name snapshot of the customer's own journal entries after erasure (the same wording as reviews and bookings).</summary>
    public const string DeletedActorName = "Удалённый пользователь";

    public static void Erase(Order order)
    {
        order.CustomerUserId = null;
        order.CustomerName = null;
        order.CustomerPhone = null;
        order.Comment = null;
        order.PersonalDataErased = true;
    }

    /// <summary>The customer's (or guest's) own journal entries keep the fact, lose the name and the account link. Staff and system entries are a different subject and stay.</summary>
    public static void TombstoneCustomerEvent(OrderEvent orderEvent)
    {
        if (orderEvent.ActorKind is not (OrderActorKind.Customer or OrderActorKind.Guest)) return;
        orderEvent.ActorNameSnapshot = DeletedActorName;
        orderEvent.ActorUserId = null;
    }
}
