-- deploy/checks/billing-precheck.sql
--
-- Cycle 5, stage 6 (ARCHITECTURE_CYCLE5.md §43.6/§54.1, B5-13). Run this against the TARGET database
-- BEFORE applying the `AddCoTenancyConstraints` migration (the one that makes
-- Company/AccountSubscription/NotificationChannel.BillingAccountId NOT NULL and adds the
-- ChannelCompanyAssignment composite FKs).
--
-- Read-only. Writes nothing, locks nothing. Safe to run any number of times, on any environment,
-- at any point before the deploy.
--
-- HOW TO READ THE OUTPUT
--   Each SELECT below is one independent check, always executed and always printed, with a leading
--   "check" column naming it and a "finding_count" column. A check with finding_count = 0 is fine —
--   its detail rows will simply be empty. A check with finding_count > 0 means the migration WILL
--   fail (deliberately — see AddCoTenancyConstraints' own guard, which raises the same class of
--   error) unless the underlying data is fixed first. The "detail" column always says exactly what
--   is wrong and which row is affected, so this script doubles as the worklist for fixing it.
--
--   Usage:  psql "$CONNECTION_STRING" -f deploy/checks/billing-precheck.sql
--
-- On this project's own environments (staging never shipped notifications to real traffic) every
-- check below is expected to return zero rows. The script exists for the environments — and the
-- future migrations reusing this same co-tenancy pattern — where that stops being true.

\echo '=== billing-precheck: 1) Companies with no BillingAccountId ==='
SELECT
    'companies_without_billing_account' AS check,
    c."Id" AS company_id,
    c."Name" AS company_name,
    c."OwnerUserId" AS owner_user_id,
    'Company has no BillingAccountId — BackfillBillingAccounts should have given every company an '
    || 'account by OwnerUserId. Investigate why this row was missed before running AddCoTenancyConstraints.'
        AS detail
FROM "Companies" c
WHERE c."BillingAccountId" IS NULL;

\echo '=== billing-precheck: 2) AccountSubscriptions with no BillingAccountId ==='
SELECT
    'subscriptions_without_billing_account' AS check,
    s."Id" AS subscription_id,
    s."OwnerUserId" AS owner_user_id,
    'AccountSubscription has no BillingAccountId — BackfillBillingAccounts should have matched it to '
    || 'an account by OwnerUserId (the unique index on AccountSubscriptions.OwnerUserId guarantees '
    || 'at most one such account). Investigate why this row was missed.'
        AS detail
FROM "AccountSubscriptions" s
WHERE s."BillingAccountId" IS NULL;

\echo '=== billing-precheck: 3) NotificationChannels with no BillingAccountId ==='
SELECT
    'channels_without_billing_account' AS check,
    n."Id" AS channel_id,
    n."OwnerUserId" AS owner_user_id,
    n."PhoneNumber" AS phone_number,
    'NotificationChannel has no BillingAccountId — BackfillChannelBillingAccounts should have matched '
    || 'it to an account by OwnerUserId. Investigate why this row was missed.'
        AS detail
FROM "NotificationChannels" n
WHERE n."BillingAccountId" IS NULL;

\echo '=== billing-precheck: 4) Cross-account channel assignments ("company on someone else''s number") ==='
-- The headline check (SPEC §3.3 p.5, §43.6): a ChannelCompanyAssignment row whose company and whose
-- channel do NOT share the same billing account. This is exactly the state the composite FK
-- (ChannelCompanyAssignment -> NotificationChannels(Id, BillingAccountId)) is built to make
-- impossible going forward — but it can only be added if no such row exists today.
SELECT
    'cross_account_channel_assignment' AS check,
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
WHERE c."BillingAccountId" IS DISTINCT FROM n."BillingAccountId";

\echo '=== billing-precheck: 5) Companies with no owner (would silently sink into an orphaned account) ==='
SELECT
    'companies_without_owner' AS check,
    c."Id" AS company_id,
    c."Name" AS company_name,
    'Company has an empty/blank OwnerUserId. BackfillBillingAccounts groups companies into accounts '
    || 'by OwnerUserId, so a company with no owner never gets a billing account and will be caught by '
    || 'check (1) instead — listed here separately because the root cause is different (a data '
    || 'integrity problem predating this cycle, not a migration gap).'
        AS detail
FROM "Companies" c
WHERE c."OwnerUserId" IS NULL OR c."OwnerUserId" = '';

\echo '=== billing-precheck: 6) Duplicate AccountSubscription rows per account ==='
-- AccountSubscriptions.BillingAccountId carries a unique index (one subscription row per account) —
-- this check catches a violation before the migration trips over it, with a clearer message.
SELECT
    'duplicate_subscriptions_per_account' AS check,
    s."BillingAccountId" AS billing_account_id,
    count(*) AS finding_count,
    'More than one AccountSubscription row points at the same BillingAccountId — violates the '
    || 'unique index AccountSubscriptions.BillingAccountId and means the account''s subscription '
    || 'history is ambiguous. Resolve which row is authoritative before migrating.'
        AS detail
FROM "AccountSubscriptions" s
WHERE s."BillingAccountId" IS NOT NULL
GROUP BY s."BillingAccountId"
HAVING count(*) > 1;

\echo '=== billing-precheck: done. Empty result sets above (other than the section headers) mean the environment is clear to migrate. ==='
