-- deploy/checks/cycle19-limit-options-report.sql
--
-- ARCHITECTURE_CYCLE19.md §385.4 (US-19-05). Read-only. Printed to the deploy log on EVERY rollout
-- (deploy-remote.sh's check_retired_limit_options, right before it runs
-- cycle19-retired-limit-options-live.sql) so the customer always has an up-to-date answer to "what did
-- the retired 'Дополнительные сотрудники'/'Дополнительные компании' options ever do on this tariff, and
-- what state are the purchased rows in" without needing host access (TD16-4). No bonus is granted by
-- this cycle (§385.4) — part (б) below shows what exists and in what state instead of "who got a bonus".
--
-- This file is only ever run by deploy-remote.sh via psql (not by any test), so \echo is fine here.

\pset footer off

\echo '=== cycle19-limit-options-report (а): plan rules for the two retired limit options ==='
SELECT
    p."Name" AS plan_name,
    p."IsActive" AS plan_is_active,
    p."IsPublic" AS plan_is_public,
    so."Code" AS option_code,
    CASE r."Availability"
        WHEN 0 THEN 'Unavailable'
        WHEN 1 THEN 'Included'
        WHEN 2 THEN 'Extra'
        ELSE r."Availability"::text
    END AS availability,
    r."IncludedQuantity" AS included_quantity,
    p."MaxEmployees" AS max_employees,
    p."MaxCompanies" AS max_companies
FROM "PlanOptionRules" r
JOIN "SubscriptionPlanConfigs" p ON p."Id" = r."PlanConfigId"
JOIN "SubscriptionOptions" so ON so."Id" = r."OptionId"
WHERE lower(btrim(so."CapabilityKey")) IN ('employees', 'companies')
ORDER BY p."Name", so."Code";

\echo '=== cycle19-limit-options-report (б): every AccountSubscriptionOption row of a retired limit option, any state ==='
SELECT
    o."BillingAccountId" AS billing_account_id,
    so."Code" AS option_code,
    o."Quantity" AS quantity,
    o."PaidUntilUtc" AS paid_until_utc,
    o."EndsAtUtc" AS ends_at_utc,
    o."ActivatedAtUtc" AS activated_at_utc,
    CASE
        WHEN (o."EndsAtUtc" IS NULL OR o."EndsAtUtc" > now())
         AND (o."PaidUntilUtc" IS NULL OR o."PaidUntilUtc" >= now())
        THEN 'live' ELSE 'ended'
    END AS state
FROM "AccountSubscriptionOptions" o
JOIN "SubscriptionOptions" so ON so."Id" = o."OptionId"
WHERE lower(btrim(so."CapabilityKey")) IN ('employees', 'companies')
ORDER BY state DESC, o."BillingAccountId", so."Code";

\echo '=== cycle19-limit-options-report: done. ==='
