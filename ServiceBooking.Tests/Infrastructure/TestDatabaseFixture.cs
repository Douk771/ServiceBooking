using ServiceBooking.TestKit;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// ARCHITECTURE_CYCLE8_PHASE2.md §91/§92.2 — one instance per test class (<c>IClassFixture</c>, not
/// <c>ICollectionFixture</c>): takes this class' own slot ("c07", <see cref="TestSlot.NextForClass"/>),
/// clones a fresh database for it from the run's template, boots the one shared
/// <see cref="CustomWebApplicationFactory"/> most functional tests in the class run against, and hands
/// out this class' <see cref="TestData"/> generator.
///
/// Replaces phase 1's <c>SlotDatabaseFixture</c>/<c>ApiDatabaseFixture</c>/<c>LegalDatabaseFixture</c>/
/// <c>DispatchDatabaseFixture</c> trio, which shared exactly three fixed databases ("api"/"legal"/
/// "dispatch") across the whole assembly — the shared suite state (US-96…US-98) that made those three
/// databases unsafe to run in parallel. A class database is created once per class and dropped once the
/// class finishes; <c>EnsureDeletedAsync</c> is still never called on it anywhere (§69.3/§79 п.1 remain
/// in force) — teardown is always DROP DATABASE, guarded by <see cref="TestDatabaseNaming.EnsureOwnedByThisRun"/>.
/// </summary>
public sealed class TestDatabaseFixture : IAsyncLifetime
{
    private TestClassDatabaseLease _lease = null!;

    /// <summary>This test class' database slot ("c07") — xUnit v2 never hands a class fixture its owning
    /// type (§92.2), so this is a bare per-process counter value, not derived from the class name; each
    /// base class logs the slot↔class pairing itself on first use (see <see cref="ApiTestBase"/>/
    /// <see cref="NotificationTestBase"/>).</summary>
    public string ClassSlot { get; private set; } = null!;

    /// <summary>This class' host identity (SuperAdmin credentials, file roots, connection string, ...) —
    /// see <see cref="TestHostSettings"/>. Built from the "api" factoryTag; a class that boots a
    /// secondary, differently-configured host (e.g. <see cref="NotificationDispatchTestFactory"/>) calls
    /// <c>TestHostSettings.Apply</c> again with its own factoryTag but the SAME <see cref="ClassSlot"/>/
    /// <see cref="ConnectionString"/>, so every host in the class still shares one database and one set
    /// of file roots.</summary>
    public TestHostIdentity Identity { get; private set; } = null!;

    /// <summary>This class' database connection string — shorthand for <c>Identity.ConnectionString</c>,
    /// kept so the many call sites written against phase 1's <c>fixture.ConnectionString</c> compile
    /// unchanged.</summary>
    public string ConnectionString => Identity.ConnectionString;

    public CustomWebApplicationFactory Factory { get; private set; } = null!;

    /// <summary>This class' single source of collision-free unique values (§94, Q12).</summary>
    public TestData Data { get; private set; } = null!;

    private int _testClassRecorded;

    /// <summary>T9 review (M3): call once from the owning base class' constructor (the first — and only
    /// — place that actually knows <c>GetType().Name</c>, since xUnit v2 never hands a class fixture its
    /// own class — §92.2) so the slot↔class pairing this doc comment already promised is actually
    /// recorded, not just logged and forgotten. Logs unconditionally (cheap, always useful locally);
    /// annotates the database itself (so a STUCK database found later by <c>status</c>/<c>sweep</c> can
    /// still be traced back) only once per fixture, guarded here rather than relying on every call site
    /// to dedupe — xUnit constructs a fresh test-class instance per <c>[Fact]</c>, so this runs once per
    /// TEST, not once per CLASS, without this guard.</summary>
    public void RecordTestClass(string testClassName)
    {
        Console.WriteLine($"[sb-test] class={testClassName} slot={ClassSlot} db={Identity.DatabaseName}");

        if (Interlocked.Exchange(ref _testClassRecorded, 1) == 1)
            return;

        // Fire-and-forget: this is diagnostics, not part of the test's own behaviour, and every test
        // constructor is synchronous — nothing here may block or fail the test.
        _ = _lease.RecordTestClassAsync(testClassName);
    }

    public async Task InitializeAsync()
    {
        ClassSlot = TestSlot.NextForClass();
        _lease = await TestRunEnvironment.LeaseClassDatabaseAsync(ClassSlot);
        Data = new TestData(ClassSlot);

        Factory = new CustomWebApplicationFactory(_lease.ConnectionString);

        // Touching Services boots the host, which runs Program.cs's migrate + role/SuperAdmin seed, and
        // populates Factory.Identity (ConfigureWebHost's return value).
        _ = Factory.Services;
        Identity = Factory.Identity;
    }

    public async Task DisposeAsync()
    {
        // Factory.DisposeAsync() stopping the host is not guaranteed to succeed (e.g. a background
        // dispatcher that never finished starting) -- the lease must still be released so the class
        // database is dropped and the process-wide refcount is decremented; otherwise a host-stop
        // failure leaks the database (and, if this was the last class, the whole container) for the
        // rest of the process.
        try
        {
            await Factory.DisposeAsync();
        }
        finally
        {
            await _lease.DropAsync();
        }
    }
}
