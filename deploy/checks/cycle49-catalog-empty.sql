-- deploy/checks/cycle49-catalog-empty.sql
--
-- ARCHITECTURE_CYCLE49_LISTING_SUBSCRIPTION.md §49.7.2, API_CONTRACT_CYCLE49_LISTING_SUBSCRIPTION.md §49.29 (DO-49-2). Read-only.
-- Стоп-условие выката: новое правило каталога салонов скрывает ВСЕ салоны (visible_now > 0 AND visible_after = 0).
-- Ровно один голый SELECT без метакоманд psql (как cycle48-slugs-live.sql): deploy-remote.sh читает строки через `psql -At`
-- и любую строку считает стопом, пока в .env нет CYCLE49_EMPTY_CATALOG_ACCEPTED=true. Пустой результат = можно выкатывать.
-- Правило «сейчас» и «после» — то же, что в cycle49-catalog-report.sql (держать в синхроне; согласие с кодом проверяет CY49-D1).
WITH base AS (
    SELECT c."Id",
           EXISTS (SELECT 1 FROM "Services" s WHERE s."CompanyId" = c."Id" AND s."IsActive") AS has_service,
           EXISTS (SELECT 1 FROM "CompanyMembers" m
                   WHERE m."CompanyId" = c."Id" AND m."Role" IN (1, 2) AND m."ProvidesServices") AS has_provider
    FROM "Companies" c
    WHERE c."Kind" = 0 AND c."IsActive" AND c."ShowInPublicListing"
      AND NOT EXISTS (SELECT 1
                      FROM "AccountSubscriptions" sub
                      JOIN "SubscriptionPlanConfigs" p ON p."Id" = sub."PlanConfigId"
                      WHERE sub."BillingAccountId" = c."BillingAccountId"
                        AND sub."IsActive" AND (sub."PaidUntil" IS NULL OR sub."PaidUntil" >= now())
                        AND p."IsActive" AND NOT p."AllowPublicListing")
),
totals AS (
    SELECT count(*) AS visible_now,
           count(*) FILTER (WHERE has_service AND has_provider) AS visible_after
    FROM base
)
SELECT visible_now, visible_after
FROM totals
WHERE visible_now > 0 AND visible_after = 0;
