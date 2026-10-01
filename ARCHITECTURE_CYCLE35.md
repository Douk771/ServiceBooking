# ARCHITECTURE — цикл 35 ServiceBooking: демо-стенд «EZBOOK Заказы» (`demo.zakaz.ezbook.ru`) на механике цикла 28

**Разделы §35.0–§35.19.** Контракт словами — `API_CONTRACT_CYCLE35.md` §35.20–§35.29. Машиночитаемая схема —
`contracts/cycle35/openapi.yaml`. Нумерация с префиксом цикла, как с цикла 29 (ответ на A35-9). В коде и документах
ссылаться с именем файла: `ARCHITECTURE_CYCLE35.md §35.9`.

**На входе:**
- корневой `SPEC.md` = `SPEC_CYCLE35_GOODS_DEMO_STAND.md`, бриф `BRIEF_CYCLE35_GOODS_DEMO.md`. Решения Р35-1…Р35-3 и
  D35-1…D35-4 приняты по колонке «Рекомендую»;
- `CURRENT_STATE.md` на `a1e2259`: §5.5 (цикл 28), §5.11 (что из демо-механики касается goods), §9.4 (C35-0-1…8);
- образец — `ARCHITECTURE_CYCLE28.md` §579–§583, `API_CONTRACT_CYCLE28.md` §597–§600a;
- код ветки `cycle/035-goods-showcase-demo-stand` (= `develop` `a1e2259`). Файлы, на которых стоят решения, названы в
  каждом разделе.

Ветку подготовил devops-инженер, архитектор её не трогает и ничего не коммитит. Корневые `ARCHITECTURE.md` и
`API_CONTRACT.md` — документы цикла 3 и по конвенции (`CURRENT_STATE.md` §10.5) не перезаписываются.

| Файл | Что в нём | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE35.md` (этот) | решения, механизмы, генератор, структура, задачи, параллельность, риски | все |
| `API_CONTRACT_CYCLE35.md` (§35.20–§35.29) | контракт словами: параметры, порядок проверок, тексты, ссылки, вывод команд оператора, поведение доски | backend, frontend, QA, devops |
| `contracts/cycle35/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3): prism, `openapi-typescript`, schemathesis, redocly, C#-валидатор `OpenApiContract.cs` | backend, frontend, QA, CI |

---

## §35.0. Что это за цикл для архитектуры

Новой сущности «товары» нет. Цикл **распространяет демо-механику цикла 28 на уже существующий продукт «Заказы»** в
том же демо-экземпляре (Р35-1). Второй механизм рядом не строится.

Меняется семь вещей:

1. **Замок конфигурации** проверяет и адрес «Заказов» (`PublicSites:OrdersBaseUrl`). В compose демо появляются второй
   origin и адрес `demo.zakaz` (§35.5).
2. **Исходящие по заказам** получают второй, «данный» замок в точке постановки в очередь и страховку в двух
   диспетчерах, где её не было (C35-0-1, §35.6).
3. **Демо-роли** становятся продуктовыми: три роли «Заказов», `GET /api/demo/status?product=` и адреса обоих демо в
   ответе (§35.7).
4. **Три новых запрета** `[DemoForbidden]` (§35.8).
5. **Генератор** получает профильный флаг `Shops` и чистый построитель магазинов. Профиль `prod` не меняется ни на
   байт (§35.9).
6. **«Живая» доска** — демо-задача `demo-board-tick` двигает статусы заказов генератора по той же временной линии,
   по которой генератор их создал (§35.10).
7. **Фронт goods** подключает готовые демо-компоненты ezbook (плашка, экран обслуживания, кнопки ролей, отказ 403).
   Плюс заготовка развёртывания `demo.zakaz` (§35.13, §35.14).

**Следствия:**
- **Стек не пересматривается.** Нет новых NuGet- и npm-пакетов, нет миграций, нет новых обязательных переменных боя
  (§35.2).
- **Боевой стек не меняется.** Все правки либо стоят за `DemoMode:Enabled`, либо срабатывают только на
  `Companies.IsShowcase`, а витринных магазинов на бою нет и не будет (D35-1). Для непомеченных данных они инертны.
- **Бэкенд и фронт связаны в двух точках**, и обе закрыты контрактом до начала работ: форма `/api/demo/*` и
  отказ 403 `X-Demo-Restricted` на трёх новых маршрутах. Фронт работает против prism-мока `contracts/cycle35`.

---

## §35.1. Итог решений одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| Р35-1 | Один инстанс на два продукта | **Блокера нет.** Один `api-demo`, одна БД, второй vhost. Проверено по коду: пул, CORS, `PublicSites`, JWT, капча, realtime-полоса, `DemoInstanceGuard` | §35.4 |
| **A35-1** | Пометка данных goods | **Миграции нет.** Хватает `Companies.IsShowcase` у магазина: на нём держатся гвард исходящих, `DemoInstanceGuard` и `ShowcaseOwnership`. Аналог `Booking.ShowcaseKind` у `Order` не нужен: витрина только на демо (D35-1), всё стирается ночью. Для одной задачи (§35.10) заказ генератора отличается от заказа посетителя правилом `Order.CreatedAtUtc ≤ demo.last-reset-utc` | §35.3 |
| **A35-2** | Генератор «Заказов» | Флаг профиля `Shops` (у `Prod` выключен) и отдельный чистый построитель `ShowcaseShopsDataset`, вызываемый **в конце** `ShowcaseDataset.Build`. Живые правила не вызываются вовсе, заказы пишутся сущностями, как записи в цикле 28. Счётчики (`OrderDailyCounter`, `OrderMonthlyUsage`, остатки, `OrdersRevision`) и журнал считаются из того же плана заказа. Байты `prod` закрепляются замороженным отпечатком **до** правки | §35.9 |
| **A35-3** | Блокеры одного инстанса | Не найдены. Лимиты памяти остаются 400 + 256 МБ, но с порогом по замеру сброса: пик `api-demo` > 360 МБ — поднять до 512 МБ | §35.4, §35.14 |
| **A35-4** | Как вход различает продукт | **По параметру.** `GET /api/demo/status?product=services\|orders` (по умолчанию `services`, как в цикле 28) отдаёт три роли своего продукта. Имена ролей уникальны: `shop-owner`, `shop-staff`, `shop-customer`, так что `POST /api/demo/login` параметра не требует. Фронт узнаёт продукт из контекста `DemoProductProvider` (goods оборачивает приложение в `orders`). Общая `LoginPage` не меняется | §35.7 |
| **A35-5** | «Живая» доска | Статус сегодняшнего заказа генератора — **чистая функция `(план заказа, сейчас)`** (`ShowcaseOrderTimeline`). Генератор вызывает её в момент сброса, демо-задача `demo-board-tick` (раз в 2 мин, только в демо-режиме) — в течение дня и дописывает недостающие переходы в журнал. Заказы посетителей задача не трогает, пока время получения не прошло больше чем на 50 минут. После этого (P1) закрывает их с понятной причиной | §35.10 |
| **A35-6** | Ссылки и домены | Все абсолютные адреса API строит **один** `PublicSiteLinks` (проверено по коду), фронт goods берёт их из ответов API. Поэтому хватает `PublicSites__OrdersBaseUrl=https://demo.zakaz.ezbook.ru` и замка на нём. Правовые документы на `demo.zakaz` — локальные SPA-маршруты goods с данными из демо-API (тот же смонтированный `legal/`). **P1 «домен текстом» переносится** по правилу SPEC: мест 11, часть в общих компонентах (долг C35-1) | §35.5 |
| **A35-7** | Запреты | Список SPEC сверен и не урезан. Решено **по токену демо-роли**, как в цикле 28 (`sb_demo` или Id демо-учётки). Отдельного маршрута «сменить роль сотрудника магазина» в коде нет: в goods сотрудника можно только добавить или удалить | §35.8 |
| **A35-8** | Картинки | Роли манифеста: `logo` и `photo` (как у салонов) и новая `product` (с миниатюрой). Ключи `logo.shop.<cat>`, `photo.shop.<cat>.<n>`, `product.<cat>.<name>`. Публикация через `ShowcaseAssetStore` с дедупликацией по хешу, файлы в `uploads/showcase/`, их сброс не стирает. Бюджет прироста — ≤ 3 МБ | §35.11 |
| **A35-9** | Нумерация | §35.0–§35.19 здесь, §35.20–§35.29 в контракте | — |
| D35-2 | Тариф демо-магазинов | Служебный скрытый тариф линии «Заказы» `«Демо»` с литеральным Id. Создаётся идемпотентно генератором (только если в графе есть магазины), переживает сброс. Назначить его настоящему аккаунту запрещено (`ShowcaseMixingGuard`) | §35.3 |
| US-35-06 | Сброс | Процедура `DemoResetService` **не меняется**: таблицы goods уже в `TRUNCATE`, файлы уже чистятся. Меняется только то, что генератор создаёт (`ShowcaseProfile.Demo.Shops = true`) | §35.12 |

---

## §35.2. Стек, зависимости, миграции, переменные

- **Стек без изменений:** .NET 8 / EF Core 8.0.11 / PostgreSQL 16, React 18 + Vite 5 + TanStack Query + zustand
  (`CURRENT_STATE.md` §1). Причина — задача целиком решается переиспользованием механики цикла 28. Любая новая
  зависимость здесь чистый риск без пользы.
- **NuGet, npm:** ничего не добавляется. Картинки рисует существующий `tools/showcase-assets/generate.py` (только
  стандартная библиотека и то, что он уже использует).
- **Миграции: нет** (A35-1, §35.3). Перечисления не дописываются. `NotificationReason.ShowcaseSuppressed` уже есть.
- **Правовые ключи:** новых нет. Плашка goods использует тот же `DemoBanner` (вне `LegalTextKey.All`, запасной текст на
  фронте). Если L35-1 потребует отдельное предупреждение в форме заказа, это будет ключ `DemoCheckoutNotice` вне
  `All`, но не в этом цикле.
- **Переменные демо** (только `docker-compose.demo.yml` и `.env.demo.example`): `PublicSites__OrdersBaseUrl`
  (по умолчанию `https://demo.zakaz.ezbook.ru`) и второй origin в `AllowedOrigins`. На бою переменных нет.
- **Конфигурация приложения:** в `appsettings.json` новая секция задачи `ScheduledTasks:demo-board-tick`
  (`Enabled: true`, `MaxRunMinutes: 1`). В `appsettings.Testing.json` она выключена. Задача регистрируется только в
  демо-режиме, как `demo-reset`.

---

## §35.3. Модель данных

### §35.3.1 Пометка (A35-1): миграции нет

| Что нужно | На чём держится | Почему хватает |
|---|---|---|
| «Это витринный магазин» | `Companies.IsShowcase` (цикл 28) | магазин — это `Company` с `Kind = Orders`. Генератор ставит флаг, как у салонов |
| Гвард исходящих | `ShowcaseOutboundGuard.IsSuppressed(company, demoMode)` | в демо-режиме глушит **все** компании. На пометке — витринные |
| `DemoInstanceGuard` | «нет непомеченной компании и записи `ShowcaseKind = None`» | заказ не существует без компании. Непомеченный магазин уже считается «настоящими данными». Заказы в проверку добавлять не нужно (C35-0-5 закрывается разбором, без кода) |
| Удаление витрины | `ShowcaseOwnership` по цепочке `CompanyId → Companies.IsShowcase` | таблицы goods переезжают из `NeverWritten` в `DeleteSteps` (§35.9.7) |
| Запреты демо | токен демо-роли (`DemoForbiddenFilter`) | пометка данных не нужна (§35.8) |
| Заказ генератора и заказ посетителя | `Order.CreatedAtUtc ≤ PlatformSettings[demo.last-reset-utc]` | генератор пишет заказы с `CreatedAtUtc < now` сброса. Посетитель создаёт заказ только после коммита: во время сброса API отвечает 503. Правило нужно одной задаче (§35.10) и только в демо |

Аналог `ShowcaseVisitorBookingRule` (ретенция заказов посетителей) **не нужен**: витрина магазинов есть только на демо,
а демо стирается каждую ночь (D35-1). На бою профиль `prod` магазинов не создаёт.

### §35.3.2 Новые константы (без таблиц)

| Константа | Где | Значение |
|---|---|---|
| `ShowcaseCatalog.OrdersShowcasePlanId` | `Services/Showcase/ShowcaseCatalog.cs` | `5a1e0c35-0000-4000-8000-000000000901` (литерал, никогда не меняется) |
| `ShowcaseCatalog.OrdersShowcasePlanName` | там же | `Демо` (D35-2: имя задаёт заказчик, по умолчанию «Демо»; менять — правкой строки в админке, код не трогать) |
| `ShowcaseCatalog.IsServicePlan(Guid)` | там же | `id == ShowcasePlanId \|\| id == OrdersShowcasePlanId` |
| `ShowcaseDemoRoles.ShopOwner/ShopStaff/ShopCustomer` | `Services/Showcase/ShowcaseDemoRoles.cs` | `shop-owner`, `shop-staff`, `shop-customer` (§35.7) |

### §35.3.3 Служебный тариф «Заказов» (D35-2)

Строка `SubscriptionPlanConfig`:

| Поле | Значение |
|---|---|
| `Line` / `IsActive` / `IsPublic` / `IsSystemFree` / `IsSystemTrial` | `Orders` / `true` / `false` / `false` / `false` |
| `PricePerMonth` | `0` |
| `MaxCompanies` / `MaxEmployees` / `MaxProductsPerShop` | `3` / `10` / `200` (с запасом под каталоги §35.9 и правки посетителя) |
| `MaxOrdersPerMonth` | `null` — без лимита. Поэтому ни предупреждений 80/100 %, ни 402 у демо-магазина не бывает (US-35-02). Счётчик `OrderMonthlyUsage` при этом ведётся и согласован |
| `AllowOrders` / `AllowPublicListing` / `AllowNotificationChannel` | `true` / `true` / `false` |
| Правила опций | «недоступна» для каждой опции каталога, как `EnsureShowcasePlanAsync` |

Создаёт её `TariffCatalogSeeder.EnsureOrdersShowcasePlanAsync` (новый метод рядом с `EnsureShowcasePlanAsync`, тот же
шаблон: по Id, только создаёт, не перезаписывает). Вызывает `ShowcaseGenerator.PersistAsync` **только если в графе есть
магазины**, поэтому `ops showcase create` на бою её не создаёт. `SubscriptionPlanConfigs` и `PlanOptionRules` в
списке разрешённых таблиц сброса: строка переживает сброс и не задваивается (US-35-06).

Каждый из 5 владельцев магазинов получает `OrdersSubscription` на этот тариф (`PaidUntil = null`, `IsActive = true`).
`AccountSubscription` (линия «Записи») у владельцев магазинов не создаётся: нет строки — значит бесплатный уровень линии,
как у любого нового аккаунта.

`ShowcaseMixingGuard.CheckServicePlan` переходит на `ShowcaseCatalog.IsServicePlan` и вызывается ещё и в
`AdminBillingController.AssignOrdersSubscriptionAsync` (сразу после проверки линии, ~стр. 494). Текст отказа тот же
(`ServicePlanText`).

### §35.3.4 Что пишет генератор в таблицы goods

`ShopSettings`, `ProductCategories`, `Products`, `ShopDailyMenus` + `ShopDailyMenuItems`, `ShopSpecialDays`, `Orders`,
`OrderItems`, `OrderEvents`, `OrderDailyCounters`, `OrderMonthlyUsages`, `OrdersSubscriptions`, `ShopCustomerNotes`,
`CompanyMembers` (роли владельца и сотрудников), `CompanyPhotos`.

**Не пишет** (US-35-02): `ConsentRecords`, `SubjectRequests`, `OrderPushSubscriptions`, `CustomerOrderPushNotifications`,
`PushSubscriptions`, `StaffPushNotifications`, `StaffMaxLinks`, `StaffMaxMessages`, `OutboundNotifications`,
`NotificationChannels`, `ClientHealthNotes`.

---

## §35.4. Один инстанс на два продукта (Р35-1, A35-3): блокера нет

| Проверка | Факт по коду | Вывод |
|---|---|---|
| Какой сайт у ссылки | `PublicSiteLinks.SiteBaseUrl(kind)` решает по виду компании, **не по Host** запроса (`ARCHITECTURE_CYCLE23.md` §400). Один API и так обслуживает два домена на бою | два vhost на один `api-demo` — штатная схема |
| `PublicSites` | обе настройки независимы; `ValidatePublicSites` проверяет формат обеих | нужен только `PublicSites__OrdersBaseUrl` в compose |
| CORS | `AllowedOrigins` — список через запятую. Замок требует `demo.*` у **каждого** | `https://demo.visit.ezbook.ru,https://demo.zakaz.ezbook.ru` проходит |
| JWT | один издатель `ServiceBooking.Demo`. Токен хранится в `localStorage` своего origin | входы на двух хостах раздельные, как `ezbook.ru` и `goods.ezbook.ru` на бою (единый вход — вне цикла) |
| Капча | site-key зашит в одну сборку, серверный ключ один | в консоли SmartCaptcha добавить хост `demo.zakaz.ezbook.ru` (ручной шаг). Без него гостевой заказ отвечает ошибкой капчи, заказ под ролью покупателя работает (как C28-14) |
| Пул БД | `Maximum Pool Size=10` у API, `max_connections=20` у Postgres | goods добавляет опрос доски (дешёвый путь по `sinceRevision`) и одну демо-задачу раз в 2 мин. Realtime-задачи goods (3 шт., каждые 5 с) крутятся на демо уже сейчас. Запаса хватает: 10 соединений API + `ops` + psql ≤ 20 |
| Фоновые задачи realtime-полосы | `staff-push-dispatch`, `customer-order-push-dispatch`, `staff-max-dispatch` уже зарегистрированы в демо | после §35.6 они только закрывают строки как подавленные |
| `DemoInstanceGuard` | магазин = компания | §35.3.1 |
| Сброс | `DemoResetTables` строится из модели EF: все 66 таблиц, включая goods, уже в `TRUNCATE` | процедура не меняется |
| Обслуживание | `DemoMaintenanceMiddleware` не смотрит на Host | 503 `X-Demo-Resetting` получают оба хоста |
| Память | §35.14.2 | лимиты остаются, есть порог по замеру |

**Итог: один инстанс, как решил заказчик.** Если при выкате замер памяти покажет, что стенд не помещается даже в
512 + 256 МБ при `available ≥ 1000 МБ` (§35.14.2), это **не** повод для второго стенда: это стоп и доклад заказчику
(вынос демо целиком на отдельную VM, compose переносится без правок).

---

## §35.5. Адреса, замок конфигурации, ссылки (US-35-01, US-35-07, A35-6)

### §35.5.1 Замок `ValidateDemoMode` (C35-0-2)

В `DeploymentSafetyChecks.ValidateDemoMode` добавляется пункт 1a, собирается в тот же список проблем:

- `PublicSites:OrdersBaseUrl` **пуст** → проблема «must be set explicitly on the demo — the default is the production
  goods». Пустое значение означает боевой адрес по умолчанию (`PublicSitesOptions`), поэтому пустота — тоже нарушение;
- хост не начинается с `demo.` → проблема по образцу пункта 1.

Пункт 2 (`AllowedOrigins` — только `demo.*`) уже есть и не меняется: он и есть «`AllowedOrigins` не содержит боевых
адресов» из US-35-07. Тест `DemoModeValidationTests`: +3 случая (пусто, боевой, `demo.zakaz` — проходит). Функциональная
фабрика `DemoHostFactory` (`Cycle28DemoContractTests.cs`) получает `PublicSites:OrdersBaseUrl =
https://demo.zakaz.ezbook.ru`, иначе все демо-тесты цикла 28 упадут на старте. **Это правка общего тестового файла, её
делает BE вместе с замком** (T-35-11).

### §35.5.2 Где строятся абсолютные адреса (A35-6)

Сверено по коду: абсолютные адреса сайтов строит только `PublicSiteLinks`. Его вызывают `ShopManageMapper`
(`publicUrl`, QR), `StorefrontController`, `OrderDtoMapper`/`PublicOrderService`/`OrderCreationService` (`orderUrl`),
`CompaniesController` (`kinds-summary` → `siteUrl`, переадресация магазина, открытого на ezbook), `CompanyDtoAssembler`,
`PushController` (`siteUrls` для service worker'а), `StaffPushLinks`, `OrderNotificationPlanner` и
`OrderMessageScheduler` (ссылки в сообщениях), `OrderLimitWarner`, `AdminController`.

Фронт goods абсолютных адресов не собирает: `LinkPage` берёт `shop.publicUrl`, «Мои заказы» — `orderUrl`,
`GoodsProfilePage`/`CabinetHomePage`/уведомления платформы — `kinds-summary.services.siteUrl`, «Поделиться» каталога —
`window.location.origin`. Ezbook ведёт на goods через `siteUrl` из API.

**Вывод:** `PublicSites__OrdersBaseUrl=https://demo.zakaz.ezbook.ru` плюс уже заданный
`PublicSites__ServicesBaseUrl=https://demo.visit.ezbook.ru` переводят на демо **все** ссылки: магазин,
«Скопировать», QR, push и сообщения (даже неотправленные), переходы между продуктами, `siteUrls` воркера. Кода для этого
писать не нужно. Проверка — функциональный тест CY35 (все URL-поля DTO начинаются с демо-адреса) и смоук §35.14.3.

**Правовые документы на `demo.zakaz`** — локальные маршруты goods (`/privacy`, `/terms`, …). Данные берутся из
`GET /api/legal/documents` демо-API, который монтирует тот же опубликованный `legal/` только для чтения
(`docker-compose.demo.yml`). Ссылок на бой нет.

Кеш каталога goods (`GoodsCatalogService`, 30 с) после сброса командой `ops` (другой процесс) может до 30 с показывать
вчерашний список. Это того же рода, что C28-13; принимаем.

### §35.5.3 P1 «домен текстом» (US-35-01, последний пункт) — переносится (R35-1)

Правило SPEC: «если мест больше пяти или текст зашит в общие компоненты боя, пункт переносится». Найдено 11 мест
(`grep` по `frontend/goods/src`, `frontend/src`, `ServiceBooking.API`):

| Где | Текст | Общий с боем компонент? |
|---|---|---|
| `goods/src/pages/cabinet/CabinetHomePage.tsx:65` | `goods.ezbook.ru/{slug}` | нет |
| `goods/src/pages/cabinet/CabinetHomePage.tsx:99` | подпись ссылки «ezbook.ru» | нет |
| `goods/src/pages/cabinet/LinkPage.tsx:159` | «Новая ссылка: goods.ezbook.ru/…» | нет |
| `goods/src/pages/cabinet/CreateShopPage.tsx:162` | «Ссылка будет такой: goods.ezbook.ru/…» | нет |
| `goods/src/components/CatalogListingSection.tsx:23,24` | «Каталог goods.ezbook.ru» (2 строки) | нет |
| `goods/src/pages/GoodsProfilePage.tsx:135,138` | подпись «ezbook.ru» | нет |
| `src/components/company/GoodsShopsNotice.tsx:16` | «goods.ezbook.ru» | **да** |
| `src/utils/staffPushTexts.ts:34,37,41` | «ezbook.ru или goods.ezbook.ru» | **да** |
| `ServiceBooking.API/Services/Shops/CatalogListingRules.cs:34` | «Магазин виден в каталоге goods.ezbook.ru» | **да** (API) |

Все **ссылки** за этими надписями ведут на демо (§35.5.2). Неверен только текст. Пункт переносится в долг **C35-1**
(«на демо часть надписей показывает боевой домен текстом») вместе со списком выше. Сделать его в следующем цикле дёшево
одним приёмом: подпись = `new URL(siteUrl).host` там, где `siteUrl` уже есть, а в API — текст без домена.

---

## §35.6. Ни одного исходящего по заказам (US-35-05, C35-0-1)

### §35.6.1 Пять путей и их замки после цикла

| Путь | Очередь | Замок 1 — конфигурация демо | Замок 2 — постановка в очередь (новое) | Страховка при отправке |
|---|---|---|---|---|
| сообщение покупателю (MAX/WhatsApp) | `OutboundNotifications` | `Notifications:Provider=logging` | **`OrderNotificationPlanner`** | есть (`NotificationDispatchTask`) |
| push покупателю о статусе | `CustomerOrderPushNotifications` | `StaffPush:Provider=logging` → `LoggingWebPushSender` | **`OrderNotificationPlanner`** | **новая** в `CustomerOrderPushDispatchTask` |
| push персоналу о новом заказе и отмене | `StaffPushNotifications` | `logging` | **`OrderNotificationPlanner`** | есть (`StaffPushDispatchTask`) |
| MAX персоналу о заказе | `StaffMaxMessages` | `StaffMax:Enabled=false` | **`OrderNotificationPlanner`** | **новая** в `StaffMaxDispatchTask` |
| предупреждение владельцу о лимите (push + MAX) | `StaffPushNotifications`, `StaffMaxMessages` | как выше | **`OrderLimitWarner`** | обе страховки выше |

### §35.6.2 Правки (все инертны для непомеченной компании вне демо-режима)

1. **`OrderNotificationPlanner.OnEventAsync`** — единственная точка, где событие заказа превращается в уведомления
   (`ARCHITECTURE_CYCLE24.md` §458). Сразу после загрузки `shop` (стр. 36):
   `if (ShowcaseOutboundGuard.IsSuppressed(shop, demoOptions.Value.Enabled)) return;`. Зависимость — `IOptions<DemoModeOptions>`,
   тот же приём, что в `NotificationScheduler`/`StaffPushScheduler`. Одна строка закрывает четыре пути.
2. **`OrderLimitWarner.AfterIncrementAsync`**: та же проверка по `shop` **после** `MarkWarnedAsync`, чтобы флаг
   «предупреждение отправлено» ставился как раньше, а постановки не было. На демо лимита у служебного тарифа нет, но
   посетитель может создать свой магазин на бесплатном тарифе.
3. **`CustomerOrderPushDispatchTask`**: страховка по образцу `StaffPushDispatchTask` стр. 80–93. Инжектится
   `ShowcaseOutboundGuard`. После выборки кандидатов `SuppressedCompanyIdsAsync(companyIds)`, их строки →
   `Skipped` + `NotificationReason.ShowcaseSuppressed`. Отправитель для них не вызывается.
4. **`StaffMaxDispatchTask`**: то же, до проверки `availability.Enabled` (чтобы причина в журнале была
   `ShowcaseSuppressed`, а не `StaffMaxPlatformDisabled`).

**Проверка (T-35-01, CY35):** хост с **включёнными** в тесте фейковыми отправителями (`IWebPushSender`, `IMaxBotMessenger`,
транспорт уведомлений). Витринный магазин вне демо-режима и любой магазин в демо-режиме: создать заказ (гость с
`notifyByMessenger`, с push-подпиской покупателя), принять, изменить, отменить покупателем, перейти 80 % лимита на
бесплатном тарифе. После прогона всех пяти диспетчеров число вызовов отправителей = 0. Строк в очередях либо нет, либо они
`Skipped/ShowcaseSuppressed`. Контрольный непомеченный магазин вне демо-режима даёт вызовы, как раньше.

### §35.6.3 Сопоставление по телефону (NFR «ПДн»)

Для единообразия с §574.5 цикла 28 два запроса заказов «по номеру» исключают витрину: `SubjectDataExporter` (стр. ~120)
и `AccountDeletionService` (стр. ~170) — к условию `guestMatchPhone` добавить `&& !o.Company.IsShowcase` (или
подзапрос по `Companies`). Маркеры `SUBJECT-PHONE-GATE` сохранить. На бою витринных магазинов нет, на демо подтверждение
телефона выключено (`stub`): правка страховочная и инертная. `ShopCustomerService` и `OrderPhoneThrottle` работают внутри
одного магазина и остаются как есть.

---

## §35.7. Демо-роли и вход (US-35-03, A35-4)

### §35.7.1 Роли

| Роль (провод) | Продукт | Кто | Ключ учётки (UUIDv5, профиль `demo`) | Куда после входа (фронт) |
|---|---|---|---|---|
| `owner`, `master`, `client` | `services` | как в цикле 28 | как в цикле 28 | `/cabinet`, `/my-bookings`, `/my-visits` |
| `shop-owner` | `orders` | владелец кофейни (только магазин, не владелец «Лаванды») | `shop:kofeinya-owner` | `/cabinet` |
| `shop-staff` | `orders` | сотрудник кофейни (`CompanyMember.Role = Master`, как у сотрудника из `StaffPage`) | `shop:kofeinya:s0` | `/cabinet` |
| `shop-customer` | `orders` | покупатель с историей в двух магазинах Москвы | `demo-shop-customer` | `/orders` |

`ShowcaseDemoRoles`:
- `All` — все шесть, в порядке таблицы. Отсюда `IsDemoUserId` автоматически закрывает и новые учётки, а
  `DemoForbiddenFilter` не меняется;
- `ForProduct(DemoProduct)` — три роли продукта;
- `ProductOf(role)`, `IsKnown(role)` — на шесть имён;
- подписи: «Войти как владелец магазина», «Войти как сотрудник магазина», «Войти как покупатель».

### §35.7.2 Сервер

- **`GET /api/demo/status`** получает query `product` (`services` | `orders`, без учёта регистра; отсутствует или пуст
  → `services`, иначе 400 text/plain). В `roles` — три роли продукта. В конец DTO добавляется `siteUrls {services,
  orders}` из `PublicSiteLinks.SiteBaseUrl` (для перехода между демо, US-35-10). Порядок проверок: `[DemoOnly]` (404) →
  разбор `product` (400) → 200. `DemoMaintenanceMiddleware` пропускает этот путь независимо от query.
- **`POST /api/demo/login`** принимает шесть имён. `DemoLoginService`: версия `TermsOwner` в токен ставится для `owner`
  **и `shop-owner`** (иначе `[RequiresOwnerTerms]` в кабинете goods ответит 451 владельцу магазина). Остальное без
  изменений: учётка по стабильному Id, `IsShowcase`, `sb_demo = 1`, 409 «Демо-данные ещё не созданы…», если учётки нет.
  Так бывает, например, для ролей магазина до первого сброса после выката цикла 35.
- Токен роли переживает сброс: Id и `SecurityStamp` детерминированы (`ShowcaseIds`), как в цикле 28.

### §35.7.3 Фронт

- `frontend/src/components/demo/DemoProductContext.tsx` (новый): `DemoProductProvider({product})`, `useDemoProduct()`,
  по умолчанию `'services'`. Без провайдера ezbook ведёт себя как до цикла.
- `useDemoStatus`: `queryKey: ['demo-status', product]`, `demoApi.getStatus(product)`. Для `services` запрос прежний
  (`/demo/status` без параметра), для `orders` — `?product=orders`.
- `DemoRoleButtons`: `ROLE_ICONS` на шесть ролей (иконки из существующего набора `Icon`: `store`, `clipboard`/`user` —
  выбрать из наличных, новые не рисовать). `demoRoleHome` — таблица §35.7.1.
- `LoginPage` **не меняется**: в goods она уже рисует `DemoRoleButtons`, и они возьмут продукт из контекста. Салонные
  кнопки на `demo.zakaz` не появятся, кнопки «Заказов» на `demo.visit` — тоже.
- Типы `DemoRole`, `DemoStatusDto` импортируются из `api-cycle35.generated.ts` вместо `api-cycle28.generated.ts`.

---

## §35.8. Запреты в демо «Заказов» (US-35-04, A35-7)

**Кого касается.** Тот же механизм, что в цикле 28: `[DemoForbidden]` + глобальный `DemoForbiddenFilter`, решение **по
токену демо-роли** (claim `sb_demo` или Id одной из шести демо-учёток). Почему не «для всех в демо-режиме»: витринным
магазином управляют только демо-роли (посетитель не может стать его сотрудником, это отсекает `ShowcaseMixingGuard`).
Значит, «по токену» и «только витринный магазин и демо-роли» здесь совпадают. Зарегистрированный посетитель свой магазин
может переименовывать как угодно: его данные ночью стираются.

| Маршрут | Почему | Сверка с кодом |
|---|---|---|
| `PUT /api/shops/{shopId}/slug` | ломает ссылку и QR для всех посетителей | `ShopsController`. Слаг меняется только здесь: `PUT /api/companies/{id}` слаг не принимает |
| `DELETE /api/Companies/{id}/members/{memberId}` | удаление сотрудника-роли — «Войти как сотрудник» ведёт в пустой кабинет | `CompanyMembersController.RemoveMember`, общий для салона и магазина (§35.19.3 п. 3) |
| `POST /api/staff-max/link-sessions` | бот на демо не подключается | `StaffMaxController` |
| «смена роли сотрудника магазина» | — | **маршрута нет**: `StaffPage` умеет только добавить (`POST …/members`, роль `Master`) и удалить. Добавить в витринный магазин можно только витринного пользователя (`ShowcaseMixingGuard.CheckMember`), а зарегистрироваться посетитель может только непомеченным. Добавление поэтому уже невозможно |

Уже действующие запреты цикла 28 (смена пароля и телефона, удаление аккаунта, заявка на тариф любой линии, пробный,
перенос компании, смена владельца) работают на `demo.zakaz` без правок: фильтр глобальный.

Дополнительно по тому же принципу рассмотрены и **не** запрещены (чинит ночной сброс, D35-4): `PUT …/catalog-listing`,
`PUT …/settings` (режим покупателя), пауза и стоп приёма, `PUT …/seller`, правка каталога и цен, фото, каналы
уведомлений (провайдер `logging` — наружу ничего не уходит).

Вне демо-режима фильтр пропускает всё. Эталон `Cycle22RouteTable.golden.txt` меняется **только атрибутами** трёх строк
и параметром `product` у `GET /api/demo/status`.

Фронт goods: `getGoodsErrorMessage` (`goods/src/utils/orderError.ts`) первым шагом зовёт общий
`getDemoRestrictedMessage` (`src/utils/demoHeaders.ts`). 403 с `X-Demo-Restricted` → текст тела, иначе всё как было.

---

## §35.9. Генератор «Заказов» (US-35-02, US-35-07, A35-2)

### §35.9.1 Устройство

```
ShowcaseDataset.Build(profile, now)            — не меняется, кроме последней строки:
    …салоны, клиенты, записи, отзывы, заметки… (ни одного изменения, порядок телефонов тот же)
    if (profile.Shops) ShowcaseShopsDataset.Build(graph, profile, phones, now);   ← новое, ПОСЛЕДНИМ

ShowcaseShopsDataset (новый, static, чистый)   — магазины, люди магазинов, каталоги, меню, особые дни, заказы
ShowcaseShopSpecs    (новый)                   — 5 магазинов §35.9.2: категории, товары, цены, часы, режимы
ShowcaseOrderTimeline (новый, чистый)          — статус и переходы заказа как функция (план, сейчас); §35.10
```

- `ShowcaseProfile`: новый `init`-флаг `Shops` (по умолчанию `false`). `Demo` → `Shops = true`. `Prod` не меняется.
- Телефоны людей магазинов берутся из **того же** `ShowcasePhones` **после** всех салонных: ни один телефон салона и
  демо-клиента не сдвигается. Блок `72005550000–72005559999` (10 000 номеров) выдерживает: салоны ~460, магазины ~550.
- Ключи Id: `ShowcaseIds.For("demo", <вид>, <ключ>)`. Виды `shop`, `shop-settings`, `category`, `product`, `menu`,
  `menu-item`, `special-day`, `order`, `order-item`, `order-event`, `order-idem`, `order-token`, `customer-note`,
  `orders-subscription`. Ключи строятся из ключа магазина, даты и порядкового номера, **никогда** из времени.
- Случайность: `ShowcaseRandom($"demo:shop:{key}")`, `…:day:{date}`, `…:order:{orderKey}`. От даты зависят только даты и
  статусы, от профиля — имена, товары, цены, слаги, Id и телефоны (US-35-02, последний пункт).
- `ShowcaseGraph` получает коллекции `ShopSettings`, `ProductCategories`, `Products`, `DailyMenus`, `DailyMenuItems`,
  `SpecialDays`, `Orders`, `OrderItems`, `OrderEvents`, `OrderDailyCounters`, `OrderMonthlyUsages`,
  `OrdersSubscriptions`, `ShopCustomerNotes` и словарь `ProductImageKeys` (`ProductId → asset key`). Магазины кладутся в
  общий `Companies` (у них есть `CityNameByCompany`, `LogoKeyByCompany`, `PhotoKeysByCompany`: `PersistAsync` обходит все
  компании одинаково).
- `ShowcaseGenerator.PersistAsync` сохраняет новые коллекции **после** `graph.Members`/`photos` и **до** `Bookings`
  в порядке FK: `ShopSettings → ProductCategories → Products → DailyMenus → DailyMenuItems → SpecialDays →
  OrdersSubscriptions → Orders → OrderItems → OrderEvents → OrderDailyCounters → OrderMonthlyUsages →
  ShopCustomerNotes`. Картинки товаров публикуются вместе с остальными, до строк (`Product.ImageUrl/ThumbnailUrl` из
  `PublishedShowcaseAsset`). `EnsureOrdersShowcasePlanAsync` — только при `graph.Shops.Any()`.

### §35.9.2 Пять магазинов (SPEC §4)

| Ключ | Магазин | Город / пояс | Режимы | Что показывает |
|---|---|---|---|---|
| `kofeinya` | кофейня (магазин демо-владельца) | Москва / `Europe/Moscow` | 07:00–23:00 ежедневно; ASAP + к времени, предзаказ на 1 день, шаг 15 мин, `Manual` | «как можно скорее», ручное принятие, звук, доска §35.10 |
| `pekarnya` | пекарня | Москва | 07:00–21:00; к времени, предзаказ 2 дня, `Auto`; `TrackStock = true` | остатки, «закончилось на сегодня» (`SoldOutForDate = сегодня`), предзаказ на завтра |
| `stolovaya` | столовая | Санкт-Петербург | Пн–Пт 11:00–17:00, Сб–Вс закрыто; ASAP | меню на дату (`ShopDailyMenu` на сегодня и 5 рабочих дней вперёд), товар «только по пятницам» (`AvailableWeekdaysMask`) |
| `cvety` | цветочный | Казань | 09:00–20:00; только к времени, предзаказ 7 дней, `MinPrepMinutes = 120` | долгая готовка, особый день (`ShopSpecialDay` через 3–10 дней: праздник с часами 08:00–22:00) |
| `fermerskaya` | фермерская лавка | Новосибирск / `Asia/Novosibirsk` | 09:00–20:00; к времени | весовые товары (`Unit = Weight`, шаг и минимум), выдача по фактическому весу |

Общее для всех: `IsShowcase = true`, слаг `primer-<ключ>` (префикс зарезервирован и для магазинов), `ShowInPublicListing =
true`, `IsActive = true`. Телефон магазина — из `ShowcasePhones` (US-35-02 требует его явно; номер вне плана нумерации).
`Email`, ссылки на карты — пусто. Адрес — улица без номера дома. `ShopSettings.CustomerMode = Anyone`. Продавец —
только вымышленное `SellerLegalName` («… (пример)»), без ИНН, ОГРН и юр. адреса (предположение SPEC §4 до L35-2).
Часы — через `ShopScheduleRules.Serialize`, тот же канон, что `PUT …/working-hours`. Каталог 15–40 товаров в 3–7
категориях, «Состав и аллергены» у части товаров, картинки не у всех (§35.11). Каждый магазин обязан проходить все
пункты `CatalogListingRules` (часы, опубликованные товары, тариф с показом, не скрыт): это проверяет CY35.

Люди: у каждого магазина свой владелец и свой `BillingAccount` (`IsShowcase`), 1–3 сотрудника. Покупатели — отдельные
пулы **по городу** (москвич не заказывает в Новосибирске): ~80 зарегистрированных (`Client`, `IsShowcase`, без пароля и
почты) и ~450 гостей. Распределение заказов скошено: у постоянных много заказов, у большинства — 1–3. Так карточка
покупателя правдоподобна (урок C28-5). Заметки магазина (`ShopCustomerNote`, ключ — канонический телефон) — у ~10 %
постоянных, нейтральные («просит без сахара», «забирает после 18:00»). Слова «аллергия» и сведений о здоровье нет:
это проверяет юнит-тест по словарю.

### §35.9.3 Заказы: объём и история

- **Окно:** 60 дней назад … 3 дня вперёд (предзаказы), в поясе каждого магазина. Объём ~5–5,5 тыс.: кофейня 35–55 в
  день, столовая 20–35 в рабочий день, пекарня 10–20, лавка 3–8, цветочный 1–4.
- **Судьбы прошлых заказов:** `Issued` ~88 %, `Rejected` ~3 % (с причиной), `CancelledByCustomer` ~4 %,
  `CancelledByShop` ~2 % (с причиной), `NotPickedUp` ~3 %. У ~5 % заказов правка магазина «было → стало»
  (`OrderEventKind.Edited`, `IsModifiedByShop`). У всех весовых — выдача с фактическим весом ±10 % (`QuantityActual`,
  `LineTotalFinal`, `FinalTotal ≠ EstimatedTotal`).
- **Сегодня и вперёд** — §35.10 (временная линия).
- **Демо-покупатель:** в кофейне и пекарне 5–10 завершённых за 30 дней (один `CancelledByCustomer`); сегодня один
  активный в кофейне со слотом **перед закрытием** (21:30–22:00), чтобы он был активен весь день; один предзаказ на
  завтра в пекарне.

### §35.9.4 Обход живых правил и согласованность (A35-2)

Генератор **не вызывает** `OrderCreationService`, капчу, проверку часов и горизонта, лимит тарифа, остатки: он пишет
сущности, как `ShowcaseGenerator` пишет записи. Ни одно правило для живых запросов не ослабляется, потому что генератор
через HTTP не ходит. Согласованность обеспечивается тем, что всё выводится из одного плана заказа:

| Инвариант | Как выполняется |
|---|---|
| Номер в пределах дня выдачи (уникальный индекс `(CompanyId, PickupDate, Number)`, C24-11) | номера 1…n по `PickupDate` в порядке `CreatedAtUtc`. `OrderDailyCounter(CompanyId, PickupDate).LastNumber = n` для каждого дня с заказами. Следующий заказ посетителя получает `n+1` через `OrderNumberAllocator` |
| `OrderMonthlyUsage` | `(BillingAccountId, Month)` — число заказов по `CreatedAtUtc` в месяце по поясу магазина. `Warned80/100AtUtc = null` |
| Остатки (пекарня) | `StockOnHand` каждого учитываемого товара ≥ резерва активных заказов + запас. У 1–2 товаров `0` и `IsSoldOut` с `SoldOutForDate = сегодня`. `OrderItem.ReservesStock = true` у позиций учитываемых товаров |
| Журнал ↔ статус | события строит `ShowcaseOrderTimeline` из плана: `Created → [Edited] → Accepted → MarkedReady → Issued` и т. д. `ChangesJson` — только через `OrderChangeLog.SerializeEdit/SerializeIssue`. Последнее событие со статусом = `Order.Status`. Временные метки возрастают и не позже «сейчас» |
| Поля заказа | заполняются так же, как в `OrderCreationService`: снимки правил магазина, `BusinessDate` = местная дата создания, `PickupStartUtc/EndUtc` из слота (ASAP — создание + время готовки), `PublicToken = PublicOrderToken.Encode(32 байта из двух UUIDv5)`, `IdempotencyKey` = UUIDv5, `Version` = число изменений. Согласия (`Consent*Version`), `CheckoutNoticeVersion` и `NotifyByMessenger` — пусто/false: журнал согласий генератор не пишет |
| Доска | `ShopSettings.OrdersRevision` = число событий магазина (любое ≥ 1) |

Тест, что журнал читается продуктом: для выборки заказов каждого магазина `StaffOrderDtoFactory.BuildAsync` и публичный
`GET /api/orders/public/{token}` отрабатывают без исключений (CY35).

### §35.9.5 Байты `prod` и салонная часть `demo` (US-35-07, US-35-01)

**Первым коммитом T-35-14, до правки генератора**, BE добавляет в `ShowcaseDatasetTests` замороженный отпечаток:
`Fingerprint(Build(Prod, Now))` (существующая функция) как строковый литерал, посчитанный на `a1e2259`. То же для
салонной части `Demo`: отпечаток по компаниям `Kind = Services`, их записям, событиям, рабочим дням и пользователям, не
относящимся к магазинам. После правки оба теста обязаны остаться зелёными **без изменения литералов**. Счётчики всего
демо-графа (`ShowcaseDemoProfileTests`, функциональные тесты сброса) меняются осознанно: их правка допустима, правка
отпечатков — нет.

### §35.9.6 Команды оператора

- `ops demo reset` (без `--yes`, «план»): к строке `будет создано: …` добавляется вторая —
  `будет создано (Заказы): shops=5 products=… …` (формат — `API_CONTRACT_CYCLE35.md` §35.26). После выполнения — то же
  в строке «выполнено». Первая строка сохраняет прежний формат, только числа `companies`/`users` в демо растут.
- `ops showcase plan --profile demo` (**новый ключ**, только план). Показывает обе строки для профиля `demo` —
  буквальное требование US-35-07. `create/recreate/delete --profile demo` → код 2 «Профиль demo создаётся только
  сбросом демо: ops demo reset». Без `--profile` всё как было (`prod`). Разбор — `OpsCommandLine`, тесты
  `OpsCommandLineTests`.

### §35.9.7 `ShowcaseOwnership` (C35-0-3)

Утверждение «в витрине нет магазинов» удаляется из `NeverWritten`: для профиля `demo` оно ложно. Таблицы goods
переходят в `DeleteSteps` **перед** `new("companies", …)` в порядке FK:

```
orderEvents            OrderEvents            CompanyId IN showcase
orderItems             OrderItems             OrderId IN (Orders of showcase companies)
orderPushSubscriptions OrderPushSubscriptions OrderId IN (Orders of showcase companies)
customerOrderPush      CustomerOrderPushNotifications  CompanyId IN showcase
staffMaxMessages       StaffMaxMessages       CompanyId IN showcase
orders                 Orders                 CompanyId IN showcase
orderDailyCounters     OrderDailyCounters     CompanyId IN showcase
shopCustomerNotes      ShopCustomerNotes      CompanyId IN showcase
dailyMenuItems         ShopDailyMenuItems     DailyMenuId IN (ShopDailyMenus of showcase companies)
dailyMenus             ShopDailyMenus         CompanyId IN showcase
specialDays            ShopSpecialDays        CompanyId IN showcase
products               Products               CompanyId IN showcase
productCategories      ProductCategories      CompanyId IN showcase
shopSettings           ShopSettings           CompanyId IN showcase
```

Имена таблиц и колонок сверить с моделью EF: `ShowcaseOwnershipCoverageTests` падает, если хоть одна таблица не
учтена. В `NeverWritten` остаётся только `NotificationChannels`. Команда удаления на бою витрину магазинов не встретит
(D35-1), но правило цикла 28 «всё созданное удаляется командой» становится верным для обоих профилей.

---

## §35.10. «Живая» доска (A35-5, US-35-02, US-35-09)

### §35.10.1 Проблема

Статусы фиксируются в момент сброса (04:00 по Москве), а доску смотрят с 07:00 до 23:00. Без движения к полудню
утренние заказы висят «протухшими» (US-35-09), а колонка «Завершённые сегодня» пуста весь день. Доска считает её по
`CompletedAtUtc` в границах сегодняшнего рабочего дня (`ShopOrdersController.GetBoard`), а до 04:00 кофейня не работала.

### §35.10.2 Временная линия — одна чистая функция

`ShowcaseOrderTimeline` (`Services/Showcase/`, без EF и часов):

```
Plan(orderId, createdAtUtc, pickupStartUtc, acceptanceMode) → OrderPlan
    offsets детерминированы из orderId (ShowcaseRandom): accept = −(30..50) мин, ready = −(5..15) мин,
    issue = +(3..20) мин от pickupStart; судьба: Issued (95 %) | NotPickedUp (5 %, в pickupStart + 45 мин)
StateAt(plan, nowUtc) → (Status, [переходы с метками времени ≤ nowUtc])
```

- **Генератор** вызывает `StateAt(plan, now_сброса)` для заказов с `PickupDate` = сегодня и позже. При сбросе в 04:00
  все сегодняшние заказы `New`/`Accepted`. Два заказа со слотом у открытия (07:00–07:15) уже `Ready` («собраны с
  вечера»). Предзаказы на завтра — `New`/`Accepted`. При ручном сбросе днём (`ops demo reset` перед встречей) та же
  функция сразу даёт выданные сегодня, готовые и принятые. Доска правдоподобна в любой момент сброса.
- **Задача `demo-board-tick`** (новая) вызывает ту же функцию в течение дня и догоняет статус.

### §35.10.3 Задача `demo-board-tick`

- `Services/Scheduling/Tasks/DemoBoardTickTask.cs` → `Services/Demo/DemoBoardTicker.TickAsync(nowUtc, ct)`. Полоса
  `main`, `DefaultPeriod = 2 мин`, `ScheduledTasks:demo-board-tick:MaxRunMinutes = 1`. **Регистрируется только в
  демо-режиме** (как `demo-reset`). Внутри повторно проверяет оба замка (`DemoMode:Enabled` и метку `instance.kind =
  demo`) и пропускает проход, пока поднят флаг обслуживания.
- **Заказы генератора** (A, P0): витринные магазины (`IsShowcase`), `CreatedAtUtc ≤ demo.last-reset-utc`, статус активный,
  `PickupDate ≤ сегодня`. Для каждого `StateAt(plan, now)`. Если целевой статус впереди текущего по линии и текущий статус
  — его предшественник по плану, применяются недостающие переходы по одному. Если посетитель уже сам перевёл заказ
  (выдал, отклонил, отменил), задача его не трогает. `Issue` — только если в заказе нет весовых позиций и позиций с
  `ReservesStock` (иначе заказ ждёт посетителя или ветки Б). В кофейне таких нет по построению.
- **Заказы посетителей** (Б, P1, US-35-09): витринные магазины, `CreatedAtUtc > demo.last-reset-utc`, активный статус,
  `(PickupEndUtc ?? PickupStartUtc) + 50 мин < now`. `New → Rejected`, `Accepted → CancelledByShop`, `Ready →
  NotPickedUp` с причиной «Демо: заказ закрыт автоматически — время получения прошло.» Остатки не списываются: у
  этих переходов списания нет. Магазины, созданные посетителями (`IsShowcase = false`), не трогаются.
- **Как пишет.** Одна транзакция на заказ: статус, `AcceptedAtUtc/ReadyAtUtc/CompletedAtUtc` (метки из плана, не
  «сейчас»), `FinalTotal = EstimatedTotal` у выдачи, `Version++`. Журнал — через `OrderEventLog.AppendAsync`, у
  которого появляется **необязательный** параметр `occurredAtUtc` (по умолчанию `DateTime.UtcNow`, продуктовые вызовы
  не меняются). Инвариант «`OrderEventLog` — единственный писатель журнала и ревизии доски» сохраняется, доска видит
  изменения по `OrdersRevision`. Уведомления, которые планирует `OrderNotificationPlanner`, гасит гвард §35.6 (демо).
  Исполнитель: для A — демо-сотрудник кофейни (`ActorKind.Staff`, его Id и имя), для Б — `ActorKind.System`.
- **Гонка с посетителем.** `DbUpdateConcurrencyException` (посетитель нажал в ту же секунду) → заказ пропускается до
  следующего прохода. Посетитель при этом получает обычный 409 `VersionMismatch` с актуальной карточкой — продуктовое
  поведение.
- **Звук на доске** срабатывает на новые заказы. Задача новых заказов не создаёт («симулятор покупателя» вне цикла),
  лишнего звука не будет.

### §35.10.4 Что это даёт относительно приёмки

- US-35-09: с открытия до закрытия нет активного заказа, чьё время получения прошло больше чем на 60 минут. Заказы
  генератора закрываются не позже `pickupStart + 45` (для `NotPickedUp`, при такте 2 мин ≤ 47), посетителей — после
  `+50` (≤ 52).
- US-35-02 «в „Завершённых сегодня“ несколько выданных»: выполняется **в часы работы кофейни** (с ~07:15 после
  ночного сброса, сразу — после дневного ручного). Между 04:00 и 07:00 колонка пуста: кофейня закрыта, и
  правдоподобных выдач быть не может. Отклонение записано в §35.19.3 п. 1.

---

## §35.11. Картинки (A35-8, Р35-3)

- `tools/showcase-assets/generate.py` дописывается тремя наборами в той же нейтральной манере (плоские иллюстрации, без
  людей, без чужих знаков): логотипы 5 магазинов (`logo.shop.<cat>`, 600×600), фото 3–6 на магазин
  (`photo.shop.<cat>.<n>`, 1200×800), товары (`product.<cat>.<name>`, 800×800 + миниатюра 320×320 в поле
  `thumbnail`). Категории: `coffee`, `bakery`, `canteen`, `flowers`, `farm`.
- Роли манифеста: `logo`, `photo` (без изменений) и новая `product`. `ShowcaseAsset.Role` — строка, код не меняется,
  кроме комментария.
- Картинки есть не у всех товаров (~60 % каталога). Одна картинка на несколько похожих товаров допустима: публикация
  дедуплицирует по хешу содержимого (C28-4), план считает так же.
- **Бюджет прироста ≤ 3 МБ** (SPEC §4): около 5 логотипов, ~25 фото и ~60 пар товар + миниатюра. Если JPEG не
  укладывается, снизить качество в генераторе, не число картинок. Тест `ShowcaseAssetStoreTests`/новый юнит: сумма
  размеров файлов с ролями магазинов ≤ 3 МБ, у каждой записи `product` есть миниатюра, все файлы из манифеста на месте.
- `LICENSES.md` — строки происхождения на каждый новый файл («нарисовано программно `generate.py`, людей нет»).
  `legal-counsel` не нужен (Р35-3).
- Сброс: `DemoResetService` сохраняет файлы из `generator.PublishedFileNames`, картинки товаров публикуются тем же
  `ShowcaseAssetStore` и сохраняются тоже. Картинки, загруженные посетителями, стираются `ClearAllFiles` (C35-0-4
  проверяет CY35).

---

## §35.12. Сброс (US-35-06): что меняется

Процедура `DemoResetService.ResetAsync` **по шагам не меняется** (§580 цикла 28 и отклонения C28-10). Меняется только
содержание:

1. `ShowcaseProfile.Demo` с `Shops = true`: генератор внутри той же транзакции создаёт и магазины, и служебный тариф
   «Заказов» (идемпотентно).
2. Строки отчёта (§35.9.6).
3. Сбой на любом шаге до коммита откатывает всё, включая магазины. Вчерашнее демо обоих продуктов остаётся целым:
   это то же свойство `TRUNCATE` в транзакции.

**Бюджет ≤ 3 мин с картинками** (C28-9, C35-0-8): оценка — сейчас 6–7 с без картинок; +~45 тыс. строк goods пачками
по 2000 даст +10–20 с, публикация ~150 файлов — секунды. Меряет CY35 (функциональный, локально) и M35 (на машине).

---

## §35.13. Фронт goods (US-35-01, US-35-03, US-35-04, US-35-10)

| Что | Где | Как |
|---|---|---|
| Продукт демо | `goods/src/GoodsApp.tsx` | корень приложения внутри `QueryClientProvider` → `<DemoProductProvider product="orders">` |
| Экран «Демо обновляется» | там же | `<DemoMaintenanceGate>` (общий, из `src/components/demo/`) оборачивает `BrowserRouter`-содержимое. Он же ставит `meta robots` в демо. Интерсептор `api/client.ts` общий и уже выставляет `demoStore.resetting` на 503 + `X-Demo-Resetting` |
| Плашка | там же | `<DemoBanner />` **над** `GoodsNavbar`, в потоке, не `fixed`. Витрина (`/:slug`), заказ (`/o/:token`), кабинет, вход — все под одним корнем. Нижняя панель корзины и плашка «Доступна новая версия» (C34-5) внизу экрана, поэтому перекрытий на 360 px нет |
| Кнопки ролей | `LoginPage` (общая) | без правок (§35.7.3) |
| Отказ демо | `goods/src/utils/orderError.ts` | §35.8 |
| (P1) Переход между демо | `src/components/demo/DemoBanner.tsx` | в конце строки плашки ссылка: при `product = services` — «Посмотреть демо „Заказов“» → `status.siteUrls.orders`, при `orders` — «Посмотреть демо „Записи“» → `status.siteUrls.services`. Только в демо (плашка иначе не рисуется). Цель касания ≥ 44 px на мобильном (`inline-flex min-h-[44px]` у ссылки) |

После сброса: токен демо-роли жив, у зарегистрированного посетителя 401 → общий интерсептор делает выход и
переадресацию на `/login` (без ошибки на экране). Корзина из `localStorage` проходит через существующую проверку
витрины (`quote`/смена каталога). Стёртый заказ `/o/:token` → 404 → существующий экран «Заказ не найден». Кода для этого
не нужно, нужны vitest-проверки (§35.16.2).

Guard-тест `sharedSources.guard.test.ts`: `src/components/demo/*` лежит в разрешённом `components/`, новый файл
контекста туда же. Список `goods-shared-sources.js` не меняется.

---

## §35.14. Развёртывание `demo.zakaz` и память (US-35-08)

### §35.14.1 Файлы в репозитории

| Файл | Изменение |
|---|---|
| `docker-compose.demo.yml` | `api-demo.environment`: `PublicSites__OrdersBaseUrl=${DEMO_ORDERS_BASE_URL:-https://demo.zakaz.ezbook.ru}`. `AllowedOrigins=${DEMO_ALLOWED_ORIGINS:-https://demo.visit.ezbook.ru,https://demo.zakaz.ezbook.ru}`. Шапка: «два vhost, один API». Лимиты памяти — §35.14.2 |
| `.env.demo.example` | описание `DEMO_ORDERS_BASE_URL`. Предупреждение: если в `.env.demo` на машине уже задан `DEMO_ALLOWED_ORIGINS` только с `demo.visit`, дописать `demo.zakaz` |
| `deploy/nginx/demo.zakaz.ezbook.conf` 🆕 | `server_name demo.zakaz.ezbook.ru`; `root /var/www/ezbook/current/__goods` (та же сборка goods из того же релиза); **свои** `map`/`log_format` с уникальными именами (`$demo_zakaz_safe_uri`, `$demo_zakaz_safe_referer`, `demo_zakaz_masked`). Имена из `goods.ezbook.conf` повторять нельзя: nginx на дубле `log_format`/переменной `map` не стартует. Маскирование `/o/<token>` и `/api/orders/public/<token>`, как у goods. `robots.txt` → `Disallow: /`; `X-Robots-Tag "noindex, nofollow"` на всех location (с повтором наследуемых заголовков); `/api/`, `/uploads/` → `127.0.0.1:5001` с `proxy_hide_header X-Robots-Tag` у `/api/`; `^~ /api/phone-verification/`, `^~ /api/notifications/provider-webhook/`, `^~ /api/notifications/unsubscribe/` → 404; `/sw.js`, `/manifest.webmanifest` → `no-cache`; `/assets/` → `public, max-age=31536000, immutable`; `/` → `try_files … /index.html` + `no-cache` + CSP goods (с `worker-src 'self' blob:`) + `X-Frame-Options DENY`. Кеш-заголовки те же, что в `goods.ezbook.conf`, иначе плашка «Доступна новая версия» не работает |
| `deploy/nginx/demo.visit.ezbook.conf` | **не меняется** (`/__goods/` → 404 остаётся) |
| `deploy/ci/demo-zakaz-smoke.sh` 🆕 | смоук §35.14.3. Отдельный скрипт, `demo-smoke.sh` не трогается |
| `deploy/deploy-remote.sh` | в шаге демо после `demo-smoke.sh` вызвать `demo-zakaz-smoke.sh https://demo.zakaz.ezbook.ru`, **если** задан `DEMO_ZAKAZ_ENABLED=true` в `.env`. Сбой — предупреждение, боевой выкат не валится (как в цикле 28) |
| `DEPLOY.md` §27 🆕 «Демо-стенд „Заказов“» | §35.14.4. §25 не переписывается |

Скрипты — только в `deploy/ci/`, инлайн-скриптов в `ci.yml` нет. `ci.yml` после правок проверить на парсинг YAML.

### §35.14.2 Память (A35-3)

| Компонент | Лимит сейчас | Ожидаемо после цикла | Основание |
|---|---|---|---|
| `postgres-demo` | 256 МБ | +20–30 МБ данных (~5 тыс. заказов, ~15 тыс. позиций, ~25 тыс. событий, индексы) | `shared_buffers=32MB` не меняется |
| `api-demo` в покое | 400 МБ (куча ≤ 256 МБ) | +10–20 МБ (кеши каталога goods, опрос доски) | тот же образ |
| `api-demo` во время сброса | — | пиковый граф: салоны (~30 тыс. объектов) + магазины (~50 тыс.). Трекер EF чистится каждые 2000 строк. Оценка пика 250–330 МБ | измерить |

**Правило:** лимиты **не** меняются до замера. M35 (ручной) — `docker stats --no-stream` во время `ops demo reset --yes`
(пик) и через сутки (покой). Если пик `api-demo` > 360 МБ (90 % лимита) или был OOM-kill: `mem_limit: 512m`,
`DOTNET_GCHeapHardLimit=0x14000000` (320 МБ). Условие запуска тогда `available ≥ 1000 МБ` (вместо 900) при работающих
бое и GlitchTip. Цифры — в таблицу `DEPLOY.md` §27. Боевой стек памяти не теряет: лимиты у демо жёсткие.

### §35.14.3 Смоук `demo-zakaz-smoke.sh <base>`

1. `GET /` → 200, `text/html`, есть `X-Robots-Tag: noindex`.
2. `GET /robots.txt` → содержит `Disallow: /`.
3. `GET /api/demo/status?product=orders` → 200, `roles[].role` = `shop-owner, shop-staff, shop-customer`,
   `siteUrls.orders` = `<base>` (плашка на фронте рисуется по этому ответу).
4. `POST /api/demo/login {"role":"shop-owner"}` → 200, есть `token`.
5. С токеном: `GET /api/shops/my` → хотя бы один магазин. `GET /api/shops/{id}` → `publicUrl` начинается с `<base>/`
   (ссылки не на бой).
6. `GET /api/goods/catalog` (те же параметры, что у главной goods: город «Москва» из `GET /api/cities`) → непустой
   список.
7. `grep -c 'goods.ezbook.ru'` по ответам шагов 3 и 5 → 0.

### §35.14.4 `DEPLOY.md` §27 — ручные шаги (выполняет человек)

0. **R35-5:** если `demo.visit` на машине ещё не развёрнут, сначала все шаги §25. Выкат цикла 35 — тогда первый выкат
   демо целиком.
1. DNS: `dig +short demo.zakaz.ezbook.ru` → адрес сервера (заведён заказчиком, Р35-2).
2. `free -m` — условие §35.14.2.
3. `.env.demo`: проверить `DEMO_ALLOWED_ORIGINS` (или удалить, чтобы взялось значение по умолчанию). При желании —
   `DEMO_ORDERS_BASE_URL`.
4. `docker compose -f docker-compose.demo.yml --env-file .env.demo up -d` — перезапуск `api-demo` с новыми
   переменными. Старт упадёт с понятным сообщением, если адрес «Заказов» не демо (замок §35.5.1).
5. `… exec -T api-demo dotnet ServiceBooking.API.dll ops demo reset --yes`. Замерить время и пик памяти (M35).
6. vhost: `sudo cp deploy/nginx/demo.zakaz.ezbook.conf /etc/nginx/sites-available/`, symlink, `sudo nginx -t && sudo
   systemctl reload nginx`.
7. `sudo certbot --nginx -d demo.zakaz.ezbook.ru`, затем `sudo certbot renew --dry-run`.
8. SmartCaptcha: добавить `demo.zakaz.ezbook.ru` в хосты существующей капчи.
9. В `.env` боя — `DEMO_ZAKAZ_ENABLED=true`.
10. `deploy/ci/demo-zakaz-smoke.sh https://demo.zakaz.ezbook.ru` и `deploy/ci/demo-smoke.sh https://demo.visit.ezbook.ru`
    (демо «Записи» не сломан).
11. Через сутки — замер памяти в таблицу §27.
12. Перед встречей с клиентом — ручной `ops demo reset --yes` (R35-2).
13. Откат: удалить symlink vhost и перезагрузить nginx. `DEMO_ZAKAZ_ENABLED` убрать. Данные «Заказов» в демо-БД не
    мешают `demo.visit`.

---

## §35.15. Контракты и CI (T-35-03 — все шаги закрепления сразу, урок C33-1)

| Шаг | Где |
|---|---|
| схема | `contracts/cycle35/openapi.yaml` (готова архитектором) |
| JSON для C#-валидатора | `contracts/cycle35/openapi.json` через `npm run contracts:json`. В `frontend/scripts/contracts-to-json.mjs` список `['cycle31', 'cycle32', 'cycle35']` |
| redocly lint | `ci.yml` шаг «Lint API contracts» — строка `../contracts/cycle35/openapi.yaml`. В шапке `contracts/redocly.yaml` — строка в перечне |
| типы фронта | `package.json`: `"types:api:cycle35": "openapi-typescript ../contracts/cycle35/openapi.yaml -o src/types/api-cycle35.generated.ts"`. Генерат закоммитить. В `ci.yml` — `npm run types:api:cycle35` и файл в `git diff --exit-code` |
| сверка JSON | `ci.yml` шаг «Contract JSON must match …» — добавить `../contracts/cycle35/openapi.json` (и имя шага) |
| самопроверка валидатора | `OpenApiContractValidatorTests.Bundled_contracts_load_and_unknown_path_is_reported` — `[InlineData("cycle35")]` |
| **смена эталона формы `/api/demo/*`** | `Cycle28DemoContractTests` (CY28-38): проверки `GET /api/demo/status` и `POST /api/demo/login` переходят на `OpenApiContract.Load("cycle35")` (в `DemoStatusDto` появилось поле `siteUrls`, строгий валидатор по схеме цикла 28 его отвергнет). Проверки 403/503 остаются на цикле 28. Правку делает BE вместе с T-35-13, иначе красный тест. `contracts/cycle28` **не правится** |
| документация | `API_DOCUMENTATION.md` §4.21 — пункт «Цикл 35: демо „Заказов“» (параметр `product`, роли, `siteUrls`, три запрета, ссылки) |
| парсинг | после правки `ci.yml` — `node -e "require('js-yaml').load(require('fs').readFileSync('.github/workflows/ci.yml','utf8'))"` (или эквивалент) |

Схема использует только ключевые слова, которые понимает `OpenApiContract.cs` (`type`, `required`, `properties`,
`additionalProperties`, `enum`, `items`, `minItems`, `maxItems`, `pattern`, `format`, `nullable`, `$ref`).

---

## §35.16. Тесты и QA (T-35-01, T-35-02)

### §35.16.1 Функциональные `ServiceBooking.Tests` (`Cycle35*.cs`, `[TestCase("CY35-xx")]`)

База — `sbtest_<ключ>_demo` (C28-12), коллекция `Cycle28Generator` (один демо-слот на прогон, классы по одному).

| Файл | Что проверяет |
|---|---|
| `Cycle35DemoOrdersGeneratorTests` | после `ops demo reset --yes`: 5 магазинов по §35.9.2 в каталоге (`GET /api/goods/catalog` по городам и «все города»). У каждого логотип, 3–6 фото, телефон из блока, часы. Каждая возможность US-35-02 есть хотя бы у одного магазина. Доска кофейни непуста (New/Accepted/Ready/предзаказы). История, сводка за 7/30/60 и сравнение, топ, лист сборки непусты. Счётчики согласованы (§35.9.4). Лимит не достигнут, предупреждений нет. Запрещённых таблиц (§35.3.4) нет. Детерминизм: два сброса на одну дату дают одинаковые Id, имена, телефоны, число заказов |
| `Cycle35DemoRolesTests` | `status` без параметра = как в цикле 28 (3 салонные роли). `?product=orders` → 3 роли магазина. `?product=x` → 400. Вход под тремя ролями «Заказов». `shop-owner` проходит `[RequiresOwnerTerms]` без 451. Сотруднику недоступны товары и настройки (403 прав, как у роли). Покупатель: «Мои заказы» по §35.9.3, каждая `/o/:token` открывается. Токен до сброса жив после. Сквозной путь: заказ под покупателем → доска (ревизия выросла) → принять/готов/выдать → статус на странице заказа |
| `Cycle35DemoRestrictionsTests` | три маршрута §35.8: 403 + `X-Demo-Restricted` + текст для демо-токена. Для зарегистрированного посетителя в своём магазине — прежнее поведение. Вне демо-режима — прежнее поведение (403 без тела / 2xx) |
| `Cycle35OutboundTests` | §35.6.2 — пять путей, фейковые отправители включены, 0 вызовов. Контроль вне демо на непомеченном магазине |
| `Cycle35ResetTests` | сброс стирает заказы, магазины, аккаунты и файлы посетителей обоих хостов и восстанавливает оба демо. Картинки товаров опубликованы заново, загруженные посетителем удалены. Служебный тариф «Заказов» ровно один после двух сбросов. Сбой до коммита (подменённый генератор бросает) → вчерашние магазины на месте. Вне демо — код 2. Время сброса ≤ 3 мин (с картинками) |
| `Cycle35LocksTests` | замок `OrdersBaseUrl` (пусто, боевой → старт падает; демо → стартует). `/api/demo/*` вне демо — 404 без тела |
| `Cycle35LinksTests` | все URL-поля (`publicUrl`, `orderUrl`, `kinds-summary.*.siteUrl`, `push/config.siteUrls`, `demo/status.siteUrls`) на демо начинаются с демо-адресов. Ни одно не содержит `goods.ezbook.ru` или `https://ezbook.ru` |
| `Cycle35BoardTickTests` | `DemoBoardTicker.TickAsync(now)` с явным временем: заказы генератора догоняют линию, журнал и ревизия согласованы. Ручное действие посетителя не перезаписывается. Заказ посетителя закрывается после +50 мин с причиной (P1). Вне демо задача не зарегистрирована |
| `Cycle35ContractTests` | ответы `/api/demo/status` (оба продукта) и `/api/demo/login` (шесть ролей) — по `contracts/cycle35` |

Эталон `Cycle22RouteTable.golden.txt` обновляется (§35.8). Юнит (`ServiceBooking.UnitTests`): `ShowcaseShopsDatasetTests`
(детерминизм, журнал ↔ статус, номера, счётчики, остатки, словарь заметок), замороженные отпечатки §35.9.5,
`ShowcaseOrderTimelineTests`, `ShowcaseOwnershipCoverageTests` (обновлён), `DemoModeValidationTests` (+3),
`DemoBoardTickRulesTests`, `OpsCommandLineTests` (`--profile demo`), `ShowcaseDemoRolesTests` (6 ролей, `ForProduct`),
манифест и бюджет картинок.

### §35.16.2 Vitest

- ezbook: `useDemoStatus` с провайдером и без (URL запроса, `queryKey`). `DemoRoleButtons` рисует роли своего продукта
  и ведёт по таблице §35.7.1. `DemoBanner` — ссылка на соседнее демо (P1), без демо — ничего. Существующие
  `DemoBanner.test`, `DemoRoleButtons.test`, `demo.test`, `demoRoles.test` зелёные.
- goods (`goods/src/demo.test.tsx` 🆕): плашка на `/`, `/:slug`, `/o/:token`, `/cabinet/:shopId/orders`, `/login`.
  Экран обслуживания по `status.resetting` и по 503 + `X-Demo-Resetting`. На `/login` три кнопки «Заказов» и запрос
  `?product=orders`. `getGoodsErrorMessage` на 403 с `X-Demo-Restricted` отдаёт тело, без заголовка — прежний текст.
  Вне демо (404 статуса) — ничего демо не рисуется.

### §35.16.3 Ручные `M35-` (`TEST_CATALOG.md`, гейт выката, не мержа)

360 и 1280 px: плашка, нижняя панель корзины и плашка новой версии не перекрываются. Сквозной сценарий US-35-03 на двух
устройствах (телефон — покупатель, ноутбук — сотрудник, звук). Экран «Демо обновляется» во время ручного сброса на обоих
хостах. Переходы `demo.visit ↔ demo.zakaz`. Экранная читалка на плашке и экране обслуживания. Замер времени сброса и
памяти (§35.14.2). Доска в течение дня (утро, полдень, вечер) без протухших заказов.

---

## §35.17. Структура проекта — что добавляется и меняется

```
ServiceBooking.API/
├── Controllers/
│   ├── DemoController.cs                       ?product, siteUrls, роли продукта
│   ├── ShopsController.cs                      [DemoForbidden] на PUT {shopId}/slug
│   ├── CompanyMembersController.cs             [DemoForbidden] на DELETE {id}/members/{memberId}
│   ├── StaffMaxController.cs                   [DemoForbidden] на POST link-sessions
│   └── AdminBillingController.cs               CheckServicePlan в AssignOrdersSubscriptionAsync
├── DTOs/Demo/DemoDtos.cs                       DemoStatusDto + SiteUrls (в конец), DemoSiteUrlsDto
├── ShowcaseAssets/                             + logos/shop-*, photos/shop-*, products/*; manifest.json; LICENSES.md
├── Services/
│   ├── DeploymentSafetyChecks.cs               ValidateDemoMode п. 1a (OrdersBaseUrl)
│   ├── Demo/DemoLoginService.cs                TermsOwner и для shop-owner
│   ├── Demo/DemoBoardTicker.cs 🆕, DemoBoardTickRules.cs 🆕
│   ├── Scheduling/Tasks/DemoBoardTickTask.cs 🆕
│   ├── Scheduling/Tasks/CustomerOrderPushDispatchTask.cs, StaffMaxDispatchTask.cs   страховка §35.6
│   ├── Orders/Notifications/OrderNotificationPlanner.cs, Orders/OrderLimitWarner.cs  гвард постановки
│   ├── Orders/OrderEventLog.cs                 необязательный occurredAtUtc
│   ├── Subjects/SubjectDataExporter.cs, AccountDeletionService.cs                   исключение витрины (§35.6.3)
│   ├── Ops/OpsCommandLine.cs, OpsCommandRunner.cs                                   --profile demo у plan
│   └── Showcase/
│       ├── ShowcaseProfile.cs                  флаг Shops
│       ├── ShowcaseCatalog.cs                  OrdersShowcasePlanId/Name, IsServicePlan
│       ├── ShowcaseDemoRoles.cs                6 ролей, ForProduct, ProductOf
│       ├── ShowcaseMixingGuard.cs              IsServicePlan
│       ├── ShowcaseGraph.cs                    коллекции goods, ShowcaseOrdersCounts
│       ├── ShowcaseDataset.cs                  одна строка в конце Build
│       ├── ShowcaseShopsDataset.cs 🆕, ShowcaseShopSpecs.cs 🆕, ShowcaseOrderTimeline.cs 🆕
│       ├── ShowcaseGenerator.cs                PersistAsync: коллекции goods, картинки товаров, тариф
│       ├── ShowcaseCommands.cs                 профиль demo только для plan
│       ├── ShowcaseOwnership.cs                goods в DeleteSteps
│       └── Tariffs/TariffCatalogSeeder.cs      EnsureOrdersShowcasePlanAsync
├── Startup/ApplicationServicesExtensions.cs    регистрация DemoBoardTickTask только в демо
├── appsettings.json, appsettings.Testing.json  ScheduledTasks:demo-board-tick
tools/showcase-assets/generate.py               наборы магазинов и товаров

ServiceBooking.UnitTests/   ShowcaseShopsDatasetTests 🆕, ShowcaseOrderTimelineTests 🆕, DemoBoardTickRulesTests 🆕,
                            ShowcaseDatasetTests (+ замороженные отпечатки), ShowcaseDemoProfileTests (счётчики),
                            ShowcaseOwnershipCoverageTests, DemoModeValidationTests, OpsCommandLineTests, ShowcaseDemoRolesTests
ServiceBooking.Tests/Tests/ Cycle35*.cs 🆕; Cycle28DemoContractTests.cs (DemoHostFactory + OrdersBaseUrl; status/login → cycle35);
                            OpenApiContractValidatorTests (+cycle35); Cycle22RouteTable.golden.txt

frontend/src/
├── api/demo.ts                                 getStatus(product), типы из api-cycle35
├── hooks/useDemoStatus.ts                      продукт из контекста
├── components/demo/DemoProductContext.tsx 🆕
├── components/demo/DemoRoleButtons.tsx          иконки 6 ролей
├── components/demo/DemoBanner.tsx               (P1) ссылка на соседнее демо
├── utils/demoRoles.ts                           6 направлений
└── types/api-cycle35.generated.ts 🆕
frontend/goods/src/
├── GoodsApp.tsx                                DemoProductProvider, DemoMaintenanceGate, DemoBanner
├── utils/orderError.ts                         403 X-Demo-Restricted
└── demo.test.tsx 🆕
frontend/package.json, frontend/scripts/contracts-to-json.mjs

contracts/cycle35/openapi.yaml (+ openapi.json) 🆕; contracts/redocly.yaml (шапка); .github/workflows/ci.yml
docker-compose.demo.yml, .env.demo.example, deploy/nginx/demo.zakaz.ezbook.conf 🆕, deploy/ci/demo-zakaz-smoke.sh 🆕,
deploy/deploy-remote.sh; DEPLOY.md §27; API_DOCUMENTATION.md §4.21; TEST_CATALOG.md «Цикл 35»
```

---

## §35.18. Разбивка работ и параллельность

Номера `T-35-01…03` — из SPEC (тесты, ручные, контракт). Задачи архитектора — `T-35-10…` (backend), `T-35-30…`
(frontend), `T-35-40…` (devops и контент).

### §35.18.1 Backend

| # | Задача | P | Зависит от | Параллельно с |
|---|---|---|---|---|
| **T-35-10** | Закрепление контракта (§35.15): `openapi.json`, `contracts-to-json.mjs`, `ci.yml`, `package.json` + генерат (вместе с FE), `OpenApiContractValidatorTests`, `API_DOCUMENTATION.md` §4.21 | P0 | — (схема готова) | всё |
| **T-35-11** | Замок `OrdersBaseUrl` + `DemoModeValidationTests` + `DemoHostFactory` (+`PublicSites:OrdersBaseUrl`) | P0 | — | T-35-12, 13, 14 |
| **T-35-12** | Исходящие §35.6: гвард в `OrderNotificationPlanner`, `OrderLimitWarner`, страховки в двух диспетчерах, фильтр §35.6.3; юнит | P0 | — | T-35-11, 13, 14 |
| **T-35-13** | Роли и вход §35.7 + запреты §35.8: `ShowcaseDemoRoles` (**константы ключей — первым коммитом, их ждёт T-35-14**), `DemoController`/`DemoDtos`, `DemoLoginService`, три `[DemoForbidden]`, эталон маршрутов, перевод CY28-38 на cycle35 | P0 | T-35-11 (фабрика) | T-35-12, 14 |
| **T-35-14** | Генератор §35.9: **сначала замороженные отпечатки §35.9.5**, затем `ShowcaseProfile.Shops`, `ShowcaseShopSpecs`, `ShowcaseShopsDataset`, `ShowcaseOrderTimeline`, граф, `PersistAsync`, тариф §35.3.3 + гвард в админке, отчёты и `--profile demo`, `ShowcaseOwnership` + тест покрытия | P0 | константы из T-35-13 | T-35-12, 13, 15 |
| **T-35-15** | Картинки §35.11 (`generate.py`, манифест, `LICENSES.md`, бюджет) — может взять любой, кто знаком с Python | P0 | — | T-35-14 (генератор без картинок работает: отсутствующие ключи пропускаются) |
| **T-35-16** | `demo-board-tick` §35.10: ветка A (P0) — правила, тикер, задача, `OrderEventLog.occurredAtUtc`; ветка Б (P1) — закрытие заказов посетителей | P0 / P1 | `ShowcaseOrderTimeline` из T-35-14 | T-35-15 |
| **T-35-17** | Функциональные `Cycle35*` своих задач (или QA по `TEST_CATALOG.md`) и прогон полного набора | P0 | по готовности | — |

Правила для всех (как в цикле 28): сборка с `-warnaserror`. Перед заявлением о готовности — `grep` новых классов и точек
вызова из §35.17 («зелёный прогон ≠ функционал»). Полный прогон тестов не запускать параллельно со вторым полным
прогоном: тесты, чувствительные ко времени, дают ложные падения. Интеграционным тестам на colima нужен `DOCKER_HOST`
(без него все падают разом за 1 мс — это не регрессия).

### §35.18.2 Frontend (весь фронт — на моке `npx @stoplight/prism mock contracts/cycle35/openapi.yaml --port 4035`)

| # | Задача | P | Зависит от | Параллельно с |
|---|---|---|---|---|
| **T-35-30** | `types:api:cycle35` + генерат, `api/demo.ts`, `DemoProductContext`, `useDemoStatus`, `demoRoles.ts`, иконки `DemoRoleButtons`; vitest ezbook | P0 | схема | T-35-31 |
| **T-35-31** | goods: `GoodsApp` (провайдер, шлюз, плашка), `orderError.ts`; `goods/src/demo.test.tsx` | P0 | T-35-30 (контекст и хук) | — |
| **T-35-32** | (P1, US-35-10) ссылка на соседнее демо в `DemoBanner` + тест | P1 | T-35-30 | T-35-31 |

### §35.18.3 DevOps, QA

| # | Задача | Когда | Готово, если |
|---|---|---|---|
| **T-35-40** | compose, `.env.demo.example`, vhost `demo.zakaz`, `demo-zakaz-smoke.sh`, шаг в `deploy-remote.sh`, `DEPLOY.md` §27, проверка парсинга `ci.yml` | сразу, параллельно | `docker compose -f docker-compose.demo.yml config` проходит; `nginx -t` на копии конфигов (локально в контейнере `nginx:stable`) проходит вместе с `goods.ezbook.conf` и `demo.visit.ezbook.conf` |
| **T-35-01** | QA: базовая линия чисел прогонов в начале цикла, `TEST_CATALOG.md` «Цикл 35» (`CY35-`, `M35-`), schemathesis по `contracts/cycle35` на демо-хосте | с начала | — |
| **T-35-02** | QA: ручные `M35-` (§35.16.3) | после выката на машину | гейт выката |

### §35.18.4 Что параллельно, что последовательно

```
день 0  ── T-35-10 (контракт закреплён) ─────────────────────────────────────────────┐
        ── T-35-40 (devops) ──────────────────────────────────────────────────────────┤
        ── T-35-30 → T-35-31 → T-35-32 (фронт на моке) ─────────────────────────────┤
        ── T-35-11 ─┬─ T-35-13 (константы ролей → остальное) ───────────────────────┤
                    ├─ T-35-12 ──────────────────────────────────────────────────────┤
                    └─ T-35-14 (отпечатки → генератор) ── T-35-16 (тик) ─────────────┤
        ── T-35-15 (картинки) ──────────────────────────────────────────────────────┤
                                                                   интеграция: фронт на реальном API
                                                                   (демо-хост локально), T-35-17, M35
```

Точки синхронизации: форма — `contracts/cycle35/openapi.yaml`; тексты, порядок проверок, ссылки, вывод команд и
поведение доски — `API_CONTRACT_CYCLE35.md`. Формы контракта урезание не меняет.

### §35.18.5 Порядок урезания (R35-1)

P0 не режутся. P1 режутся в порядке: **T-35-32** (US-35-10, переход между демо; поле `siteUrls` в ответе остаётся) →
P1-пункт US-35-01 (уже перенесён, §35.5.3) → **ветка Б T-35-16** (закрытие заказов посетителей). Ветка A `demo-board-tick`
— P0: на ней держится пункт US-35-02 «Завершённые сегодня».

---

## §35.19. Риски, долги, отклонения

### §35.19.1 Риски

| # | Риск | Решение |
|---|---|---|
| R35-1 | Объём | порядок урезания §35.18.5. Генератор — самая большая задача, её нижние границы объёма (§35.9.3) можно снижать без изменения механики |
| R35-2 | Посетитель «ломает» демо до ночи (D35-4) | стабильные роли, запреты §35.8, ручной `ops demo reset --yes` перед встречей (`DEPLOY.md` §27 шаг 12) |
| R35-3 | Правовой: посетитель вводит реальные имя и телефон (L35-1, L35-2) | плашка на всех страницах, хранение ≤ 24 ч, ни одного исходящего (§35.6), продавец без реквизитов. Юрист до выдачи ссылки клиентам (D35-3) |
| R35-4 | Утечка на бой (ссылка, QR, push) | один `PublicSiteLinks` + замок `OrdersBaseUrl` + CY35 «ссылки» + смоук шаг 7 |
| R35-5 | Стенд на машине не проверен | `DEPLOY.md` §27 шаг 0 |
| R35-6 | Сдвиг байтов `prod` или салонного демо при правке генератора | замороженные отпечатки **до** правки (§35.9.5), построитель магазинов вызывается последним |
| R35-7 | nginx не стартует из-за дубля `log_format`/`map` с `goods.ezbook.conf` | уникальные имена (§35.14.1), `nginx -t` в T-35-40 |
| R35-8 | Тикер спорит с посетителем за заказ | оптимистичная блокировка, пропуск до следующего прохода. Ручные действия посетителя не перезаписываются |
| R35-9 | Память стенда | порог по замеру, путь 512 МБ, при нехватке — стоп и доклад (§35.4) |
| R35-10 | CY28-38 краснеет от нового поля `siteUrls` | перевод двух проверок на cycle35 в той же задаче (§35.15) |
| R35-11 | Тексты goods L1–L20 выставлены на показ клиентам без юриста | не меняются. Заказчик предупреждён в SPEC §0-bis |

### §35.19.2 Долги, которые заводит цикл (в `CURRENT_STATE.md` §9 как C35-*)

- **C35-1.** На демо часть надписей показывает боевой домен текстом (11 мест, §35.5.3). Ссылки при этом верные.
- **C35-2.** Юрист не смотрел демо «Заказов» (L35-1, L35-2): плашка — прежний `DemoBanner`, продавец без реквизитов.
  Закрыть до выдачи ссылки клиентам.
- **C35-3.** Колонка «Завершённые сегодня» пуста с ночного сброса до открытия кофейни (~07:15) (§35.10.4).
- **C35-4.** Кеш каталога goods до 30 с после сброса командой из другого процесса (§35.5.2, как C28-13).
- Закрываются: C35-0-1 (§35.6), C35-0-2 (§35.5.1), C35-0-3 (§35.9.7), C35-0-4 (CY35 сброса), C35-0-5 (§35.3.1, без
  кода), C35-0-6 (§35.13), C35-0-7 (тариф §35.3.3; публичной сетки нет по D35-2), C35-0-8 (замер M35).

### §35.19.3 Отклонения от буквы SPEC и решения сверх неё (читать обязательно)

1. **«Завершённые сегодня» — с открытия кофейни, а не с 04:00** (US-35-02). Правдоподобной выдачи до открытия не
   бывает. При дневном ручном сбросе колонка непуста сразу (§35.10).
2. **Задача `demo-board-tick`** — новая фоновая задача демо, хотя SPEC оставлял способ на выбор. Это не «симулятор
   покупателя»: новых заказов она не создаёт.
3. **Запрет `DELETE …/members/{memberId}` действует и в демо «Записи»** (маршрут общий). Демо-владелец «Лаванды» больше
   не может удалить демо-мастера. Это та же дыра («кнопка ведёт в пустой кабинет»), US-35-04 разрешает дополнять
   список. Других изменений для `demo.visit`, кроме ссылок (US-35-10), нет.
4. **Параметр `product`, а не хост** (A35-4). Хост — ненадёжный признак (тесты, прокси, локальная разработка).
   Параметр явный и по умолчанию сохраняет ответ цикла 28.
5. **`siteUrls` в ответе статуса** — новое поле, из-за которого две проверки CY28-38 переходят на контракт цикла 35.
6. **Телефон у витринных магазинов есть** (SPEC US-35-02 требует), в отличие от салонов цикла 28 (§583.3 п. 5). Номер из
   блока `+7 (200)` вне плана нумерации.
7. **Кофейня работает 07:00–23:00** (SPEC не задаёт часы) — чтобы демо было живым с утра до вечера.
8. **`ops showcase plan --profile demo`** — новый ключ только для плана. Создавать демо-профиль по-прежнему можно только
   сбросом.
9. **Фильтр витрины в двух выборках заказов «по номеру»** (§35.6.3) — страховка сверх SPEC по NFR «ПДн»; для
   настоящих данных инертна.
