\set ON_ERROR_STOP on
\timing on
-- QA цикл 25, бенч §515: 200 000 заказов за 366 дней у одного магазина (по 2 позиции), 3000 покупателей, сегодня — ~547 «Принят».
-- Запуск: psql -v shop=<uuid магазина> -v wd=<рабочий день YYYY-MM-DD> -d <БД> -f seed_orders.sql
-- Магазин и товары создаёт seed.py (через API); статусы: 70 % выдан, 10 % отменён покупателем, 8 % отклонён, 5 % отменён магазином, 7 % не забран.
-- товары: 12 (8 штучных, 4 весовых), клоны товара «Борщ», созданного seed.py
INSERT INTO "Products"
SELECT (jsonb_populate_record(NULL::"Products", to_jsonb(p) || jsonb_build_object('Id', gen_random_uuid(), 'Name', 'Товар ' || g,
  'Unit', CASE WHEN g > 8 THEN 1 ELSE 0 END, 'Position', g))).*
FROM "Products" p, generate_series(1, 12) g WHERE p."CompanyId" = :'shop' AND p."Name" = 'Борщ';
CREATE TEMP TABLE prod AS SELECT "Id", row_number() over (order by "Position", "Name") rn, "Unit", "Name", "Price" FROM "Products" WHERE "CompanyId"=:'shop' AND "DeletedAtUtc" IS NULL;
CREATE TEMP TABLE gen AS
SELECT i, ((i-1) % 366) AS d, ((i-1) / 366 + 1) AS seq FROM generate_series(1, 200000) i;
-- orders
INSERT INTO "Orders" ("Id","CompanyId","Number","BusinessDate","PublicToken","Status","Version","CustomerKind","CustomerUserId","CustomerName","CustomerPhone","CustomerPhoneVerified","Comment",
 "AcceptanceModeSnapshot","AllowCustomerCancelSnapshot","CustomerModeSnapshot","EstimatedTotal","FinalTotal","HasWeightItems","IsModifiedByShop","StatusReason","IdempotencyKey",
 "PersonalDataErased","CreatedAtUtc","AcceptedAtUtc","ReadyAtUtc","CompletedAtUtc","UpdatedAtUtc","NotifyByMessenger","PickupDate","PickupEndUtc","PickupKind","PickupStartUtc")
SELECT gen_random_uuid(), :'shop', (seq + 1000)::int, :'wd'::date - d, md5(i::text) || md5((i*7)::text),
  CASE WHEN d = 0 THEN 1 WHEN i % 100 < 70 THEN 3 WHEN i % 100 < 80 THEN 5 WHEN i % 100 < 88 THEN 4 WHEN i % 100 < 93 THEN 6 ELSE 7 END,
  1, 1, NULL,
  (ARRAY['Анна','Мария','Елена','Ольга','Иван','Пётр','Алексей','Дмитрий','Сергей','Юлия'])[1 + i % 10] || ' ' || (ARRAY['Иванова','Петрова','Смирнов','Кузнецова','Попов','Соколова','Лебедев','Козлова'])[1 + i % 8],
  '79' || lpad((i % 3000)::text, 9, '0'), false, CASE WHEN i % 5 = 0 THEN 'без лука' END,
  0, true, 0, (300 + (i % 20) * 50)::numeric, CASE WHEN d = 0 THEN NULL WHEN i % 100 < 70 THEN (300 + (i % 20) * 50)::numeric END, false, false, NULL, gen_random_uuid(),
  false, (:'wd'::date - d)::timestamp AT TIME ZONE 'UTC' + interval '5 hours' + (seq % 400) * interval '1 minute', NULL, NULL,
  CASE WHEN d = 0 THEN NULL ELSE (:'wd'::date - d)::timestamp AT TIME ZONE 'UTC' + interval '9 hours' END,
  (:'wd'::date - d)::timestamp AT TIME ZONE 'UTC' + interval '9 hours', false, :'wd'::date - d, NULL, 0,
  (:'wd'::date - d)::timestamp AT TIME ZONE 'UTC' + interval '6 hours' + (seq % 600) * interval '1 minute'
FROM gen;
-- items: 2 per order
INSERT INTO "OrderItems" ("Id","OrderId","Position","ProductId","NameSnapshot","Unit","UnitPrice","PortionTextSnapshot","WeightStepGrams","QuantityOrdered","QuantityActual","LineTotalEstimated","LineTotalFinal","ReservesStock")
SELECT gen_random_uuid(), o."Id", k, p."Id", p."Name", p."Unit", p."Price", NULL, CASE WHEN p."Unit" = 1 THEN 50 END,
  CASE WHEN p."Unit" = 1 THEN 250 + 50 * (o."Number" % 8) ELSE 1 + (o."Number" % 3) END,
  CASE WHEN o."Status" = 3 THEN CASE WHEN p."Unit" = 1 THEN 250 + 50 * (o."Number" % 8) ELSE 1 + (o."Number" % 3) END END,
  p."Price" * CASE WHEN p."Unit" = 1 THEN (250 + 50 * (o."Number" % 8)) / 1000.0 ELSE 1 + (o."Number" % 3) END,
  CASE WHEN o."Status" = 3 THEN p."Price" * CASE WHEN p."Unit" = 1 THEN (250 + 50 * (o."Number" % 8)) / 1000.0 ELSE 1 + (o."Number" % 3) END END, false
FROM "Orders" o CROSS JOIN generate_series(1, 2) k
JOIN prod p ON p.rn = 1 + ((o."Number" * 3 + k) % (SELECT count(*) FROM prod))
WHERE o."CompanyId" = :'shop' AND o."Number" >= 1000;
ANALYZE "Orders"; ANALYZE "OrderItems";
SELECT count(*) AS orders, count(*) FILTER (WHERE "Status"=1) AS accepted, count(DISTINCT "CustomerPhone") AS phones FROM "Orders" WHERE "CompanyId"=:'shop';
SELECT count(*) AS items FROM "OrderItems";
