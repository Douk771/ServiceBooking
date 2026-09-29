namespace ServiceBooking.Core.Entities;

/// <summary>
/// ARCHITECTURE_CYCLE24.md §448.2, §459.4 — orders CREATED by all shops of an account in one calendar month (in the shop's time zone).
/// PK (BillingAccountId, Month), <see cref="Month"/> = the first day of the month. Incremented with one upsert inside the creation
/// transaction, whose row lock serializes the orders of the account and makes the monthly limit hard. No personal data.
/// </summary>
public class OrderMonthlyUsage
{
    public Guid BillingAccountId { get; set; }
    public DateOnly Month { get; set; }
    public int Count { get; set; }
    public DateTime? Warned80AtUtc { get; set; }
    public DateTime? Warned100AtUtc { get; set; }
}
