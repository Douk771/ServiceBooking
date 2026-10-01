using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 36 guard: the seeded demo template (<see cref="DemoSeededTemplate"/>) must not drift away from the product. One database gets the real product reset, another is
/// cloned from the template; both must hold the same rows (count of every table, the identity of companies and users) and the same published files.
/// </summary>
[Collection("DemoSeededGuard")]
public class DemoSeededTemplateGuardTests : IAsyncLifetime
{
    private DemoScenarioState _real = null!;
    private DemoScenarioState _clone = null!;

    public async Task InitializeAsync()
    {
        _real = await DemoScenarioState.CreateAsync(DemoDatabaseSlots.GuardReal, seeded: false);
        _clone = await DemoScenarioState.CreateAsync(DemoDatabaseSlots.GuardClone, seeded: true);
    }

    public async Task DisposeAsync()
    {
        try { if (_clone is not null) await _clone.DisposeAsync(); }
        finally { if (_real is not null) await _real.DisposeAsync(); }
    }

    private static async Task<Dictionary<string, long>> TableCountsAsync(DemoScenarioState state)
    {
        using var scope = state.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var list = new NpgsqlCommand("select table_name from information_schema.tables where table_schema = 'public' and table_type = 'BASE TABLE' order by 1", connection))
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            await using var count = new NpgsqlCommand($"select count(*) from \"{table}\"", connection);
            counts[table] = (long)(await count.ExecuteScalarAsync())!;
        }
        return counts;
    }

    private static async Task<List<string>> IdentityAsync(DemoScenarioState state)
    {
        using var scope = state.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var companies = await db.Companies.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id + "|" + c.Slug + "|" + c.Name).ToListAsync();
        var users = await db.Users.AsNoTracking().Where(u => u.IsShowcase).OrderBy(u => u.Id).Select(u => u.Id + "|" + u.PhoneNumber).ToListAsync();
        // Bookings and orders are counted per table above; here only what the generator makes deterministic (shops, demo people; the SuperAdmin of the host is random).
        return [.. companies, .. users];
    }

    private static string[] Files(string slot) =>
        Directory.Exists(DemoSeededTemplate.FileRootOf(slot))
            ? [.. new[] { "public", "private" }.SelectMany(part => Directory.Exists(Path.Combine(DemoSeededTemplate.FileRootOf(slot), part))
                ? Directory.EnumerateFiles(Path.Combine(DemoSeededTemplate.FileRootOf(slot), part), "*", SearchOption.AllDirectories)
                    .Select(f => part + "/" + Path.GetRelativePath(Path.Combine(DemoSeededTemplate.FileRootOf(slot), part), f))
                : []).Order()]
            : [];

    [Fact, TestCase("CY36-03")]
    public async Task ClonedTemplate_HoldsTheSameRowsAndFiles_AsTheRealProductReset()
    {
        var real = await TableCountsAsync(_real);
        var clone = await TableCountsAsync(_clone);

        real.Keys.Should().BeEquivalentTo(clone.Keys);
        real.Sum(p => p.Value).Should().BeGreaterThan(5000, "the demo is a full stand, not an empty database");
        var different = real.Where(p => clone[p.Key] != p.Value).Select(p => $"{p.Key}: real {p.Value}, clone {clone[p.Key]}").ToList();
        different.Should().BeEmpty("a table of the seeded template differs from a real reset");

        (await IdentityAsync(_clone)).Should().Equal(await IdentityAsync(_real), "ids, slugs, names and phones are deterministic for one date");
        Files(DemoDatabaseSlots.GuardClone).Should().Equal(Files(DemoDatabaseSlots.GuardReal), "the published pictures are copied with the template");
    }
}
