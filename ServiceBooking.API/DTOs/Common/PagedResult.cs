namespace ServiceBooking.API.DTOs.Common;

/// <summary>The one envelope shape for all four paginated selections (US-49, ARCHITECTURE.md §15.1).</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total, bool HasNext);

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
}
