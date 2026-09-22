-- deploy/checks/billing-migration-check.sql
--
-- Cycle 5, stage 6 (ARCHITECTURE_CYCLE5.md §54.5, US-73's acceptance criterion: "no company loses any
-- capability by moving to the account-level billing model"). Read-only, one SELECT per company,
-- comparing "as it was" (the old per-company rule: a subscription looked up by the company's OWNER,
-- limits applied per company) against "as it is now" (the new account rule: a subscription looked up
-- by the company's BillingAccountId, limits summed across every company on that account, plus the
-- GrandfatheredEmployeeBonus and any purchased extra-employees option).
--
-- This comparison is possible ONLY because AccountSubscriptions.OwnerUserId was deliberately kept as a
-- history column (§43.4) rather than dropped once BillingAccountId took over — see that column's own
-- remarks in the entity/AppDbContext.
--
-- Usage:
--   psql "$CONNECTION_STRING" -f deploy/checks/billing-migration-check.sql
-- Run it BEFORE the migration (to see the old numbers) and AFTER (to see the new ones) — or, since both
-- "old" and "new" are computed from columns that already coexist post-backfill, in a single run any
-- time after BackfillBillingAccounts has executed. Empty result = the acceptance criterion holds: no
-- company lost anything. Any row returned names exactly what changed and for which company, so this
-- script IS the QA sign-off artifact (§54.5: "прогон на стенде... результат прикладывается к отчёту QA").

WITH old_plan AS (
    -- "As it was": one subscription per PERSON (AccountSubscriptions.OwnerUserId), limits applied to
    -- each of that person's companies individually — the pre-cycle-5 rule bit for bit
    -- (SubscriptionResolver.Resolve, before this cycle read BillingAccountId).
    SELECT
        c."Id" AS company_id,
        s."Id" AS old_subscription_id,
        s."PaidUntil" AS old_paid_until,
        s."IsActive" AS old_sub_is_active,
        p."Name" AS old_plan_name,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowOnlineBooking" ELSE false END AS old_allow_online_booking,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowMailing" ELSE false END AS old_allow_mailing,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowAnalytics" ELSE false END AS old_allow_analytics,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowPublicListing" ELSE true END AS old_allow_public_listing,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowOnlinePayment" ELSE false END AS old_allow_online_payment,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowNotificationChannel" ELSE false END AS old_allow_notification_channel,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."MaxEmployees" ELSE 1 END AS old_max_employees_per_company
    FROM "Companies" c
    LEFT JOIN "AccountSubscriptions" s ON s."OwnerUserId" = c."OwnerUserId"
    LEFT JOIN "SubscriptionPlanConfigs" p ON p."Id" = s."PlanConfigId"
),
new_plan AS (
    -- "As it is now": one subscription per ACCOUNT (BillingAccountId), limits summed across the
    -- account (SubscriptionResolver.Resolve as it reads today).
    SELECT
        c."Id" AS company_id,
        c."BillingAccountId" AS billing_account_id,
        s."Id" AS new_subscription_id,
        s."PaidUntil" AS new_paid_until,
        s."IsActive" AS new_sub_is_active,
        p."Name" AS new_plan_name,
        ba."GrandfatheredEmployeeBonus" AS bonus,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowOnlineBooking" ELSE false END AS new_allow_online_booking,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowMailing" ELSE false END AS new_allow_mailing,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowAnalytics" ELSE false END AS new_allow_analytics,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowPublicListing" ELSE true END AS new_allow_public_listing,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowOnlinePayment" ELSE false END AS new_allow_online_payment,
        CASE WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE
             THEN p."AllowNotificationChannel" ELSE false END AS new_allow_notification_channel,
        CASE
            WHEN s."IsActive" AND (s."PaidUntil" IS NULL OR s."PaidUntil" >= now()) AND p."IsActive" IS TRUE THEN
                CASE WHEN p."MaxEmployees" IS NULL THEN NULL
                     ELSE p."MaxEmployees" + COALESCE(ba."GrandfatheredEmployeeBonus", 0)
                          + COALESCE((
                              SELECT SUM(aso."Quantity")
                              FROM "AccountSubscriptionOptions" aso
                              JOIN "SubscriptionOptions" so ON so."Id" = aso."OptionId"
                              WHERE aso."BillingAccountId" = c."BillingAccountId"
                                AND so."CapabilityKey" = 'employees'
                                AND (aso."EndsAtUtc" IS NULL OR aso."EndsAtUtc" > now())
                          ), 0)
                END
            ELSE 1 + COALESCE(ba."GrandfatheredEmployeeBonus", 0)
        END AS new_max_employees_on_account
    FROM "Companies" c
    LEFT JOIN "BillingAccounts" ba ON ba."Id" = c."BillingAccountId"
    LEFT JOIN "AccountSubscriptions" s ON s."BillingAccountId" = c."BillingAccountId"
    LEFT JOIN "SubscriptionPlanConfigs" p ON p."Id" = s."PlanConfigId"
),
seats_used AS (
    -- Employees currently on the payroll of each company's account (mirrors AccountUsageReader's own
    -- query) — needed for the "seats_limit_new < seats_used_now" acceptance bullet.
    SELECT c."BillingAccountId" AS billing_account_id, COUNT(DISTINCT cm."UserId") AS seats_used
    FROM "Companies" c
    JOIN "CompanyMembers" cm ON cm."CompanyId" = c."Id"
    WHERE c."BillingAccountId" IS NOT NULL
    GROUP BY c."BillingAccountId"
)
SELECT
    c."Id" AS company_id,
    c."Name" AS company_name,
    o.company_id IS NOT NULL AS has_old_row,
    n.billing_account_id AS billing_account_id,
    CASE
        WHEN n.billing_account_id IS NULL THEN 'company_without_billing_account'
        WHEN o.old_plan_name IS DISTINCT FROM n.new_plan_name THEN 'plan_changed'
        WHEN o.old_paid_until IS DISTINCT FROM n.new_paid_until THEN 'paid_until_changed'
        WHEN o.old_allow_online_booking    IS DISTINCT FROM n.new_allow_online_booking
          OR o.old_allow_mailing            IS DISTINCT FROM n.new_allow_mailing
          OR o.old_allow_analytics          IS DISTINCT FROM n.new_allow_analytics
          OR o.old_allow_public_listing     IS DISTINCT FROM n.new_allow_public_listing
          OR o.old_allow_online_payment     IS DISTINCT FROM n.new_allow_online_payment
          OR o.old_allow_notification_channel IS DISTINCT FROM n.new_allow_notification_channel
            THEN 'boolean_capability_changed'
        WHEN n.new_max_employees_on_account IS NOT NULL
             AND n.new_max_employees_on_account < COALESCE(su.seats_used, 0)
            THEN 'seats_limit_new_below_seats_used_now'
        WHEN o.old_max_employees_per_company IS NOT NULL
             AND (n.new_max_employees_on_account IS NULL
                  OR n.new_max_employees_on_account < o.old_max_employees_per_company)
            THEN 'employee_limit_decreased'
        ELSE NULL
    END AS finding,
    jsonb_build_object(
        'old_plan', o.old_plan_name, 'new_plan', n.new_plan_name,
        'old_paid_until', o.old_paid_until, 'new_paid_until', n.new_paid_until,
        'old_max_employees_per_company', o.old_max_employees_per_company,
        'new_max_employees_on_account', n.new_max_employees_on_account,
        'seats_used_now', su.seats_used
    ) AS detail
FROM "Companies" c
LEFT JOIN old_plan o ON o.company_id = c."Id"
LEFT JOIN new_plan n ON n.company_id = c."Id"
LEFT JOIN seats_used su ON su.billing_account_id = n.billing_account_id
WHERE
    n.billing_account_id IS NULL
    OR o.old_plan_name IS DISTINCT FROM n.new_plan_name
    OR o.old_paid_until IS DISTINCT FROM n.new_paid_until
    OR o.old_allow_online_booking    IS DISTINCT FROM n.new_allow_online_booking
    OR o.old_allow_mailing            IS DISTINCT FROM n.new_allow_mailing
    OR o.old_allow_analytics          IS DISTINCT FROM n.new_allow_analytics
    OR o.old_allow_public_listing     IS DISTINCT FROM n.new_allow_public_listing
    OR o.old_allow_online_payment     IS DISTINCT FROM n.new_allow_online_payment
    OR o.old_allow_notification_channel IS DISTINCT FROM n.new_allow_notification_channel
    OR (n.new_max_employees_on_account IS NOT NULL AND n.new_max_employees_on_account < COALESCE(su.seats_used, 0))
    OR (o.old_max_employees_per_company IS NOT NULL
        AND (n.new_max_employees_on_account IS NULL OR n.new_max_employees_on_account < o.old_max_employees_per_company))
ORDER BY c."Id";

-- Companion check: no number serves a company outside its own billing account (same finding as
-- billing-precheck.sql's check 4 — kept here too so a single run of this script after the migration is
-- a complete "nothing was lost, and the new co-tenancy rule holds" sign-off).
SELECT
    'cross_account_channel_assignment_post_migration' AS finding,
    a."Id" AS assignment_id, a."CompanyId" AS company_id, a."ChannelId" AS channel_id
FROM "ChannelCompanyAssignments" a
JOIN "Companies" c ON c."Id" = a."CompanyId"
JOIN "NotificationChannels" n ON n."Id" = a."ChannelId"
WHERE c."BillingAccountId" IS DISTINCT FROM n."BillingAccountId";
