using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ServiceBooking.API.Services.Orders;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Demo;

/// <summary>What one pass of <see cref="DemoBoardTicker"/> did: orders of the generator it looked at and advanced, visitor orders it closed, orders it left for the next pass.</summary>
public sealed record DemoBoardTickResult(int Scanned, int Advanced, int Closed, int Skipped, string Summary);

/// <summary>
/// ARCHITECTURE_CYCLE35.md §35.10.3 (A35-5) — the "live" board of the demo shops. The generator fixes the status of today's orders at the moment of the reset; this service,
/// called every two minutes by the task <c>demo-board-tick</c> (demo mode only), catches them up along the same plan (<see cref="ShowcaseOrderTimeline"/>), so in the
/// afternoon the morning's orders are already handed over and nothing shows a pickup time that passed an hour ago.
///
/// <list type="bullet">
/// <item><b>Branch A (P0) — orders of the generator</b> (created before the last reset, in a showcase shop, active, pickup day today or earlier): the transitions of the plan that
/// are due are applied one by one, with the PLANNED moments, by the demo employee. An order a visitor has already moved is left alone.</item>
/// <item><b>Branch B (P1) — orders of visitors</b> (created after the reset): once the pickup time has passed by 50 minutes they are closed with a plain reason
/// (<see cref="DemoBoardTickRules.VisitorClosedReason"/>).</item>
/// </list>
///
/// It creates no orders and no notifications (the guard of <c>OrderNotificationPlanner</c> holds in demo mode). The journal and the board revision are written by
/// <see cref="OrderEventLog"/> — still the only writer — in one transaction per order; a visitor pressing a button in the same second wins (the order's version
/// is a concurrency token) and the order is left to the next pass. Both locks of the demo are checked again here, and a pass is skipped while the reset runs.
/// </summary>
public sealed class DemoBoardTicker(
    AppDbContext db, OrderEventLog eventLog, IOptions<DemoModeOptions> options, DemoMaintenanceFlag maintenanceFlag, ILogger<DemoBoardTicker> logger)
{
    /// <summary>The most orders one pass touches per branch: after a long stop the rest is done by the next passes, and a pass never outgrows its minute.</summary>
    private const int BatchSize = 200;

    private sealed record ShopInfo(Guid Id, string Slug, string TimeZoneId, string OwnerUserId);

    public async Task<DemoBoardTickResult> TickAsync(DateTime nowUtc, CancellationToken ct)
    {
        if (!options.Value.Enabled) return Skipped("disabled: not the demo mode");
        if (!await DemoInstanceGuard.IsDemoDatabaseAsync(db, ct)) return Skipped("skipped: the database is not marked as a demo one");
        if (maintenanceFlag.IsResetting(nowUtc)) return Skipped("skipped: the demo is being reset");
        if (await ReadLastResetAsync(ct) is not { } lastReset) return Skipped("skipped: no reset yet");

        var shops = await db.Companies.AsNoTracking().Where(c => c.IsShowcase && c.Kind == CompanyKind.Orders)
            .Select(c => new ShopInfo(c.Id, c.Slug, c.TimeZoneId, c.OwnerUserId)).ToDictionaryAsync(s => s.Id, ct);
        if (shops.Count == 0) return Skipped("skipped: the demo has no shops");

        var actors = new Dictionary<Guid, OrderActor>();
        int scanned = 0, advanced = 0, closed = 0, skipped = 0;

        // ── Branch A ──
        // The scan only finds candidates (no tracking); every order is then reloaded INSIDE its own transaction and decided on its fresh state, so one conflict (or one
        // detached graph) never touches the others.
        var shopIds = shops.Keys.ToList();
        var farthestToday = DateOnly.FromDateTime(nowUtc.AddHours(14)); // the latest "today" of any time zone; the exact day is checked per shop below
        var generated = await db.Orders.AsNoTracking()
            .Where(o => shopIds.Contains(o.CompanyId) && o.CreatedAtUtc <= lastReset && o.PickupDate <= farthestToday
                        && (o.Status == OrderStatus.New || o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready))
            .OrderBy(o => o.PickupStartUtc).ThenBy(o => o.Id).Select(o => new { o.Id, o.CompanyId, o.PickupDate }).Take(BatchSize).ToListAsync(ct);
        foreach (var candidate in generated)
        {
            ct.ThrowIfCancellationRequested();
            var shop = shops[candidate.CompanyId];
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.FindSystemTimeZoneById(shop.TimeZoneId)));
            if (candidate.PickupDate > today) continue;

            scanned++;
            if (!actors.TryGetValue(shop.Id, out var actor)) actors[shop.Id] = actor = await DemoStaffOfAsync(shop, ct);
            switch (await AdvanceAsync(candidate.Id, lastReset, nowUtc, actor, ct))
            {
                case StepOutcome.Done: advanced++; break;
                case StepOutcome.Conflict: skipped++; break;
            }
        }

        // ── Branch B ──
        var grace = nowUtc - DemoBoardTickRules.VisitorOrderGrace;
        var visitors = await db.Orders.AsNoTracking()
            .Where(o => shopIds.Contains(o.CompanyId) && o.CreatedAtUtc > lastReset && (o.PickupEndUtc ?? o.PickupStartUtc) < grace
                        && (o.Status == OrderStatus.New || o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Ready))
            .OrderBy(o => o.PickupStartUtc).ThenBy(o => o.Id).Select(o => o.Id).Take(BatchSize).ToListAsync(ct);
        foreach (var orderId in visitors)
        {
            ct.ThrowIfCancellationRequested();
            scanned++;
            switch (await CloseVisitorOrderAsync(orderId, lastReset, nowUtc, ct))
            {
                case StepOutcome.Done: closed++; break;
                case StepOutcome.Conflict: skipped++; break;
            }
        }

        var summary = $"scanned {scanned}, advanced {advanced}, closed {closed}, left for the next pass {skipped}";
        if (advanced + closed + skipped > 0) logger.LogInformation("demo-board-tick: {Summary}", summary);
        return new DemoBoardTickResult(scanned, advanced, closed, skipped, summary);
    }

    private enum StepOutcome { NothingToDo, Done, Conflict }

    private async Task<StepOutcome> AdvanceAsync(Guid orderId, DateTime lastReset, DateTime nowUtc, OrderActor actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null || !DemoBoardTickRules.IsGeneratorOrder(order.CreatedAtUtc, lastReset)) return StepOutcome.NothingToDo;

            var plan = ShowcaseOrderTimeline.Plan(order.Id, order.CreatedAtUtc, order.PickupStartUtc, order.AcceptanceModeSnapshot);
            var steps = DemoBoardTickRules.DueSteps(order.Status, plan, nowUtc, DemoBoardTickRules.CanIssue(order.HasWeightItems, order.Items.Any(i => i.ReservesStock)));
            if (steps.Count == 0) return StepOutcome.NothingToDo;

            foreach (var step in steps)
            {
                var at = DemoBoardTickRules.JournalMoment(step.AtUtc, order.UpdatedAtUtc);
                var from = order.Status;
                order.Status = step.To;
                order.UpdatedAtUtc = at;
                if (step.Kind == OrderEventKind.Accepted) order.AcceptedAtUtc = at;
                if (step.Kind == OrderEventKind.MarkedReady) order.ReadyAtUtc = at;
                if (OrderStateMachine.IsTerminal(step.To)) order.CompletedAtUtc = at;

                decimal? totalBefore = null, totalAfter = null;
                if (step.Kind == OrderEventKind.Issued)
                {
                    // Pieces only (DemoBoardTickRules.CanIssue): the actual quantity is the ordered one and the final total is the estimate, like the product's issue does.
                    foreach (var item in order.Items)
                    {
                        item.QuantityActual = item.QuantityOrdered;
                        item.LineTotalFinal = item.LineTotalEstimated;
                    }
                    order.FinalTotal = order.EstimatedTotal;
                    totalBefore = order.EstimatedTotal;
                    totalAfter = order.FinalTotal;
                }

                order.Version++;
                await eventLog.AppendAsync(order, step.Kind, actor, from, step.To, totalBefore: totalBefore, totalAfter: totalAfter, occurredAtUtc: at);
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return StepOutcome.Done;
        }
        catch (DbUpdateConcurrencyException)
        {
            // A visitor moved the same order in the same moment: his action wins, the task tries again in two minutes.
            await transaction.RollbackAsync(ct);
            logger.LogInformation("demo-board-tick: order {OrderId} was changed by somebody else meanwhile, left for the next pass", orderId);
            return StepOutcome.Conflict;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<StepOutcome> CloseVisitorOrderAsync(Guid orderId, DateTime lastReset, DateTime nowUtc, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null || DemoBoardTickRules.IsGeneratorOrder(order.CreatedAtUtc, lastReset)) return StepOutcome.NothingToDo;
            if (DemoBoardTickRules.VisitorClosure(order.Status, order.PickupStartUtc, order.PickupEndUtc, nowUtc) is not { } closure) return StepOutcome.NothingToDo;

            var from = order.Status;
            var at = DemoBoardTickRules.JournalMoment(nowUtc, order.UpdatedAtUtc);
            order.Status = closure.To;
            order.StatusReason = DemoBoardTickRules.VisitorClosedReason;
            order.CompletedAtUtc = at;
            order.UpdatedAtUtc = at;
            order.Version++;
            // No stock is written off: none of these three transitions has a write-off in the product either.
            await eventLog.AppendAsync(order, closure.Kind, new OrderActor(OrderActorKind.System, null, null), from, closure.To,
                reason: DemoBoardTickRules.VisitorClosedReason, occurredAtUtc: at);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return StepOutcome.Done;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            logger.LogInformation("demo-board-tick: visitor's order {OrderId} was changed by somebody else meanwhile, left for the next pass", orderId);
            return StepOutcome.Conflict;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>The demo employee of a shop: the first employee of the generator (<c>shop:&lt;key&gt;:s0</c>), the owner when a visitor removed him.</summary>
    private async Task<OrderActor> DemoStaffOfAsync(ShopInfo shop, CancellationToken ct)
    {
        var key = shop.Slug.StartsWith(ShowcaseCatalog.SlugPrefix, StringComparison.Ordinal) ? shop.Slug[ShowcaseCatalog.SlugPrefix.Length..] : shop.Slug;
        var preferred = ShowcaseIds.For(ShowcaseProfile.Demo.Name, "user", $"shop:{key}:s0").ToString();
        var userId = await db.CompanyMembers.AsNoTracking().AnyAsync(m => m.CompanyId == shop.Id && m.UserId == preferred && m.Role == UserRole.Master, ct)
            ? preferred
            : shop.OwnerUserId;
        var name = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (u.FirstName + " " + u.LastName).Trim()).FirstOrDefaultAsync(ct);
        return new OrderActor(OrderActorKind.Staff, userId, name);
    }

    private async Task<DateTime?> ReadLastResetAsync(CancellationToken ct)
    {
        var raw = await db.PlatformSettings.AsNoTracking().Where(s => s.Key == DemoCatalog.LastResetKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;
    }

    private static DemoBoardTickResult Skipped(string summary) => new(0, 0, 0, 0, summary);
}
