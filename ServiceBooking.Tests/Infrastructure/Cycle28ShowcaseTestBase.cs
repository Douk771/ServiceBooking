using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceBooking.API.Services.Ops;
using ServiceBooking.API.Services.Showcase;
using ServiceBooking.API.Services.Showcase.Tariffs;
using ServiceBooking.Core.Entities;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.Tests.Infrastructure;

/// <summary>
/// Cycle 28 (pass A) — shared helpers for the QA scenarios CY28-*. Written from SPEC.md / API_CONTRACT_CYCLE28.md. The "mark a real company as a showcase one"
/// helper is a test-harness shortcut (the generator itself is exercised separately in <c>Cycle28ShowcaseGeneratorTests</c>): it flips the marks the same way the
/// generator does (company, billing account, owner and staff users, the hidden service tariff) so that guards, filters and suppression can be tested on small data.
/// </summary>
public abstract class Cycle28ShowcaseTestBase(TestDatabaseFixture fixture) : ApiTestBase(fixture)
{
    protected static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Runs an <c>ops …</c> command in-process on this class' host, exactly as <c>dotnet ServiceBooking.API.dll ops …</c> does (same services, no HTTP).</summary>
    protected async Task<(int Exit, string Output)> RunOpsAsync(params string[] words)
    {
        var cli = OpsCommandLine.Parse(["ops", .. words])!;
        var writer = new StringWriter();
        var exit = await OpsCommandRunner.RunAsync(Factory.Services, cli, writer);
        return (exit, writer.ToString());
    }

    protected async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await query(db);
    }

    protected async Task DbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await action(db);
    }

    /// <summary>Creates the hidden service tariff ("Витрина (служебный)") the way <c>ops showcase create</c> does.</summary>
    protected async Task EnsureShowcasePlanAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<TariffCatalogSeeder>();
        await seeder.EnsureShowcasePlanAsync();
    }

    /// <summary>
    /// Turns an already-built real company into a showcase one: company (+ optionally open for booking), its billing account, the owner and the given
    /// staff users are marked, and the account's subscription moves to the hidden service tariff (no paid-until, like the generator).
    /// </summary>
    protected async Task MakeShowcaseAsync(Guid companyId, bool bookingOpen, params string[] staffUserIds)
    {
        await EnsureShowcasePlanAsync();
        await DbAsync(async db =>
        {
            var company = await db.Companies.FirstAsync(c => c.Id == companyId);
            company.IsShowcase = true;
            company.ShowcaseBookingOpen = bookingOpen;
            company.ShowInPublicListing = true;
            var account = await db.BillingAccounts.FirstAsync(a => a.Id == company.BillingAccountId);
            account.IsShowcase = true;
            var ids = staffUserIds.Append(company.OwnerUserId).Distinct().ToList();
            foreach (var user in await db.Users.Where(u => ids.Contains(u.Id)).ToListAsync())
                user.IsShowcase = true;
            var subscription = await db.AccountSubscriptions.FirstOrDefaultAsync(s => s.BillingAccountId == account.Id);
            if (subscription is null)
            {
                db.AccountSubscriptions.Add(new AccountSubscription
                {
                    Id = Guid.NewGuid(), OwnerUserId = company.OwnerUserId, BillingAccountId = account.Id,
                    PlanConfigId = ShowcaseCatalog.ShowcasePlanId, IsActive = true, PaidUntil = null,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
            }
            else
            {
                subscription.PlanConfigId = ShowcaseCatalog.ShowcasePlanId;
                subscription.PaidUntil = null;
                subscription.IsActive = true;
            }
            await db.SaveChangesAsync();
        });
    }
}

/// <summary>
/// The showcase generator writes ~10 thousand bookings per run and the tests create and delete it a dozen times. On the shared, small test database
/// server this starves every other class running at the same moment (30 s command timeouts in unrelated tests), so the collection runs ALONE:
/// xUnit does not start it in parallel with any other collection.
/// </summary>
[CollectionDefinition("Cycle28Generator", DisableParallelization = true)]
public sealed class Cycle28GeneratorCollection;
