namespace ServiceBooking.Core.Entities;

/// <summary>ARCHITECTURE_CYCLE23.md §388.2, §396.5 — last issued order number of a shop's business day. PK (CompanyId, BusinessDate).</summary>
public class OrderDailyCounter
{
    public Guid CompanyId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public int LastNumber { get; set; }
}
