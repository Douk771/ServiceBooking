-- QA цикл 25, бенч §515: 200 витринных магазинов в том же городе (клоны магазина qa-shop-25, по одному опубликованному товару).
-- Запуск: psql -d <БД> -f seed_shops.sql. Перед клонированием приведите исходный магазин в состояние «открыт, принимает».
\set ON_ERROR_STOP on
CREATE TEMP TABLE newshops AS SELECT gen_random_uuid() AS id, g FROM generate_series(1, 200) g;
INSERT INTO "Companies"
SELECT (jsonb_populate_record(NULL::"Companies", to_jsonb(c) || jsonb_build_object('Id', n.id, 'Slug', 'bench-shop-' || n.g, 'Name', 'Магазин ' || lpad(n.g::text, 3, '0'), 'Address', 'ул. Тестовая, ' || n.g))).*
FROM "Companies" c CROSS JOIN newshops n WHERE c."Slug" = 'qa-shop-25';
INSERT INTO "ShopSettings"
SELECT (jsonb_populate_record(NULL::"ShopSettings", to_jsonb(s) || jsonb_build_object('CompanyId', n.id))).*
FROM "ShopSettings" s CROSS JOIN newshops n WHERE s."CompanyId" = (SELECT "Id" FROM "Companies" WHERE "Slug" = 'qa-shop-25');
INSERT INTO "Products"
SELECT (jsonb_populate_record(NULL::"Products", to_jsonb(p) || jsonb_build_object('Id', gen_random_uuid(), 'CompanyId', n.id))).*
FROM (SELECT * FROM "Products" WHERE "CompanyId" = (SELECT "Id" FROM "Companies" WHERE "Slug" = 'qa-shop-25') AND "IsPublished" AND "DeletedAtUtc" IS NULL ORDER BY "Position" LIMIT 1) p CROSS JOIN newshops n;
SELECT count(*) FROM "Companies" WHERE "Slug" LIKE 'bench-shop-%';
