-- deploy/checks/cycle48-slugs-live.sql
--
-- ARCHITECTURE_CYCLE48_STOREFRONT.md §48.7.4, API_CONTRACT_CYCLE48_STOREFRONT.md §48.30.3 (DO-48-3). Read-only.
-- Deploy-time gate: цикл 48 резервирует первые сегменты `contacts` и `payment-info` на сайтах goods/dom/bani. Если на бою уже
-- есть магазин, «Дом» или «Баня» (Kind <> 0, не салон) с таким адресом, его страница станет недоступна.
--
-- Deliberately exactly ONE bare SELECT, no psql meta-commands (\echo/\pset), as cycle19-retired-limit-options-live.sql:
-- deploy-remote.sh reads the rows through `psql -At` and treats any row as a stop (check_cycle48_slugs).
-- Empty result = OK.
SELECT c."Id", c."Kind", c."Slug"
FROM "Companies" c
WHERE c."Kind" <> 0
  AND lower(c."Slug") IN ('contacts', 'payment-info')
