namespace ServiceBooking.Core.Enums;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §396.1 — an action staff can take on an order right now (computed by
/// OrderStateMachine, shown as buttons). Never persisted, serialized by name.
/// </summary>
public enum OrderAction
{
    Accept,
    Reject,
    MarkReady,
    Issue,
    NotPickedUp,
    Cancel,
    Edit
}
