-- deploy/checks/cycle49-catalog-report.sql
--
-- ARCHITECTURE_CYCLE49_LISTING_SUBSCRIPTION.md §49.7.1, API_CONTRACT_CYCLE49_LISTING_SUBSCRIPTION.md §49.29 (DO-49-2).
-- READ-ONLY: только SELECT. Печатается в лог КАЖДОГО выката (deploy-remote.sh, check_cycle49_catalog) и запускается вручную на бою
-- ДО выката (DEPLOY.md §36.3), чтобы заказчик решил (Q49-1, M49-05). Без телефонов, e-mail и ФИО.
-- Старое правило: Kind=0 (салон) AND IsActive AND ShowInPublicListing AND нет действующей подписки на активном тарифе с AllowPublicListing=false
-- («действует» = IsActive AND (PaidUntil IS NULL OR PaidUntil >= now()), как SubscriptionUsability).
-- Новое правило: старое AND есть активная услуга AND есть участник Role IN (1=Master, 2=CompanyOwner) с ProvidesServices.
-- Этот файл запускает только psql, поэтому \echo уместен.

\pset footer off
\pset null '(null)'

\echo '=== cycle49-report [1] итоги: сейчас / после / пропадут ==='
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
)
SELECT count(*) AS visible_now,
       count(*) FILTER (WHERE has_service AND has_provider) AS visible_after,
       count(*) FILTER (WHERE NOT (has_service AND has_provider)) AS disappearing
FROM base;

\echo '=== cycle49-report [2] пропадающие салоны (имя, адрес, причина, витринная компания) ==='
WITH base AS (
    SELECT c."Id", c."Name", c."Slug", c."IsShowcase",
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
)
SELECT b."Name" AS name, b."Slug" AS slug,
       CASE WHEN NOT b.has_service AND NOT b.has_provider THEN 'нет активных услуг и оказывающих услуги'
            WHEN NOT b.has_service THEN 'нет активных услуг'
            ELSE 'нет оказывающих услуги' END AS reason,
       b."IsShowcase" AS is_showcase
FROM base b
WHERE NOT (b.has_service AND b.has_provider)
ORDER BY b."Name", b."Slug";

\echo '=== cycle49-report: done. ==='
