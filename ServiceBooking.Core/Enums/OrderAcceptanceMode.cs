namespace ServiceBooking.Core.Enums;

/// <summary>ARCHITECTURE_CYCLE23.md §388.2 — how a new order enters the shop. Persisted as a number: append-only.</summary>
public enum OrderAcceptanceMode
{
    /// <summary>The order is created as New and staff accept it.</summary>
    Manual = 0,

    /// <summary>The order is created as Accepted straight away.</summary>
    Auto = 1
}
