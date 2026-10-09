-- deploy/checks/cycle40-rollback-assignments.sql
--
-- ARCHITECTURE_CYCLE40.md §40.17 (DO-40-01). ЗАПИСЫВАЕТ в БД. Запускать ТОЛЬКО при откате релиза цикла 40 назад (после
-- deploy/rollback.sh), решением человека. Цикл 40 не читал и не писал ChannelCompanyAssignments (номер работал на все
-- компании аккаунта), поэтому у компаний, подключённых после выката, назначений нет — а старый код маршрутизирует только по ним.
-- Скрипт назначает каждую активную компанию аккаунта на ПЕРВЫЙ живой канал (State <> 7 Replaced, по CreatedAt, Id)
-- каждого транспорта её аккаунта. Идемпотентен: существующие назначения не трогает (уникальность (CompanyId, Transport)),
-- повторный запуск ничего не меняет. Составной FK (ChannelId, BillingAccountId, Transport) гарантирован выборкой из того же аккаунта.
-- Схему БД НЕ откатывает (миграция остаётся; старый код работает на новой схеме, §40.2.1).
--
-- Запуск (человеком на боевой машине, лучше сначала на копии):
--   docker compose -f docker-compose.prod.yml --env-file .env exec -T postgres \
--     psql -U postgres -d servicebooking -v ON_ERROR_STOP=1 < deploy/checks/cycle40-rollback-assignments.sql
-- AssignedByUserId = владелец канала (OwnerUserId) — NOT NULL в схеме.

BEGIN;

\echo '=== до: назначений ==='
SELECT count(*) AS assignments_before FROM "ChannelCompanyAssignments";

WITH first_live AS (
    SELECT DISTINCT ON (c."BillingAccountId", c."Transport")
        c."Id", c."BillingAccountId", c."Transport", c."OwnerUserId"
    FROM "NotificationChannels" c
    WHERE c."State" <> 7 AND c."BillingAccountId" IS NOT NULL
    ORDER BY c."BillingAccountId", c."Transport", c."CreatedAt", c."Id"
),
ins AS (
    INSERT INTO "ChannelCompanyAssignments"
        ("Id", "ChannelId", "CompanyId", "Transport", "BillingAccountId", "AssignedAtUtc", "AssignedByUserId")
    SELECT gen_random_uuid(), f."Id", co."Id", f."Transport", f."BillingAccountId", now(), f."OwnerUserId"
    FROM first_live f
    JOIN "Companies" co ON co."BillingAccountId" = f."BillingAccountId" AND co."IsActive"
    WHERE NOT EXISTS (
        SELECT 1 FROM "ChannelCompanyAssignments" a
        WHERE a."CompanyId" = co."Id" AND a."Transport" = f."Transport")
    RETURNING 1
)
SELECT count(*) AS assignments_inserted FROM ins;

\echo '=== после: назначений ==='
SELECT count(*) AS assignments_after FROM "ChannelCompanyAssignments";

COMMIT;
