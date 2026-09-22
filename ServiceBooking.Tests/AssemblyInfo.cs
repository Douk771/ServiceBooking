using Xunit;

// T8-P11a (US-100, ARCHITECTURE_CYCLE8_PHASE2.md §99.2): randomizes test order WITHIN a class every
// run, with a reproducible seed (ServiceBooking.Tests/Infrastructure/RandomTestCaseOrderer.cs), to
// surface hidden "test B only passes after test A" ordering dependencies instead of letting a stable
// default order hide them indefinitely.
[assembly: TestCaseOrderer("ServiceBooking.Tests.Infrastructure.RandomTestCaseOrderer", "ServiceBooking.Tests")]

// ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.1/§98.1: every test class now gets its own database, cloned from
// the run's shared template, via a per-class TestDatabaseFixture (TestSlot.NextForClass) — the isolation
// this attribute originally protected (a single shared "servicebooking_test"/slot database) no longer
// exists, so class-level parallel execution is safe and is now ON (T8-P7; xunit.runner.json/§93.1 sets
// maxParallelThreads).
//
// ROLLBACK (§98.1, one line): if a flaky/racy test surfaces that class-per-database isolation alone does
// not explain, uncomment the line below to force the whole assembly back to sequential execution while
// it is diagnosed. That is the entire rollback — no other file needs to change.
// [assembly: CollectionBehavior(DisableTestParallelization = true)]
