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
        var normalizedPage = page is >= 1 ? page.Value : 1;
        var normalizedPageSize = pageSize switch
        {
            null or <= 0 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize.Value
        };
        return (normalizedPage, normalizedPageSize);
    }

    public static PagedResult<T> Create<T>(IReadOnlyList<T> items, int page, int pageSize, int total) =>
        new(items, page, pageSize, total, (long)page * pageSize < total);
}
