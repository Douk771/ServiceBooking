namespace ServiceBooking.API.Services;

/// <summary>
/// The one place that turns user text into an ILIKE "contains" pattern (ARCHITECTURE_CYCLE25.md §501.2): '%', '_' and the escape character
/// itself are escaped, so a search for "_" or "%" matches those characters and not "any character". Pure.
/// </summary>
public static class LikePattern
{
    public static string Escape(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public static string Contains(string value) => $"%{Escape(value)}%";
}
