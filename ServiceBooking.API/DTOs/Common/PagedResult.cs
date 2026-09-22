namespace ServiceBooking.API.DTOs.Common;

/// <summary>The one envelope shape for all four paginated selections (US-49, ARCHITECTURE.md §15.1).</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total, bool HasNext);

/// <summary>
/// Cycle 5 billing-admin envelope (contracts/cycle7/openapi.yaml PagedAdminBillingAccounts /
/// PagedAdminSubscriptionRequests, `additionalProperties: false`, required
/// [items, page, pageSize, totalCount]) — deliberately a SEPARATE shape from <see cref="PagedResult{T}"/>
/// rather than a rename of it. Renaming PagedResult itself would also change the wire shape of the four
/// pre-cycle-5 endpoints (AdminController users/companies/channels, ReviewsController, MastersController,
/// CompanyNotificationsController) that this cycle's contract does not govern and did not ask to change —
/// out of scope here, and a needless breaking change for whatever consumes them today (§3.11 in
/// CURRENT_STATE.md/API_CONTRACT.md documents `{items,page,pageSize,total,hasNext}` as the established,
/// still-current convention for those). Only the two cycle-5 billing-admin list endpoints use this one.
/// </summary>
public record ContractPagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>
/// Normalizes the two query parameters every paginated endpoint accepts, and builds the envelope —
/// one place so "page &lt; 1 → 1" and "pageSize &gt; 100 → clamped, not 400" (US-49 p.6) can't drift
/// between the four call sites.
/// </summary>
public static class Pagination
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize)
    {
        var normalizedPageSize = pageSize switch
        {
            null or <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value
        };

        // Every call site computes an int offset as (page - 1) * pageSize (e.g. AdminController,
        // ReviewsController, MastersController). page is caller-supplied and unbounded above — a huge
        // page (up to int.MaxValue) times pageSize overflows a 32-bit int, producing a negative OFFSET
        // that either throws at the DB (public, anonymous endpoints included) or, for in-memory LINQ,
        // silently wraps Skip's clamping and returns page 1's data instead of an empty page. Clamping
        // here — the one place all four call sites funnel through — keeps every call site overflow-safe
        // without each of them having to know why.
        var maxPage = int.MaxValue / normalizedPageSize;
        var normalizedPage = page switch
        {
            null or < 1 => 1,
            _ when page.Value > maxPage => maxPage,
            _ => page.Value
        };

        return (normalizedPage, normalizedPageSize);
    }

    public static PagedResult<T> Create<T>(IReadOnlyList<T> items, int page, int pageSize, int total) =>
        new(items, page, pageSize, total, (long)page * pageSize < total);

    /// <summary>See <see cref="ContractPagedResult{T}"/> — the cycle-5 billing-admin envelope only.</summary>
    public static ContractPagedResult<T> CreateContract<T>(IReadOnlyList<T> items, int page, int pageSize, int totalCount) =>
        new(items, page, pageSize, totalCount);

    /// <summary>
    /// The one place every `?search=` query parameter (AdminController users/companies,
    /// AdminBillingController billing-accounts, CitiesController, MastersController) funnels through
    /// before it reaches a `.Contains(...)`/LIKE translation.
    ///
    /// Postgres' `text` type rejects an embedded NUL byte (U+0000) outright — Npgsql surfaces that as an
    /// unhandled exception (500), not a query that simply matches nothing — and other C0/C1 control
    /// characters (form feed, escape sequences, etc.) are never meaningful in a name/phone/email search
    /// anyway. Strips every Unicode control character (category Cc) rather than allow-listing ASCII, so
    /// non-Latin search text (Cyrillic company names, etc.) is left untouched — only the class of
    /// characters that actually caused the crash is removed. Returns null (not empty string) once
    /// stripping leaves nothing searchable, so call sites' existing `IsNullOrWhiteSpace` guard still
    /// short-circuits an all-control-character input the same way it already does for blank input.
    /// </summary>
    public static string? SanitizeSearch(string? search)
    {
        if (string.IsNullOrEmpty(search)) return search;
        var cleaned = new string(search.Where(c => !char.IsControl(c)).ToArray());
        return cleaned.Length == 0 ? null : cleaned;
    }
}
