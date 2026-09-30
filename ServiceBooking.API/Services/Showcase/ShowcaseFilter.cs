namespace ServiceBooking.API.Services.Showcase;

/// <summary>The <c>showcase</c> query parameter of the admin lists (API_CONTRACT_CYCLE28.md §594.1).</summary>
public enum ShowcaseFilter { All, Only, Exclude }

public static class ShowcaseFilterParser
{
    public const string InvalidText = "showcase должен быть одним из: all, only, exclude.";

    /// <summary>Not passed or blank means <see cref="ShowcaseFilter.All"/> (the behaviour before cycle 28). Matching ignores case; any other
    /// value is refused (400), unlike the older lenient filters — this parameter is new, there is no legacy caller to keep silently compatible.</summary>
    public static bool TryParse(string? raw, out ShowcaseFilter filter)
    {
        filter = ShowcaseFilter.All;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        switch (raw.Trim().ToLowerInvariant())
        {
            case "all": return true;
            case "only": filter = ShowcaseFilter.Only; return true;
            case "exclude": filter = ShowcaseFilter.Exclude; return true;
            default: return false;
        }
    }
}
