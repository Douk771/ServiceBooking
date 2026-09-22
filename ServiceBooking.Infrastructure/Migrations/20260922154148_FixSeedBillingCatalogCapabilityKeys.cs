using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Cycle 07 backend report, NB-4. SeedBillingCatalog was itself edited (§54.3 п.2 fixed the
    /// CapabilityKey literal from the option's own "extra-"-prefixed Code to the bare capability name,
    /// see <see cref="ServiceBooking.API.Services.Billing.CapabilityKeys"/>), but the migration's own
    /// step 2 is guarded by <c>WHERE NOT EXISTS (... WHERE so."Code" = v.code)</c>: any database where
    /// the FIRST revision of SeedBillingCatalog already ran (a local/dev database, or a shared test slot
    /// applied before the fix landed on this branch) has the two options rows already present with
    /// "Code" satisfying that guard, so the corrected INSERT is skipped forever and the wrong
    /// CapabilityKey ("extra-companies"/"extra-employees") survives untouched.
    ///
    /// This migration is the one-time, idempotent UPDATE that actually corrects those rows wherever
    /// they exist, independent of when/whether SeedBillingCatalog's corrected revision got to run its
    /// INSERT first. Safe to run twice: the second run's WHERE clauses simply match nothing.
    ///
    /// Same problem, same fix, for a second seed correction (cycle-07 backend report, NB-final): the
    /// system-free plan row is now seeded with <c>IsPublic = true</c> (SPEC.md П4 — the free plan
    /// belongs on the price list "как обычная строка прайса"), but that column-value fix has the exact
    /// same idempotency gap as above (SeedBillingCatalog's step 1 is guarded by
    /// <c>WHERE NOT EXISTS (... WHERE "IsSystemFree" = true)</c>). Corrected here too, scoped to the
    /// still-default state (<c>IsSystemFree = true AND IsPublic = false</c>) so a superadmin who
    /// deliberately unpublished the free plan after seeding is not silently overridden.
    /// </summary>
    public partial class FixSeedBillingCatalogCapabilityKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "SubscriptionOptions" SET "CapabilityKey" = 'companies'
                WHERE "Code" = 'extra-companies' AND "CapabilityKey" = 'extra-companies';

                UPDATE "SubscriptionOptions" SET "CapabilityKey" = 'employees'
                WHERE "Code" = 'extra-employees' AND "CapabilityKey" = 'extra-employees';

                UPDATE "SubscriptionPlanConfigs" SET "IsPublic" = true
                WHERE "IsSystemFree" = true AND "IsPublic" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op, same reasoning as SeedBillingCatalog's own Down: reverting a capability
            // key correction on rows an admin may since have relied on (purchased options, plan rules
            // already resolved against the corrected key) would silently break live resolution again,
            // not just "undo a seed".
        }
    }
}
