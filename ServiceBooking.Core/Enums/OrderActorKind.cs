namespace ServiceBooking.Core.Enums;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §388.2 — who acted. Persisted as a number: append-only. <c>Order.CustomerKind</c>
/// only ever holds Customer or Guest; the rest are journal actors.
/// </summary>
public enum OrderActorKind
{
    Customer = 0,
    Guest = 1,
    Staff = 2,
    SuperAdmin = 3,
    System = 4
}
