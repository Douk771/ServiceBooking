using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Cycle 5, stage 2 of 6 (ARCHITECTURE_CYCLE5.md §54.2/§54.4, US-73) — provisions one
    /// <c>BillingAccount</c> per today's company owner and points every existing
    /// <c>Company.BillingAccountId</c>/<c>AccountSubscription.BillingAccountId</c> at it. No
    /// <c>AccountSubscription</c> row is created, duplicated or deleted — there is already at most one
    /// per owner (unique <c>OwnerUserId</c>), and this migration only fills in its new foreign key.
    ///
    /// <c>GrandfatheredEmployeeBonus</c> (§54.4, the one non-trivial spot of this migration) makes sure
    /// the new SUMMED-across-companies employee limit is never lower than what an owner already uses
    /// today under the per-company limit:
    ///
    ///     bonus = GREATEST(0, base × (companyCount − 1), seatsUsed − base)
    ///
    /// where `base` is the owner's current plan's MaxEmployees (Free's 1 if there is no usable
    /// subscription), `companyCount` is how many companies they own, and `seatsUsed` is the total
    /// CompanyMembers rows across all of them (today's actual enforcement point, ARCHITECTURE_CYCLE5.md
    /// §45.1's CompaniesController.AddMember — still counted per company at the end of this stage; the
    /// account-wide reader that would let two companies "borrow" from each other's headroom is
    /// AccountUsageReader, ARCHITECTURE_CYCLE5.md §46, a later slice of this cycle). `base × (companyCount
    /// − 1)` preserves the allowance the owner had for their extra branches (each one used to get its own
    /// `base` seats); `seatsUsed − base` covers the case where a single company already has more members
    /// than the account's new, un-multiplied base. An owner on an unlimited plan (`MaxEmployees IS NULL`)
    /// needs no bonus — unlimited already covers whatever they use.
    ///
    /// Additive and reversible (§54.1): no NOT NULL, no composite FKs — Down simply clears every FK this
    /// migration set and deletes the rows it created. Safe to re-run Up after a Down: it's idempotent
    /// (re-derives the same bonus from the same source data, WHERE NOT EXISTS guards account creation).
    /// </summary>
    public partial class BackfillBillingAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- 1. One billing account per existing company owner (skip owners who already have one —
                -- keeps this migration idempotent if it's ever re-run after a manual Down).
                INSERT INTO "BillingAccounts" ("Id", "OwnerUserId", "GrandfatheredEmployeeBonus", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), owners."OwnerUserId", 0, now() AT TIME ZONE 'utc', now() AT TIME ZONE 'utc'
                FROM (SELECT DISTINCT "OwnerUserId" FROM "Companies") owners
                WHERE NOT EXISTS (
                    SELECT 1 FROM "BillingAccounts" ba WHERE ba."OwnerUserId" = owners."OwnerUserId"
                );

                -- 2. Every company points at its owner's account.
                UPDATE "Companies" c
                SET "BillingAccountId" = ba."Id"
                FROM "BillingAccounts" ba
                WHERE ba."OwnerUserId" = c."OwnerUserId"
                  AND c."BillingAccountId" IS DISTINCT FROM ba."Id";

                -- 3. The owner's own subscription row (at most one, per today's unique OwnerUserId
                -- index) points at the same account. Rows are not moved, copied or created.
                UPDATE "AccountSubscriptions" s
                SET "BillingAccountId" = ba."Id"
                FROM "BillingAccounts" ba
                WHERE ba."OwnerUserId" = s."OwnerUserId"
                  AND s."BillingAccountId" IS DISTINCT FROM ba."Id";

                -- 4. Grandfathered employee bonus (§54.4 formula above).
                WITH usable_subscription AS (
                    SELECT s."OwnerUserId", spc."MaxEmployees" AS max_employees
                    FROM "AccountSubscriptions" s
                    JOIN "SubscriptionPlanConfigs" spc
                        ON spc."Id" = s."PlanConfigId" AND spc."IsActive" = true
                    WHERE s."IsActive" = true
                      AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now() AT TIME ZONE 'utc')
                ),
                owner_usage AS (
                    SELECT
                        c."OwnerUserId",
                        COUNT(DISTINCT c."Id") AS company_count,
                        COUNT(cm."Id") AS seats_used
                    FROM "Companies" c
                    LEFT JOIN "CompanyMembers" cm ON cm."CompanyId" = c."Id"
                    GROUP BY c."OwnerUserId"
                )
                UPDATE "BillingAccounts" ba
                SET "GrandfatheredEmployeeBonus" = GREATEST(
                        0,
                        COALESCE(us.max_employees, 1) * (ou.company_count - 1),
                        ou.seats_used - COALESCE(us.max_employees, 1)
                    ),
                    "UpdatedAtUtc" = now() AT TIME ZONE 'utc'
                FROM owner_usage ou
                LEFT JOIN usable_subscription us ON us."OwnerUserId" = ou."OwnerUserId"
                WHERE ba."OwnerUserId" = ou."OwnerUserId"
                  -- An owner on an unlimited plan (MaxEmployees IS NULL) has a matching row in
                  -- usable_subscription with max_employees NULL — distinguished here from "no usable
                  -- subscription at all" (which also has max_employees NULL after the LEFT JOIN, but
                  -- means the Free baseline of 1, not "unlimited") via EXISTS.
                  AND NOT EXISTS (
                      SELECT 1 FROM usable_subscription us2
                      WHERE us2."OwnerUserId" = ou."OwnerUserId" AND us2.max_employees IS NULL
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Companies" SET "BillingAccountId" = NULL WHERE "BillingAccountId" IS NOT NULL;
                UPDATE "AccountSubscriptions" SET "BillingAccountId" = NULL WHERE "BillingAccountId" IS NOT NULL;
                DELETE FROM "BillingAccounts";
                """);
        }
    }
}
