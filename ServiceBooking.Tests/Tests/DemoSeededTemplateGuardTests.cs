using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using ServiceBooking.Infrastructure.Data;
using ServiceBooking.Tests.Infrastructure;

namespace ServiceBooking.Tests.Tests;

/// <summary>
/// Cycle 36 guard: the seeded demo template (<see cref="DemoSeededTemplate"/>) must not drift away from the product. One database gets the real product reset, another is
/// cloned from the template; both must hold the same rows (count of every table, the content (hash of the deterministic columns) of the key tables) and the same published files.
/// </summary>
[Collection("DemoSeededGuard")]
public class DemoSeededTemplateGuardTests : IAsyncLifetime
{
    private DemoScenarioState _real = null!;
    private DemoScenarioState _clone = null!;

    public async Task InitializeAsync()
    {
        // The generator depends on "now" (orders «as soon as possible» and the event journal are cut at it): the real reset is made at the moment of the template.
        await DemoSeededTemplate.EnsureAsync();
        _real = await DemoScenarioState.CreateAsync(DemoDatabaseSlots.GuardReal, seeded: false, resetAtUtc: DemoSeededTemplate.ResetAtUtc);
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

    /// <summary>SHA-256 of the deterministic columns of the key tables (random ids are left out: bookings, orders, items and the SuperAdmin get new Guids on every reset).</summary>
    private static async Task<Dictionary<string, string>> ContentHashesAsync(DemoScenarioState state)
    {
        using var scope = state.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        static string Hash(IEnumerable<string> rows) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", rows.Order(StringComparer.Ordinal)))));
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string F(IFormattable? v) => v?.ToString(null, inv) ?? "";

        var hashes = new Dictionary<string, string>();
        hashes["Companies"] = Hash((await db.Companies.AsNoTracking().Select(c => new { c.Id, c.Slug, c.Name, c.Phone, c.Kind, c.CreatedAt }).ToListAsync())
            .Select(c => $"{c.Id}|{c.Slug}|{c.Name}|{c.Phone}|{c.Kind}|{F(c.CreatedAt)}"));
        hashes["ShowcaseUsers"] = Hash((await db.Users.AsNoTracking().Where(u => u.IsShowcase).Select(u => new { u.Id, u.PhoneNumber, u.FirstName, u.LastName }).ToListAsync())
            .Select(u => $"{u.Id}|{u.PhoneNumber}|{u.FirstName}|{u.LastName}"));
        hashes["Services"] = Hash((await db.Services.AsNoTracking().Select(x => new { x.Id, x.CompanyId, x.Name, x.Price, x.DurationMinutes }).ToListAsync())
            .Select(x => $"{x.Id}|{x.CompanyId}|{x.Name}|{F(x.Price)}|{x.DurationMinutes}"));
        hashes["Products"] = Hash((await db.Products.AsNoTracking().Select(x => new { x.Id, x.CompanyId, x.Name, x.Price, x.Unit }).ToListAsync())
            .Select(x => $"{x.Id}|{x.CompanyId}|{x.Name}|{F(x.Price)}|{x.Unit}"));
        hashes["Bookings"] = Hash((await db.Bookings.AsNoTracking().Select(x => new { x.CompanyId, x.Date, x.StartTime, x.Status, x.Price, x.GuestPhone, x.CreatedAt }).ToListAsync())
            .Select(x => $"{x.CompanyId}|{x.Date:yyyy-MM-dd}|{x.StartTime}|{x.Status}|{F(x.Price)}|{x.GuestPhone}|{F(x.CreatedAt)}"));
        hashes["Orders"] = Hash((await db.Orders.AsNoTracking().Select(x => new { x.CompanyId, x.Number, x.Status, x.CustomerPhone, x.EstimatedTotal, x.CreatedAtUtc }).ToListAsync())
            .Select(x => $"{x.CompanyId}|{x.Number}|{x.Status}|{x.CustomerPhone}|{F(x.EstimatedTotal)}|{F(x.CreatedAtUtc)}"));
        hashes["OrderItems"] = Hash((await db.OrderItems.AsNoTracking().Select(x => new { x.NameSnapshot, x.QuantityOrdered, x.UnitPrice }).ToListAsync())
            .Select(x => $"{x.NameSnapshot}|{F(x.QuantityOrdered)}|{F(x.UnitPrice)}"));
        hashes["OrderEvents"] = Hash((await db.OrderEvents.AsNoTracking().Select(x => new { x.CompanyId, x.Kind, x.OccurredAtUtc, x.ToStatus }).ToListAsync())
            .Select(x => $"{x.CompanyId}|{x.Kind}|{F(x.OccurredAtUtc)}|{x.ToStatus}"));
        hashes["Reviews"] = Hash((await db.Reviews.AsNoTracking().Select(x => new { x.Rating, x.Comment, x.CreatedAt }).ToListAsync())
            .Select(x => $"{x.Rating}|{x.Comment}|{F(x.CreatedAt)}"));
        hashes["ClientNotes"] = Hash((await db.ClientNotes.AsNoTracking().Select(x => new { x.Note, x.CreatedAt }).ToListAsync())
            .Select(x => $"{x.Note}|{F(x.CreatedAt)}"));
        return hashes;
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

        var realHashes = await ContentHashesAsync(_real);
        var cloneHashes = await ContentHashesAsync(_clone);
        cloneHashes.Where(p => realHashes[p.Key] != p.Value).Select(p => p.Key).Should().BeEmpty("the content of a table of the seeded template differs from a real reset at the same moment");
        Files(DemoDatabaseSlots.GuardClone).Should().Equal(Files(DemoDatabaseSlots.GuardReal), "the published pictures are copied with the template");
    }
}
