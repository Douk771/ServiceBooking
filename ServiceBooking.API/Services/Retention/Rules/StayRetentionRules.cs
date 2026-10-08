using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Stays;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Retention.Rules;

// ARCHITECTURE_CYCLE37.md §37.13.3 (ЮР-6). All six rules of «Дома»; values are configuration (RetentionPeriods.Stay*). A booking "ends" at the LATER of its
// check-out date and its final status moment; the cutoffs below are applied to both so a booking is never touched early.

/// <summary>Payment-proof files: deleted after <see cref="RetentionPeriods.StayPaymentProofDays"/>; the row stays with <c>PurgedAtUtc</c>, the booking gets the mark and a journal event. The fact of payment stays in the booking.</summary>
public sealed class StayPaymentProofRule(AppDbContext db, FileStorage storage) : IRetentionRule
{
    public string Name => "stay-payment-proofs";

    public async Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayPaymentProofDays;
        if (days <= 0) return new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true };
        var cutoff = ctx.NowUtc.AddDays(-days);
        var cutoffDate = DateOnly.FromDateTime(cutoff).AddDays(1);

        var scanned = 0;
        var affected = 0;
        var cursor = Guid.Empty;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await db.StayBookings
                .Where(b => b.Id > cursor && b.PaymentProofsPurgedAtUtc == null && b.CheckOutDate < cutoffDate && (b.TerminalAtUtc == null || b.TerminalAtUtc < cutoff) &&
                            db.StayPaymentProofs.Any(p => p.StayBookingId == b.Id && p.StorageKey != null))
                .OrderBy(b => b.Id).Take(ctx.BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;
            var keys = new List<string>();
            foreach (var b in batch)
            {
                // the exact moment (the check-out time in the zone of the booking) is decided here, not by the coarse date filter
                if (ctx.NowUtc <= StayTime.ToUtc(b.TimeZoneIdSnapshot, b.CheckOutDate, b.CheckOutTimeSnapshot).AddDays(days) || (b.TerminalAtUtc is not null && b.TerminalAtUtc >= cutoff)) continue;
                scanned++;
                var proofs = await db.StayPaymentProofs.Where(p => p.StayBookingId == b.Id && p.StorageKey != null).ToListAsync(ct);
                foreach (var p in proofs)
                {
                    keys.Add(p.StorageKey!);
                    p.StorageKey = null;
                    p.PurgedAtUtc = ctx.NowUtc;
                }
                b.PaymentProofsPurgedAtUtc = ctx.NowUtc;
                db.StayBookingEvents.Add(new StayBookingEvent
                {
                    Id = Guid.NewGuid(), StayBookingId = b.Id, CompanyId = b.CompanyId, Kind = StayBookingEventKind.PaymentProofsPurged, OccurredAtUtc = ctx.NowUtc,
                    ActorKind = StayActorKind.System, ActorNameSnapshot = "Система", DetailsJson = $"{{\"reason\":\"retention\",\"files\":{proofs.Count}}}",
                });
                affected++;
            }
            cursor = batch[^1].Id;
            if (!ctx.DryRun)
            {
                await db.SaveChangesAsync(ct);
                foreach (var key in keys) storage.DeletePrivate(key); // after the commit: a lost file is cheaper than a row pointing at nothing
            }
            db.ChangeTracker.Clear();
            if (batch.Count < ctx.BatchSize) break;
        }
        return new RetentionOutcome(Name, scanned, affected, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: scanned={scanned} affected={affected}");
    }
}

/// <summary>Bookings "Снята: не оплачена" are depersonalised after <see cref="RetentionPeriods.StayUnpaidBookingDays"/> (30) from the removal.</summary>
public sealed class StayUnpaidPersonalizationRule(AppDbContext db) : IRetentionRule
{
    public string Name => "stay-unpaid-personalization";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayUnpaidBookingDays;
        if (days <= 0) return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true });
        var cutoff = ctx.NowUtc.AddDays(-days);
        IQueryable<StayBooking> Query(Guid cursor) => db.StayBookings
            .Where(b => b.Id > cursor && !b.PersonalDataErased && b.Status == StayBookingStatus.ExpiredUnpaid && b.TerminalAtUtc != null && b.TerminalAtUtc < cutoff)
            .OrderBy(b => b.Id);
        return RetentionRuleRunner.RunAsync(Name, Query, b => b.Id, StayPersonalData.Erase, ctx, db, ct, dateOf: b => b.TerminalAtUtc!.Value);
    }
}

/// <summary>Other bookings (a final status, or after check-out) are depersonalised after <see cref="RetentionPeriods.StayBookingPersonalDataDays"/> (3 years); their guest events are tombstoned.</summary>
public sealed class StayBookingPersonalizationRule(AppDbContext db) : IRetentionRule
{
    public string Name => "stay-booking-personalization";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayBookingPersonalDataDays;
        if (days <= 0) return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true });
        var cutoff = ctx.NowUtc.AddDays(-days);
        var cutoffDate = DateOnly.FromDateTime(cutoff);
        IQueryable<StayBooking> Query(Guid cursor) => db.StayBookings
            .Where(b => b.Id > cursor && !b.PersonalDataErased && b.CheckOutDate < cutoffDate && (b.TerminalAtUtc == null || b.TerminalAtUtc < cutoff))
            .OrderBy(b => b.Id);
        return RetentionRuleRunner.RunAsync(Name, Query, b => b.Id,
            mutate: b =>
            {
                StayPersonalData.Erase(b);
                foreach (var e in db.StayBookingEvents.Where(e => e.StayBookingId == b.Id)) StayPersonalData.TombstoneGuestEvent(e);
            },
            ctx, db, ct, dateOf: b => b.TerminalAtUtc ?? b.CheckOutDate.ToDateTime(TimeOnly.MinValue));
    }
}

/// <summary>The journal of a booking is deleted after <see cref="RetentionPeriods.StayBookingEventDays"/> (3 years) from each event (BookingEventRule's shape).</summary>
public sealed class StayBookingEventRule(AppDbContext db) : IRetentionRule
{
    public string Name => "stay-booking-events";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayBookingEventDays;
        if (days <= 0) return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true });
        var cutoff = ctx.NowUtc.AddDays(-days);
        IQueryable<StayBookingEvent> Query(Guid cursor) => db.StayBookingEvents.Where(e => e.Id > cursor && e.OccurredAtUtc < cutoff).OrderBy(e => e.Id);
        return RetentionRuleRunner.RunAsync(Name, Query, e => e.Id, e => db.StayBookingEvents.Remove(e), ctx, db, ct, dateOf: e => e.OccurredAtUtc);
    }
}

/// <summary>A guest's browser subscriptions go <see cref="RetentionPeriods.StayGuestPushSubscriptionDays"/> (7) after the booking ended (a final status or past check-out).</summary>
public sealed class StayGuestPushSubscriptionRule(AppDbContext db) : IRetentionRule
{
    public string Name => "stay-guest-push-subscriptions";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayGuestPushSubscriptionDays;
        if (days <= 0) return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true });
        var cutoff = ctx.NowUtc.AddDays(-days);
        var cutoffDate = DateOnly.FromDateTime(cutoff);
        IQueryable<StayGuestPushSubscription> Query(Guid cursor) => db.StayGuestPushSubscriptions
            .Where(s => s.Id > cursor && s.StayBooking != null && (s.StayBooking.CheckOutDate < cutoffDate || (s.StayBooking.TerminalAtUtc != null && s.StayBooking.TerminalAtUtc < cutoff)))
            .OrderBy(s => s.Id);
        return RetentionRuleRunner.RunAsync(Name, Query, s => s.Id, s => db.StayGuestPushSubscriptions.Remove(s), ctx, db, ct, dateOf: s => s.CreatedAtUtc);
    }
}

public sealed class StayGuestPushNotificationRule(AppDbContext db) : IRetentionRule
{
    public string Name => "stay-guest-push-notifications";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayGuestPushNotificationDays;
        if (days <= 0) return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true });
        var cutoff = ctx.NowUtc.AddDays(-days);
        IQueryable<StayGuestPushNotification> Query(Guid cursor) => db.StayGuestPushNotifications.Where(n => n.Id > cursor && n.CreatedAt < cutoff).OrderBy(n => n.Id);
        return RetentionRuleRunner.RunAsync(Name, Query, n => n.Id, n => db.StayGuestPushNotifications.Remove(n), ctx, db, ct, dateOf: n => n.CreatedAt);
    }
}
