using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using ServiceBooking.API.Services;
using ServiceBooking.API.Services.Health;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.API.Services.Scheduling;
using ServiceBooking.API.Services.Scheduling.Tasks;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Startup;

/// <summary>
/// Migrations, roles and the SuperAdmin account (with its consent records) at startup. Cycle 22 P5 (ARCHITECTURE_CYCLE22.md §378): moved verbatim out of Program.cs, whose call order is unchanged.
/// </summary>
internal static class StartupSeedingExtensions
{
    public static async Task SeedAsync(this WebApplication app, WebApplicationBuilder builder)
    {
    // Seed roles and super-admin on startup
    using (var scope = app.Services.CreateScope())
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legalDocumentProvider = scope.ServiceProvider.GetRequiredService<LegalDocumentProvider>();
        var consentLedger = scope.ServiceProvider.GetRequiredService<ConsentLedger>();

        await db.Database.MigrateAsync();

        string[] roles = ["Client", "Master", "CompanyOwner", "SuperAdmin"];
        foreach (var role in roles)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        // The SuperAdmin, like every account, is now identified by phone (UserName == phone). Email is
        // optional and kept only for display. Config key SuperAdmin:Phone drives login. US-26: seeded with
        // the SAME canonical form AuthController.Login normalizes to — otherwise a config value like
        // "+70000000000" would seed "UserName = +70000000000" while every login attempt normalizes to
        // "70000000000" and never finds it.
        var adminPhone = builder.Configuration["SuperAdmin:Phone"] is { Length: > 0 } rawAdminPhone
            ? PhoneNormalizer.Normalize(rawAdminPhone)
            : null;
        var adminEmail = builder.Configuration["SuperAdmin:Email"];
        var adminPassword = builder.Configuration["SuperAdmin:Password"];
        if (adminPhone is not null && adminPassword is not null)
        {
            var admin = await userManager.FindByNameAsync(adminPhone);
            if (admin is null)
            {
                admin = new AppUser { FirstName = "Super", LastName = "Admin", Email = adminEmail, UserName = adminPhone, PhoneNumber = adminPhone };
                // Without this check a password that fails the Identity policy leaves `admin` unsaved and
                // AddToRoleAsync below then throws something unrelated to the actual cause.
                var createAdmin = await userManager.CreateAsync(admin, adminPassword);
                if (!createAdmin.Succeeded)
                    throw new InvalidOperationException(
                        "Failed to seed the SuperAdmin account: " +
                        string.Join("; ", createAdmin.Errors.Select(e => e.Description)));
                await userManager.AddToRoleAsync(admin, "SuperAdmin");

                // US-37, ARCHITECTURE.md §6.3: LegalConsentFilter applies to every authenticated route,
                // SuperAdmin's own admin API included — there is no carve-out for the seeded account in the
                // contract. Without this the freshly-seeded SuperAdmin would be locked out of everything
                // outside the allow-list until they happened to call POST /api/legal/accept, which nothing
                // in the admin UI prompts them to do. Recording consent to the currently-loaded documents
                // at seed time is the same conceptual act AuthController.Register performs for every other
                // new account; if no manifest is loaded yet (only possible outside Production), this is
                // skipped and the account behaves like any pre-cycle-C account until it next logs in after
                // the manifest is fixed.
                var legalSnapshot = legalDocumentProvider.Current;
                var seededPrivacyDoc = legalSnapshot?.Get(LegalDocumentType.Privacy);
                var seededTermsDoc = legalSnapshot?.Get(LegalDocumentType.TermsClient);
                if (seededPrivacyDoc is not null && seededTermsDoc is not null)
                {
                    var seedSubject = ServiceBooking.API.Services.Legal.ConsentSubject.ForUser(admin.Id);
                    await consentLedger.GrantAsync(new ServiceBooking.API.Services.Legal.ConsentGrant(
                        seedSubject, LegalDocumentType.Privacy.ToString(), seededPrivacyDoc.Version, seededPrivacyDoc.ContentHash,
                        Purpose: null, ConsentAct.Acknowledged, ConsentSource.Registration));
                    await consentLedger.GrantAsync(new ServiceBooking.API.Services.Legal.ConsentGrant(
                        seedSubject, LegalDocumentType.TermsClient.ToString(), seededTermsDoc.Version, seededTermsDoc.ContentHash,
                        Purpose: null, ConsentAct.Accepted, ConsentSource.Registration));
                }
            }
        }
    }
    }
}
