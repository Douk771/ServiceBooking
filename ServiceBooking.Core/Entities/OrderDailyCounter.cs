namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE23.md §388.2, §396.5 — last issued order number of a shop's day. PK (CompanyId, PickupDate).
/// Since cycle 24 (§451.4) the day is the PICKUP day, not the creation day (the column was renamed from BusinessDate;
/// existing rows stay right — before cycle 24 the two dates were the same).
/// </summary>
public class OrderDailyCounter
{
    public Guid CompanyId { get; set; }
    public DateOnly PickupDate { get; set; }
    public int LastNumber { get; set; }
}
