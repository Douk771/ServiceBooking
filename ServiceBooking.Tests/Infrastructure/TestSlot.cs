namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// ARCHITECTURE_CYCLE8_PHASE2.md §91.3: since cycle 8 phase 2, a "slot" is a per-test-class database
/// name segment ("c07", ...), generated once per <see cref="TestDatabaseFixture"/> by
/// <see cref="NextForClass"/> — <c>TestDatabaseNaming</c>'s regexp (<c>^[a-z][a-z0-9]{0,11}$</c>)
/// already accepts these names unchanged, so the phase-1 defence-in-depth against dropping the wrong
/// database needed zero changes for this.
///
/// <see cref="Api"/>/<see cref="Legal"/>/<see cref="Dispatch"/> are phase 1's fixed slot names — kept as
/// reserved words (never handed out by <see cref="NextForClass"/>) rather than deleted, per §91.3: if the
/// US-95 funnel ever sends a class back to a shared database, these are the names it would reuse.
/// </summary>
public static class TestSlot
{
    public const string Api = "api";
    public const string Legal = "legal";
    public const string Dispatch = "dispatch";

    private static int _classCounter;

    /// <summary>Next never-reused class slot for this process ("c01", "c02", ...) — xUnit v2 does not
    /// pass a class fixture its owning type (§92.2), so this is a bare counter, not a name derived from
    /// the class; <see cref="TestDatabaseFixture"/> logs the class↔slot pairing itself.</summary>
    public static string NextForClass() => $"c{Interlocked.Increment(ref _classCounter):D2}";
}
