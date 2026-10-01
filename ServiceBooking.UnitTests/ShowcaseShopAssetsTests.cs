using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Services.Showcase;

namespace ServiceBooking.UnitTests;

/// <summary>
/// ARCHITECTURE_CYCLE35.md §35.11 (T-35-15) — the committed pictures of the demo shops: the manifest names every key the catalogs and the dataset ask for, every file of the manifest
/// exists, every product has a thumbnail, the set stays inside the budget and every file has its line in LICENSES.md. Reads the committed files off disk: no database, no network.
/// </summary>
public class ShowcaseShopAssetsTests
{
    private sealed record Entry(string Key, string Role, string File, string? Thumbnail, int Width, int Height);

    private static readonly Lazy<(string Root, IReadOnlyList<Entry> Entries)> Committed = new(() =>
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "ServiceBooking.API", "ShowcaseAssets");
            if (File.Exists(Path.Combine(candidate, "manifest.json")))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(candidate, "manifest.json")));
                var entries = doc.RootElement.GetProperty("assets").EnumerateArray().Select(a => new Entry(
                    a.GetProperty("key").GetString()!, a.GetProperty("role").GetString()!, a.GetProperty("file").GetString()!,
                    a.GetProperty("thumbnail").ValueKind == JsonValueKind.Null ? null : a.GetProperty("thumbnail").GetString(),
                    a.GetProperty("width").GetInt32(), a.GetProperty("height").GetInt32())).ToList();
                return (candidate, entries);
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("ServiceBooking.API/ShowcaseAssets/manifest.json not found above " + AppContext.BaseDirectory);
    });

    private static bool IsShopEntry(Entry e) => e.Key.StartsWith("product.", StringComparison.Ordinal) || e.Key.Contains(".shop.", StringComparison.Ordinal);

    private static ShowcaseGraph Graph => ShowcaseDataset.Build(ShowcaseProfile.Demo, new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void EveryPictureTheDatasetAsksForIsInTheManifest_AndEveryShopPictureIsUsed()
    {
        var keys = Committed.Value.Entries.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var g = Graph;
        var asked = g.LogoKeyByCompany.Where(kv => g.Companies.Single(c => c.Id == kv.Key).Kind == Core.Enums.CompanyKind.Orders).Select(kv => kv.Value)
            .Concat(g.PhotoKeysByCompany.Where(kv => g.Companies.Single(c => c.Id == kv.Key).Kind == Core.Enums.CompanyKind.Orders).SelectMany(kv => kv.Value))
            .Concat(g.ProductImageKeys.Values)
            .ToHashSet(StringComparer.Ordinal);

        asked.Where(k => !keys.Contains(k)).Should().BeEmpty("a key that is not in the manifest is silently skipped and the demo shows no picture there");
        // Photos: the dataset takes 3–6 per shop, the set draws six per category.
        Committed.Value.Entries.Where(e => IsShopEntry(e) && e.Role is "logo" or "product" && !asked.Contains(e.Key)).Should().BeEmpty("no shop picture is drawn for nothing");
    }

    [Fact]
    public void EveryFileOfTheManifestIsOnDisk_WithTheDeclaredRoleAndSize()
    {
        foreach (var e in Committed.Value.Entries)
        {
            File.Exists(Path.Combine(Committed.Value.Root, e.File)).Should().BeTrue(e.File);
            if (e.Thumbnail is not null) File.Exists(Path.Combine(Committed.Value.Root, e.Thumbnail)).Should().BeTrue(e.Thumbnail);
            e.Role.Should().BeOneOf("logo", "photo", "service", "product");
        }
        Committed.Value.Entries.Select(e => e.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryProductHasAThumbnail_TheOthersHaveNone()
    {
        var products = Committed.Value.Entries.Where(e => e.Role == "product").ToList();

        products.Should().NotBeEmpty().And.OnlyContain(e => e.Thumbnail != null && e.Key.StartsWith("product."));
        Committed.Value.Entries.Where(e => e.Role != "product").Should().OnlyContain(e => e.Thumbnail == null);
        products.Should().OnlyContain(e => e.Width == 800 && e.Height == 800);
    }

    [Fact]
    public void ThePicturesOfTheShopsStayInsideTheBudgetOfThreeMegabytes_AndEachFileIsSmall()
    {
        var files = Committed.Value.Entries.Where(IsShopEntry).SelectMany(e => new[] { e.File, e.Thumbnail }).Where(f => f != null)
            .Select(f => new FileInfo(Path.Combine(Committed.Value.Root, f!))).ToList();

        files.Sum(f => f.Length).Should().BeLessThanOrEqualTo(3 * 1024 * 1024, "§35.11: the shops add at most 3 MB");
        files.Should().OnlyContain(f => f.Length <= 250 * 1024);
        Committed.Value.Entries.SelectMany(e => new[] { e.File, e.Thumbnail }).Where(f => f != null)
            .Sum(f => new FileInfo(Path.Combine(Committed.Value.Root, f!)).Length).Should().BeLessThanOrEqualTo(8 * 1024 * 1024, "the whole set stays inside the budget of cycle 28");
    }

    [Fact]
    public void TheSalonEntriesAreStillThere_TheShopsAreAdditionsOnly()
    {
        var entries = Committed.Value.Entries;

        entries.Count(e => !IsShopEntry(e)).Should().Be(68, "cycle 28: 7 logos, 42 photos, 19 services — never redrawn by the shops' pictures");
        entries.Where(e => !IsShopEntry(e)).Select(e => e.Role).Distinct().Should().BeEquivalentTo("logo", "photo", "service");
        entries.Count(e => e.Key.StartsWith("logo.shop.")).Should().Be(5);
        entries.Count(e => e.Key.StartsWith("photo.shop.")).Should().Be(30);
    }

    [Fact]
    public void EveryFileHasItsLineInTheLicenseJournal_NoPeopleNoForeignSources()
    {
        var journal = File.ReadAllText(Path.Combine(Committed.Value.Root, "LICENSES.md"));

        foreach (var e in Committed.Value.Entries.Where(IsShopEntry))
        {
            journal.Should().Contain($"`{e.Key}`").And.Contain($"`{e.File}`");
            if (e.Thumbnail != null) journal.Should().Contain($"`{e.Thumbnail}`");
        }
        journal.Should().Contain("Нарисовано программно").And.NotContain("http");
    }
}
