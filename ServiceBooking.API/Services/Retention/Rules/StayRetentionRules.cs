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
            .Where(s => s.Id > cursor && ((s.StayBooking != null && (s.StayBooking.CheckOutDate < cutoffDate || (s.StayBooking.TerminalAtUtc != null && s.StayBooking.TerminalAtUtc < cutoff))) ||
                (s.StayServiceOrderId != null && db.StayServiceOrders.Any(o => o.Id == s.StayServiceOrderId && (o.TerminalAtUtc != null && o.TerminalAtUtc < cutoff ||
                    db.StayServiceSessions.Any(x => x.StayServiceOrderId == o.Id && x.EndUtc < cutoff))))))
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

// ── Cycle 39 (ARCHITECTURE_CYCLE39.md §39.13.3, ЮР39-9) — stand-alone orders of services. A session carries no personal data, so the owner's schedule stays whole. ──

/// <summary>Files of payment proofs of orders: deleted after <see cref="RetentionPeriods.StayPaymentProofDays"/> from the LATER of the end of the session and the final status; the row stays with <c>PurgedAtUtc</c>.</summary>
public sealed class StayServiceOrderPaymentProofRule(AppDbContext db, FileStorage storage) : IRetentionRule
{
    public string Name => "stay-service-order-payment-proofs";

    public async Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayPaymentProofDays;
        if (days <= 0) return new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true };
        var cutoff = ctx.NowUtc.AddDays(-days);
        var scanned = 0;
        var affected = 0;
        var cursor = Guid.Empty;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await db.StayServiceOrders
                .Where(o => o.Id > cursor && o.PaymentProofsPurgedAtUtc == null && (o.TerminalAtUtc == null || o.TerminalAtUtc < cutoff) &&
                            db.StayServiceSessions.Any(s => s.StayServiceOrderId == o.Id && s.EndUtc < cutoff) &&
                            db.StayPaymentProofs.Any(p => p.StayServiceOrderId == o.Id && p.StorageKey != null))
                .OrderBy(o => o.Id).Take(ctx.BatchSize).ToListAsync(ct);
            if (batch.Count == 0) break;
            var keys = new List<string>();
            foreach (var o in batch)
            {
                scanned++;
                var proofs = await db.StayPaymentProofs.Where(p => p.StayServiceOrderId == o.Id && p.StorageKey != null).ToListAsync(ct);
                foreach (var p in proofs)
                {
                    keys.Add(p.StorageKey!);
                    p.StorageKey = null;
                    p.PurgedAtUtc = ctx.NowUtc;
                }
                o.PaymentProofsPurgedAtUtc = ctx.NowUtc;
                db.StayServiceOrderEvents.Add(new StayServiceOrderEvent
                {
                    Id = Guid.NewGuid(), StayServiceOrderId = o.Id, CompanyId = o.CompanyId, Kind = StayServiceOrderEventKind.PaymentProofsPurged, OccurredAtUtc = ctx.NowUtc,
                    ActorKind = StayActorKind.System, ActorNameSnapshot = "Система", DetailsJson = $"{{\"reason\":\"retention\",\"files\":{proofs.Count}}}",
                });
                affected++;
            }
            cursor = batch[^1].Id;
            if (!ctx.DryRun)
            {
                await db.SaveChangesAsync(ct);
                foreach (var key in keys) storage.DeletePrivate(key);
            }
            db.ChangeTracker.Clear();
            if (batch.Count < ctx.BatchSize) break;
        }
        return new RetentionOutcome(Name, scanned, affected, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: scanned={scanned} affected={affected}");
    }
}

/// <summary>Orders "Снят: не оплачен" are depersonalised after <see cref="RetentionPeriods.StayServiceOrderUnpaidDays"/> (30) from the removal.</summary>
public sealed class StayServiceOrderUnpaidPersonalizationRule(AppDbContext db) : IRetentionRule
{
    public string Name => "stay-service-order-unpaid-personalization";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayServiceOrderUnpaidDays;
        if (days <= 0) return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true });
        var cutoff = ctx.NowUtc.AddDays(-days);
        IQueryable<StayServiceOrder> Query(Guid cursor) => db.StayServiceOrders
            .Where(o => o.Id > cursor && !o.PersonalDataErased && o.Status == StayBookingStatus.ExpiredUnpaid && o.TerminalAtUtc != null && o.TerminalAtUtc < cutoff).OrderBy(o => o.Id);
        return RetentionRuleRunner.RunAsync(Name, Query, o => o.Id, StayPersonalData.EraseOrder, ctx, db, ct, dateOf: o => o.TerminalAtUtc!.Value);
    }
}

/// <summary>Other orders are depersonalised <see cref="RetentionPeriods.StayServiceOrderPersonalDataDays"/> (3 years) after the LATER of the end of the session and the final status; their guest events are tombstoned.</summary>
public sealed class StayServiceOrderPersonalizationRule(AppDbContext db) : IRetentionRule
{
    public string Name => "stay-service-order-personalization";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayServiceOrderPersonalDataDays;
        if (days <= 0) return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true });
        var cutoff = ctx.NowUtc.AddDays(-days);
        IQueryable<StayServiceOrder> Query(Guid cursor) => db.StayServiceOrders
            .Where(o => o.Id > cursor && !o.PersonalDataErased && (o.TerminalAtUtc == null || o.TerminalAtUtc < cutoff) &&
                        db.StayServiceSessions.Any(s => s.StayServiceOrderId == o.Id && s.EndUtc < cutoff)).OrderBy(o => o.Id);
        return RetentionRuleRunner.RunAsync(Name, Query, o => o.Id,
            mutate: o =>
            {
                StayPersonalData.EraseOrder(o);
                foreach (var e in db.StayServiceOrderEvents.Where(e => e.StayServiceOrderId == o.Id)) StayPersonalData.TombstoneGuestEvent(e);
            },
            ctx, db, ct, dateOf: o => o.TerminalAtUtc ?? o.CreatedAtUtc);
    }
}

/// <summary>The journal of an order is deleted after <see cref="RetentionPeriods.StayServiceOrderEventDays"/> (3 years) from each event.</summary>
public sealed class StayServiceOrderEventRule(AppDbContext db) : IRetentionRule
{
    public string Name => "stay-service-order-events";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayServiceOrderEventDays;
        if (days <= 0) return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true });
        var cutoff = ctx.NowUtc.AddDays(-days);
        IQueryable<StayServiceOrderEvent> Query(Guid cursor) => db.StayServiceOrderEvents.Where(e => e.Id > cursor && e.OccurredAtUtc < cutoff).OrderBy(e => e.Id);
        return RetentionRuleRunner.RunAsync(Name, Query, e => e.Id, e => db.StayServiceOrderEvents.Remove(e), ctx, db, ct, dateOf: e => e.OccurredAtUtc);
    }
}

/// <summary>The journal of the schedule of a service (it names employees) is deleted after <see cref="RetentionPeriods.StayServiceScheduleEventDays"/> (3 years) from each event.</summary>
public sealed class StayServiceScheduleEventRule(AppDbContext db) : IRetentionRule
{
    public string Name => "stay-service-schedule-events";

    public Task<RetentionOutcome> ApplyAsync(RetentionContext ctx, CancellationToken ct)
    {
        var days = ctx.Periods.StayServiceScheduleEventDays;
        if (days <= 0) return Task.FromResult(new RetentionOutcome(Name, 0, 0, $"retention[{(ctx.DryRun ? "dry" : "live")}] {Name}: срок хранения не настроен, правило пропущено") { Skipped = true });
        var cutoff = ctx.NowUtc.AddDays(-days);
        IQueryable<StayServiceScheduleEvent> Query(Guid cursor) => db.StayServiceScheduleEvents.Where(e => e.Id > cursor && e.OccurredAtUtc < cutoff).OrderBy(e => e.Id);
        return RetentionRuleRunner.RunAsync(Name, Query, e => e.Id, e => db.StayServiceScheduleEvents.Remove(e), ctx, db, ct, dateOf: e => e.OccurredAtUtc);
    }
}
