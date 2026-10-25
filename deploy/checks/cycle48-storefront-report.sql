-- deploy/checks/cycle48-storefront-report.sql
--
-- ARCHITECTURE_CYCLE48_STOREFRONT.md §48.14.3 (DO-48-3). READ-ONLY: только SELECT. Печатается в лог КАЖДОГО выката
-- (deploy-remote.sh, check_cycle48_slugs, перед cycle48-slugs-live.sql), чтобы заказчик видел состояние боя без доступа к хосту.
-- Стоп-сигнал здесь один — блок 1, он вынесен в cycle48-slugs-live.sql. Остальные блоки только информируют (ТЛ48-08…10, R48-4, R48-14).
-- Этот файл запускает только deploy-remote.sh через psql, поэтому \echo уместен.

\pset footer off
\pset null '(null)'

\echo '=== cycle48-report [1] компании не-салоны с адресом contacts / payment-info (СТОП, если есть строки) ==='
SELECT c."Id", c."Kind", c."Slug", c."Name"
FROM "Companies" c
WHERE c."Kind" <> 0
  AND lower(c."Slug") IN ('contacts', 'payment-info')
ORDER BY c."Kind", c."Slug";

\echo '=== cycle48-report [2] строки «Сеть», «Сеть магазинов», «Пробный период»: тексты на бою (сверить с `ops tariffs plan`; md5 — чтобы увидеть правку) ==='
SELECT p."Line", p."Name", p."IsActive", p."IsPublic", p."IsSystemTrial", p."PricePerMonth",
       p."Description", p."Highlights",
       md5(coalesce(p."Description", '') || '|' || coalesce(p."Highlights", '')) AS text_md5
FROM "SubscriptionPlanConfigs" p
WHERE p."Name" IN ('Сеть', 'Сеть магазинов') OR p."IsSystemTrial"
ORDER BY p."Line", p."SortOrder", p."Name";

\echo '=== cycle48-report [3] pricing.public-enabled (нет строки = витрина выключена, R48-4) ==='
SELECT s."Key", s."Value", s."UpdatedAt"
FROM "PlatformSettings" s
WHERE s."Key" = 'pricing.public-enabled';

\echo '=== cycle48-report [4] pricing.legal-notice и pricing.orders.legal-notice (на бою ожидаются пустыми, A48-7) ==='
SELECT s."Key", s."Value", s."UpdatedAt"
FROM "PlatformSettings" s
WHERE s."Key" IN ('pricing.legal-notice', 'pricing.orders.legal-notice')
ORDER BY s."Key";

\echo '=== cycle48-report [5] окно рассылок пробного периода, дней (нет строки = умолчание 7, R48-14) ==='
SELECT s."Key", s."Value", s."UpdatedAt"
FROM "PlatformSettings" s
WHERE s."Key" = 'trial.mailing-window-days';

\echo '=== cycle48-report [6] аккаунты на «Сети» и «Сети магазинов» (ТЛ48-10: счета по опубликованной цене) ==='
SELECT sub."BillingAccountId", p."Line", p."Name" AS plan_name, p."PricePerMonth", sub."IsActive", sub."PaidUntil"
FROM "AccountSubscriptions" sub
JOIN "SubscriptionPlanConfigs" p ON p."Id" = sub."PlanConfigId"
WHERE p."Name" IN ('Сеть', 'Сеть магазинов')
ORDER BY p."Line", sub."IsActive" DESC, sub."PaidUntil" NULLS LAST, sub."BillingAccountId";

\echo '=== cycle48-report: done. ==='
