using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using ServiceBooking.Core.Entities;

namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// Cycle 22 D1 — the one definition of "this subscription is in force right now": it exists, is active,
/// and has no end date or an end date not yet passed (<c>PaidUntil &gt;= now</c>, inclusive, as every
/// former copy had it). Whether its PLAN is still active is a separate, caller-specific condition and is
/// deliberately not folded in here.
///
/// Two forms of the same rule: <see cref="IsUsable"/> for an entity already in memory, and
/// <see cref="UsableAt"/> for an EF query (<c>Where(SubscriptionUsability.UsableAt(now))</c>). The one
/// place the rule is still spelled out by hand is the admin billing-accounts list projection
/// (<c>AdminBillingController</c>, cycle 18 B4): there it sits inside a SQL CASE over a LEFT JOINed,
/// possibly-null subscription, which an Expression parameter cannot be spliced into — that copy carries
/// a comment pointing here.
/// </summary>
public static class SubscriptionUsability
{
    public static bool IsUsable([NotNullWhen(true)] AccountSubscription? sub, DateTime now) =>
        sub is not null && sub.IsActive && (!sub.PaidUntil.HasValue || sub.PaidUntil >= now);

    public static Expression<Func<AccountSubscription, bool>> UsableAt(DateTime now) =>
        s => s.IsActive && (!s.PaidUntil.HasValue || s.PaidUntil >= now);
}
