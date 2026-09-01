using Microsoft.EntityFrameworkCore;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Collection fixture: wipes the "servicebooking_test" database exactly once before any
/// test in the collection runs, then boots a single shared CustomWebApplicationFactory
/// (whose own Program.cs startup code re-applies migrations and seeds roles/SuperAdmin).
/// </summary>
public class TestDatabaseFixture : IAsyncLifetime
{
    // Overridable via SERVICEBOOKING_TEST_CONNECTION so CI (and any dev box with a differently
    // configured Postgres) can point the whole suite at a different database without editing code —
    // falls back to the same literal that has always worked locally when the variable isn't set.
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("SERVICEBOOKING_TEST_CONNECTION")
        ?? "Host=localhost;Database=servicebooking_test;Username=postgres;Password=";

    public CustomWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using (var db = new AppDbContext(options))
        {
            // Drop first so the factory's own startup migration+seed logic runs against a clean schema.
            await db.Database.EnsureDeletedAsync();
        }

        Factory = new CustomWebApplicationFactory();

        // Touching Services boots the host, which runs Program.cs's migrate + role/SuperAdmin seed.
        _ = Factory.Services;
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }
}

[CollectionDefinition("Api")]
public class ApiCollection : ICollectionFixture<TestDatabaseFixture>;
