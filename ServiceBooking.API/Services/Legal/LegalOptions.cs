namespace ServiceBooking.API.Services.Legal;

/// <summary>Bound from configuration section "Legal" (ARCHITECTURE.md §4.1, §4.3).</summary>
public class LegalOptions
{
    /// <summary>
    /// Directory containing legal.json and the document HTML files. Empty/unset resolves to
    /// &lt;ContentRoot&gt;/App_Data/legal, the same "empty means the in-repo default" convention
    /// Storage:PrivateRoot/PublicRoot already use.
    /// </summary>
    public string? Root { get; set; }

    /// <summary>Minimum seconds between mtime checks of legal.json. Default 30 (ARCHITECTURE.md §4.3).</summary>
    public int ReloadSeconds { get; set; } = 30;
}
