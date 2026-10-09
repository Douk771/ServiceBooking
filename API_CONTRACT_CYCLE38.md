# API_CONTRACT — цикл 38 ServiceBooking: публичные тарифы «Заказов»

**Разделы §38.20–§38.27.** Решения — `ARCHITECTURE_CYCLE38.md` §38.0–§38.17. Форма — `contracts/cycle38/openapi.yaml`
(при расхождении по форме прав он). Проект существующий: ниже описаны фактические маршруты, которых касается цикл, и одна новинка.

Сводка HTTP-изменений цикла:

| Маршрут | Изменение |
|---|---|
| `GET /api/pricing/orders` | **новый**, анонимный |
| `GET /api/pricing` | **не меняется** (ни тело, ни коды, ни заголовки, ни кеш) |
| `GET /api/admin/pricing/preview` | не меняется (только «Запись») |
| `GET/PUT /api/admin/platform-settings` | **не меняется** — переключателя «Заказов» нет (решение заказчика Q38-3) |
| `POST/PUT/DELETE /api/admin/plans…`, `…/options…` | не меняются; побочный эффект — сброс кеша сетки «Заказов» тоже (§38.23) |
| `GET /api/billing/subscription?line=Orders` | не меняется; новые тарифы появятся в `availablePlans` после `ops tariffs apply` |

Миграций БД нет. Новый CLI-вывод — в `ops tariffs plan|apply` (§38.24).

---

## §38.20. Конвенции

Без изменений: JSON camelCase; деньги — `number` в рублях; `null` пишется явно (сериализатор не опускает `null`);
404 — пустое тело; анонимные публичные маршруты — без rate limit, ответ из памяти сервера.

## §38.21. `GET /api/pricing` — как есть (для сверки «ни на байт»)

Описан контрактами циклов 7, 18, 28 (`contracts/cycle7/openapi.yaml`, `API_CONTRACT_CYCLE7.md` §39, `API_CONTRACT_CYCLE18.md` §370).
Фактическое поведение на `55d01ee`, которое цикл обязан сохранить:
- 404 без тела, если выключен `pricing.public-enabled`, или соглашение `TermsOwner` — черновик, или правовой снимок недоступен;
- 200: `{ version, currency: "RUB", plans: PublicPlanDto[], options: PublicOptionDto[], notice, legalNotice }`, где
  `PublicPlanDto = { id, name, description, pricePerMonth, highlights (≤5), includedCompanies, includedEmployees, sortOrder, isFree, isTrial }`;
- **только** тарифы `Line = Services` с `IsActive && IsPublic`;
- `ETag: W/"<16 hex>"`, `Cache-Control: public, max-age=60`, `If-None-Match` → 304; кеш `pricing:public`, 60 с.

Проверка неизменности: существующие тесты `PricingTests`, `LegalPricingGateTests`, `Cycle28TariffsTests`, `PricingCatalogBuilderTests`
зелёные без правки; новый тест T38-B05 проверяет, что тарифы Orders в ответ не попадают.

## §38.22. `GET /api/pricing/orders` — новый

**Доступ:** анонимный (`[AllowAnonymous]`), без rate limit, вне `LegalConsentFilter` (анонимный запрос он не трогает).

**Запрос:** без параметров. Необязательный заголовок `If-None-Match`.

**Ответы:**

| Код | Когда | Тело | Заголовки |
|---|---|---|---|
| 200 | в линейке Orders есть хотя бы один тариф `IsActive && IsPublic`, не служебный | `OrdersPublicPricingDto` | `ETag: W/"<16 hex>"`, `Cache-Control: public, max-age=60` |
| 304 | `If-None-Match` совпал с текущим ETag | пусто | — |
| 404 | таких тарифов нет (например, на бою до `ops tariffs apply`) | пусто | — |

Других кодов маршрут не отдаёт (500 — только как общий сбой). Переключатель `pricing.public-enabled` и состояние `TermsOwner`
на ответ **не влияют**; сетка «Записи» и сетка «Заказов» публикуются независимо.

**Тело 200:**

```json
{
  "version": "3f9a0c1d2e4b5a69",
  "currency": "RUB",
  "plans": [
    {
      "id": "5a1e0c38-0000-4000-8000-000000000020",
      "name": "Лавка",
      "description": "Для пекарни, кофейни или небольшого магазина с постоянным потоком заказов.",
      "pricePerMonth": 690,
      "highlights": ["Всё, что в бесплатном", "Предзаказ на дату и меню на день", "Весовые товары с точной суммой при выдаче"],
      "includedShops": 1,
      "includedMembers": 5,
      "includedProductsPerShop": 300,
      "includedOrdersPerMonth": 1500,
      "sortOrder": 20,
      "isFree": false
    }
  ],
  "notice": "Подключение тарифа выполняет администратор платформы — оставьте заявку в кабинете, и мы свяжемся с вами.",
  "legalNotice": null
}
```

| Поле | Тип | Источник / правило |
|---|---|---|
| `version` | string, 16 hex | = значение внутри ETag (SHA-256 JSON тела, первые 16 hex) |
| `currency` | `"RUB"` | константа |
| `plans` | array, ≥ 1 | `Line = Orders && IsActive && IsPublic && Id ∉ {служебные}`; сортировка `SortOrder`, затем `PricePerMonth` |
| `plans[].id` | uuid | `SubscriptionPlanConfig.Id` |
| `plans[].name` | string | `Name` |
| `plans[].description` | string \| null | `Description` |
| `plans[].pricePerMonth` | number ≥ 0 | `PricePerMonth` |
| `plans[].highlights` | string[] ≤ 5 | `Highlights` по `\n`, `Trim`, пустые выброшены, первые 5 (то же правило, что у «Записи») |
| `plans[].includedShops` | int \| null | `MaxCompanies`; `null` — без ограничения |
| `plans[].includedMembers` | int \| null | `MaxEmployees` (участники вместе с владельцем); `null` — без ограничения |
| `plans[].includedProductsPerShop` | int, **не null** | `min(MaxProductsPerShop ?? потолок, потолок)`, потолок — `Orders:MaxProductsPerShop` (по умолчанию 1000) |
| `plans[].includedOrdersPerMonth` | int \| null | `MaxOrdersPerMonth` (на аккаунт, календарный месяц); `null` — без ограничения |
| `plans[].sortOrder` | int | `SortOrder` |
| `plans[].isFree` | bool | `IsSystemFree` |
| `notice` | string | константа `OrdersPricingCatalogBuilder.DefaultNotice` |
| `legalNotice` | string \| null | `PlatformSettings["pricing.orders.legal-notice"]`, пустое/пробелы → `null` |

**Чего в ответе нет никогда:** поля `options` и `isTrial`; тарифов линейки Services; служебного «Демо»
(`ShowcaseCatalog.OrdersShowcasePlanId`) — даже если ему поставят `IsPublic` в админке; тарифов с `IsActive = false` или
`IsPublic = false`; правил опций; числа подписчиков; флагов `Allow*`.

## §38.23. Кеш и сброс

- Ключ `pricing:public:orders`, 60 с, `IMemoryCache`. Кешируется и результат «сетки нет» (404).
- `PricingCatalogCache.Invalidate()` сбрасывает `pricing:public`, `pricing:public:orders` и `pricing:public-enabled`. Он уже
  вызывается из всех изменяющих действий `AdminPlansController` и `AdminOptionsController` и при смене `pricingPublicEnabled`:
  правка тарифа «Заказов» в админке видна на `GET /api/pricing/orders` сразу.
- `ops tariffs apply` выполняется в отдельном процессе и кеш работающего API не сбрасывает: изменения видны в течение 60 с
  (так же, как у «Записи», строка отчёта об этом остаётся).

## §38.24. `ops tariffs plan|apply` — дополнение отчёта

Аргументы, коды выхода и замок `ops:tariffs` — без изменений (`ARCHITECTURE_CYCLE28.md` §573.1). Порядок вывода:
1. Раздел «Записи» — строки как сейчас, без изменений формата.
2. Раздел «Заказов», строки с префиксом `«Заказы»: `:
   - `«Заказы»: создан: Лавка (id 5a1e0c38-0000-4000-8000-000000000020)` / `будет создан: …`;
   - `«Заказы»: уже есть, не трогаю: {Name} — совпадает с сеткой` или `— цена в админке 790, в сетке 690 — оставлено как есть; …`;
   - `«Заказы»: правила опций: добавлено N (у созданных тарифов — «доплата» для notifications.whatsapp, остальные «недоступна»)`;
   - `«Заказы»: бесплатный тариф (решение Q38-2): изменено — название: «Заказы · Бесплатно» → «Бесплатный»` (по строке на поле) или
     `«Заказы»: бесплатный тариф «{Name}»: уже соответствует сетке или изменён в админке — не трогаю`;
   - `«Заказы»: переключателя нет — сетка видна на GET /api/pricing/orders, как только в ней есть публичный тариф`.
3. Общие хвостовые строки (`готово; …60 секунд` / `режим: только показать …`) — как сейчас.

`TariffCatalogReport` (внутренний тип, не HTTP) получает поля `OrdersPlansCreated`, `OrdersRulesAdded`, `OrdersFreeFieldsAligned`
в конце, с умолчанием 0. Значения существующих полей относятся только к «Записи».

`ops demo reset --yes` вызывает тот же `ApplyAsync` — сетка «Заказов» засевается на demo.zakaz без отдельного шага.

## §38.25. Контракты внутри фронта (между исполнителями фронта и QA)

Не HTTP, но фиксируются здесь, чтобы задачи T-38-04…10 шли параллельно:
- типы конфига шаблона — `ARCHITECTURE_CYCLE38.md` §38.3.1 (файл `src/components/landing/types.ts`);
- `PricingLine`/`PricingGridView`/`PricingPlanView` — §38.7.1 (файл `src/components/pricing/pricingLine.ts`);
- ключи TanStack Query: `['public-pricing', 'services']`, `['public-pricing', 'orders']`; старый `['public-pricing']` не используется;
- `goods/src/api/ordersPricing.ts`: `ordersPricingApi.get(): Promise<OrdersPublicPricingDto | null>` — `null` только на 404, прочие
  ошибки пробрасываются; тип — из `src/types/api-cycle38.generated.ts` (`components['schemas']['OrdersPublicPricingDto']`);
- FAQ: `FaqItem = { question: string; answer: string | readonly [string, ...string[]]; link?: { to: string; label: string } }`,
  6–8 пунктов на сервис;
- стабильные якоря и метки DOM, на которые опираются тесты и внешние ссылки:

| Сервис | id / атрибут |
|---|---|
| оба | `#faq` (секция FAQ), `#faq-title`, `#pricing-title`, `#biz-title`, `#biz-steps-title` |
| «Запись» | `#companies` (рамка каталога, ссылки `/#companies`), `#clients-title` (`tabIndex=-1`), `#clients-steps-title`, `#clients-list-title` |
| «Заказы» | `#catalog` (рамка), `#shop-list` (`tabIndex=-1`, `aria-label="Магазины"`), `data-testid="catalog-list"`, `data-testid="catalog-shop"`, `#buyers-title` (`tabIndex=-1`), `#buyers-steps-title`, `#buyers-track-title` |
| шаблон | `data-testid="landing-screenshot-placeholder"` (не более одного на странице) |

## §38.26. Маршруты SPA goods

`contracts/cycle23/goods-routes.json` → `spaRoutes` получает `"/pricing"` (слово `pricing` уже в `reservedSlugs`, магазин с таким
адресом создать нельзя — `SlugPolicy`). `GoodsApp.tsx`: `<Route path="/pricing">` до `/:slug`, `'/pricing'` в
`CONSENT_GATE_BYPASS_PATHS`. Заголовок вкладки — «Тарифы и цены — EZBOOK Заказы». При 404 сетки страница показывает
«Тарифы пока не опубликованы.», ссылки «Тарифы» в шапке и секции «Тарифы» на главной нет.

## §38.27. Как проверить, что стороны сошлись

| Кто | Чем | Что |
|---|---|---|
| CI | `npx @redocly/cli lint … ../contracts/cycle38/openapi.yaml` | схема корректна |
| CI | `npm run types:api:cycle38` + `git diff --exit-code src/types/api-cycle38.generated.ts` | генерат фронта = схема |
| CI | `npm run contracts:json` + `git diff --exit-code ../contracts/cycle38/openapi.json` | JSON для C#-валидатора = схема |
| backend-тесты | `OpenApiContract.Load("cycle38").AssertResponse("get", "/api/pricing/orders", 200, body)` | реальный ответ = схема (строго: лишнее поле — ошибка) |
| backend-тесты | `Cycle22RouteTable.golden.txt` | ровно один новый маршрут `GET api/pricing/orders`, анонимный, без rate limit |
| фронт | `npx @stoplight/prism mock contracts/cycle38/openapi.yaml --port 4038` | разработка goods `/pricing` без бэкенда |
| QA | `schemathesis run contracts/cycle38/openapi.yaml --base-url http://localhost:5000 --checks all` после `ops tariffs apply` | ответы сервера = схема, без ручных тестов |
