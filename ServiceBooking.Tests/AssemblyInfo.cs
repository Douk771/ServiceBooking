using Xunit;

// ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.1/§98.1: every test class now gets its own database, cloned from
// the run's shared template, via a per-class TestDatabaseFixture (TestSlot.NextForClass) — the isolation
// this attribute originally protected (a single shared "servicebooking_test"/slot database) no longer
// exists. This line is kept ON deliberately, as ONE piece: cycle 8 phase 2 (T8-P3…P6) lands the class-
// per-database refactor BEFORE actually enabling parallel execution, so the suite stays provably
// sequential (465/465, one class at a time) while that refactor is reviewed. Flipping parallelism on is
// T8-P7, a separate, later step, gated on a second recon pass (§97.1) — removing this single line is that
// switch; putting it back is the whole rollback (§98.1).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
