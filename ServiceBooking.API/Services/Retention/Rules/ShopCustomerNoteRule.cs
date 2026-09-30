using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §504.4 [legal L20]. A note about a customer follows the depersonalization of the shop's orders and has no term of its own: it is
/// deleted when the shop has NO order left with this phone that is not erased (<c>PersonalDataErased = false</c> and a phone). While
/// <c>Retention:OrderPersonalDataDays</c> is 0 nothing is erased, so nothing is deleted here — the rule is inert until legal-counsel gives that number.
/// Dry-run is the task's common one (same query, no SaveChanges).
/// </summary>
public sealed class ShopCustomerNoteRule(AppDbContext db) : IRetentionRule
{
    public string Name => "shop-customer-notes";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        IQueryable<ShopCustomerNote> Query(Guid cursor) => db.ShopCustomerNotes
            .Where(n => n.Id > cursor)
            .Where(n => !db.Orders.Any(o => o.CompanyId == n.CompanyId && !o.PersonalDataErased && o.CustomerPhone == n.Phone)) // SUBJECT-PHONE-GATE: not-account-scoped — retention sweep of the shop's own notes (US-25-11)
            .OrderBy(n => n.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, n => n.Id, mutate: n => db.ShopCustomerNotes.Remove(n), ctx, db, ct, dateOf: n => n.UpdatedAtUtc);
    }
}
