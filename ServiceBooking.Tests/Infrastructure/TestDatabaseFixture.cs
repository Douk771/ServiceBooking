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
    private const string TestConnectionString =
        "Host=localhost;Database=servicebooking_test;Username=postgres;Password=";

    public CustomWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(TestConnectionString)
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
