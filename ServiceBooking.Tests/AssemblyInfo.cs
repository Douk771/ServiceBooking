using Xunit;

// All functional tests share one running app instance and one Postgres database
// (see TestDatabaseFixture), so test collections must run sequentially, not in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
