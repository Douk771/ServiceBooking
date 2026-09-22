using Xunit;

// All functional tests within a slot share one running app instance and one Postgres database
// (see ApiDatabaseFixture/LegalDatabaseFixture/DispatchDatabaseFixture), so test collections must run
// sequentially, not in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
