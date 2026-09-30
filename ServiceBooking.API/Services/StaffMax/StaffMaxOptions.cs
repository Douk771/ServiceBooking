namespace ServiceBooking.API.Services.StaffMax;

/// <summary>
/// ARCHITECTURE_CYCLE25.md §510.3 — <c>Notifications:StaffMax</c>. <see cref="Enabled"/> is the platform switch and is <c>false</c> in the repository:
/// a person turns it on with <c>STAFFMAX_ENABLED=true</c> on the machine (Q-25-2, DEPLOY.md §23).
/// </summary>
public sealed class StaffMaxOptions
{
    public const string SectionName = "Notifications:StaffMax";

    public bool Enabled { get; set; }
    public int LinkSessionTtlMinutes { get; set; } = 10;
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>Parallel sends of the dispatcher (§499.3): leaves most of MAX's 30 rps to the phone confirmation.</summary>
    public int MaxParallel { get; set; } = 4;
    public int MaxMessagesPerSecond { get; set; } = 10;
    public int MaxAttempts { get; set; } = 4;
    public int BatchSize { get; set; } = 100;

    /// <summary>A row attempted within this window is treated as in flight and not picked again (the push rule).</summary>
    public int InFlightGraceMinutes { get; set; } = 5;
}
