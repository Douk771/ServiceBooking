namespace ServiceBooking.API.Services.Billing;

/// <summary>
/// ARCHITECTURE_CYCLE19.md §384.1/§400 — the ONE place that computes an account's employee/company
/// limit after cycle 19. Purchased "extra-*" options no longer contribute anything: the limit is
/// exactly the tariff's field plus the account's grandfathered employee bonus. Pure function, no DB
/// access, so every caller (resolver, 409 overflow check on plan assignment, 402 text builders) shares
/// one arithmetic instead of three copies drifting apart.
/// </summary>
public static class AccountLimitFormula
{
    /// <summary>
    /// Computes the effective employee/company limit. <c>null</c> in a plan field means "unlimited" —
    /// the bonus is never added to <c>null</c> (an unlimited plan stays unlimited). A negative bonus is
    /// treated as zero (defensive; the bonus is meant to only ever grow or stay at zero).
    /// </summary>
    public static (int? Employees, int? Companies) Compute(
        int? planMaxEmployees, int? planMaxCompanies, int grandfatheredEmployeeBonus)
    {
        var bonus = Math.Max(grandfatheredEmployeeBonus, 0);
        var employees = planMaxEmployees is { } maxEmployees ? maxEmployees + bonus : (int?)null;
        return (employees, planMaxCompanies);
    }
}
