namespace ServiceBooking.API.Services;

/// <summary>
/// Date filters bound from the query string arrive as Kind=Unspecified (a plain date like
/// <c>?from=2026-09-01</c>) or Kind=Local (an ISO value with 'Z' or an offset — the MVC binder converts
/// it to server-local time). Npgsql 6+ writes only Kind=Utc into a timestamptz parameter and throws
/// otherwise, so every such filter must pass through here before it reaches a query.
/// A plain date is read as UTC; an explicit instant keeps its moment.
/// </summary>
public static class QueryDateTime
{
    public static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Unspecified } v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
        { } v => v.ToUniversalTime(),
    };
}
