using ServiceBooking.TestKit;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Base for the three slot fixtures (ARCHITECTURE_CYCLE8.md §66/§70.1). Each fixture registers with the
/// process-wide <see cref="TestRunEnvironment"/> on <see cref="InitializeAsync"/> and unregisters on
/// <see cref="DisposeAsync"/> — the shared server/databases are created once, by whichever fixture
/// initializes first, and torn down once, by whichever releases last.
/// </summary>
public abstract class SlotDatabaseFixture : IAsyncLifetime
{
    protected abstract string Slot { get; }

    /// <summary>Connection string for this fixture's slot database. Only valid after
    /// <see cref="InitializeAsync"/> has run — xUnit guarantees that before any test in the owning
    /// collection executes.</summary>
    public string ConnectionString { get; private set; } = null!;

    public virtual async Task InitializeAsync()
    {
        var lease = await TestRunEnvironment.AcquireAsync();
        ConnectionString = lease.ConnectionStringFor(Slot);
    }

    public virtual Task DisposeAsync() => TestRunEnvironment.ReleaseAsync();
}

/// <summary>
/// Collection fixture for the "Api" xUnit collection: owns the <see cref="TestSlot.Api"/> database and
/// the single shared <see cref="CustomWebApplicationFactory"/> most functional tests run against.
/// Replaces the old TestDatabaseFixture, which wiped and re-migrated a single, hardcoded
/// "servicebooking_test" database — that database is now one of three per-run, per-slot databases
/// created by <see cref="TestRunEnvironment"/>, and nothing ever calls EnsureDeletedAsync on it (§69.3):
/// each run gets its own fresh database instead of wiping a shared one.
/// </summary>
public sealed class ApiDatabaseFixture : SlotDatabaseFixture
{
    protected override string Slot => TestSlot.Api;

    public CustomWebApplicationFactory Factory { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();

        Factory = new CustomWebApplicationFactory(ConnectionString);

        // Touching Services boots the host, which runs Program.cs's migrate + role/SuperAdmin seed.
        _ = Factory.Services;
    }

    public override async Task DisposeAsync()
    {
        // Factory.DisposeAsync() stopping the host is not guaranteed to succeed (e.g. a background
        // dispatcher that never finished starting) -- base.DisposeAsync() (TestRunEnvironment.ReleaseAsync)
        // must still run so the shared refcount is decremented and the container/databases are torn
        // down; otherwise a host-stop failure leaks the environment for the rest of the process.
        try
        {
            await Factory.DisposeAsync();
        }
        finally
        {
            await base.DisposeAsync();
        }
    }
}

/// <summary>
/// Collection fixture (also part of the "Api" xUnit collection, alongside <see cref="ApiDatabaseFixture"/>
/// — a collection definition may back more than one <c>ICollectionFixture</c>) owning the
/// <see cref="TestSlot.Legal"/> database, used by <see cref="LegalDocumentsTestFactory"/> and the classes
/// that construct one directly.
/// </summary>
public sealed class LegalDatabaseFixture : SlotDatabaseFixture
{
    protected override string Slot => TestSlot.Legal;
}

/// <summary>
/// Collection fixture for the "NotificationDispatch" xUnit collection: owns the
/// <see cref="TestSlot.Dispatch"/> database used by <see cref="NotificationDispatchTestFactory"/>.
/// </summary>
public sealed class DispatchDatabaseFixture : SlotDatabaseFixture
{
    protected override string Slot => TestSlot.Dispatch;
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiDatabaseFixture>, ICollectionFixture<LegalDatabaseFixture>;
