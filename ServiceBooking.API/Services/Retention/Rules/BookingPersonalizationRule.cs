using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

/// <summary>
/// T5-B8/B9. Anonymizes a <see cref="Core.Entities.Booking"/> once its visit date is older than
/// <see cref="RetentionPeriods.BookingPersonalizationDays"/> — the exact same fields and
/// <see cref="Core.Entities.Booking.ClientDeleted"/> flag <c>ProfileController.DeleteAccount</c> already
/// uses for the subject-initiated path (§49.1: "механизм уже есть"), just triggered by age instead of a
/// request. Price/CommissionPercent/Status/Company/Service are untouched — the salon's revenue and
/// commission history for a completed visit must survive, same as the request-initiated path.
/// </summary>
public sealed class BookingPersonalizationRule(AppDbContext db) : IRetentionRule
{
    public string Name => "booking-personalization";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var cutoff = DateOnly.FromDateTime(RetentionPlan.CutoffsFor(ctx.NowUtc, ctx.Periods).BookingPersonalization);

        IQueryable<Core.Entities.Booking> Query(Guid cursor) => db.Bookings
            .Where(b => b.Id > cursor && !b.ClientDeleted && b.Date < cutoff)
            .OrderBy(b => b.Id);

        return RetentionRuleRunner.RunAsync(
            Name, Query, b => b.Id,
            mutate: b =>
            {
                b.ClientId = null;
                b.GuestName = null;
                b.GuestPhone = null;
                b.GuestEmail = null;
                b.Notes = null;
                b.ClientDeleted = true;
            },
            ctx, db, ct,
            dateOf: b => b.Date.ToDateTime(TimeOnly.MinValue));
    }
}
