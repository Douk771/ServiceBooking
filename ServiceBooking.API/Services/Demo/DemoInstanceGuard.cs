using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Demo;

/// <summary>What <see cref="DemoInstanceGuard.Decide"/> says about a database at the start of a demo-mode instance.</summary>
public enum DemoInstanceDecision
{
    /// <summary>The database carries the mark <c>instance.kind = demo</c>.</summary>
    Ok,

    /// <summary>No mark, and nothing in the database looks real: the mark is written and the start goes on.</summary>
    WriteMark,

    /// <summary>A mark of another kind: this database belongs to something else.</summary>
    RefuseWrongKind,

    /// <summary>No mark, and the database holds an unmarked company or booking: this is a production database.</summary>
    RefuseRealData,
}

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.2, lock 2 — the DATA lock of demo mode, after migrations and before seeding. The key <c>instance.kind</c> of
/// <c>PlatformSettings</c>:
/// <list type="bullet">
/// <item>present and <c>demo</c> — fine;</item>
/// <item>present and anything else — the start is refused;</item>
/// <item>absent — allowed only if the database has NO company that is not a showcase one and NO booking with <c>ShowcaseKind = None</c>; then the mark is written.
/// Otherwise the start is refused: «БД содержит настоящие данные — это не демо-БД».</item>
/// </list>
/// A production instance neither writes nor reads the key. <c>ops demo reset</c> needs BOTH locks (this mark and <c>DemoMode:Enabled</c>).
/// </summary>
public static class DemoInstanceGuard
{
    public const string RealDataMessage = "БД содержит настоящие данные — это не демо-БД. Демо-режим не запущен.";

    /// <summary>Pure decision from what the database contains.</summary>
    public static DemoInstanceDecision Decide(string? instanceKind, bool hasUnmarkedCompany, bool hasUnmarkedBooking)
    {
        if (instanceKind is not null)
            return string.Equals(instanceKind, DemoCatalog.InstanceKindDemo, StringComparison.Ordinal) ? DemoInstanceDecision.Ok : DemoInstanceDecision.RefuseWrongKind;
        return hasUnmarkedCompany || hasUnmarkedBooking ? DemoInstanceDecision.RefuseRealData : DemoInstanceDecision.WriteMark;
    }

    /// <summary>Applies the lock at start. Throws <see cref="InvalidOperationException"/> when the database must not run as a demo; writes the mark when it is a
    /// fresh, empty one.</summary>
    public static async Task EnsureDemoDatabaseAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var kind = await ReadInstanceKindAsync(db, ct);
        var hasCompany = kind is null && await db.Companies.AnyAsync(c => !c.IsShowcase, ct);
        var hasBooking = kind is null && !hasCompany && await db.Bookings.AnyAsync(b => b.ShowcaseKind == Core.Enums.ShowcaseBookingKind.None, ct);

        switch (Decide(kind, hasCompany, hasBooking))
        {
            case DemoInstanceDecision.Ok:
                return;
            case DemoInstanceDecision.WriteMark:
                db.PlatformSettings.Add(new PlatformSetting
                {
                    Key = DemoCatalog.InstanceKindKey, Value = DemoCatalog.InstanceKindDemo, UpdatedAt = DateTime.UtcNow, UpdatedByUserId = DemoCatalog.StampedBy,
                });
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Demo database marked: {Key} = {Value}", DemoCatalog.InstanceKindKey, DemoCatalog.InstanceKindDemo);
                return;
            case DemoInstanceDecision.RefuseWrongKind:
                throw new InvalidOperationException(
                    $"DemoMode:Enabled is true, but the database is marked {DemoCatalog.InstanceKindKey} = '{kind}', not '{DemoCatalog.InstanceKindDemo}'. " +
                    "This database belongs to another kind of instance: the demo does not start on it.");
            default:
                throw new InvalidOperationException(
                    "DemoMode:Enabled is true, but " + RealDataMessage + " Point the demo at its own empty database (name ending in _demo).");
        }
    }

    /// <summary>The mark of the database, or null when there is none.</summary>
    public static Task<string?> ReadInstanceKindAsync(AppDbContext db, CancellationToken ct = default) =>
        db.PlatformSettings.AsNoTracking().Where(s => s.Key == DemoCatalog.InstanceKindKey).Select(s => s.Value).FirstOrDefaultAsync(ct);

    /// <summary>True when the database carries the demo mark (lock 2 of <c>ops demo reset</c>).</summary>
    public static async Task<bool> IsDemoDatabaseAsync(AppDbContext db, CancellationToken ct = default) =>
        string.Equals(await ReadInstanceKindAsync(db, ct), DemoCatalog.InstanceKindDemo, StringComparison.Ordinal);
}
