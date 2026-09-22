namespace ServiceBooking.TestKit;

/// <summary>
/// ARCHITECTURE_CYCLE8_PHASE2.md §91.3: since cycle 8 phase 2, a "slot" is a per-test-class database
/// name segment ("c07", ...), generated once per <c>TestDatabaseFixture</c> by
/// <see cref="NextForClass"/> — <c>TestDatabaseNaming</c>'s regexp (<c>^[a-z][a-z0-9]{0,11}$</c>)
/// already accepts these names unchanged, so the phase-1 defence-in-depth against dropping the wrong
/// database needed zero changes for this.
///
/// <see cref="Api"/>/<see cref="Legal"/>/<see cref="Dispatch"/> are phase 1's fixed slot names — kept as
/// reserved words (never handed out by <see cref="NextForClass"/>) rather than deleted, per §91.3: if the
/// US-95 funnel ever sends a class back to a shared database, these are the names it would reuse.
///
/// T9 review (M3-adjacent move): lives in ServiceBooking.TestKit, not ServiceBooking.Tests.Infrastructure
/// where it was originally written, so ServiceBooking.UnitTests (which references TestKit but not
/// ServiceBooking.Tests — the two test projects don't reference each other) can cover
/// <see cref="NextForClass"/> directly instead of the arithmetic only ever being exercised indirectly by
/// a full functional run.
/// </summary>
public static class TestSlot
{
    public const string Api = "api";
    public const string Legal = "legal";
    public const string Dispatch = "dispatch";

    /// <summary>ARCHITECTURE_CYCLE8_PHASE2.md §91.3 / contracts/cycle8/testkit-status.schema.json's own
    /// `slot` pattern (T9 review, L5): `c[0-9]{2,3}` in the schema, and `^[a-z][a-z0-9]{0,11}$` in
    /// <c>TestDatabaseNaming</c>. `"c{n:D2}"` stays within both only up to n=999 ("c999", 4 digits) — at
    /// n=1000 it silently becomes "c1000", which still satisfies <c>TestDatabaseNaming</c>'s looser
    /// regexp (so nothing would refuse to create/drop it) but no longer matches the schema's `c[0-9]{2,3}`,
    /// meaning `status --json`/`sweep --json` output would silently stop validating against its own
    /// contract for that one resource. §91.4 already calls c999 "запаса хватает навсегда" for a process
    /// that only ever has ~29 classes — this is a documented ceiling with a clear failure message instead
    /// of a silent one, not a scenario expected to fire in practice.</summary>
    private const int MaxClassIndex = 999;

    private static int _classCounter;

    /// <summary>Next never-reused class slot for this process ("c01", "c02", ...) — xUnit v2 does not
    /// pass a class fixture its owning type (§92.2), so this is a bare counter, not a name derived from
    /// the class; <c>TestDatabaseFixture</c> logs the class↔slot pairing itself.</summary>
    public static string NextForClass() => FormatOrThrow(Interlocked.Increment(ref _classCounter));

    /// <summary>Pure formatting/guard logic, split out of <see cref="NextForClass"/> so the L5 overflow
    /// boundary (999 → 1000) is unit-testable without racing the real, process-wide, never-reset
    /// <see cref="_classCounter"/> up to 1000 real increments from a test.</summary>
    internal static string FormatOrThrow(int index)
    {
        if (index > MaxClassIndex)
        {
            throw new TestSafetyException(
                $"[sb-test] Отказ: запрошен тест-класс #{index}, а слоты класса ограничены c01…c{MaxClassIndex} " +
                "(ARCHITECTURE_CYCLE8_PHASE2.md §91.3, contracts/cycle8/testkit-status.schema.json's slot " +
                "pattern c[0-9]{2,3}). Что сделать: это, скорее всего, означает утечку — TestSlot.NextForClass " +
                "вызывается на каждый новый ЭКЗЕМПЛЯР TestDatabaseFixture, а не переиспользуется; проверьте, что " +
                "фикстуры действительно создаются один раз на тест-класс, а не на каждый [Fact].");
        }

        return $"c{index:D2}";
    }
}
