-- deploy/checks/cycle19-retired-limit-options-live.sql
--
-- ARCHITECTURE_CYCLE19.md §385.2/§385.3. Read-only. The deploy-time gate: an "AccountSubscriptionOption"
-- row of a retired limit option (CapabilityKey, trimmed and lower-cased, is 'employees' or 'companies')
-- that is NOT finished yet — not closed (EndsAtUtc null or in the future) and, if it carries its own
-- paid-through date, that date has not passed. Subscription state and the current plan's own rule for
-- the option are deliberately NOT considered (§385.2) — this is stricter than "does it count in the
-- limit today".
--
-- Deliberately exactly ONE bare SELECT, no psql meta-commands (\echo/\pset/\gexec) — this file is also
-- read and executed by LIM19-020 through Npgsql, which cannot interpret psql meta-commands. Any change
-- to the WHERE clause here must be mirrored in ServiceBooking.API.Services.Billing.RetiredLimitOptions.
-- LiveRetiredRows (the LINQ twin) — LIM19-020 fails if the two ever disagree.
--
-- Empty result = OK (no unfinished purchases of a retired limit option). Any row = the deploy must stop
-- before touching the host (deploy-remote.sh's check_retired_limit_options, run right after
-- check_legal_manifest, before "Recording rollback point").
SELECT
    o."BillingAccountId" AS billing_account_id,
    so."Code" AS option_code,
    so."CapabilityKey" AS capability_key,
    o."Quantity" AS quantity,
    o."PaidUntilUtc" AS paid_until_utc,
    o."EndsAtUtc" AS ends_at_utc,
    o."ActivatedAtUtc" AS activated_at_utc
FROM "AccountSubscriptionOptions" o
JOIN "SubscriptionOptions" so ON so."Id" = o."OptionId"
WHERE lower(btrim(so."CapabilityKey")) IN ('employees', 'companies')
  AND (o."EndsAtUtc" IS NULL OR o."EndsAtUtc" > now())
  AND (o."PaidUntilUtc" IS NULL OR o."PaidUntilUtc" >= now())
ORDER BY o."BillingAccountId", so."Code";
