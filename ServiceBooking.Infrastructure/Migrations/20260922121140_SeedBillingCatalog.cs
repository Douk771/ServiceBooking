using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ServiceBooking.Infrastructure.Migrations
{
    /// <summary>
    /// Cycle 5 (ARCHITECTURE_CYCLE5.md §54.3, B3) — the catalog rows §44 and <c>SubscriptionResolver</c>
    /// need to have anything to resolve at all. Runs LAST, after <c>AddCoTenancyConstraints</c>: the
    /// "paid channels → option quantity" step below reads <c>NotificationChannel.BillingAccountId</c>,
    /// which is only guaranteed non-null once that migration's guard has passed. §54.1's own table
    /// describes this as folded into stage 2 (<c>BackfillBillingAccounts</c>), but this codebase already
    /// split that single conceptual stage into six granular migrations (§54.1's own numbering column) —
    /// this migration continues that same granularity rather than reaching back to edit an
    /// already-applied-in-this-branch migration to reorder it ahead of a backfill it depends on.
    ///
    /// Four things, each idempotent (guarded by <c>WHERE NOT EXISTS</c> / <c>ON CONFLICT DO NOTHING</c>,
    /// safe to re-run):
    ///
    /// 1. The system free plan (§54.3, П4/§64 п.5) — <c>IsSystemFree</c>, <c>PricePerMonth = 0</c>,
    ///    limits 1/1, <c>AllowPublicListing = true</c>, everything else off — word for word
    ///    <see cref="ServiceBooking.API.Services.EffectivePlan.Free"/>. Only created if no
    ///    <c>IsSystemFree</c> row exists yet (an environment may already have hand-created one).
    /// 2. Three catalog options, all with <c>PricePerMonth = NULL</c> — nobody is charged anything the
    ///    moment this migration runs; a superadmin has to deliberately price each one before it's for
    ///    sale (§43.3's own convention, same as <c>notifications.channel.price-per-month</c> in cycle 4).
    ///    <c>Code</c> (the option's own catalog identity) keeps the "extra-" prefix ("extra-companies",
    ///    "extra-employees"); <c>CapabilityKey</c> (what the option actually grants) is the bare
    ///    capability name ("companies"/"employees", <see cref="ServiceBooking.API.Services.Billing.CapabilityKeys"/>),
    ///    matching §44.2/§54.3 and <see cref="ServiceBooking.API.Services.SubscriptionResolver"/>'s reads.
    ///    A prior pass conflated the option's code with its capability key and seeded "extra-companies"/
    ///    "extra-employees" as the CapabilityKey too; this migration corrects that (see
    ///    <see cref="ServiceBooking.API.Services.Billing.CapabilityKeys"/> remarks).
    /// 3. A <c>PlanOptionRule</c> row for every (existing plan × these three options): <c>Extra</c> for
    ///    the two limit-boosting options, and for <c>notifications.whatsapp</c>, <c>Extra</c> if the
    ///    plan already had <c>AllowNotificationChannel = true</c>, else <c>Unavailable</c> — reproducing
    ///    today's gate one for one, so no plan silently starts or stops offering WhatsApp the moment this
    ///    migration runs.
    /// 4. One <c>AccountSubscriptionOption</c> row per billing account that has at least one
    ///    <c>NotificationChannel</c> with <c>PaidUntilUtc >= now()</c>: <c>Quantity</c> = how many such
    ///    channels it has, <c>PaidUntilUtc</c> = the latest of those paid-through dates. This is the
    ///    single line this whole migration exists for (US-73's acceptance criterion): without it,
    ///    <c>EffectivePlan.PaidNotificationNumbers</c> is 0 for every account regardless of history, and
    ///    <c>NotificationGate</c> blocks every message — including ones from an account that paid for its
    ///    number before this cycle shipped.
    ///
    /// US-74 is upheld by construction, not by omission: step 2 leaves <c>notifications.whatsapp</c>
    /// with <c>PricePerMonth = NULL</c> and <c>IsPublic = false</c>, so <c>PricingCatalogBuilder</c>
    /// (§48's <c>IsActive &amp;&amp; IsPublic &amp;&amp; PricePerMonth != null</c> filter) never serves it
    /// on the public price list, and the admin write endpoint is the only thing that can ever change
    /// that — this migration does not, and must not, set a price on it.
    /// </summary>
    public partial class SeedBillingCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                -- 1. System free plan — literally EffectivePlan.Free (§54.3). Skipped if one already
                -- exists (the partial unique index on IsSystemFree would reject a second one anyway;
                -- this WHERE NOT EXISTS just gives a clean idempotent no-op instead of relying on a
                -- constraint violation).
                INSERT INTO "SubscriptionPlanConfigs"
                    ("Id", "Name", "PricePerMonth", "MaxEmployees", "MaxCompanies",
                     "AllowOnlineBooking", "AllowMailing", "AllowAnalytics", "AllowPublicListing",
                     "AllowOnlinePayment", "AllowNotificationChannel", "PhotoQuotaMb", "PhotoRetention",
                     "Description", "IsActive", "NotifyDaysBefore", "CreatedAt",
                     "Highlights", "IsPublic", "SortOrder", "IsSystemFree")
                SELECT
                    gen_random_uuid(), 'Бесплатный', 0, 1, 1,
                    false, false, false, true,
                    false, false, 100, 0,
                    'Чтобы попробовать: одна компания, один сотрудник, запись руками.', true, 7, now() AT TIME ZONE 'utc',
                    'Показ в каталоге салонов' || E'\n' || '1 компания' || E'\n' || '1 сотрудник', false, -1, true
                WHERE NOT EXISTS (SELECT 1 FROM "SubscriptionPlanConfigs" WHERE "IsSystemFree" = true);

                -- 2. Three catalog options, all unpriced (§43.3: PricePerMonth = NULL = "not for sale
                -- until an admin says otherwise"). Code keeps the "extra-" prefix (the option's own
                -- catalog identity); CapabilityKey is the bare capability name per §44.2/§54.3
                -- ("companies"/"employees"), matching SubscriptionResolver's reads via CapabilityKeys.
                INSERT INTO "SubscriptionOptions"
                    ("Id", "Code", "Name", "Description", "Kind", "CapabilityKey", "PricePerMonth",
                     "UnitName", "MaxQuantity", "UnitPriceText", "IsPublic", "IsActive", "SortOrder",
                     "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), v.code, v.name, v.description, 1, v.capability_key, NULL,
                       v.unit_name, NULL, NULL, false, true, v.sort_order, now() AT TIME ZONE 'utc', now() AT TIME ZONE 'utc'
                FROM (VALUES
                    ('extra-companies', 'Дополнительная компания', 'Ещё одна точка на той же подписке.', 'companies', 'компания', 0),
                    ('extra-employees', 'Дополнительные сотрудники', 'Сверх включённых в тариф.', 'employees', 'сотрудник', 1),
                    ('notifications.whatsapp', 'Рассылки в WhatsApp', 'Номер для рассылки уведомлений клиентам в WhatsApp.', 'notifications.whatsapp', 'номер', 2)
                ) AS v(code, name, description, capability_key, unit_name, sort_order)
                WHERE NOT EXISTS (SELECT 1 FROM "SubscriptionOptions" so WHERE so."Code" = v.code);

                -- 3. Availability rule per existing plan × these three options (§54.3): the two limit
                -- options are always purchasable as Extra; WhatsApp mirrors today's
                -- AllowNotificationChannel flag exactly, so no plan's WhatsApp offering changes the
                -- moment this migration runs.
                INSERT INTO "PlanOptionRules" ("Id", "PlanConfigId", "OptionId", "Availability", "IncludedQuantity")
                SELECT gen_random_uuid(), p."Id", o."Id",
                       CASE
                           WHEN o."Code" IN ('extra-companies', 'extra-employees') THEN 2 -- Extra
                           WHEN o."Code" = 'notifications.whatsapp' AND p."AllowNotificationChannel" THEN 2 -- Extra
                           ELSE 0 -- Unavailable
                       END,
                       NULL
                FROM "SubscriptionPlanConfigs" p
                CROSS JOIN "SubscriptionOptions" o
                WHERE o."Code" IN ('extra-companies', 'extra-employees', 'notifications.whatsapp')
                  AND NOT EXISTS (
                      SELECT 1 FROM "PlanOptionRules" r
                      WHERE r."PlanConfigId" = p."Id" AND r."OptionId" = o."Id"
                  );

                -- 4. Paid channels → notifications.whatsapp option quantity (US-73's acceptance
                -- criterion): one AccountSubscriptionOption row per billing account that has at least
                -- one currently-paid channel, Quantity = how many, PaidUntilUtc = the latest of them.
                -- Runs after AddCoTenancyConstraints, so every NotificationChannel already has a
                -- non-null BillingAccountId.
                INSERT INTO "AccountSubscriptionOptions"
                    ("Id", "BillingAccountId", "OptionId", "Quantity", "PaidUntilUtc", "EndsAtUtc",
                     "ActivatedAtUtc", "ActivatedByUserId", "RequestedQuantity", "RequestedAtUtc", "RequestedByUserId")
                SELECT
                    gen_random_uuid(), paid."BillingAccountId", o."Id", paid.channel_count, paid.latest_paid_until,
                    NULL, now() AT TIME ZONE 'utc', NULL, NULL, NULL, NULL
                FROM (
                    SELECT n."BillingAccountId", count(*) AS channel_count, max(n."PaidUntilUtc") AS latest_paid_until
                    FROM "NotificationChannels" n
                    WHERE n."PaidUntilUtc" >= now() AT TIME ZONE 'utc'
                    GROUP BY n."BillingAccountId"
                ) paid
                CROSS JOIN "SubscriptionOptions" o
                WHERE o."Code" = 'notifications.whatsapp'
                  AND NOT EXISTS (
                      SELECT 1 FROM "AccountSubscriptionOptions" aso
                      WHERE aso."BillingAccountId" = paid."BillingAccountId" AND aso."OptionId" = o."Id"
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberate no-op, same reasoning as BackfillBillingAccounts/BackfillChannelBillingAccounts:
            // this migration only inserts catalog rows an admin may since have edited (priced, renamed,
            // repointed rules) or accounts may since have purchased against — silently deleting them on
            // a Down would destroy real decisions made after this ran, not just "undo a backfill". If a
            // rollback genuinely needs the seeded rows gone, that is a manual, reviewed data decision,
            // not something this migration should do unconditionally.
        }
    }
}
