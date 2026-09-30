using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §577.4 (D-1) — a visitor of the site may book into an OPEN showcase (fictional) company; the booking carries a real name and
/// phone the visitor typed, so it is deleted (with its services and journal rows, by cascade) once it is older than
/// <see cref="RetentionPeriods.ShowcaseVisitorBookingHours"/>. Only bookings marked <see cref="ShowcaseBookingKind.Visitor"/> are touched: the
/// generator's own bookings (<see cref="ShowcaseBookingKind.Seeded"/>) and every real booking are out of reach by the selection itself. Backed by the
/// partial index <c>IX_Bookings_ShowcaseVisitor</c>. Dry-run and live mode use the same query (<see cref="RetentionRuleRunner"/>).
/// </summary>
public sealed class ShowcaseVisitorBookingRule(AppDbContext db) : IRetentionRule
{
    public string Name => "showcase-visitor-booking";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        if (ctx.Periods.ShowcaseVisitorBookingHours <= 0)
        {
            // Same convention as BookingEventRule: 0 reads as "not configured", never as "delete everything now".
            var skipped = $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено";
            return Task.FromResult(new RetentionOutcome(Name, Scanned: 0, Affected: 0, skipped) { Skipped = true });
        }

        var cutoff = CutoffFor(ctx.NowUtc, ctx.Periods.ShowcaseVisitorBookingHours);

        IQueryable<Booking> Query(Guid cursor) => db.Bookings
            .Where(b => b.Id > cursor && b.ShowcaseKind == ShowcaseBookingKind.Visitor && b.CreatedAt < cutoff)
            .OrderBy(b => b.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, b => b.Id,
            mutate: b => db.Bookings.Remove(b),
            ctx, db, ct,
            dateOf: b => b.CreatedAt);
    }

    /// <summary>The instant before which a visitor booking is eligible for deletion. Pure, for the unit test.</summary>
    public static DateTime CutoffFor(DateTime nowUtc, int hours) => nowUtc.AddHours(-hours);
}
