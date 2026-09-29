using Microsoft.EntityFrameworkCore;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE19.md §384.1/§382 — the ONE place that defines "retired limit option": a catalog
/// option whose <see cref="SubscriptionOption.CapabilityKey"/>, after <c>Trim()</c> and
/// <c>ToLowerInvariant()</c>, is <see cref="CapabilityKeys.Employees"/> or
/// <see cref="CapabilityKeys.Companies"/>. After cycle 19 the account limit is the tariff field plus
/// the grandfathered bonus only (<see cref="AccountLimitFormula"/>) — these options are never read as
/// a live limit contributor anywhere, but their rows (<see cref="SubscriptionOption"/>,
/// <see cref="PlanOptionRule"/>, <see cref="AccountSubscriptionOption"/>, requested-options JSON) are
/// never deleted or rewritten either (§383.2).
/// </summary>
public static class RetiredLimitOptions
{
    public static bool IsRetiredCapability(string? capabilityKey) =>
        capabilityKey is not null &&
        capabilityKey.Trim().ToLowerInvariant() is CapabilityKeys.Employees or CapabilityKeys.Companies;

    public static bool IsRetired(SubscriptionOption option) => IsRetiredCapability(option.CapabilityKey);

    /// <summary>Server-side filter, translated by EF to <c>btrim(lower(...))</c> so it runs in SQL, not
    /// in memory — every listing endpoint (§386.1) composes this instead of re-deriving the
    /// normalization inline.</summary>
    public static IQueryable<SubscriptionOption> WhereNotRetired(this IQueryable<SubscriptionOption> q) =>
        q.Where(o => o.CapabilityKey == null ||
            (o.CapabilityKey.Trim().ToLower() != CapabilityKeys.Employees &&
             o.CapabilityKey.Trim().ToLower() != CapabilityKeys.Companies));

    public static IQueryable<PlanOptionRule> WhereNotRetired(this IQueryable<PlanOptionRule> q) =>
        q.Where(r => r.Option.CapabilityKey == null ||
            (r.Option.CapabilityKey.Trim().ToLower() != CapabilityKeys.Employees &&
             r.Option.CapabilityKey.Trim().ToLower() != CapabilityKeys.Companies));

    public static IQueryable<AccountSubscriptionOption> WhereNotRetired(this IQueryable<AccountSubscriptionOption> q) =>
        q.Where(o => o.Option.CapabilityKey == null ||
            (o.Option.CapabilityKey.Trim().ToLower() != CapabilityKeys.Employees &&
             o.Option.CapabilityKey.Trim().ToLower() != CapabilityKeys.Companies));

    /// <summary>
    /// ARCHITECTURE_CYCLE19.md §385.2 — the LINQ twin of
    /// <c>deploy/checks/cycle19-retired-limit-options-live.sql</c>: rows the deploy gate and the
    /// startup report both call "not finished" — a retired-limit-option row that is not closed
    /// (<c>EndsAtUtc</c> null or in the future) and whose own paid-through date, if any, has not
    /// passed. Subscription state and plan rule are deliberately NOT considered (§385.2) — this is
    /// stricter than "does it count today".
    /// </summary>
    public static IQueryable<AccountSubscriptionOption> LiveRetiredRows(AppDbContext db, DateTime nowUtc) =>
        db.AccountSubscriptionOptions
            .Where(o => o.Option.CapabilityKey != null &&
                (o.Option.CapabilityKey.Trim().ToLower() == CapabilityKeys.Employees ||
                 o.Option.CapabilityKey.Trim().ToLower() == CapabilityKeys.Companies))
            .Where(o => o.EndsAtUtc == null || o.EndsAtUtc > nowUtc)
            .Where(o => o.PaidUntilUtc == null || o.PaidUntilUtc >= nowUtc);
}
