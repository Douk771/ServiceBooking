using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Services.Showcase;

/// <summary>A precondition of a showcase command is not met (no such city in the directory, showcase already exists…): the operator's exit code 2.</summary>
public sealed class ShowcaseRefusedException(string message) : Exception(message);

/// <summary>
/// ARCHITECTURE_CYCLE28.md §575.5 — writes a <see cref="ShowcaseGraph"/> into the database. Everything is written with the real entities through the same EF
/// context as the product (no separate SQL that could drift from the model) and the product's live rules are simply never called — the generator does not
/// ask for a captcha, does not check the booking horizon or the frequency, and does not parse phone numbers: it does not go through the HTTP door at all.
///
/// Runs inside the caller's transaction (the caller holds the advisory lock and commits). Files are copied BEFORE the commit
/// (<see cref="ShowcaseAssetStore.PublishAsync"/> is idempotent: file name = content hash), so a committed row never points at a missing file.
/// </summary>
public class ShowcaseGenerator(
    AppDbContext db, RoleManager<IdentityRole> roleManager, ShowcaseAssetStore assets, TariffCatalogSeeder tariffSeeder, ILogger<ShowcaseGenerator> logger)
{
    private const int BatchSize = 2000;
    private static readonly string[] RoleNames = ["Client", "Master", "CompanyOwner"];

    /// <summary>Refuses (<see cref="ShowcaseRefusedException"/>) when a city of the showcase is missing from the directory.</summary>
    public async Task<Dictionary<string, int>> ResolveCitiesAsync(CancellationToken ct)
    {
        var rows = await db.Cities.Where(c => c.IsActive && ShowcaseSpecs.Cities.Contains(c.Name)).OrderBy(c => c.Id).Select(c => new { c.Name, c.Id }).ToListAsync(ct);
        var byName = rows.GroupBy(r => r.Name).ToDictionary(g => g.Key, g => g.First().Id);
        var missing = ShowcaseSpecs.Cities.Where(name => !byName.ContainsKey(name)).ToList();
        if (missing.Count > 0)
            throw new ShowcaseRefusedException($"В справочнике городов нет: {string.Join(", ", missing)}. Витрина не создана.");
        return byName;
    }

    /// <summary>The asset keys of a graph that exist in the manifest and on disk: how many photos and files the plan promises.</summary>
    public (int Photos, int Files) CountAssets(ShowcaseGraph graph)
    {
        var photoKeys = graph.PhotoKeysByCompany.Values.SelectMany(k => k).ToList();
        var allKeys = photoKeys.Concat(graph.LogoKeyByCompany.Values).Concat(graph.ServiceImageKeys.Values);
        return (photoKeys.Count(assets.IsAvailable), assets.CountFiles(allKeys));
    }

    /// <summary>Names of the files in <c>uploads/showcase/</c> that the rows written by <see cref="PersistAsync"/> point at.</summary>
    public IReadOnlySet<string> PublishedFileNames => assets.PublishedFileNames;

    /// <summary>Writes the whole graph. The caller has already refused when a showcase exists.</summary>
    public async Task PersistAsync(ShowcaseGraph graph, IReadOnlyDictionary<string, int> cityIds, string profile, CancellationToken ct)
    {
        await EnsureRolesAsync();
        await tariffSeeder.EnsureShowcasePlanAsync(ct);
        db.ChangeTracker.Clear();

        var roleIds = await db.Roles.Where(r => RoleNames.Contains(r.Name!)).ToDictionaryAsync(r => r.Name!, r => r.Id, ct);
        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        var commandTimeout = db.Database.GetCommandTimeout();
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(300));
        try
        {
            // Pictures first (idempotent file copies); rows that point at them come next.
            var logos = new Dictionary<Guid, PublishedShowcaseAsset?>();
            foreach (var (companyId, key) in graph.LogoKeyByCompany) logos[companyId] = await assets.PublishAsync(key, ct);
            var serviceImages = new Dictionary<string, PublishedShowcaseAsset?>(StringComparer.Ordinal);
            foreach (var key in graph.ServiceImageKeys.Values.Distinct()) serviceImages[key] = await assets.PublishAsync(key, ct);
            var photos = new List<CompanyPhoto>();
            foreach (var company in graph.Companies)
            {
                var seenHashes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in graph.PhotoKeysByCompany[company.Id])
                {
                    if (await assets.PublishAsync(key, ct) is not { } published || !seenHashes.Add(published.ContentHash)) continue;
                    photos.Add(new CompanyPhoto
                    {
                        Id = ShowcaseIds.For(profile, "photo", $"{company.Slug}:{key}"),
                        CompanyId = company.Id,
                        Url = published.Url,
                        ThumbnailUrl = published.ThumbnailUrl ?? published.Url,
                        ContentType = published.ContentType,
                        SizeBytes = published.SizeBytes,
                        Width = published.Width,
                        Height = published.Height,
                        ContentHash = published.ContentHash,
                        Position = photos.Count(p => p.CompanyId == company.Id),
                        CreatedAtUtc = company.CreatedAt,
                    });
                }
            }

            foreach (var company in graph.Companies)
            {
                company.CityId = cityIds[graph.CityNameByCompany[company.Id]];
                company.LogoUrl = logos[company.Id]?.Url;
            }
            foreach (var service in graph.Services)
                service.ImageUrl = serviceImages[graph.ServiceImageKeys[service.Id]]?.Url;

            await SaveAsync(graph.Users, ct);
            await SaveAsync(graph.UserRoles.Select(r => new IdentityUserRole<string> { UserId = r.UserId, RoleId = roleIds[r.RoleName] }).ToList(), ct);
            await SaveAsync(graph.BillingAccounts, ct);
            await SaveAsync(graph.Companies, ct);
            await SaveAsync(graph.Subscriptions, ct);
            await SaveAsync(graph.Members, ct);
            await SaveAsync(graph.Services, ct);
            await SaveAsync(graph.MasterServices, ct);
            await SaveAsync(graph.ScheduleTemplates, ct);
            await SaveAsync(graph.WorkingHours, ct);
            await SaveAsync(graph.ScheduleBreaks, ct);
            await SaveAsync(photos, ct);
            await SaveAsync(graph.Bookings, ct);
            await SaveAsync(graph.BookingServices, ct);
            await SaveAsync(graph.BookingEvents, ct);
            // The demo profile only (US-28-13): empty lists for the production showcase.
            await SaveAsync(graph.Reviews, ct);
            await SaveAsync(graph.ClientNotes, ct);
            logger.LogInformation("showcase create: {Counts} reviews={Reviews} clientNotes={Notes}", graph.Counts(photos.Count), graph.Reviews.Count, graph.ClientNotes.Count);
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
            db.Database.SetCommandTimeout(commandTimeout);
        }
    }

    /// <summary>The photos actually written, for the report (same rule as <see cref="PersistAsync"/>).</summary>
    public int PhotosThatWillBeWritten(ShowcaseGraph graph) => CountAssets(graph).Photos;

    private async Task EnsureRolesAsync()
    {
        foreach (var role in RoleNames)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
    }

    private async Task SaveAsync<T>(IReadOnlyList<T> items, CancellationToken ct) where T : class
    {
        for (var i = 0; i < items.Count; i += BatchSize)
        {
            db.AddRange(items.Skip(i).Take(BatchSize));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
        }
    }
}
