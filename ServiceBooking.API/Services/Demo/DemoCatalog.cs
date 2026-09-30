namespace ServiceBooking.API.Services.Demo;

/// <summary>ARCHITECTURE_CYCLE28.md §579–§580 — keys of the demo instance in <c>PlatformSettings</c>. Platform settings survive the nightly reset (they are on the
/// allow-list of the TRUNCATE), so the instance mark and the time of the last reset are still there afterwards.</summary>
public static class DemoCatalog
{
    /// <summary>Instance mark (lock 2 of §579.2): <c>demo</c> on a demo database.</summary>
    public const string InstanceKindKey = Showcase.ShowcaseCatalog.InstanceKindKey;

    public const string InstanceKindDemo = "demo";

    /// <summary>ISO-8601 UTC time of the last finished reset.</summary>
    public const string LastResetKey = "demo.last-reset-utc";

    /// <summary>Who stamped the settings the demo writes itself (there is no user).</summary>
    public const string StampedBy = "demo";
}
