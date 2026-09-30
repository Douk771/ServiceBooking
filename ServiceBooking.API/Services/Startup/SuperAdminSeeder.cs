using Microsoft.AspNetCore.Identity;
using ServiceBooking.API.Services.Legal;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.API.Services.Startup;

/// <summary>
/// The Identity roles and the SuperAdmin account (with its consent records) — moved VERBATIM out of <c>StartupSeedingExtensions</c> by cycle 28, pass B
/// (ARCHITECTURE_CYCLE28.md §580, §582.1) without changing behavior, so that the demo reset can put the same SuperAdmin back after it wipes the database:
/// "the same code, not a copy". <c>StartupSeedingExtensions</c> calls <see cref="EnsureRolesAndSuperAdminAsync"/> at every start; <c>DemoResetService</c> calls it
/// after the TRUNCATE.
/// </summary>
public sealed class SuperAdminSeeder(
    RoleManager<IdentityRole> roleManager,
    UserManager<AppUser> userManager,
    LegalDocumentProvider legalDocumentProvider,
    ConsentLedger consentLedger,
    IConfiguration configuration)
{
    public static readonly string[] RoleNames = ["Client", "Master", "CompanyOwner", "SuperAdmin"];

    public async Task EnsureRolesAndSuperAdminAsync()
    {
        foreach (var role in RoleNames)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        // The SuperAdmin, like every account, is now identified by phone (UserName == phone). Email is
        // optional and kept only for display. Config key SuperAdmin:Phone drives login. US-26: seeded with
        // the SAME canonical form AuthController.Login normalizes to — otherwise a config value like
        // "+70000000000" would seed "UserName = +70000000000" while every login attempt normalizes to
        // "70000000000" and never finds it.
        var adminPhone = configuration["SuperAdmin:Phone"] is { Length: > 0 } rawAdminPhone
            ? PhoneNormalizer.Normalize(rawAdminPhone)
            : null;
        var adminEmail = configuration["SuperAdmin:Email"];
        var adminPassword = configuration["SuperAdmin:Password"];
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
                    var seedSubject = ConsentSubject.ForUser(admin.Id);
                    await consentLedger.GrantAsync(new ConsentGrant(
                        seedSubject, LegalDocumentType.Privacy.ToString(), seededPrivacyDoc.Version, seededPrivacyDoc.ContentHash,
                        Purpose: null, ConsentAct.Acknowledged, ConsentSource.Registration));
                    await consentLedger.GrantAsync(new ConsentGrant(
                        seedSubject, LegalDocumentType.TermsClient.ToString(), seededTermsDoc.Version, seededTermsDoc.ContentHash,
                        Purpose: null, ConsentAct.Accepted, ConsentSource.Registration));
                }
            }
        }
    }
}
