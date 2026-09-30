namespace ServiceBooking.API.Services.Demo;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.1 — section <c>DemoMode</c>. Off by default; only the demo stand (a separate compose project, pass B of cycle 28)
/// turns it on, and then <c>DeploymentSafetyChecks.ValidateDemoMode</c> refuses to start unless every address, the database, the JWT issuer and the
/// providers look like a demo instance (§579.2, lock 1).
/// Readers: <see cref="Showcase.ShowcaseOutboundGuard"/> (a demo instance sends nothing to anybody), the demo middleware and filters, <c>DemoController</c>,
/// <c>DemoResetService</c> and the nightly <c>demo-reset</c> task.
/// </summary>
public sealed class DemoModeOptions
{
    public const string SectionName = "DemoMode";

    /// <summary>Demo mode of this instance. A production instance never has it on.</summary>
    public bool Enabled { get; set; }

    /// <summary>Local time of the nightly reset, <c>HH:mm</c>, in <see cref="TimeZoneId"/>.</summary>
    public string ResetLocalTime { get; set; } = "04:00";

    public string TimeZoneId { get; set; } = "Europe/Moscow";

    /// <summary>The "reset in progress" flag file. Shared by the API process and the <c>ops demo reset</c> process (one container, one volume). A relative
    /// path is resolved against the content root.</summary>
    public string MaintenanceFlagPath { get; set; } = "App_Data/state/demo-resetting";

    public bool TryGetResetLocalTime(out TimeOnly time) => TimeOnly.TryParseExact(ResetLocalTime, "HH:mm", out time);
}
