namespace ServiceBooking.API.Services.Demo;

/// <summary>
/// ARCHITECTURE_CYCLE28.md §579.1 — section <c>DemoMode</c>. Off by default; only the demo stand (a separate compose project, pass B of cycle 28)
/// turns it on. In pass A it is read by exactly one consumer, <see cref="Showcase.ShowcaseOutboundGuard"/>: a demo instance sends nothing to anybody.
/// The rest of the section (reset time, maintenance flag, start-up locks) arrives with the demo stand itself.
/// </summary>
public sealed class DemoModeOptions
{
    public const string SectionName = "DemoMode";

    public bool Enabled { get; set; }
}
