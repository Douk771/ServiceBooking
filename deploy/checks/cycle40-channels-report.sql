-- deploy/checks/cycle40-channels-report.sql
--
-- ARCHITECTURE_CYCLE40.md §40.17 (DO-40-01). READ-ONLY: ничего не пишет и не блокирует. Запускать на БД ДО выката цикла 40
-- (миграция Cycle40ChannelOptions ещё не применена), поэтому скрипт читает только колонки, существующие до неё:
-- ни Bookings.NotifyByMessenger, ни строки notifications.max может не быть.
--
-- Запуск (ручной, человеком; агенты на боевую машину не заходят):
--   docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres \
--     psql -U postgres -d servicebooking -v ON_ERROR_STOP=1 < deploy/checks/cycle40-channels-report.sql
-- Лучше сначала на копии боевой БД. Код возврата psql != 0 — СТОП (см. блок 1).
--
-- Блоки: 1 стоп-сигнал (оплаченные каналы); 2 строки двух опций без даты; 3 аккаунты с двумя живыми каналами одного
-- транспорта; 4 компании, которые начнут слать без назначения; 5 будущие записи, по которым напоминания перестанут уходить
-- (Р40-Ю2) — блок 5 «для сведения заказчику», на выкат не влияет.
-- Транспорт: 0 = WhatsApp, 1 = Max. ChannelState: 7 = Replaced (живой канал = State <> 7).

\pset footer off
\pset null '(null)'

\echo '=== cycle40-report [1] СТОП-СИГНАЛ: каналы, оплаченные по старому и по новому правилу (ожидается 0 и 0) ==='
-- Старое правило (до цикла 40): у аккаунта есть живая (не истёкшая) строка опции notifications.whatsapp.
-- Новое правило (§40.3.1): оплата по транспорту — строка своей опции (whatsapp -> notifications.whatsapp,
-- max -> notifications.max) с датой: EndsAtUtc не прошёл И (PaidUntilUtc >= now ИЛИ PaidUntilUtc пуст и [триал: до конца триала |
-- наследная строка: до конца подписки «Записей»]).
CREATE TEMP TABLE _c40_channel_pay AS
SELECT
    c."Id" AS channel_id,
    c."BillingAccountId" AS billing_account_id,
    c."Transport" AS transport,
    c."State" AS state,
    c."PhoneNumber" AS phone,
    EXISTS (
        SELECT 1 FROM "AccountSubscriptionOptions" o
        JOIN "SubscriptionOptions" so ON so."Id" = o."OptionId"
        WHERE o."BillingAccountId" = c."BillingAccountId"
          AND so."Code" = 'notifications.whatsapp'
          AND o."Quantity" >= 1
          AND (o."EndsAtUtc" IS NULL OR o."EndsAtUtc" > now())
    ) AS paid_old_rule,
    EXISTS (
        SELECT 1 FROM "AccountSubscriptionOptions" o
        JOIN "SubscriptionOptions" so ON so."Id" = o."OptionId"
        JOIN "BillingAccounts" ba ON ba."Id" = o."BillingAccountId"
        LEFT JOIN "AccountSubscriptions" s ON s."BillingAccountId" = o."BillingAccountId"
        WHERE o."BillingAccountId" = c."BillingAccountId"
          AND so."Code" = CASE c."Transport" WHEN 1 THEN 'notifications.max' ELSE 'notifications.whatsapp' END
          AND o."Quantity" >= 1
          AND (o."EndsAtUtc" IS NULL OR o."EndsAtUtc" > now())
          AND (
                (o."PaidUntilUtc" IS NOT NULL AND o."PaidUntilUtc" >= now())
             OR (o."PaidUntilUtc" IS NULL AND o."GrantedByTrial" AND ba."TrialEndsAtUtc" > now())
             OR (o."PaidUntilUtc" IS NULL AND NOT o."GrantedByTrial" AND s."PaidUntil" >= now())
          )
    ) AS paid_new_rule
FROM "NotificationChannels" c
WHERE c."State" <> 7;

SELECT
    count(*) FILTER (WHERE paid_old_rule) AS paid_by_old_rule,
    count(*) FILTER (WHERE paid_new_rule) AS paid_by_new_rule,
    count(*) AS live_channels_total
FROM _c40_channel_pay;

SELECT channel_id, billing_account_id,
       CASE transport WHEN 1 THEN 'Max' ELSE 'WhatsApp' END AS transport,
       state, phone, paid_old_rule, paid_new_rule
FROM _c40_channel_pay
WHERE paid_old_rule OR paid_new_rule
ORDER BY billing_account_id, transport;

\echo '=== cycle40-report [2] строки опций notifications.whatsapp/max без даты (PaidUntilUtc IS NULL): триальные / наследные ==='
SELECT
    o."BillingAccountId" AS billing_account_id,
    so."Code" AS option_code,
    o."Quantity" AS quantity,
    CASE WHEN o."GrantedByTrial" THEN 'trial' ELSE 'legacy (по сроку подписки)' END AS kind,
    o."EndsAtUtc" AS ends_at_utc,
    ba."TrialEndsAtUtc" AS account_trial_ends_utc,
    s."PaidUntil" AS subscription_paid_until
FROM "AccountSubscriptionOptions" o
JOIN "SubscriptionOptions" so ON so."Id" = o."OptionId"
JOIN "BillingAccounts" ba ON ba."Id" = o."BillingAccountId"
LEFT JOIN "AccountSubscriptions" s ON s."BillingAccountId" = o."BillingAccountId"
WHERE so."Code" IN ('notifications.whatsapp', 'notifications.max')
  AND o."PaidUntilUtc" IS NULL
ORDER BY o."BillingAccountId", so."Code";

\echo '=== cycle40-report [3] аккаунты с двумя и более живыми каналами одного транспорта ("лишний номер", §5.2 SPEC) ==='
SELECT
    c."BillingAccountId" AS billing_account_id,
    CASE c."Transport" WHEN 1 THEN 'Max' ELSE 'WhatsApp' END AS transport,
    count(*) AS live_channels,
    string_agg(c."Id"::text || ' [' || c."State"::text || ' ' || coalesce(c."PhoneNumber", '-') || ']', ', ' ORDER BY c."CreatedAt", c."Id") AS channels
FROM "NotificationChannels" c
WHERE c."State" <> 7
GROUP BY c."BillingAccountId", c."Transport"
HAVING count(*) > 1
ORDER BY c."BillingAccountId", transport;

\echo '=== cycle40-report [4] активные компании, у которых есть живой канал аккаунта, но НЕТ назначения на него: после выката начнут слать ==='
-- Раньше сообщения шли только через ChannelCompanyAssignments; теперь номер работает на все компании аккаунта (US-03).
SELECT
    co."Id" AS company_id,
    co."Name" AS company_name,
    co."BillingAccountId" AS billing_account_id,
    CASE c."Transport" WHEN 1 THEN 'Max' ELSE 'WhatsApp' END AS transport,
    c."Id" AS channel_id,
    c."State" AS channel_state
FROM "Companies" co
JOIN "NotificationChannels" c ON c."BillingAccountId" = co."BillingAccountId" AND c."State" <> 7
WHERE co."IsActive"
  AND NOT EXISTS (
      SELECT 1 FROM "ChannelCompanyAssignments" a
      WHERE a."CompanyId" = co."Id" AND a."Transport" = c."Transport")
ORDER BY co."BillingAccountId", co."Name", transport;

\echo '=== cycle40-report [5] будущие записи без аккаунта клиента: по ним напоминания в мессенджер перестанут уходить (Р40-Ю2) — для сведения ==='
-- До выката отличить запись сотрудника от гостевой можно только косвенно: гостевая запись несёт снимок согласия
-- (ConsentAcceptedAtUtc), запись сотрудника — нет. У тех и других ClientId пуст. Часть гостей после выката поставит отметку
-- сама (новые записи), но уже созданные получают NotifyByMessenger = null -> шлём только на номер аккаунта с согласием.
SELECT
    co."Id" AS company_id,
    co."Name" AS company_name,
    count(*) FILTER (WHERE b."ConsentAcceptedAtUtc" IS NULL) AS future_staff_created,
    count(*) FILTER (WHERE b."ConsentAcceptedAtUtc" IS NOT NULL) AS future_guest,
    count(*) AS future_total
FROM "Bookings" b
JOIN "Companies" co ON co."Id" = b."CompanyId"
WHERE b."ClientId" IS NULL
  AND b."GuestPhone" IS NOT NULL
  AND b."Status" IN (0, 1)
  AND b."Date" >= (now() AT TIME ZONE 'UTC')::date
GROUP BY co."Id", co."Name"
ORDER BY future_total DESC, co."Name";

SELECT
    count(*) FILTER (WHERE b."ConsentAcceptedAtUtc" IS NULL) AS total_future_staff_created,
    count(*) FILTER (WHERE b."ConsentAcceptedAtUtc" IS NOT NULL) AS total_future_guest
FROM "Bookings" b
WHERE b."ClientId" IS NULL
  AND b."GuestPhone" IS NOT NULL
  AND b."Status" IN (0, 1)
  AND b."Date" >= (now() AT TIME ZONE 'UTC')::date;

\echo '=== cycle40-report: проверка стоп-сигнала (блок 1) ==='
-- Падает (и с ON_ERROR_STOP даёт ненулевой код возврата), если найден хотя бы один оплаченный канал.
DO $$
DECLARE n_old int; n_new int;
BEGIN
    SELECT count(*) FILTER (WHERE paid_old_rule), count(*) FILTER (WHERE paid_new_rule)
      INTO n_old, n_new FROM _c40_channel_pay;
    IF n_old > 0 OR n_new > 0 THEN
        RAISE EXCEPTION 'СТОП: оплаченных каналов по старому правилу %, по новому %. Выкат цикла 40 остановлен до решения заказчика (DEPLOY.md §29 п. 1)', n_old, n_new;
    END IF;
    RAISE NOTICE 'OK: оплаченных каналов нет (0 и 0)';
END $$;

DROP TABLE _c40_channel_pay;
