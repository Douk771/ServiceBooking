-- deploy/checks/cycle43-messenger-options.sql
--
-- ARCHITECTURE_CYCLE43.md §43.11 (DO-43-03). READ-ONLY: только SELECT и временные таблицы сессии. Запускать ДО и ПОСЛЕ выката
-- цикла 43; читает только колонки, существующие до миграции Cycle43MessengerOptionPricing. Строки опций должны совпасть.
--
-- Запуск (ручной, человеком; агенты на боевую машину не заходят), вывод сохранить в файл:
--   docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres \
--     psql -U postgres -d servicebooking -v ON_ERROR_STOP=1 < deploy/checks/cycle43-messenger-options.sql | tee cycle43-before.txt
-- Транспорт: whatsapp -> notifications.whatsapp, max -> notifications.max.

\pset footer off
\pset null '(null)'

\echo '=== cycle43-report [1] каталог двух опций: цена и IsActive ==='
SELECT so."Code", so."PricePerMonth", so."IsActive", so."IsPublic", so."MaxQuantity"
FROM "SubscriptionOptions" so
WHERE so."Code" IN ('notifications.whatsapp', 'notifications.max')
ORDER BY so."Code";

\echo '=== cycle43-report [2] переключатели открытия продаж (нет строки = умолчание из конфигурации) ==='
SELECT ps."Key", ps."Value"
FROM "PlatformSettings" ps
WHERE ps."Key" IN ('notifications.option.whatsapp.open', 'notifications.option.max.open')
ORDER BY ps."Key";

\echo '=== cycle43-report [3] строки опций по транспорту x вид строки ==='
SELECT so."Code" AS option_code,
       CASE
         WHEN o."GrantedByTrial" AND o."PaidUntilUtc" IS NULL THEN 'пробная без даты'
         WHEN o."GrantedByTrial" THEN 'пробная с датой'
         WHEN o."PaidUntilUtc" IS NULL THEN 'наследная (без своей даты)'
         WHEN o."PaidUntilUtc" >= now() THEN 'своя дата в будущем'
         ELSE 'своя дата в прошлом'
       END AS row_kind,
       (o."EndsAtUtc" IS NOT NULL) AS ends_at_set,
       count(*) AS rows_count
FROM "AccountSubscriptionOptions" o
JOIN "SubscriptionOptions" so ON so."Id" = o."OptionId"
WHERE so."Code" IN ('notifications.whatsapp', 'notifications.max')
GROUP BY 1, 2, 3
ORDER BY 1, 2, 3;

\echo '=== cycle43-report [4] оплаченный мессенджер без подписки «Записей» (И-4) ==='
SELECT o."BillingAccountId", so."Code" AS option_code, o."PaidUntilUtc", o."GrantedByTrial", s."PaidUntil" AS subscription_paid_until
FROM "AccountSubscriptionOptions" o
JOIN "SubscriptionOptions" so ON so."Id" = o."OptionId"
LEFT JOIN "AccountSubscriptions" s ON s."BillingAccountId" = o."BillingAccountId"
WHERE so."Code" IN ('notifications.whatsapp', 'notifications.max')
  AND o."Quantity" >= 1
  AND (o."EndsAtUtc" IS NULL OR o."EndsAtUtc" > now())
  AND (o."PaidUntilUtc" IS NOT NULL AND o."PaidUntilUtc" >= now())
  AND (s."BillingAccountId" IS NULL OR s."PaidUntil" IS NULL OR s."PaidUntil" < now())
ORDER BY o."BillingAccountId", so."Code";

\echo '=== cycle43-report [5] число PlanOptionRules мессенджеров (должно не меняться) ==='
SELECT so."Code" AS option_code, count(*) AS plan_option_rules
FROM "PlanOptionRules" r
JOIN "SubscriptionOptions" so ON so."Id" = r."OptionId"
WHERE so."Code" IN ('notifications.whatsapp', 'notifications.max')
GROUP BY so."Code"
ORDER BY so."Code";
