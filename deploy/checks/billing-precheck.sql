-- deploy/checks/billing-precheck.sql
--
-- Cycle 5, stage 6 (ARCHITECTURE_CYCLE5.md §43.6/§54.1/§54.5, B5-13, B7). Safe to run against the
-- target database AT ANY POINT before the deploy — including before ANY cycle-5 migration has been
-- applied at all. Checks 1-4 look at columns (`BillingAccountId` on Companies/AccountSubscriptions/
-- NotificationChannels) that only exist once `AddBillingAccounts`/
-- `AddChannelBillingAccountAndSubscriptionOptions` have run; on a database that predates those
-- migrations, each such check reports itself SKIPPED instead of failing on "column does not exist" —
-- that is expected and not a broken script. Re-run this script again after those migrations (and
-- ideally right before `AddCoTenancyConstraints`) to get the real findings.
--
-- Read-only. Writes nothing, locks nothing. Safe to run any number of times, on any environment,
-- at any point before the deploy.
--
-- HOW TO READ THE OUTPUT
--   Each check below is always executed and always prints something, either its detail rows or a
--   single SKIPPED row explaining why it doesn't apply yet. Every check's result set carries a
--   "finding_count" column: 0 (or a SKIPPED row) is fine, > 0 means the migration WILL fail
--   (deliberately — see AddCoTenancyConstraints' own guard, which raises the same class of error)
--   unless the underlying data is fixed first. The "detail" column always says exactly what is wrong
--   and which row is affected, so this script doubles as the worklist for fixing it.
--
--   Usage:  psql "$CONNECTION_STRING" -f deploy/checks/billing-precheck.sql
--
-- On this project's own environments (staging never shipped notifications to real traffic) every
-- check below is expected to return zero findings. The script exists for the environments — and the
-- future migrations reusing this same co-tenancy pattern — where that stops being true.
--
-- When this fits in the deploy checklist: see DEPLOY.md's "Cycle 5 billing migration" section. Run
-- once early (any state), and again immediately before applying `AddCoTenancyConstraints` — that
-- second run is the one whose findings must be all-zero for the deploy to proceed.

\pset footer off

-- Column-existence guard, reused by every check below that reads a cycle-5-only column.
-- Returns TRUE once the named column exists on the named table.
\set has_companies_billing_account 'EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = ''Companies'' AND column_name = ''BillingAccountId'')'
\set has_subscriptions_billing_account 'EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = ''AccountSubscriptions'' AND column_name = ''BillingAccountId'')'
\set has_channels_billing_account 'EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = ''NotificationChannels'' AND column_name = ''BillingAccountId'')'

\echo '=== billing-precheck: 1) Companies with no BillingAccountId ==='
SELECT CASE WHEN :has_companies_billing_account THEN $sql$
    SELECT
        'companies_without_billing_account' AS check,
        count(*) OVER () AS finding_count,
        c."Id" AS company_id,
        c."Name" AS company_name,
        c."OwnerUserId" AS owner_user_id,
        'Company has no BillingAccountId — BackfillBillingAccounts should have given every company an '
        || 'account by OwnerUserId. Investigate why this row was missed before running AddCoTenancyConstraints.'
            AS detail
    FROM "Companies" c
    WHERE c."BillingAccountId" IS NULL
$sql$ ELSE $sql$
    SELECT
        'companies_without_billing_account' AS check,
        0 AS finding_count,
        NULL::uuid AS company_id,
        NULL::text AS company_name,
        NULL::text AS owner_user_id,
        'SKIPPED — Companies."BillingAccountId" does not exist yet on this database. This check applies '
        || 'once the AddBillingAccounts migration has run; re-run this script then.' AS detail
$sql$ END AS query
\gexec

\echo '=== billing-precheck: 2) AccountSubscriptions with no BillingAccountId ==='
SELECT CASE WHEN :has_subscriptions_billing_account THEN $sql$
    SELECT
        'subscriptions_without_billing_account' AS check,
        count(*) OVER () AS finding_count,
        s."Id" AS subscription_id,
        s."OwnerUserId" AS owner_user_id,
        'AccountSubscription has no BillingAccountId — BackfillBillingAccounts should have matched it to '
        || 'an account by OwnerUserId (the unique index on AccountSubscriptions.OwnerUserId guarantees '
        || 'at most one such account). Investigate why this row was missed.'
            AS detail
    FROM "AccountSubscriptions" s
    WHERE s."BillingAccountId" IS NULL
$sql$ ELSE $sql$
    SELECT
        'subscriptions_without_billing_account' AS check,
        0 AS finding_count,
        NULL::uuid AS subscription_id,
        NULL::text AS owner_user_id,
        'SKIPPED — AccountSubscriptions."BillingAccountId" does not exist yet on this database. This '
        || 'check applies once the AddBillingAccounts migration has run; re-run this script then.' AS detail
$sql$ END AS query
\gexec

\echo '=== billing-precheck: 3) NotificationChannels with no BillingAccountId ==='
SELECT CASE WHEN :has_channels_billing_account THEN $sql$
    SELECT
        'channels_without_billing_account' AS check,
        count(*) OVER () AS finding_count,
        n."Id" AS channel_id,
        n."OwnerUserId" AS owner_user_id,
        n."PhoneNumber" AS phone_number,
        'NotificationChannel has no BillingAccountId — BackfillChannelBillingAccounts should have matched '
        || 'it to an account by OwnerUserId. Investigate why this row was missed.'
            AS detail
    FROM "NotificationChannels" n
    WHERE n."BillingAccountId" IS NULL
$sql$ ELSE $sql$
    SELECT
        'channels_without_billing_account' AS check,
        0 AS finding_count,
        NULL::uuid AS channel_id,
        NULL::text AS owner_user_id,
        NULL::text AS phone_number,
        'SKIPPED — NotificationChannels."BillingAccountId" does not exist yet on this database. This '
        || 'check applies once the AddChannelBillingAccountAndSubscriptionOptions migration has run; '
        || 're-run this script then.' AS detail
$sql$ END AS query
\gexec

\echo '=== billing-precheck: 4) Cross-account channel assignments ("company on someone else''s number") ==='
-- The headline check (SPEC §3.3 p.5, §43.6): a ChannelCompanyAssignment row whose company and whose
-- channel do NOT share the same billing account. This is exactly the state the composite FK
-- (ChannelCompanyAssignment -> NotificationChannels(Id, BillingAccountId)) is built to make
-- impossible going forward — but it can only be added if no such row exists today. Needs BOTH
-- Companies and NotificationChannels to already carry BillingAccountId.
SELECT CASE WHEN :has_companies_billing_account AND :has_channels_billing_account THEN $sql$
    SELECT
        'cross_account_channel_assignment' AS check,
        count(*) OVER () AS finding_count,
        a."Id" AS assignment_id,
        a."CompanyId" AS company_id,
        c."BillingAccountId" AS company_billing_account_id,
        a."ChannelId" AS channel_id,
        n."BillingAccountId" AS channel_billing_account_id,
        'A company is assigned to a number that belongs to a DIFFERENT billing account. Someone is being '
        || 'served by a number they do not pay for. Fix by either unassigning the company '
        || '(DELETE FROM "ChannelCompanyAssignments" WHERE "Id" = ...) or transferring the company to the '
        || 'channel''s account via POST /api/admin/companies/{id}/transfer before migrating.'
            AS detail
    FROM "ChannelCompanyAssignments" a
    JOIN "Companies" c ON c."Id" = a."CompanyId"
    JOIN "NotificationChannels" n ON n."Id" = a."ChannelId"
    WHERE c."BillingAccountId" IS DISTINCT FROM n."BillingAccountId"
$sql$ ELSE $sql$
    SELECT
        'cross_account_channel_assignment' AS check,
        0 AS finding_count,
        NULL::uuid AS assignment_id,
        NULL::uuid AS company_id,
        NULL::uuid AS company_billing_account_id,
        NULL::uuid AS channel_id,
        NULL::uuid AS channel_billing_account_id,
        'SKIPPED — Companies."BillingAccountId" and/or NotificationChannels."BillingAccountId" do not '
        || 'exist yet on this database. This check applies once both backfill migrations have run; '
        || 're-run this script then.' AS detail
$sql$ END AS query
\gexec

\echo '=== billing-precheck: 5) Companies with no owner (would silently sink into an orphaned account) ==='
-- Companies/OwnerUserId predate cycle 5, but this script promises to run at ANY point, including
-- against a database that hasn't even had cycle 1's schema applied yet (e.g. a brand-new empty
-- database pointed at by mistake) — guard on table existence too, not just the cycle-5 columns.
SELECT CASE WHEN EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'Companies') THEN $sql$
    SELECT
        'companies_without_owner' AS check,
        count(*) OVER () AS finding_count,
        c."Id" AS company_id,
        c."Name" AS company_name,
        'Company has an empty/blank OwnerUserId. BackfillBillingAccounts groups companies into accounts '
        || 'by OwnerUserId, so a company with no owner never gets a billing account and will be caught by '
        || 'check (1) instead — listed here separately because the root cause is different (a data '
        || 'integrity problem predating this cycle, not a migration gap).'
            AS detail
    FROM "Companies" c
    WHERE c."OwnerUserId" IS NULL OR c."OwnerUserId" = ''
$sql$ ELSE $sql$
    SELECT
        'companies_without_owner' AS check,
        0 AS finding_count,
        NULL::uuid AS company_id,
        NULL::text AS company_name,
        'SKIPPED — the "Companies" table does not exist yet on this database (no migrations applied at '
        || 'all). Re-run this script once the base schema has been created.' AS detail
$sql$ END AS query
\gexec

\echo '=== billing-precheck: 6) Duplicate AccountSubscription rows per account ==='
-- AccountSubscriptions.BillingAccountId carries a unique index (one subscription row per account) —
-- this check catches a violation before the migration trips over it, with a clearer message. Only
-- meaningful once the column exists.
SELECT CASE WHEN :has_subscriptions_billing_account THEN $sql$
    SELECT
        'duplicate_subscriptions_per_account' AS check,
        count(*) AS finding_count,
        s."BillingAccountId" AS billing_account_id,
        'More than one AccountSubscription row points at the same BillingAccountId — violates the '
        || 'unique index AccountSubscriptions.BillingAccountId and means the account''s subscription '
        || 'history is ambiguous. Resolve which row is authoritative before migrating.'
            AS detail
    FROM "AccountSubscriptions" s
    WHERE s."BillingAccountId" IS NOT NULL
    GROUP BY s."BillingAccountId"
    HAVING count(*) > 1
$sql$ ELSE $sql$
    SELECT
        'duplicate_subscriptions_per_account' AS check,
        0 AS finding_count,
        NULL::uuid AS billing_account_id,
        'SKIPPED — AccountSubscriptions."BillingAccountId" does not exist yet on this database. This '
        || 'check applies once the AddBillingAccounts migration has run; re-run this script then.' AS detail
$sql$ END AS query
\gexec

\echo '=== billing-precheck: done. finding_count = 0 (or SKIPPED) everywhere above means clear to migrate. ==='
