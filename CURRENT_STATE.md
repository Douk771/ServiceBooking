# CURRENT_STATE — фактическое состояние кодовой базы ServiceBooking

**Актуально по состоянию на коммит: `49f0d60` (`develop` = `origin/develop` = `cycle/032-salon-settings-compact-layout`, мердж `fix/goods-sound-remember`; дата коммита — 2026-10-01), дата обновления: 2026-10-01.**
**Режим: обновление поверх полного сканирования на `1837373`** (прежние точечные обновления — на `b9c2a79`, `aeed251`, `364cc2b`).
**Обновление на `49f0d60` (✔ `git diff 364cc2b..49f0d60`, 4 файла):** `6a71f50` — документы цикла 31 (CHANGELOG, README, этот
файл; закрывает C31-8), `c7a4ace` — фикс goods мимо циклов: звук новых заказов запоминается (§5.5). Бэкенд, контракты,
миграции, пакеты не менялись. Добавлен §5.9 — фактическая раскладка настроек компании в goods и в ezbook (вход для
цикла 32 «компактные настройки салона»; код прочитан, ничего не запускалось). Обновлены §5.5, §5.9 (новый), §9.1, §10.2.
**Следующий diff отсчитывайте от `49f0d60`.**

Предыдущая шапка (на `364cc2b`): сверено по git (✔): `git diff c19a83c..364cc2b` (99 файлов). В нём цикл 30 (уже был описан в этом файле на `aeed251`,
влит мерджем `e1d7cdb`), три мелких фикса goods мимо циклов (`53def4f`, `9659742`, `474f8be`) и **цикл 31** —
`5c1d133`…`67f0fd6`, мердж `364cc2b`. При этом мердже `CURRENT_STATE.md` взят в версии `develop`, так что прежняя
правка из ветки 31 (`a8d3337`, «фактическое устройство до цикла 31») в файл не попала. Её суть теперь устарела, и вместо
неё в §5.8 описано состояние **после** цикла 31. Обновлены §0, §1, §2, §3, §4, §5.4, §5.5, §5.7 (шапка), §5.8 (новый),
§6.2, §7.1, §7.3, §9.1, §10.

## 0. Как читать этот документ

**Почему документ переписан.** Прежняя редакция копила точечные обновления 27 циклов. В ней было около 11 400 строк
(около 1,4 МБ): блоки поверх блоков, у каждого своя метка (🗓, 💳, 📸, 🔔, 🛒, ⏰, 📊, 🪪, ✅27…). Целиком её не читал ни
один агент, а точечные правки уже расходились с кодом. Эта редакция — новая инвентаризация кода на `491406c` (цикл 26)
плюс сверенный diff `491406c..1837373` (цикл 27, только фронт goods и документы), и опорой служит она. Прежняя
редакция не удалена, её можно взять из git: `git show 1837373:CURRENT_STATE.md`. Она включает блоки ✅27/🏪27 цикла 27,
их суть перенесена в §5.5. Номера пунктов долга (C15-8, V1, NP1, C25-6…) сохранены, поэтому поиск по номеру работает в
обеих версиях.

**Как шло сканирование.** Сканирование шло на `491406c`. Перед коммитом выяснилось, что `develop` за это время
сдвинулся: влит цикл 27 (fast-forward до `1837373`). Этот diff пересчитан отдельно, в нём 10 файлов, из кода меняется
только `frontend/goods/src/components/BusinessBlock.tsx` и его тесты. Бэкенд, контракты и миграции цикл 27 не трогал.

**Три пометки достоверности** (ставятся там, где это существенно):
- ✔ — сверено по коду или git в этом сканировании;
- ↪ — перенесено из прежней редакции, в этом сканировании **не перепроверялось**;
- 🖥 — факт о боевой машине. В репозитории его нет, в этом сканировании он не проверялся (SSH не использовался).
  Источник — прежняя редакция, блок «🚀20», проверено по SSH 30.09.2026.

**Ветки** (✔ по локальным ссылкам `origin/*` на 2026-09-30, `git fetch` в этом обновлении не делался):
- На `49f0d60` (✔ локальные ссылки, `git fetch` не делался): `origin/develop` = `develop` =
  `origin/cycle/032-salon-settings-compact-layout` = `49f0d60`; ветка цикла 32 создана и пока не отличается от `develop`.
- `origin/develop` на прошлом обновлении = `364cc2b`. В нём влиты **цикл 29** «Доделки цикла 26» (§5.5), **цикл 30** «Для покупателей» и
  скриншоты на главной goods (мердж `e1d7cdb`, §5.7) и **цикл 31** «Общий блок фото, мультизагрузка, блок „Каталог“
  салона, компактная шапка карточки» (мердж `364cc2b`, §5.8). Мимо циклов влиты фиксы goods `fix/goods-storefront-mobile-width`
  (`53def4f`), `fix/goods-pickup-chips-narrow` (`9659742`) и `fix/goods-cabinet-tabs-scroll` (`474f8be`) — §5.5.
- `origin/cycle/031-shared-photo-uploader`, `origin/cycle/030-user-section-screenshots`, `origin/cycle/029-cycle26-followups`
  влиты в `develop`.
- `origin/cycle/028-showcase-data-demo-stand` = `673473c` — на 37 коммитов впереди и на 15 позади `origin/develop` (✔
  `rev-list` на `364cc2b`). `develop` до цикла 30 включительно (`e1d7cdb`) в неё уже влит, цикла 31 в ней нет. **Это уже
  не только документы.** Diff от общей базы — 151 файл, около 18 тыс. строк: демо-режим и витрина (`Services/Demo/`,
  `ShowcaseReseedTask`, правило retention `ShowcaseVisitorBookingRule`, `Services/Ops/OpsCommand*`), правки биллинга,
  триала, админки, одна новая миграция, генерат `api-cycle28.generated.ts`, свои `SPEC.md` и `CURRENT_STATE.md`. В этом
  обновлении ветка не сканировалась, только diff по именам файлов. С файлами цикла 31 пересекается только
  `frontend/package.json`. Когда ветка будет вливаться, её `SPEC.md` (спека 28) столкнётся с корневым `SPEC.md` (сейчас
  это спека цикла 31, §10.5). Конфликт в CURRENT_STATE.md разрешает codebase-analyst: сводит обе версии по смыслу,
  выбирать файл целиком нельзя.
- `origin/cycle/027-goods-business-block` влита в `develop` целиком (0 впереди, 0 позади).

**Ориентиры.** На `491406c` было 1 058 коммитов и 1 783 отслеживаемых файла; цикл 27 добавил 7 коммитов.
`origin/master` — `263c661` (2026-07-28), тегов нет, ветки `release-candidate` на origin нет.

---

## 1. Стек и версии (✔ по csproj, package.json, lock-файлу, конфигам)

### Бэкенд
| Что | Версия / значение | Где зафиксировано |
|---|---|---|
| Платформа | .NET 8 (`net8.0`), C#; `Nullable` и `ImplicitUsings` включены во всех проектах | `*.csproj` |
| Веб | ASP.NET Core 8, контроллеры MVC (`[ApiController]`), не Minimal API | `ServiceBooking.API.csproj` |
| ORM | EF Core 8.0.11 + `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11, Code First; миграции применяются на старте (`MigrateAsync`) | `ServiceBooking.Infrastructure.csproj`, `Startup/StartupSeedingExtensions.cs` |
| СУБД | PostgreSQL 16 (`postgres:16-alpine` в compose и тестах, `postgres:16` в CI-сервисе) | `docker-compose*.yml`, `TestKit/TestInfrastructure.cs`, `ci.yml` |
| Аутентификация | ASP.NET Identity (`IdentityDbContext<AppUser>`) + JWT Bearer 8.0.11. Токен живёт 7 дней, refresh-токенов нет. Логин — телефон | `Startup/AuthenticationExtensions.cs`, `Services/TokenService.cs` |
| Логи | Serilog.AspNetCore 8.0.3 (JSON в stdout и в `logs/app-.json`) и Sentry.Serilog 4.13.0: отправка в GlitchTip при непустом `Sentry:Dsn` | `Startup/LoggingExtensions.cs` |
| Изображения | SkiaSharp 2.88.8 + `NativeAssets.Linux.NoDependencies`; нужен glibc-образ, alpine не годится | `ImageProcessor.cs`, `Dockerfile` |
| QR | QRCoder 1.6.0 (`PngByteQRCode`) | `PhoneVerification/QrImage.cs`, `Shops/ShopQrCode.cs` |
| Web Push | Lib.Net.Http.WebPush 3.3.1 (VAPID) | `Services/Notifications/WebPush/` |
| Swagger | Swashbuckle 6.5.0, **только в Development** | `Startup/ApiExtensions.cs` |
| Инструменты | `dotnet-ef` 8.0.11 (локальный tool) | `.config/dotnet-tools.json` |

Решения (`.sln`): `ServiceBooking.Core`, `.Infrastructure`, `.API`, `.LegalKit` (CLI правовых текстов), `.TestKit`
(библиотека и CLI тестовой инфраструктуры), `.UnitTests`, `.Tests`. Файла `global.json` и `Directory.Packages.props` нет.

### Фронтенд (`frontend/`, один npm-пакет, два приложения)
| Что | Объявлено в package.json → установлено по lock |
|---|---|
| React / ReactDOM | ^18.3.1 → 18.3.1 |
| TypeScript | ^5.5.3 → 5.9.3 (`strict`, `noUnusedLocals/Parameters`) |
| Vite | ^5.4.8 → 5.4.21; `@vitejs/plugin-react` 4.7.0 |
| Роутинг | react-router-dom ^6.26.2 → 6.30.6 |
| Серверное состояние | @tanstack/react-query ^5.56.2 → 5.101.0 |
| Клиентское состояние | zustand ^4.5.5 → 4.5.7 |
| HTTP | axios ^1.20.0 → 1.20.0 |
| Формы | react-hook-form ^7.53.0 → 7.78.0 |
| Даты | date-fns ^3.6.0 |
| Стили | Tailwind CSS ^3.4.13 → 3.4.19, PostCSS, autoprefixer; два конфига: `tailwind.config.js`, `tailwind.goods.config.js` |
| Тесты | Vitest 3.2.7, jsdom 25, @testing-library/react 16, jest-dom, user-event |
| Качество | ESLint 9 (flat config, typescript-eslint 8.70, react, react-hooks, react-refresh), Prettier 3.9 |
| Контракты | openapi-typescript 7.13 (генерация типов), @redocly/cli 2.54 (линт OpenAPI). Скрипт `contracts:json` (цикл 31) импортирует `js-yaml` 4.3.2, а он в `package.json` **не объявлен**: пакет приходит транзитивно от ESLint (`@eslint/eslintrc`) и `@redocly/openapi-core` (§9.1, C31-9) |
| Съёмка скриншотов (цикл 30, только dev) | playwright-core ^1.63.0 → 1.63.0 (браузеры не скачивает, работает с установленным Google Chrome), @types/node ^26.6.3 → 26.6.3 (только для `tsconfig.node.json`) |

Node: в CI — 20 (`actions/setup-node`), на машине сканирования — 24.16. Менеджер пакетов — **npm**
(`package-lock.json`). У .NET — NuGet (`PackageReference`).

### Внешние сервисы и интеграции (✔ по коду; состояние на бою — 🖥)
Каждая интеграция спрятана за переключателем. Значение по умолчанию безопасное. Нераспознанное значение **роняет
старт** — это правило для всех интеграций, а не только для первой строки таблицы.

| Интеграция | Переключатель (appsettings → env в `docker-compose.prod.yml`) | По умолчанию | 🖥 На бою |
|---|---|---|---|
| GREEN-API, канал WhatsApp для уведомлений клиентам салонов | `Notifications:Provider` → `NOTIFICATIONS_PROVIDER` (`logging`\|`green-api`) | `logging` (заглушка) | не задан → `logging` |
| GREEN-API MAX, второй транспорт уведомлений | то же значение (`green-api`) | заглушка | заглушка |
| Бот MAX (`platform-api.max.ru`) для подтверждения телефона | `PhoneVerification:Provider` → `PHONEVERIFY_PROVIDER` (`stub`\|`max-bot`) | `stub` | `max-bot` (включено 24.09.2026) |
| Бот MAX для сообщений персоналу goods о заказах | `Notifications:StaffMax:Enabled` → `STAFFMAX_ENABLED`; требует `max-bot` | `false` | `false` (C25-1) |
| Web Push персоналу (ezbook и goods) и покупателям goods — один провайдер | `Notifications:StaffPush:Provider` → `WEBPUSH_STAFFPUSH_PROVIDER` (`logging`\|`web-push`) | `logging` | не задан → `logging` |
| Яндекс SmartCaptcha | `SmartCaptcha:SecretKey` на сервере; site-key — сборочная переменная `VITE_SMARTCAPTCHA_SITEKEY` (GitHub Variables) | пусто → проверка пропускается | ↪ задан |
| GlitchTip (Sentry-совместимый), сбор ошибок и «сигналы оператору» | `Sentry:Dsn`; своя установка — `docker-compose.glitchtip.yml`, `deploy/nginx/errors.ezbook.conf` | пусто → выключено | ↪ развёрнут |
| Ссылки на Яндекс Карты и 2ГИС | только строки, которые ввёл владелец (`MapLinkValidation`); API карт не вызывается | — | — |
| Геокодер Яндекса | **удалён** в цикле 19. Остались 5 теневых колонок `Companies.Address*` | — | — |

**Интеграций, которых нет** (✔): платёжного шлюза нет — ни YooKassa, ни Robokassa, ни других. Предоплата — флаг
`Company.RequirePrepayment` и `PaymentStatus.Pending`, в «Оплачено» запись переводит персонал
(`PATCH /api/bookings/{id}/mark-paid`). Тарифы назначает суперадмин вручную по заявке. **Отправки e-mail нет**: SMTP и
почтовых SDK в коде нет, «рассылка» — это заглушка (§5.3). SMS нет.

---

## 2. Структура репозитория (✔)

| Путь | Что там |
|---|---|
| `ServiceBooking.Core/` | `Entities/` (67 файлов, POCO-сущности), `Enums/` (47). Зависит только от `Microsoft.AspNetCore.Identity.EntityFrameworkCore` |
| `ServiceBooking.Infrastructure/` | `Data/AppDbContext.cs` (1 086 строк, вся Fluent-конфигурация) и `Migrations/` (78 миграций + снапшот) |
| `ServiceBooking.API/` | Веб-приложение, в нём же **вся бизнес-логика** (отдельного слоя Application нет) |
| `ServiceBooking.API/Program.cs` | **Точка входа**, 48 строк — только порядок вызовов расширений |
| `ServiceBooking.API/Startup/` | Инфраструктура запуска: `ApiExtensions` (MVC/JSON/CORS/Swagger/обработчик 500/раздача загрузок), `AuthenticationExtensions` (JWT, `OnTokenValidated`), `ApplicationServicesExtensions` (DI прикладных сервисов, фоновые задачи, подтверждение телефона), `NotificationServicesExtensions` (транспорты, реестры, Web Push), `RateLimitingExtensions`, `DeploymentValidationExtensions` (fail-fast проверки прод-конфигурации), `HealthEndpointsExtensions` (`/api/health/live`, `/api/health/ready`), `LoggingExtensions`, `StartupSeedingExtensions` (миграции, роли, суперадмин) |
| `ServiceBooking.API/Controllers/` | 53 контроллера (с циклом 31 — `CompanyCatalogListingController`) и 2 помощника (`BookingEndpointHelpers`, `AdminAccountDtoBuilder`). Мелкие DTO часто лежат в конце файла контроллера |
| `ServiceBooking.API/DTOs/<Домен>/` | Крупные DTO (`record`), `DTOs/Common/` — `PagedResult<T>`, `Optional<T>` |
| `ServiceBooking.API/Services/` | Бизнес-логика. В корне 45 файлов общего назначения (слоты, доступ, телефоны, файлы, изображения, проверки деплоя…) и предметные подкаталоги: `Billing/` (36), `Bookings/` (7), `Companies/` (10, с циклом 31 — `SalonListingRules`, `SalonListingQuery`), `Legal/` (20), `Notifications/` (30 + `GreenApi/`, `GreenApiMax/`, `WebPush/`), `Orders/` (23 + `Notifications/`, `Reports/`), `Shops/` (30), `PhoneVerification/` (13 + `Max/` 16), `StaffMax/` (7), `Retention/` (+ `Rules/` 27 правил), `Scheduling/` (+ `Tasks/` 10 задач), `Subjects/` (права субъекта ПДн), `Signals/` (GlitchTip), `PublicSites/`, `Health/`, `Hosting/` |
| `ServiceBooking.API/App_Data/legal/` | **Собранный** артефакт правовых текстов (18 файлов, `legal.json`). Лежит в git, руками не правится |
| `ServiceBooking.LegalKit/` | CLI `build/check/links/publish/rollback/status` для правовых текстов. Ссылается на API как на библиотеку |
| `ServiceBooking.TestKit/` | Изоляция тестовых БД (Testcontainers, лизинг баз `sbtest_<key>_<slot>`), CLI `status/sweep/doctor` |
| `ServiceBooking.UnitTests/` | 179 файлов `.cs`, 1 596 атрибутов `[Fact]`/`[Theory]` (✔ пересчёт на `364cc2b`) |
| `ServiceBooking.Tests/` | Функциональные тесты API: `Infrastructure/` (28 файлов: фабрики хоста, фикстуры, валидатор `OpenApiContract.cs`), `Tests/` (85 файлов `.cs` + эталон маршрутов, 1 103 атрибута `[Fact]`/`[Theory]`, ✔ пересчёт на `364cc2b`) |
| `frontend/src/` | Приложение **ezbook.ru** (запись на услуги). Точка входа — `main.tsx` → `App.tsx` |
| `frontend/goods/` | Приложение **goods.ezbook.ru** (заказы на самовывоз). Точка входа — `goods/src/main.tsx` → `GoodsApp.tsx`; `goods/index.html`, `goods/public/` (свой `sw.js`, манифест) |
| `frontend/scripts/merge-goods-dist.mjs` | Кладёт сборку goods в `dist/__goods/`, чтобы оба сайта уезжали одним релизом |
| `frontend/scripts/contracts-to-json.mjs` | Цикл 31: `npm run contracts:json` — `contracts/<цикл>/openapi.yaml` → `openapi.json` через `js-yaml`. Список циклов в скрипте — **только `cycle31`** (§5.8, §9.1) |
| `frontend/goods-shared-sources.js` (+ `.d.ts`) | Цикл 31: **единый список** ezbook-исходников, которые можно импортировать из goods. Из него строятся `content` в `tailwind.goods.config.js` и регэксп ESLint; полноту списка проверяет guard-тест `goods/src/sharedSources.guard.test.ts` (§5.8, §6.2) |
| `frontend/src/components/company/` | Общие компоненты карточки и кабинета компании для обоих сайтов: `CompanyCard`, `CompanyMapLinks`, `CompanyLogoMark`, `CompanyPhotoGallery`; с цикла 31 ещё `CompanyPhotosSection` (переехал из `src/pages/owner/`), `usePhotoBatchUpload`, `CatalogListingCard`, `companyActionLink.ts` |
| `frontend/scripts/screenshots/` | Цикл 30: локальный стенд `sb-shots` (`stack.sh`), засев демо-данных goods через HTTP API (`seed-goods-demo.mjs`, `demo-data.mjs`), съёмка кадров (`capture-goods-screenshots.mjs`), `README.md` пересъёмки. В сборку и CI не входят (§5.7) |
| `frontend/goods/src/assets/screenshots/` | Цикл 30: 5 WebP-кадров главной goods, манифест `screenshots.json`, модуль подключения `shots.ts` |
| `contracts/` | Машиночитаемые контракты по циклам (`cycleN/openapi.yaml`, JSON-схемы и векторы), `redocly.yaml`. Цикл 30 — только JSON-схемы `cycle30/screenshots-manifest.schema.json` и `seed-state.schema.json`, OpenAPI нет. Цикл 31 — `cycle31/openapi.yaml` + `openapi.json` |
| `legal-drafts/` | **Исходники** правовых текстов (HTML + `legal.json`), правятся только здесь |
| `legal-internal/` | Внутренние правовые документы (markdown), в сборку не входят |
| `deploy/` | `deploy.sh`, `deploy-remote.sh`, `rollback.sh`, `ssh-deploy-wrapper.sh`, `nginx/` (3 vhost), `backup/` (systemd-таймер `pg_dump`), `monitor/` (health-alert), `checks/` (SQL-гейты выката), `ci/` (смоуки, сверка пинов образов, дрейф снапшота миграций) |
| `tools/bench/cycle22`, `cycle25` | Скрипты и результаты нагрузочных замеров |
| `docs/` | Руководство пользователя по ролям (ezbook) и два документа команды (§10.2) |
| Корень | `README.md`, `CHANGELOG.md`, `API_DOCUMENTATION.md`, `TEST_CATALOG.md`, `DEPLOY.md`, `DEPLOY-windows.md`, документы циклов `SPEC_*`/`ARCHITECTURE_*`/`API_CONTRACT_*`/`LEGAL_*` (§10.5), `docker-compose{,.prod,.glitchtip}.yml`, `.env.*.example`, `.editorconfig` |

**Слои и направление зависимостей:** `Core` ← `Infrastructure` ← `API` ← (`LegalKit`, `Tests`, `UnitTests`). Бизнес-логика
живёт в `API/Services` и частично в контроллерах: права, транзакции, advisory-lock'и. Чистые правила вынесены в
статические классы без EF и HTTP — именно они покрыты юнит-тестами. Инфраструктурный код — это `Startup/`, `Infrastructure/`,
адаптеры `Services/Notifications/GreenApi*`, `WebPush/`, `PhoneVerification/Max/`, `FileStorage`, `ImageProcessor`,
`Scheduling/ScheduledTaskRunner`.

**Два продукта на одной платформе.** Компания — это либо салон (`CompanyKind.Services`, сайт ezbook.ru), либо магазин
(`CompanyKind.Orders`, сайт goods.ezbook.ru). Вид задаётся при создании и больше не меняется. Разводит продукты
`Services/Companies/CompanyKindGuard`:
- маршруты записи отвечают магазину 409;
- маршруты заказов не видят салон.

Аккаунт общий, у двух линеек тарифов (`SubscriptionPlanConfig.Line`) раздельные подписки и лимиты.

**Процесс в фоне один.** Фоновые задачи крутит `ScheduledTaskRunner` (hosted service) внутри API. Очередей и брокеров
нет, отдельного worker-процесса нет.

---

## 3. Модель данных (✔ по `AppDbContext` и `Entities/`)

62 `DbSet` плюс таблицы Identity (`AspNetUsers` = `AppUser`, роли, клеймы). Миграций **78** (по `*.Designer.cs`),
последняя — `20260930004926_Cycle25OrdersInsightsMax`. **Циклы 26, 27, 29, 30 и 31 миграций не добавляли** (✔ на
`364cc2b`). Цикл 31 сущностей не менял. В enum `CatalogListingCheckCode` (`Services/Shops/CatalogListingRules.cs`, в БД
не хранится) в конец добавлено значение `SalonBlocked`, его отдаёт только чек-лист салона.

**Ядро и пользователи.**
- `AppUser` — Identity-пользователь. Добавлены `FirstName`, `LastName`, `AvatarUrl`, `DeletedAtUtc` (надгробие после
  удаления аккаунта).
- Роли — `Client`, `Master`, `CompanyOwner`, `SuperAdmin`. `Master`/`CompanyOwner` пересчитывает только
  `IdentityRoleSync` из членств.
- `City` — справочник: `TimeZoneId`, `SearchName`, `Region`.

**Компании (общие для двух продуктов).**
- `Company` содержит:
  - `Kind`, `Slug`, `Name`, `Description`, `LogoUrl`;
  - `Address` — свободный текст, город туда не входит;
  - `Phone`, `Email`, `CityId`;
  - `TimeZoneId` + `TimeZoneIsManual`;
  - `OwnerUserId` + `BillingAccountId` (NOT NULL на уровне БД, CLR-тип `Guid?`);
  - `YandexMapsUrl`, `TwoGisUrl`;
  - флаги `AllowSelfBooking`, `RequirePrepayment`, `ShowInPublicListing`, `IsActive`;
  - `BookingHorizonDays`, `ClientRescheduleMinHours`;
  - 5 теневых колонок `Address*`.
- `CompanyMember` — роль в компании, `CommissionPercent`, `ProvidesServices`. `CompanyOwnerChangeLog` — журнал смены
  владельца.
- `CompanyPhoto` — галерея, до 10 фото. С цикла 26 она есть **и у магазина**.

**Запись на услуги (ezbook).**
- `Service`, `MasterService`.
- Расписание: `WorkingHours` (по датам), `ScheduleBreak`, `WeeklyScheduleTemplate`.
- `Booking` хранит:
  - гостевые поля;
  - снимки цены и комиссии;
  - согласия;
  - поля «записал за другого» и подтверждения законного представителя;
  - `Status` (`Pending/Confirmed/Cancelled/Completed/NoShow`) и `PaymentStatus` (`NotRequired/Pending/Paid`).
- `BookingService` — состав визита со снимками. `BookingEvent` — журнал изменений записи (append-only).
- Отзывы (`Review`), карточка клиента: `ClientNote`, `ClientNotePhoto` (приватное хранилище), `ClientHealthNote`
  (только при письменном согласии). `MailLog` — журнал «рассылок».

**Заказы (goods).**
- `ShopSettings` (1:1 с компанией) хранит:
  - режим покупателя и режим приёма;
  - отмену покупателем и учёт остатков;
  - часы работы (JSON);
  - пауз/стоп;
  - ASAP/предзаказ, шаг слотов, глубину предзаказа, минимальное время приготовления;
  - push/мессенджер покупателю, `StaffMaxEnabled`;
  - реквизиты продавца.
- Каталог: `ProductCategory`, `Product` (штучный или весовой, `AvailableWeekdaysMask`, `StockOnHand`, мягкое удаление).
  `ShopDailyMenu` + `ShopDailyMenuItem` — меню на дату, `ShopSpecialDay` — особые дни.
- `Order` хранит:
  - номер в пределах дня выдачи;
  - `PickupKind`, `PickupDate`/`StartUtc`/`EndUtc`;
  - `PublicToken`, `Status`;
  - `Version` — оптимистичная блокировка;
  - данные покупателя;
  - снимки правил магазина;
  - суммы: оценка и итог.

  Статусы: `New/Accepted/Ready/Issued/Rejected/CancelledByCustomer/CancelledByShop/NotPickedUp`.
- `OrderItem` — снимки. `OrderEvent` — журнал. `OrderDailyCounter` — нумерация.
- `OrdersSubscription`, `OrderMonthlyUsage` — тариф и счётчик линейки «Заказы». Системный бесплатный тариф линейки —
  константа `OrdersFreePlan.SeedId` (строку сеет миграция цикла 24).
- Push покупателю: `OrderPushSubscription`, `CustomerOrderPushNotification`. `ShopCustomerNote` — заметка о покупателе
  (ключ — канонический телефон).
- MAX персоналу: `StaffMaxLink`, `StaffMaxLinkSession`, `StaffMaxMessage`.

**Биллинг.**
- `BillingAccount` — владелец подписки. Хранит заявку на смену тарифа (поля `Requested*`, отдельной сущности нет),
  состояние триала `Trial*`, реквизиты оператора согласия.
- `SubscriptionPlanConfig` — тариф: `Line`, лимиты, флаги `Allow*`, `IsSystemFree`/`IsSystemTrial`, `IsPublic`.
- `AccountSubscription`, `SubscriptionOption`, `PlanOptionRule`, `AccountSubscriptionOption`, `SubscriptionChangeLog`.
- `TrialGrant`, `TrialPhoneRegistration` (HMAC телефона).

**Уведомления.**
- `NotificationChannel` — канал владельца, секрет провайдера зашифрован.
- `ChannelCompanyAssignment`, `ChannelStateEvent`, `ChannelPaymentLog`.
- `OutboundNotification` — очередь сообщений по записям и заказам.
- `CompanyNotificationSettings`, `NotificationTemplate` + `History`, `NotificationOptOut`.
- `PlatformSetting` + `ChangeLog` — настройки платформы, которые правит суперадмин.
- Push персоналу: `PushSubscription`, `StaffPushNotification`.

**Права и правовое.**
- `ConsentRecord` — журнал согласий.
- `SubjectRequest` — обращения субъектов ПДн.
- Подтверждение телефона: `PhoneVerificationSession`, `VerifiedPhone`.
- `GuestDataGateEvent` — журнал гейта гостевых данных, без IP и телефона.
- `PlatformNotice` + `Acknowledgement` — уведомления платформы с подтверждением прочтения.
- `ScheduledTaskState` — состояние фоновых задач.

**Инварианты, которые держит схема или код (не нарушать):**
- **Append-only enum'ы**, значения которых зашиты в SQL-фильтры индексов или битовые маски: `NotificationStatus`,
  `NotificationType` (биты `EnabledTypeMask`), `NotificationTransport`, `BookingEventKind`/`BookingActorKind`.
  Новые значения — только в конец.
- Пять колонок `Companies.AddressVerifiedInputKey/AddressVerifiedAt/AddressPrecision/AddressLatitude/AddressLongitude` —
  теневые свойства EF. CLR-свойствами их не делать, `DropColumn` не генерировать (C19-7).
- Правило «один канал на транспорт» держит уникальный индекс `(CompanyId, Transport)` и составной FK
  `(Id, BillingAccountId)`.
- Миграция `Cycle20PurgeHealthNotesWithoutWrittenConsent` необратима (`Down` пуст). `ResetUnverifiedPhoneNumberConfirmedMirror`
  необратима. У `Cycle24OrdersTimeNotifyTariffs` `Down` может упасть на дублях номеров (C24-10).
- Сидинг:
  - роли и суперадмин — на старте из конфига;
  - справочник городов, каталог опций и системный тариф «Заказы · Бесплатно» — миграциями;
  - **тариф «Триал» не сидируется** (C18-1).

---

## 4. Реальные API-эндпоинты (✔ по атрибутам контроллеров)

**266 маршрутов в контроллерах плюс 2 health (`/api/health/live`, `/api/health/ready`) — итого 268** (✔ на `364cc2b`;
цикл 31 добавил 2). Эталон со всеми атрибутами — `ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt` (268 строк: авторизация, rate limit, фильтры
вроде `RequiresOwnerTerms`, параметры). Его сверяет тест `Cycle22RouteTableTests`: новый маршрут без правки эталона
роняет тест. Эталон — первоисточник, список ниже — его человекочитаемая сводка.

Обозначения: `anon` — атрибута авторизации нет (часть методов сама проверяет пользователя или токен); `auth` —
`[Authorize]`; `SA` — только `SuperAdmin`. `{id}` везде — GUID.

**Аутентификация и профиль**
- `POST /api/auth/register`, `POST /api/auth/login` — anon, лимиты `auth-register`/`auth-login`.
- `GET|PUT /api/profile`, `GET /api/profile/export`, `POST /api/profile/{change-password,change-phone,avatar}`,
  `GET /api/profile/delete-account/preview`, `POST /api/profile/delete-account` — auth.
- `GET|POST /api/profile/consents`, `POST /api/profile/consents/revoke`, `GET /api/profile/consents/revoke-preview` — auth.
- `GET /api/phone-verification/config`, `POST /api/phone-verification/sessions`,
  `GET|DELETE /api/phone-verification/sessions/{sessionId}`, `POST /api/phone-verification/max/webhook/{token}` — anon.
- `POST /api/subject-requests` — anon: обращение субъекта ПДн.

**Компании (общие для салона и магазина)**
- anon: `GET /api/companies` (каталог), `GET /api/companies/public` (пагинированный каталог с городом и поиском),
  `GET /api/companies/{slug}` (публичная карточка; **отдаёт DTO и для магазина**, C25-10), `GET /api/companies/{id}/masters`,
  `GET /api/companies/{id}/photos`.
- auth: `GET /api/companies/{my,member,kinds-summary}`, `POST /api/companies`, `PUT /api/companies/{id}`,
  `POST /api/companies/{id}/logo`, `GET /api/companies/{id}/{photo-usage,stats}`, `PUT /api/companies/{id}/address`,
  `POST /api/companies/address/notice`.
- Галерея — auth: `POST /api/companies/{id}/photos` (лимит `company-photos`, 20/мин на пользователя),
  `DELETE /api/companies/{id}/photos/{photoId}` (`?reason=DepictedPersonRequest` только для SA) и
  `PUT /api/companies/{id}/photos/order` (оба — лимит `company-photos-edit`, 60/мин). До цикла 31 все три были на общем
  `uploads` (10/мин). Отдельного маршрута «сделать обложкой» нет: обложка — первая позиция в `photos/order`.
- **Блок «Каталог ezbook.ru» салона (цикл 31)** — auth: `GET|PUT /api/companies/{id}/catalog-listing`
  (`CompanyCatalogListingController`; `PUT` с `[RequiresOwnerTerms]`, лимита частоты нет). Отвечает тем же
  `CatalogListingDto`, что маршрут магазина. Чужая или несуществующая компания → 404 (владелец или SA через
  `CompanyAccess.CanManageCompanyAsync`), магазин → 409 строкой (`CompanyKindGuard.RejectShop`), `PUT` без `showInCatalog`
  → 400 `SalonListingRules.MissingValueText`, включение на тарифе без показа → 409 JSON
  `{code: CatalogListingNotAllowedByPlan, message}`. Подробно — §5.8.
- Сотрудники — auth: `GET|POST /api/Companies/{id}/members`,
  `PUT /api/Companies/{id}/members/{memberId}/{provides-services,services,commission}`, `DELETE /api/Companies/{id}/members/{memberId}`.
- Push персоналу — auth: `GET|PUT /api/companies/{companyId}/staff-push-settings`.

**Запись на услуги (ezbook)**
- `GET /api/Bookings/{slots,availability}` — anon, лимит `availability`. `GET /api/Bookings/occupied` — auth.
- `POST /api/bookings` — anon/auth, лимит `booking-create`, капча для анонима.
- auth: `GET /api/bookings/{id}`, `GET /api/bookings/{id}/history`, `GET /api/bookings/client`,
  `PATCH /api/bookings/{id}/{reschedule,cancel}`.
- `GET /api/bookings/master` — Master, CompanyOwner.
- `PATCH /api/bookings/{id}/{complete,mark-paid,noshow}` — Master, CompanyOwner, SA.
- `GET /api/services` — anon. `POST /api/services`, `PUT|DELETE /api/services/{id}`, `POST /api/services/{id}/image` — auth.
- auth: `GET|PUT|DELETE /api/workinghours[/{id}]`, `GET|PUT /api/schedule-template`, `POST /api/schedule-template/apply`.
- Мастер и клиенты — auth:
  - `GET /api/masters/clients`, `POST /api/masters/clients/notes`, `DELETE /api/masters/clients/notes/{id}`;
  - `POST /api/client-notes/{noteId}/photos`, `GET /api/client-notes/photos/{id}[/thumb]`, `DELETE /api/client-notes/photos/{id}`;
  - `GET|POST …/clients/{clientKey}/photo-consent`, `GET|PUT|DELETE …/clients/{clientKey}/health-note`,
    `GET …/health-consent-form`, `POST …/health-written-consent`, `POST …/health-written-consent/revoke`,
    `POST …/health-consent` — последний отвечает **410**. Префикс у всех — `/api/companies/{companyId}`.
- Отзывы: `POST /api/reviews`, `GET /api/reviews/can-review` — auth; `GET /api/reviews/{companyId}/reviews` — anon.
- Отчёты: `GET /api/reports/masters` — CompanyOwner, SA.
- «Рассылка»: `POST|GET /api/companies/{id}/mail` — auth. Это **заглушка**, §5.3.

**Уведомления салона (WhatsApp/MAX)**
- auth:
  - `GET|PUT /api/companies/{companyId}/notification-settings`, `GET /api/companies/{companyId}/notification-templates`,
    `PUT …/notification-templates/{type}`, `POST …/notification-templates/{type}/preview`;
  - `GET /api/companies/{companyId}/notifications[/summary]`.
- Каналы — auth:
  - `GET|POST /api/notification-channels`, `GET /api/notification-channels/offer`, `GET|DELETE /api/notification-channels/{id}`;
  - `POST …/{id}/{accept-risk,connect,test-message,replace,companies}`, `GET …/{id}/qr`, `DELETE …/{id}/companies/{companyId}`.
- `GET|PUT /api/notifications/preferences` — auth. `GET|POST /api/notifications/unsubscribe/{token}`,
  `POST /api/notifications/provider-webhook/{token}`, `POST /api/notifications/provider-webhook/{transport}/{token}` — anon.
- Web Push — auth: `GET /api/push/config`, `GET|POST /api/push/subscriptions`, `DELETE /api/push/subscriptions/{current|{id}}`.

**Магазин (goods) — кабинет, всё `auth`, права внутри через `ShopAccess`**
- `POST /api/shops`, `GET /api/shops/my`, `GET /api/shops/slug-check`, `GET /api/shops/{shopId}`,
  `PUT /api/shops/{shopId}/{settings,seller,slug}`, `GET|PUT /api/shops/{shopId}/catalog-listing`, `GET /api/shops/{shopId}/qr`.
- Каталог:
  - `GET|POST …/categories`, `PUT|DELETE …/categories/{categoryId}`, `PUT …/categories/{categoryId}/weekdays`, `PUT …/category-order`;
  - `GET|POST …/products`, `PUT|DELETE …/products/{productId}`, `POST|DELETE …/products/{productId}/image`,
    `PUT …/products/{productId}/{sold-out,stock}`, `PUT …/product-order`.
- Меню на дату: `GET …/daily-menus`, `GET|PUT|DELETE …/daily-menus/{date}`, `POST …/daily-menus/{date}/copy`.
- Время и приём:
  - `GET|PUT …/working-hours`, `GET …/special-days`, `PUT|DELETE …/special-days/{date}`;
  - `PUT …/{pickup-settings,acceptance}`, `GET …/{ordering-status,pickup-slots}`.
- Заказы:
  - `GET …/order-board`, `GET …/orders/{orderId}`;
  - `POST …/orders/{orderId}/{accept,reject,ready,not-picked-up,cancel,issue-quote,issue}`;
  - `PUT …/orders/{orderId}/{items,pickup}`.
- Отчёты: `POST …/order-history` (фильтры в теле, чтобы телефон не попадал в URL), `GET …/{summary,picklist}`.
- Покупатель: `GET /api/shops/{shopId}/customers/{customerRef}`, `GET|PUT …/customers/{customerRef}/note`. `customerRef` —
  Id заказа.
- `GET|PUT /api/shops/{shopId}/notification-settings`.
- `GET /api/staff-max`, `POST /api/staff-max/link-sessions`, `DELETE /api/staff-max/link`.

**Магазин (goods) — покупатель, anon**
- `GET /api/goods/catalog` — каталог города, кеш 30 с. С цикла 29 у элемента есть `logoUrl` (`string | null`).
- `GET /api/storefront/{slug}`, `GET /api/storefront/{slug}/pickup-slots`, `POST /api/storefront/{slug}/{quote,orders}`.
- `GET /api/orders/public/{token}`, `POST /api/orders/public/{token}/cancel`,
  `POST /api/orders/public/{token}/push-subscription[/remove]`.
- `GET /api/orders/my` — auth.

**Биллинг владельца (auth)**
- `GET /api/billing/subscription`, `POST|DELETE /api/billing/subscription/request`.
- `GET|POST /api/billing/trial`, `POST /api/billing/trial/terms-acknowledgement`.
- `GET|PUT /api/billing/operator-details`.
- `GET /api/pricing` — anon, витрина тарифов.

**Правовое**
- anon: `GET /api/legal/documents[/{type}]`, `GET /api/legal/texts/{key}`.
- auth: `GET /api/legal/consent-status`, `POST /api/legal/accept`, `GET /api/legal/notices`,
  `POST /api/legal/notices/{id}/acknowledge`, `GET /api/legal/notices/{id}/attachment`.
- `GET /api/cities` — anon.

**Админка (SA)**
- Пользователи и компании:
  - `GET /api/admin/{stats,users,companies,bookings}`, `PUT /api/admin/users/{id}/roles`, `PUT /api/admin/companies/{id}[/owner]`;
  - `GET /api/admin/owners/{ownerUserId}/subscription[-history]`;
  - `GET /api/admin/companies/{companyId}/{transfer/preview,owner-history}`, `POST …/transfer`.
- Биллинг:
  - `GET /api/admin/billing-accounts[/{accountId}]`, `PUT …/{accountId}/subscription`, `GET …/{accountId}/subscription-history`;
  - `POST …/{accountId}/trial[/regrant]`, `GET /api/admin/trial-terms/{version}`;
  - `GET /api/admin/subscription-requests`, `POST …/{id}/reject`, `GET /api/admin/subscription-change-reasons`.
- Тарифы и опции:
  - `GET|POST /api/admin/plans`, `PUT|DELETE /api/admin/plans/{id}`, `PUT …/{id}/{system-free,system-trial}`;
  - `GET|POST /api/admin/options`, `PUT|DELETE /api/admin/options/{id}`, `GET /api/admin/option-capabilities`;
  - `GET /api/admin/pricing/preview`.
- Каналы: `GET /api/admin/notification-channels[/summary]`, `POST …/{id}/{suspend,resume}`.
- Платформа:
  - `GET /api/admin/scheduled-tasks`, `GET|PUT /api/admin/platform-settings`;
  - `GET /api/admin/retention/policy`, `GET /api/admin/guest-data-gate-events`.
- Права и уведомления:
  - `GET|POST /api/admin/subject-requests`, `POST …/{id}/status`;
  - `GET /api/admin/legal/readiness`;
  - `GET|POST /api/admin/notices`, `POST /api/admin/notices/preview`, `POST …/{id}/revoke`, `GET …/{id}/attachment`;
  - `GET /api/admin/phone-verification/diagnostics`.

**Сквозное поведение API** (✔ `Startup/ApiExtensions.cs`):
- Осознанные 4xx — **голая строка `text/plain` по-русски**. Автовалидация модели приводится к той же форме
  (`ModelValidationErrorFormatter`), `SuppressMapClientErrors = true` — у `NotFound()`/`Conflict()` пустое тело, а не
  ProblemDetails.
- Исключения: массив Identity при регистрации и JSON-коды у отдельных маршрутов цикла 25
  (например, 409 `CatalogListingNotAllowedByPlan`).
- Необработанное исключение вне Development — `application/problem+json` с `traceId`.
- Enum'ы в JSON — строками (`JsonStringEnumConverter`). Частичные обновления различают «не прислали» и «null» через
  `Optional<T>`.
- Коды по смыслу: `402` — упёрлись в тариф, `451` — нужно принять новую редакцию документов (`LegalConsentFilter`,
  глобальный фильтр с allow-list), `429` — лимиты. Лимиты — только именованные политики и `[EnableRateLimiting]`
  (42 места в контроллерах, ✔ на `364cc2b`; из них `uploads` — 5, логотип, аватар, услуги, товары, заметки), глобального
  лимитера нет. Значения политик — `appsettings.json` → `RateLimits:<политика>`, в `appsettings.Testing.json` у всех
  10000.
- CORS — `AllowedOrigins`. Forwarded headers доверяют только `ForwardedHeaders:TrustedNetworks` (один хоп nginx).

**Маршруты фронтенда** (✔ `App.tsx`, `GoodsApp.tsx`):
- ezbook: `/`, `/login`, `/register`, `/company/:slug`, `/embed/:slug` (виджет без навбара), `/my-bookings`,
  `/my-visits`, `/cabinet`, `/dashboard`, `/owner`, `/owner/company/:id`, `/admin`, `/billing`, `/pricing`, `/profile`,
  `/profile/consents`, `/profile/delete`, `/notices`, `/u/:token` (отписка),
  `/companies/:companyId/clients/:clientKey/health-consent-form`, правовые алиасы (`/privacy`, `/terms`,
  `/terms-owner`, `/pdn-consent`, `/channel-risk`, `/offer-channel`, `/payment-terms`, `/data-request`).
- goods:
  - `/`, `/city/:cityId` (каталог), `/:slug` (витрина), `/o/:token` (заказ), `/orders`, `/login`, `/register`,
    `/profile[/consents]`, `/notices`, те же правовые алиасы;
  - кабинет: `/cabinet`, `/cabinet/new`, `/cabinet/devices`, `/cabinet/subscription`,
    `/cabinet/:shopId/{orders,catalog,menu,hours,settings,notifications,staff,link,history,summary,picklist}`,
    `/cabinet/:shopId/customers/:customerRef`.
- Карта маршрутов goods и зарезервированные слаги — единый источник `contracts/cycle23/goods-routes.json`. Его читают и
  бэкенд (embedded resource, `SlugPolicy`), и тест фронта.

---

## 5. Что реализовано и в каком состоянии

### 5.1 Работает и выпущено (🖥 по состоянию на 30.09.2026)
Ezbook.ru выкачен вручную кнопкой `deploy-staging.yml` из `develop`: последний подтверждённый выкат — `7117660`
(цикл 20, 30.09.2026). Машина одна, она же «стенд». Данные на ней тестовые: 4 пользователя, 2 компании. Там работает
запись на услуги:
- регистрация и вход по телефону;
- компании, услуги, сотрудники, расписание по датам и шаблоны;
- слоты, гостевая и авторизованная запись, перенос и отмена по правилам сервера;
- журнал изменений записи, отзывы, база клиентов с заметками и фото, отчёты, админка;
- биллинг-аккаунты и тарифы с ручным назначением;
- правовой контур: согласия, 451-гейт, выгрузка и удаление аккаунта, обращения субъектов, сроки хранения, уведомления
  платформы;
- подтверждение телефона ботом MAX;
- виджет `/embed/:slug`.

Опубликован правовой комплект редакции `2026-09-30` (`isDraft: false`). Retention работает в боевом режиме
(`RETENTION_DRY_RUN=false`). Бэкап — systemd-таймер. Состояние выката циклов 21–26 из репозитория не видно: по
прежней редакции деплой `4739e0b` (цикл 25) на машину был запущен, результат не зафиксирован (C25-11). В README goods
назван «пока не выкачено».

### 5.2 Код есть целиком, но выключен переключателем (✔ дефолты; 🖥 бой)
- **Уведомления клиентам салонов в WhatsApp/MAX через GREEN-API.** Каналы, шаблоны, очередь, диспетчер, вебхуки, отписка —
  всё есть. На бою `logging`. Живого вызова GREEN-API не делал ни один тест и ни один человек, партнёрского договора нет
  (C-13, C20-3).
- **Web Push** персоналу ezbook/goods и покупателям goods. Есть VAPID, service worker'ы `frontend/public/sw.js` и
  `frontend/goods/public/sw.js`, диспетчеры на полосе `realtime`. На бою `logging`. Проверки на реальных устройствах не
  было (C24-2).
- **MAX персоналу goods** — `STAFFMAX_ENABLED=false`, живого смоука не было (C25-1).
- **Пробный тариф («Триал»).** Код активации, фоновая задача `trial-lifecycle` и снапшоты есть, но тарифа в каталоге нет:
  его надо создать руками (C18-1).

### 5.3 Заглушки и неполные места (✔)
- **Рассылка клиентам салона** (`MailingController.SendMail`). Проверяет права и тариф (`AllowMailing`), собирает e-mail
  клиентов компании, пишет `MailLog` и отвечает «Рассылка поставлена в очередь». **Никакой отправки нет**: ни очереди, ни
  SMTP.
- **Онлайн-предоплата.** Флаг есть, платёжного шлюза нет. `PaymentStatus.Pending` снимает только персонал отметкой «оплачено».
- **Шаг сетки слотов** захардкожен: `SlotCalculator.StepMinutes = 30` (§9.22).
- **Тексты правовых уведомлений goods** не написаны юристом. Ключи `OrderCheckoutNotice`, `OrderMessengerConsent`,
  `OrderPreorderNotice`, `ShopCustomerNoteNotice` вне `LegalTextKey.All`, фронт показывает fallback.
  `SellerInfoRequirements` пуст. `Retention:OrderPersonalDataDays = 0`, так что ПДн заказов не удаляются никогда
  (C23-2, C24-3, C25-3).
- **Галерея магазина** (цикл 26): загрузка, удаление и порядок открыты, но `GET /api/companies/{id}/photo-usage` для магазина
  по-прежнему отвечает 409 — счётчик квоты фото у магазина не показывается.
- **Приложение № 2** (`legal-drafts/13-payment-terms.html`) написано, но в манифест намеренно не включено.
- Маркеров TODO/FIXME/HACK/`NotImplementedException` в коде (`*.cs`, `frontend/src`, `frontend/goods/src`) **нет ни
  одного** ✔. Незавершённость видна по переключателям и fallback'ам, а не по комментариям.

### 5.4 Мёртвый и исторический код (✔)
- 5 теневых колонок `Companies.Address*` от удалённого геокодера (C19-7). Строки опций-лимитов `extra-*` (`IsActive=false`)
  и их правила остаются в БД. Ограждение — `RetiredLimitOptions`/`.WhereNotRetired()`.
- `frontend/src/types/api-cycle*.generated.ts` для циклов 7, 9, 14, 18, 19, 20, 23, 24, 25, 26, 29, 31 — сверочные генераты
  контрактов. Часть из них ни один модуль не импортирует, их держит только CI-сверка. Прикладные типы — рукописные
  `src/types/index.ts` и `goods/src/types.ts`.
- `openapi-cycle6.yaml` в корне и `contracts/cycle{10,11,15,17}/openapi.yaml` — исторические контракты, в CI не линтуются.
- Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` — это документы **цикла 3** (последняя правка — 2026-09-17), а не текущая
  архитектура.
- ↪ C22-1: восемь навигационных свойств EF без ссылок в коде.

### 5.5 Что сделали последние циклы

**Цикл 31 — общий блок фото с мультизагрузкой, блок «Каталог ezbook.ru» у салона, компактная шапка карточки**
(✔ `git diff c19a83c..364cc2b`, влит мерджем `364cc2b`). Без миграций и без новых пакетов. Добавлены 2 маршрута
(`GET|PUT /api/companies/{id}/catalog-listing`) и 2 политики частоты для галереи. Подробно — §5.8.

**Фикс goods мимо циклов после цикла 31 (✔ `c7a4ace`, мердж `49f0d60`):** `goods/src/hooks/useNewOrderSound.ts` —
выбор «звук включён» хранится в `localStorage` под ключом `goods.newOrderSound` (`'1'` или ключа нет; ошибки хранилища
глотаются). После перезагрузки `initialState()` создаёт `AudioContext` в состоянии suspended, состояние хука — `blocked`,
а первое `pointerdown`/`touchend`/`click`/`keydown` на странице (слушатели с `capture`) делает `resume()`. `disable()`
удаляет ключ. Тестов на хук по-прежнему нет.

**Фиксы goods мимо циклов (✔, влиты в `develop` между циклами 30 и 31):**
- `53def4f` — у страницы магазина `StorefrontPage` на телефоне боковые поля 16 → 12 px;
- `9659742` — компактнее чипы даты и времени (`pickup/RadioChips.tsx`, `storefront/PickupPicker.tsx`);
- `474f8be` — вкладки кабинета магазина (`cabinet/ShopLayout.tsx`) больше не сбрасывают горизонтальную прокрутку.
  Error boundary в `GoodsApp` с `key=pathname` пересоздаёт layout на каждый клик, поэтому позиция хранится в
  переменной уровня модуля `tabsScrollLeft` и восстанавливается callback-ref'ом. Тестов на это нет.

**Цикл 30 — блок «Для покупателей» и скриншоты на главной goods** — влит в `develop` мерджем `e1d7cdb`. Подробно — §5.7.

**Цикл 29 — «Доделки цикла 26» (✔ `git diff b9c2a79..c19a83c`, влит в `develop`).** Без миграций и без новых маршрутов.
- API:
  - `GoodsCatalogShopDto` (`GET /api/goods/catalog`) — в конец добавлен `LogoUrl` (пустой → `null`), он же хранится в
    кеше каталога (`GoodsCatalogService.CatalogEntry`), поэтому новый логотип появляется не позже чем через 30 с;
  - `MalformedKeyGuardValueProviderFactory` оборачивает `JQueryFormValueProviderFactory`: незакрытая `[` в имени поля
    multipart-формы даёт 400 `text/plain` с текстом `RequestTexts.MalformedFormFieldName` вместо 500
    (`ModelValidationErrorFormatter`). Ветка для строки запроса — «защита на будущее», на net8 не срабатывает;
  - `CompanyDtoAssembler.GetPhotosOrderedAsync` удалён, публичная карточка салона берёт галерею из
    `CompanyPhotoQueries.OrderedAsync` — этим закрыт C26-4.
- Фронт: `components/company/CompanyLogoMark.tsx` (логотип или первая буква, `utils/companyInitial.ts`) в карточке
  компании, в каталоге goods (`CatalogHomePage` → `ShopRow`) и в каталоге ezbook (`HomePage`); галерея и ссылки на карты
  во встраиваемой форме `/embed/:slug`; настройки салона `CompanyManagePage` разбиты на группы полей; генерат
  `api-cycle29.generated.ts`, goods берёт типы каталога из него.
- Контракты: `contracts/cycle29/openapi.yaml` (+ `openapi.json`), `contracts/cycle26/openapi.json`. CI теперь линтует
  контракты 26 и 29 и сверяет генераты 26 и 29 — **C26-1 закрыт**.
- Тесты: функциональные `Cycle29QaTests.cs`, `Cycle29ContractTests.cs` (сверка ответов с OpenAPI через новый
  валидатор подмножества `ServiceBooking.Tests/Infrastructure/OpenApiContract.cs`, самопроверка —
  `OpenApiContractValidatorTests.cs`), юнит `MalformedKeyGuardValueProviderFactoryTests`, vitest V29-*.
- Документы: `ARCHITECTURE_CYCLE29.md`, `API_CONTRACT_CYCLE29.md` (нумерация §29.x — по номеру цикла, не сквозная),
  спека — `SPEC_CYCLE29_CYCLE26_FOLLOWUPS.md`, CHANGELOG — раздел «Не выпущено — цикл 29». Ручные кейсы 360 px M29-01…09
  выполнены частично (TEST_CATALOG, раздел цикла 29).

**После `1837373` — только документы (✔ `git diff 1837373..b9c2a79`).** `4737b2f` — полное пересканирование этого файла.
`b9c2a79` — в `CHANGELOG.md` добавлен раздел «Не выпущено — цикл 26: одна карточка компании…» (строка 34, под
разделом цикла 27), в `README.md` цикл 26 описан в блоках goods и ezbook и в «Чего пока нет». Этим закрыт C26-6.

**Цикл 27 — блок «Для бизнеса» на главной goods (✔ `git diff 491406c..1837373`).** Затронут только фронт goods: бэкенд,
контракты и миграции не менялись (`API_CONTRACT_CYCLE27.md` §553: «API не меняется»).
- `frontend/goods/src/components/BusinessBlock.tsx` переписан. Блок рендерится последним внутри `CatalogHomePage` на `/`
  и `/city/:cityId`. В нём:
  - `h2#biz-title` «Магазин и кафе принимают заказы без звонков и переписок»;
  - абзац;
  - список из 4 преимуществ;
  - кнопки «Подключить магазин» (`/cabinet/new`, аноним идёт через `/register?returnTo=%2Fcabinet%2Fnew`) и
    «Войти в кабинет» (`/cabinet`);
  - панель `bg-cream-deep` с `h3#biz-steps-title` и тремя шагами в стиле секции `#how` ezbook (круг с `Icon`, без номеров).
- Тексты зашиты в JSX. i18n и CMS нет.
- Тесты: `BusinessBlock.test.tsx` (T27-01…10) и `BusinessBlock.qa.test.tsx` (QA27-01…09). Ручные кейсы M27-01…05 —
  в `TEST_CATALOG.md`.
- Документы:
  - `SPEC.md` теперь спека цикла 27, спека цикла 26 заархивирована как `SPEC_CYCLE26_COMPANY_CARD_UNIFIED.md`;
  - `ARCHITECTURE_CYCLE27.md` (§543–§552) и `API_CONTRACT_CYCLE27.md` (§553) — **номера § совпадают с номерами цикла 26**
    (C27-1);
  - CHANGELOG — верхний раздел «Не выпущено — … цикл 27».
- Каталог магазинов на главной goods — сетка карточек (`aae5b44`, ветка `fix/goods-catalog-cards-sound`, влита мимо
  цикла агентов). Звук нового заказа держится на уровне модуля (`useNewOrderSound`). Тестов на `CatalogHomePage` и на
  хук нет.

**Цикл 26 — «Единая карточка компании» (✔ `git diff 0d41df4..491406c`).**
Без миграций и без новых зависимостей. **Новых маршрутов нет, изменилось поведение трёх.**
- Галерея `POST/DELETE/PUT order /api/companies/{id}/photos` открыта и магазину: `CompanyKindGuard` в этих трёх методах
  снят. Тексты лимита и перестановки зависят от вида компании (`CompanyPhotoTexts`: «салона»/«магазина»).
  `photo-usage` и остальные салонные маршруты для магазина по-прежнему 409.
- В `StorefrontDto` в конец добавлены `Email` (пробельный — `null`) и `Photos`. У неактивного магазина `Photos` пуст.
- В `ShopManageDto` в конец добавлены `CityRegion`, `UtcOffsetMinutes`, `TimeZoneChangeAllowed`, `TimeZoneChangeLockedText`.
- `PUT /api/companies/{id}` для магазина (`Kind=Orders`):
  - часовой пояс задаётся только городом, ручной `timeZoneId` ≠ поясу города → 400 «Часовой пояс магазина задаётся
    городом»;
  - у магазина **с заказами** смена города на пояс с другим смещением UTC → 409 (`ShopTimeZoneChangePolicy`), на пояс с
    тем же смещением → 200;
  - салон работает по-старому (`CompanyTimeZoneResolver.ForUpdate`).
- `CompanyPhotoQueries.OrderedAsync` — общий упорядоченный запрос галереи для `GET …/photos` и витрины.
- Фронт:
  - общий `frontend/src/components/company/CompanyCard.tsx` — шапку карточки используют `CompanyPage` (ezbook) и
    `StorefrontPage` (goods);
  - `utils/publicAddress.ts` — склейка «город, адрес»;
  - единый `telHref` в `utils/phone.ts`, goods-копия `dial.ts` удалена;
  - `ShopProfileSection` в настройках goods — одно сохранение, город и пояс, ссылки карт, e-mail;
  - галерея магазина в настройках через общий `CompanyPhotosSection` с пропсами `kind`/`onChanged`;
  - генерат `src/types/api-cycle26.generated.ts`.
- `491406c` — починка сборки: `CompanyCard.tsx` использует относительные импорты, потому что в `vite.config.ts` ezbook
  **нет** алиаса `@` (см. §6.2).
- Тесты: функциональные `Cycle26CompanyCardTests.cs` (`CY26-`, 24 теста), юнит `CompanyPhotoTextsTests`,
  `ShopTimeZoneChangePolicyTests`, vitest `ShopProfileSection.test.tsx`, `publicAddress.test.ts` и правки соседних тестов.
- **Что цикл 26 сам зафиксировал как несделанное** (✔ по CHANGELOG, раздел цикла 26, «Чего этот цикл не даёт»; в коде
  подтверждается отсутствием соответствующих компонентов/полей, отдельно не перепроверялось каждое):
  - кабинет салона не перекомпонован по образцу `ShopProfileSection` (было необязательной задачей, отложено);
  - нет фото и логотипов в карточках каталогов ezbook и goods, предпросмотра карточки в кабинете, модерации и подписей
    к фото, тарифного лимита на галерею магазина (см. C26-5); сотрудник магазина галереей не управляет;
  - во встраиваемой форме салона (`/embed/:slug`) нет фото и ссылок на карты;
  - вёрстка на узком экране (360 px) и наезд логотипа в браузере не проверялись — сквозного браузерного набора нет;
  - контрактная проверка по `contracts/cycle26` в финальном прогоне QA не запускалась (и в CI её нет — C26-1).
- Документация цикла 26 дописана отдельным коммитом `b9c2a79` уже после влития: раздел в CHANGELOG и абзацы README.

### 5.6 Фоновые задачи (✔ `ApplicationServicesExtensions`, `appsettings.json`)
Десять `IScheduledTask`, регистрируются поимённо:

| Задача | Полоса | Что делает |
|---|---|---|
| `photo-retention-cleanup` | main | удаляет фото заметок по сроку тарифа |
| `notification-dispatch` | main | отправляет очередь WhatsApp/MAX |
| `channel-health` | main | проверяет каналы и простой |
| `max-webhook-renew` | main | продлевает подписку вебхука бота MAX |
| `subject-request-due-soon` | main | напоминает оператору о сроке обращения |
| `trial-lifecycle` | main | ведёт окно рассылок, пороги и переход «триал → Free». **Секции в `appsettings.json` нет** |
| `data-retention` | main | 27 правил retention. В репозитории `DryRun=true`, на бою 🖥 `false` |
| `staff-push-dispatch` | realtime | push персоналу, период 5 с |
| `customer-order-push-dispatch` | realtime | push покупателям, период 5 с |
| `staff-max-dispatch` | realtime | MAX персоналу, период 5 с |

Тик `main` — 10 с, тик `realtime` — 1 с. В окружении `Testing` все задачи выключены (`appsettings.Testing.json`).

### 5.7 Главная goods, путь покупателя, доска заказов, изображения во фронте — цикл 30 (✔ по коду на `aeed251`)
Раздел описывает состояние **после цикла 30** («блок „Для покупателей“ и настоящие скриншоты на главной goods»). Ветка
`cycle/030-user-section-screenshots` влита в `develop` мерджем `e1d7cdb`. Документы цикла: спека в архиве
`SPEC_CYCLE30_USER_SECTION_SCREENSHOTS.md`, `ARCHITECTURE_CYCLE30.md` (§30.0–§30.15), `API_CONTRACT_CYCLE30.md`
(§30.20–§30.27; «API не меняется», только формы файлов засева и манифеста). Бэкенд, миграции, маршруты и OpenAPI цикл 30
не менял ✔. После `aeed251` в зоне этого раздела менялся только `cabinet/ShopLayout.tsx` (фикс `474f8be`, §5.5),
описанное ниже осталось в силе.

**Главная goods** — `frontend/goods/src/pages/CatalogHomePage.tsx`, маршруты `/` и `/city/:cityId` (`GoodsApp.tsx`).
Порядок внутри `<main class="max-w-[1180px] …">`:
1. шапка без картинки: eyebrow «Заказы с самовывозом», `h1` «Где заказать в вашем городе», строка «Выберите магазин,
   соберите заказ и заберите его в удобное время.», под ней **ссылка-якорь «Как сделать заказ» → `#buyers-title`**
   (цикл 30);
2. `form role="search"`: город (`CityCombobox`), поиск, «Открыто сейчас», ссылка «Поделиться»;
3. `section#shop-list` (`aria-label="Магазины"`, `tabIndex=-1`, `scroll-mt-24`) — сетка карточек магазинов с
   логотипом (`CompanyLogoMark`, цикл 29) и пагинацией;
4. `<BuyersBlock />` — **новый блок «Для покупателей»** (цикл 30);
5. `<BusinessBlock />` (цикл 27) — последний элемент `main`, с цикла 30 со скриншотом доски.

**Блок «Для покупателей»** — `frontend/goods/src/components/BuyersBlock.tsx` (90 строк). Разметка — копия
`BusinessBlock` (решение A2, паритет классов держит тест T30-13, общего компонента разметки нет). Тексты — константы
`steps`/`track` в файле (вариант A из `ARCHITECTURE_CYCLE30.md §30.3`), i18n нет:
- `section[aria-labelledby=buyers-title]`, eyebrow «Для покупателей», `h2#buyers-title` (`tabIndex=-1`, цель якоря)
  «Соберите заказ с телефона и заберите, когда он готов», абзац;
- кнопки: «Выбрать магазин» — `<a href="#shop-list">`, «Мои заказы» — `Link` на `/orders` (аноним уходит на логин
  через защиту маршрута);
- панель `bg-cream-deep` с `h3#buyers-steps-title` «Как сделать заказ» и `ol` из трёх шагов с `Icon` (store,
  shopping-bag, clock);
- `h3#buyers-track-title` «Как следить за заказом» — список из 5 пунктов; **пункт 3 обещает уведомление о смене статуса
  (push)** — см. §9.1, R-F; строка «Оплата — при получении в магазине.»;
- рядом — скриншот страницы заказа (`ScreenshotFigure` с `orderPageShot`).

**`ScreenshotFigure`** — `goods/src/components/ScreenshotFigure.tsx`: `<figure><picture>` с необязательными `<source
type="image/webp" media=…>`, `<img loading="lazy" decoding="async" width height>` и `figcaption`. `width`/`height` —
CSS-размер 1x-кадра из манифеста (без сдвига вёрстки). Используется в обоих блоках.

**Руководства покупателю в `docs/` по-прежнему нет** (§10.2) — описание шагов есть только в самом блоке.
На ezbook образец шагов — секция `#how` в `frontend/src/pages/HomePage.tsx`.

**Путь покупателя, который можно описывать (по коду):**
- витрина `/:slug` (`StorefrontPage`) → корзина (`useCart`, хранится в браузере) → оформление на одном экране с выбором
  времени получения (гость — имя, телефон и капча; вошедший — номер аккаунта);
- страница заказа `/o/:token` (`frontend/goods/src/pages/OrderPage.tsx`, 283 строки), вход не нужен: крупный номер
  заказа (`h1`, `data-testid="order-number"`), `OrderStatusBadge large`, `OrderTimeline` (шаги из `order.timeline`,
  их собирает сервер), состав заказа, изменения магазина «было → стало», контакты и ссылки на карты
  (`CompanyMapLinks`), карточка подписки на push (`OrderPushCard`). Опрос каждые 10 с, пока статус не финальный
  (`isTerminalStatus`), в скрытой вкладке опрос на паузе;
- `/orders` (`MyOrdersPage`, только вошедшим) — активные заказы сверху, затем завершённые за 30 дней.

**Статусы заказа** — `ServiceBooking.Core/Enums/OrderStatus.cs`: `New`, `Accepted`, `Ready`, `Issued`, `Rejected`,
`CancelledByCustomer`, `CancelledByShop`, `NotPickedUp`. Русский текст статуса (`statusText`) присылает сервер. Цвета
плашек — `goods/src/components/OrderStatusBadge.tsx`.

**Доска заказов персонала** — `/cabinet/:shopId/orders`, `frontend/goods/src/pages/cabinet/OrdersScreenPage.tsx`
(354 строки). Три колонки «Новые» / «Принятые» / «Готовы к выдаче» (`md:grid-cols-3`), на телефоне — вкладки
(`role="tablist"`). Карточка — `components/orders/OrderCard.tsx`. Данные — `useOrderBoardPolling` (свой worker-таймер
`hooks/boardTimer.worker.ts`), звук нового заказа — `useNewOrderSound`, экран не гаснет — `useWakeLock`. Под колонками — секция
«Завершённые сегодня» (`h2`). В API и в `StartupSeedingExtensions` демо-данных для доски по-прежнему нет (фикстуры
`components/orders/testData.ts` — только для тестов). Демо-данные goods теперь создаёт **внешний скрипт засева цикла 30**
через HTTP API и только в локальном стенде `sb-shots` (ниже). Витринные и демо-данные `origin/cycle/028-…` — только для
«Записи» (услуги).

**Изображения во фронте:**
- ezbook: одна растровая картинка — `frontend/src/assets/salon-hero.jpg`, hero ezbook (`HomePage.tsx`, блок
  `hidden md:block aspect-[4/5]`, на телефоне не показывается). ⚠️ Файл по содержимому — **PNG** 941×1672 размером
  ~1,7 МБ, несмотря на расширение `.jpg` (коммит `4a3f469`). Цикл 30 его не трогал;
- goods (цикл 30): своя папка **`frontend/goods/src/assets/screenshots/`** — 5 WebP-кадров и манифест:
  - `order-page-1x.webp` / `order-page-2x.webp` (страница заказа `/o/:token`, 390×509 CSS, ~10/21 КБ) — в `BuyersBlock`;
  - `board-desktop-1x.webp` / `board-desktop-2x.webp` (доска `/cabinet/:shopId/orders`, 1280×820 CSS, ~47/102 КБ) и
    `board-phone-2x.webp` (390×900 CSS, ~47 КБ) — в `BusinessBlock`, один `<picture>`: `<source media="(min-width:
    768px)">` отдаёт десктопный кадр, `<img>` — телефонный, грузится ровно один;
  - `screenshots.json` — манифест (схема `contracts/cycle30/screenshots-manifest.schema.json`): `capturedAt`,
    `sourceCommit`, `browser`, CSS-размеры, имена и вес файлов, `orderNumber`/`pickupClock`/`statusText` для alt. Текущие
    кадры сняты 2026-09-30 с `sourceCommit: "6ea8d4c-dirty"` (снимали с незакоммиченными правками);
  - `shots.ts` импортирует кадры и манифест и собирает пропсы `orderPageShot`/`boardShot`; `MD_MEDIA` дублирует
    брейкпоинт Tailwind `md` (менять вместе с `tailwind.goods.config.js`); alt строится из манифеста;
- конвейера оптимизации картинок в сборке по-прежнему нет (плагинов в `package.json` нет): WebP кодирует Chrome при
  съёмке, Vite отдаёт файлы как есть с хешем в имени. Бюджет веса (2x: заказ ≤ 120 КБ, доска-десктоп ≤ 200 КБ,
  доска-телефон ≤ 120 КБ, заказ + доска ≤ 450 КБ) проверяет тест T30-16 и сам скрипт съёмки;
- `srcSet` и `loading="lazy"` — у `ScreenshotFigure`, галереи компании (`CompanyPhotoGallery.tsx`) и `ui/AuthedImage.tsx`.

**Скрипты засева и съёмки (цикл 30)** — `frontend/scripts/screenshots/`, в сборку, тесты CI и образ не входят:
- `stack.sh up|down|reset` — корневой `docker-compose.yml` как отдельный compose-проект **`sb-shots`** (свой том,
  API `:55000`, Postgres `:55432`, переопределяются `SB_API_PORT`/`SB_DB_PORT`), `ASPNETCORE_ENVIRONMENT=Development`.
  `reset` = `down -v` + `up -d --build postgres api` — пересборка образа API. Для colima сам выставляет `DOCKER_HOST` и
  `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE`. Обычный dev-стек не затрагивается;
- `seed-goods-demo.mjs` (`npm run shots:seed`) + `demo-data.mjs` (выдуманные владелец «Елена Демидова» `+79000000000`,
  «Пекарня на Садовой», категории и товары, телефоны `+7 900 000-00-xx`). Ходит **только через существующий HTTP API**:
  регистрация владельца, `POST /api/shops`, настройки, часы, самовывоз, приём заказов, категории и товары, гостевые заказы
  через витрину, перевод части заказов в «Принят»/«Готов». Замки до первой записи: хост из белого списка (`localhost`,
  `127.0.0.1`, `::1` или `SHOTS_ALLOW_HOST`), `*.ezbook.ru` — отказ всегда; `GET /swagger/index.html` = 200 **и** в теле
  Swagger UI, а не SPA-фолбэк (`isSwaggerUiHtml`), иначе код 3. Затем ожидание `/api/health/ready`. Состояние (пароль
  владельца, id магазина, публичный токен заказа) пишется в `scripts/screenshots/.state/seed.json` (схема
  `contracts/cycle30/seed-state.schema.json`), `.state/` закрыт `.gitignore`. Съёмка должна идти днём по времени
  магазина (07:00–20:30) и не позже ~55 мин после засева;
- `capture-goods-screenshots.mjs` (`npm run shots:capture`) — `playwright-core` + установленный Google Chrome
  (`SHOTS_CHROME_PATH`), сам поднимает Vite goods на `:55174` (или `SHOTS_WEB_URL`), снимает `/o/:token` и доску, проверяет
  запретные строки в кадре (`localhost`, «Ссылка на этот заказ», «Не удалось», …; `SHOTS_FORBID_EXTRA`), бюджет веса и
  размеры, затем пишет 5 WebP и `screenshots.json`. Коды выхода 0–5 — в README и `API_CONTRACT_CYCLE30.md §30.26`;
- **как переснять** — `frontend/scripts/screenshots/README.md`: когда (правки `OrderPage`, `OrderTimeline`,
  `OrderStatusBadge`, `GoodsNavbar`, `OrdersScreenPage`, `ShopLayout`, `OrderCard`, палитры Tailwind), что нужно
  (Docker/colima, Node ≥ 20, Chrome, интернет для Google Fonts, чистое дерево — иначе `sourceCommit` с `-dirty`), шаги
  `stack.sh reset` → `shots:seed` → `shots:capture` → осмотр кадров → vitest → `ajv-cli` по схеме → коммит →
  `stack.sh down`, ручной запасной путь через DevTools. Замер FE-30-4: около 20 минут ушло на сборку образа API в
  `stack.sh reset` на загруженной colima, съёмка — 42 с; ориентир на свободной машине — до 15 минут. Автоматической
  проверки свежести кадров нет (устаревание — риск R-D).

**Типизация скриптов и node-тестов (цикл 30).** Браузерные `tsconfig.json` и `tsconfig.goods.json` получили
`"types": ["vite/client"]` (без `node` и без `vitest/globals`, `aeed251`) и исключают три файла, которым нужны
node-типы: `shots.test.ts`, `seed-goods-demo.test.ts`, `BuyersBlock.qa.test.tsx`. Их проверяет отдельный
`frontend/tsconfig.node.json` (`types: ["node", "vite/client"]`) — скрипт `npm run typecheck:node`, в CI — шаг
«Type-check node (shots)» после `tsc` для goods. Сами `.mjs`-скрипты `tsc` не проверяет.

**Тесты главной goods (vitest):**
- `BuyersBlock.test.tsx` (T30-01…13, включая паритет классов с `BusinessBlock` T30-13), `BuyersBlock.qa.test.tsx`
  (QA30-01…10, написаны QA по SPEC: тексты дословно, иерархия заголовков, запретные слова, alt/width/height/lazy,
  `<picture>` с `md`-источником, бюджет веса);
- `BusinessBlock.screenshot.test.tsx` (T30-14: кадр доски в «Для бизнеса»), `assets/screenshots/shots.test.ts` (T30-15…17:
  форма манифеста и наличие файлов, WebP и бюджет веса, пиксели = CSS × плотность), `seed-goods-demo.test.ts` (замки
  хоста, `isSwaggerUiHtml`, окно съёмки, пароль);
- **`CatalogHomePage.test.tsx` теперь есть**: V29-32 (логотип в `ShopRow`, цикл 29), T30-18 (порядок: магазины →
  «Для покупателей» → «Для бизнеса», ссылка «Как сделать заказ» до формы поиска), T30-19 (блоки остаются при ошибке
  каталога);
- T27/QA27 (`BusinessBlock.test.tsx`, `BusinessBlock.qa.test.tsx`) не менялись. `OrderPage.test.tsx`,
  `OrdersScreenPage.test.tsx`, `OrderCard.test.tsx` покрывают снятые экраны, но **не** сверяют их с кадрами.
- Ручные кейсы M30-01…09 — `TEST_CATALOG.md`, раздел цикла 30; в `ARCHITECTURE_CYCLE30.md §30.14` их 12 (M30-01…12),
  нумерация в двух местах не совпадает (§9.1).

### 5.8 Галерея фото, блок «Каталог», шапка карточки компании — цикл 31 (✔ по коду на `364cc2b`, ничего не запускалось)
Цикл 31: «Общий блок фото, мультизагрузка, блок „Каталог“ у салона, компактная шапка карточки». Документы: корневой
`SPEC.md` (US-31-01…08, T-31-01…04, Q-31-1…10), `ARCHITECTURE_CYCLE31.md` (§31.0–§31.18), `API_CONTRACT_CYCLE31.md`
(§31.20–§31.29), `contracts/cycle31/openapi.yaml` + `openapi.json`. Миграций, новых пакетов и переменных окружения нет.
Два новых ключа `RateLimits` имеют значения по умолчанию в коде. Ниже — факты по коду. Там, где код отличается от
архитектуры, это сказано явно.

**Сервер: одно правило видимости салона (R-3).**
- `Services/Companies/SalonListingRules.cs` — чистое правило (`SalonListingInput(IsActive, AllowedByPlan,
  ShowInPublicListing)` → `CatalogListingResult`). Видим ⇔ активен ∧ тариф разрешает ∧ владелец включил показ. Пункты
  чек-листа по порядку:
  1. `SalonBlocked` «Салон заблокирован администратором» — только если салон не активен, никогда не `done`;
  2. `NotAllowedByPlan` — только если тариф не разрешает;
  3. `HiddenByOwner` — всегда, `done = ShowInPublicListing`.

  Тексты пунктов 2–3 — это константы `CatalogListingRules`, одна копия на оба продукта. Свои константы: статусы
  «Салон виден в каталоге ezbook.ru» / «Салона сейчас нет в каталоге», подсказка про тариф и `MissingValueText`.
- `Services/Companies/SalonListingQuery.cs` — SQL-двойник правила: `VisibleInSalonCatalog(db, nowUtc)` =
  `Kind = Services ∧ IsActive ∧ ShowInPublicListing` + `PublicListingQuery.WhereAllowsPublicListing`. Его вызывают **оба**
  каталога: `CompaniesController.GetAll` (`GET /api/companies`) и `GetPublic` (`GET /api/companies/public`). В `GetAll`
  тарифный фильтр переехал из памяти (`plans[c.Id].AllowPublicListing`) в SQL. Что правило и запрос согласованы, проверяет
  только матричный функциональный тест CY31-05 (комментарий ⚠️ в коде).
- `Controllers/CompanyCatalogListingController.cs` — `GET|PUT api/companies/{id:guid}/catalog-listing`, порядок проверок
  и коды — в §4. Тариф считается через `SubscriptionResolver.GetEffectivePlanAsync`, в `PUT` один раз (`5aca4a4`). Advisory
  lock нет: пишется одна колонка `Company.ShowInPublicListing`. `PUT /api/companies/{id}` поле `showInPublicListing`
  по-прежнему принимает (совместимость, CY31-13). `CompanyKindGuard`: маршрут вписан в комментарий закрытого списка
  салонных маршрутов.
- `CatalogListingRules.HiddenByOwnerText` теперь «Показ включен в настройках» (было «Показ выключен в настройках»). Текст
  описывает условие и одинаков при `done = true/false`. Это меняет вывод и у магазина (`GET /api/shops/{id}/catalog-listing`).

**Сервер: лимиты частоты галереи (R-1).** `Startup/RateLimitingExtensions.cs` — политики `company-photos` (20/мин на
пользователя, `POST …/photos`) и `company-photos-edit` (60/мин, `DELETE …/photos/{photoId}` и `PUT …/photos/order`).
Значения лежат в `appsettings.json` → `RateLimits`, в `appsettings.Testing.json` — 10000. `uploads` (10/мин) остался у
логотипа, аватара, услуг, товаров и фото заметок. Текст 429 у галереи прежний: «Too many uploads. Try again in a minute.».
Явной ветки в `OnRejected`, которую просила архитектура §31.6.1, **нет**: галерея попадает в ветку `_` по умолчанию, в
коде об этом только комментарий. Суммарный бюджет обработки изображений на пользователя вырос: 10 → 10 + 20 загрузок в
минуту. Сервер порядок новых фото не менял: `Position = count` под `pg_advisory_xact_lock`. Порядок держит клиент,
который отправляет файлы строго по одному.

**Фронт: общий блок галереи** — `frontend/src/components/company/CompanyPhotosSection.tsx` (398 строк; переехал из
`src/pages/owner/` через `git mv`, реэкспорта со старого пути нет). Его используют ezbook `CompanyManagePage` → `SettingsTab`
и goods `cabinet/SettingsPage.tsx` (через `@/components/company/…`).
- Сетка `grid-cols-2 sm:grid-cols-3`. Панель кнопок на плитке видна всегда, кроме устройств с hover и точным указателем
  (`[@media(hover:hover)_and_(pointer:fine)]:opacity-0`). Там она показывается по `group-hover` и `group-focus-within`.
  Кнопка «Сделать обложкой» до `md` — иконка с `aria-label`, с `md` — текст.
- **Мультизагрузка:** `<input type="file" multiple accept="image/jpeg,image/png,image/webp">` и перетаскивание.
  Кнопка «Выбрать фото» и зона выбора неактивны, пока грузится сама галерея (`503cfc2`). Логика разнесена так:
  - `src/utils/photoBatch.ts`, чистые функции: `PHOTO_MAX_PHOTOS = 10`, `PHOTO_MAX_BYTES = 5 МБ`, `planPhotoBatch`
    (сначала отсев по типу и размеру, потом обрезка по остатку мест), `photoRejectText`, `overLimitText`,
    `isTransientUploadError` (429, нет ответа, 5xx);
  - `src/components/company/usePhotoBatchUpload.ts`, хук очереди: цикл `for … of` с `await`, **строго по одному** файлу
    в порядке выбора. Ошибка одного файла не останавливает остальные. После размонтирования следующий файл не стартует.
    `retryFailed` повторяет только транзиентные ошибки;
  - `companyPhotosApi.upload(companyId, file, onProgress?)` — необязательный колбэк прогресса через axios
    `onUploadProgress`.

  Каждое загруженное фото сразу кладётся в кэш `['company-photos', id]` (upsert по `id`). Инвалидация — один раз на
  пакет. Под зоной выбора — панель пакета: `role="status"` «Загружено N из M», список файлов со статусом и процентом,
  одно сообщение о файлах сверх лимита, кнопки «Повторить неудавшиеся» и «Скрыть». Превью выбранных файлов нет.
  Отменить пакет нельзя.
- Лимит 10 фото по-прежнему продублирован: фронт — `PHOTO_MAX_PHOTOS` (в компоненте ещё локальный алиас `MAX_PHOTOS`),
  сервер — `CompanyPhotoOrdering.MaxPhotosPerCompany`.

**Фронт: сборка стилей goods видит общие файлы (T-31-01).** `frontend/goods-shared-sources.js` задаёт такие списки:
- `GOODS_SHARED_EZBOOK_PAGES` — 8 ezbook-страниц, которые можно импортировать из goods: `LegalDocumentPage`,
  `SubjectRequestPage`, `ConsentsPage`, `LoginPage`, `RegisterPage`, `NoticesPage`, `BillingPage`,
  `owner/NotificationsSection`. `owner/CompanyPhotosSection` из списка убран;
- `GOODS_SCANNED_EZBOOK_DIRS` — `components`, `utils`, `hooks`;
- `GOODS_MARKUP_FREE_EZBOOK_DIRS` — `api`, `store`, `types` (в них не должно быть `.tsx`);
- `GOODS_SHARED_EZBOOK_FILES` — `queryClient.ts`, `pages/billingPageHelpers.ts`.

Из этих списков генерируются `content` в `tailwind.goods.config.js` (`goodsTailwindContent()`) и регэксп
`no-restricted-imports` в `eslint.config.js` (`goodsAllowedPagesRegex()`). Попутно в сборку стилей goods попали
`NoticesPage` (C31-5) и `src/utils`, `src/hooks`. Guard-тест `frontend/goods/src/sharedSources.guard.test.ts` (vitest,
`node:fs`) обходит граф импортов goods (`@/`, `@goods/`, относительные, динамические) и проверяет:
- каждый достижимый файл из `frontend/src/` попадает под одно из правил списка;
- каждая страница из списка существует;
- в каталогах «без разметки» нет `.tsx`;
- `content` конфига глубоко равен `goodsTailwindContent()`, то есть конфиг не правили руками.

Это единственная автоматическая защита от класса дефектов C31-1: сам вид плитки проверяется только вручную (M31).

**Фронт: блок «Каталог».**
- `src/components/company/CatalogListingCard.tsx` — чистая вёрстка на пропсах (`title`, `switchLabel`, `headingId`,
  `headingAs` `h2|h3`, `data: CatalogListingView`, `isLoading`, `loadError`, `onRetry`, `saving`, `saveError`, `onToggle`).
  Разметка и `data-testid` перенесены из goods (`listing-status`, `listing-check`, `listing-not-allowed`, `role="switch"`).
  Переключатель контролируемый, всегда показывает серверное значение, оптимистичного обновления нет. Загрузку и ошибку
  рисуют примитивы `src/components/ui`, goods-`StatePanels` не используется.
- Обёртки над ним:
  - goods `goods/src/components/CatalogListingSection.tsx` («Каталог goods.ezbook.ru», `h2`);
  - ezbook `src/pages/owner/SalonCatalogListingSection.tsx` («Каталог ezbook.ru», `h3`, «Показывать салон в каталоге
    ezbook.ru»): ключ `['company-catalog-listing', companyId]`, `retry: false`, после PUT — `setQueryData` и
    инвалидация `['my-companies']`. API — `src/api/companyCatalogListing.ts`, тип — `SalonCatalogListingDto` из генерата
    `api-cycle31.generated.ts` (экспорт в `src/types/index.ts`), ошибки — `src/utils/catalogListingError.ts`.
- `SettingsTab` (`CompanyManagePage.tsx`): из формы убраны чекбокс «Показывать компанию в общем списке» и поле
  `showInPublicListing` (оно больше не уходит в `PUT /api/companies/{id}`). Блок каталога стоит отдельной карточкой
  сразу после основной формы, перед галереей. При создании компании (`CabinetPage`) чекбокс остался.

**Фронт: шапка `CompanyCard`** (`src/components/company/CompanyCard.tsx`, 144 строки). Её используют `CompanyPage`
(ezbook) и `StorefrontPage` (goods).
- Шапка свёрстана CSS-сеткой. Порядок в DOM: логотип → `h1` → описание → слот `children` → действия → сведения. Третья
  колонка на `≥ sm` появляется только при непустом слоте: `children={false}` обёртку слота не создаёт.
- `data-testid`:
  - `company-card-actions` — телефон `tel:` и ссылки карт в одной строке, есть только при телефоне или ссылке карт;
  - `company-card-meta` — адрес и e-mail, есть только при адресе или e-mail;
  - `company-card-slot`.
- Классы ссылок действий — общая константа `COMPANY_ACTION_LINK_CLASS`, у телефона без ссылки —
  `COMPANY_ACTION_STATIC_CLASS` (`companyActionLink.ts`, высота 44 px, фокус-обводка). У `CompanyMapLinks` новый
  необязательный проп `className`: шапка передаёт `contents`, `EmbedPage` и `OrderPage` не передают ничего.
- `CompanyCardSkeleton` повторяет ту же сетку.

**Тесты цикла 31** (подробно — `TEST_CATALOG.md`, раздел «Цикл 31», строка ~6417):
- функциональные:
  - `Cycle31CatalogListingTests.cs` — CY31-01…14, в том числе матрица CY31-05 и совместимость CY31-13;
  - `Cycle31GalleryRateLimitTests.cs` — CY31-20…25: свой хост `ProdLimitsHost` с продовыми лимитами 10/20/60 через
    `builder.UseSetting`. `RateLimitTestFactory`, вопреки архитектуре, не менялся. CY31-25 — галерея магазина, этого
    кейса в архитектуре не было;
  - в эталоне маршрутов +2 строки и новые политики у трёх строк галереи;
- **`Cycle31ContractConformanceTests.cs` (CY31-30…34 из архитектуры) не написан.** Ни один функциональный тест не
  читает `contracts/cycle31/openapi.json`: `OpenApiContract.Load` вызывается только для `cycle26` и `cycle29`. В
  `TEST_CATALOG.md` это записано как «отложено»;
- юнит: `SalonListingRulesTests.cs` (таблица истинности 8 строк), правка `CatalogListingRulesTests.cs`;
- vitest: `photoBatch.test.ts`, `CompanyPhotosSection.test.tsx` (переехал, 374 строки), `CatalogListingCard.test.tsx`,
  `SalonCatalogListingSection.test.tsx`, `CompanyCard.test.tsx` (новый), `catalogListingError.test.ts`,
  `sharedSources.guard.test.ts`, правки `CompanyManagePage.test.tsx`, `CompanyPage.test.tsx`;
- ручные M31-01…10 (плитка на 1280 и 360 px, touch и клавиатура, пакет из 10 фото, ошибки пакета, блок каталога во всех
  состояниях, шапка `/myasnoy` и `/company/:slug`, `/embed/:slug`) — **не выполнены**: вердикт в `TEST_CATALOG.md` —
  «НЕ ВЫПОЛНЕНО QA», у QA-агента не было браузера.

### 5.9 Настройки компании: goods против ezbook — фактическая раскладка (✔ по коду на `49f0d60`, ничего не запускалось)
Вход для цикла 32. Здесь описано, как устроено сейчас. Что менять, решают аналитик и архитектор.

**goods — `frontend/goods/src/pages/cabinet/SettingsPage.tsx` (234 строки), маршрут `/cabinet/:shopId/settings`.**
- Обёртка — `<main className="max-w-[760px] mx-auto px-4 sm:px-8 pt-8 flex flex-col gap-5">`, отступы между карточками
  задаёт `gap-5` контейнера. Порядок: `ShopProfileSection` (`key={shop.id}`) → `CompanyPhotosSection kind="shop"` →
  `RulesSection` (правила приёма заказов, `PUT /api/shops/{id}/settings`) → `CatalogListingSection` → `SellerSection`
  (реквизиты, `PUT /api/shops/{id}/seller`). `RulesSection` и `SellerSection` — локальные функции файла, у каждой своя
  кнопка «Сохранить …» и локальный `SavedNote` («Сохранено» на 2,5 с).
- **Блок основной информации — `frontend/goods/src/components/profile/ShopProfileSection.tsx` (376 строк)**, лежит в
  goods, а не в общем `src/components/company/`, и завязан на goods: тип `ShopManageDto`, `InlineError` из goods
  `StatePanels`, `getGoodsErrorMessage`/`httpStatus` из `goods/src/utils/orderError`, тексты «магазина»/«покупателей».
  Общие части берёт через `@/`: `companiesApi`, `companyAddressApi`, `ui/{Button,Card,CityCombobox,Input,Modal,PhoneInput}`,
  `company/PublicAddressNotice`, `utils/{companyManageError,mapLinksFieldError,timezone}`.
  - Разметка — одна `Card p-6`. Шапка: логотип 64×64 (или первая буква) рядом с `h2` «Профиль магазина» и кнопкой
    «Загрузить/Заменить логотип» (отдельный `POST …/logo`, сразу при выборе файла). Ниже одна `<form>`: название* и
    описание (textarea 2 строки) → сетка `sm:grid-cols-2` телефон + e-mail (с подсказкой) → `fieldset` «Адрес и карты» с
    сеткой город | адрес и сеткой Яндекс Карты | 2ГИС → **одна** кнопка «Сохранить».
  - Состояние локальное, инициализируется один раз (`useState`, `baseline` в `useRef`), без react-hook-form. Сохранение —
    цепочка: локальная проверка названия → если меняется UTC-смещение, модалка «Сменить город?» (или ошибка у поля
    города, если `timeZoneChangeAllowed = false`) → если изменён адрес, `PublicAddressNotice` → `PUT /api/companies/{id}`
    (name, description, phone, email; `cityId` — только если город сменился; ссылки карт — только изменённые, очищенная
    уходит как `""`) → при изменённом адресе `PUT /api/companies/{id}/address`. Ошибка адреса после успешного PUT —
    «Не удалось сохранить адрес…» у поля и «Остальные изменения сохранены». Ошибки 400/409 раскладываются по полям
    (город, ссылки карт). После успеха инвалидируются `['shop', id]`, `['my-shops']`, `['storefront']`.
  - Ручного часового пояса у магазина нет: пояс только от города (сервер это же требует, §5.5 цикл 26).
  - Тесты: `ShopProfileSection.test.tsx` (262 строки), `goods/src/pages/cabinet/SettingsPage.test.tsx`.

**ezbook — `SettingsTab` в `frontend/src/pages/owner/CompanyManagePage.tsx` (строки ~701–1002; файл 1 312 строк).**
Вкладка «Настройки» — пятая из пяти («Услуги», «Расписание», «Сотрудники», «Уведомления», «Настройки»). Страница —
`max-w-4xl mx-auto px-8 pt-11 pb-24` (у goods — 760 px и `px-4 sm:px-8`).
- `h2` «Настройки компании» над карточками. Первая `Card p-6`: сверху логотип 64×64 + кнопка (как у goods, но без
  заголовка рядом и без `alt` с видом компании), ниже `<form>` на **react-hook-form** (`values` из `['my-companies']`,
  `resetOptions.keepDirtyValues`, «есть правки» считается по `dirtyFields` — урок цикла 13). Группы `fieldset` с
  `legend` (цикл 29): «Основное» (название, описание 3 строки) → «Контакты» (телефон, e-mail; телефон — обычный `Input`,
  не `PhoneInput`) → «Адрес и карты» → «Запись» (горизонт записи 0–365, окно переноса/отмены 0–168 с юридической
  подписью `CANCEL_WINDOW_FIELD_CAPTION` — «не переписывать без legal-counsel», чекбоксы «Разрешить клиентам записываться
  самостоятельно» и «Требовать предоплату…», блокируются тарифом `planAllowsOnlineBooking`/`planAllowsOnlinePayment`).
  Внизу «Сохранено», ошибка и кнопка «Сохранить изменения» (`disabled={!isDirty}`).
- **Внутри группы «Адрес и карты» — три независимых сохранения в одной форме:**
  - `CompanyAddressField` (`src/components/company/CompanyAddressField.tsx`) — своя кнопка, `PublicAddressNotice`,
    `PUT …/address`;
  - `CityTimeZoneFields` (локальная функция, строки ~1014–1105) — `h3` «Город и часовой пояс», пояснение про
    напоминания, `CityCombobox`, чекбокс «Указать часовой пояс вручную (IANA…)» и поле IANA, своя кнопка «Сохранить»
    (`type="button"`, шлёт ровно `{cityId, timeZoneId}`; Enter гасится `swallowImplicitSubmit`);
  - ссылки карт — поля основной формы (уходят общим `PUT`, ошибка 400 раскладывается по полю через `mapLinksFieldError`).
  Сервер (`CompaniesController.Update`, строки ~344–400) при этом принимает `cityId`/`timeZoneId` в том же `PUT
  /api/companies/{id}`, что и остальные поля; для салона пояс считает `CompanyTimeZoneResolver.ForUpdate` (ручной пояс
  разрешён), блокировки смены пояса, как у магазина с заказами, у салона нет. Адрес — только отдельным маршрутом.
- После формы, в обёртке `<div>` **без** `gap`: `SalonCatalogListingSection` (`CatalogListingCard`, `h3` «Каталог
  ezbook.ru») → `CompanyPhotosSection` (`h3` «Фотографии салона») → `WidgetCard` («Виджет для сайта», `h2`,
  `mt-[18px]`) → `PhotoUsageCard` («Хранилище фото клиентов», `h2`, `mt-[18px]`; на ошибке не рендерится). У карточки
  каталога и галереи **внешнего отступа нет**, `Card` (`src/components/ui/Card.tsx`) своих отступов не добавляет — по
  коду они стоят вплотную к форме и друг к другу (глазами не проверялось). Уровни заголовков смешаны: `h2` у формы,
  виджета и хранилища, `h3` у каталога и галереи.
- Логотип-заглушка и ошибки: `getLogoErrorMessage`, `getCompanyManageErrorMessage` (`src/utils/companyManageError`);
  ошибки выводятся `<p className="text-danger">`, не `InlineError` (он в goods).
- Тесты: `CompanyManagePage.test.tsx` (337 строк) — блоки `SettingsTab` циклов 13, 17/20, 29, 31 (V29-01…06: поля в
  именованных группах, основное сохранение без `cityId/timeZoneId/address`, сохранение города шлёт ровно
  `{cityId, timeZoneId}`, Enter не сабмитит, ошибка Яндекс-ссылки у поля; C31: нет чекбокса каталога, `showInPublicListing`
  не уходит). Тесты завязаны на текущую раскладку и тексты меток — перекомпоновка их затронет.
  `CompanyAddressField.test.tsx`, `SalonCatalogListingSection.test.tsx`, `CompanyPhotosSection.test.tsx`,
  `NotificationSettingsTab.test.tsx` (импортирует из `CompanyManagePage`) — рядом.
- Пользовательская документация этой вкладки — `docs/owner.md` §5, отстаёт от кода (§10.2).

**Что общее уже есть:** `CompanyPhotosSection` (проп `kind`), `CatalogListingCard`, `PublicAddressNotice`,
`CityCombobox`, `PhoneInput`, `mapLinksFieldError`, `formatCityTimeZone`. Общего компонента «основная информация
компании» нет: у goods — `ShopProfileSection` (одна форма, одно сохранение), у ezbook — поля внутри `SettingsTab`
(react-hook-form + два отдельных сохранения для адреса и города). Типы разные: `ShopManageDto` (goods) и `Company`
(`src/types`, `['my-companies']`). goods-страницы могут импортировать ezbook-код только из списков
`frontend/goods-shared-sources.js` (§5.8); обратное направление — ezbook импортирует из `goods/` — в проекте не
используется.

---

## 6. Конвенции проекта (✔ выборочно по коду; им следовать, а не вводить рядом свои)

### 6.1 Бэкенд (C#)
- **Primary constructors** у контроллеров и сервисов, file-scoped namespaces. Полей `_db` нет.
- **DTO — позиционные `record`.** AutoMapper нет, маппинг ручной (`*Mapper`, `*Assembler`, `MapToDto`). Новое поле
  дописывается **в конец** record'а с дефолтом, чтобы позиционные вызовы продолжали компилироваться. Атрибуты валидации
  ставятся на **параметры** конструктора: `[property: Required]` даёт 500.
- **Чистые правила — статические классы без EF и HTTP** в `Services/<Домен>/`: `*Rules`, `*Policy`, `*Texts`,
  `*Calculator`, `*StateMachine`. Они и покрываются юнит-тестами. Контроллер держит права, транзакцию и advisory-lock.
  Поднимать `AppDbContext` в юнит-тестах в проекте не принято, логика с БД проверяется функционально. Если то же
  правило нужно и в SQL, рядом кладут «двойника» — `IQueryable`-расширение (`SalonListingQuery` при `SalonListingRules`,
  `PublicListingQuery`), а согласованность двух сторон доказывают матричным функциональным тестом (CY31-05).
- **Ошибки для пользователя — русский текст, который собирает сервер** (`*Texts.cs`: `ShopTexts`, `OrderTexts`,
  `BillingTexts`, `PhoneVerificationTexts`, `NotificationTexts`, `ChannelPresentation`…). Фронт показывает его дословно
  и формулировок не сочиняет. Старые английские сообщения массово не переводились.
- **Порядок проверок в эндпоинте:** сначала права, потом вид компании (`CompanyKindGuard`) и принадлежность. Не быть
  оракулом существования: «нет» и «не ваше» отвечают одинаково.
- **Доступ к компании** — только `Services/CompanyAccess.CanManageCompanyAsync(..., superAdminBypass)` и
  `Services/CompanyMembership`. К магазину — `Services/Shops/ShopAccess`/`ShopAccessResolver` (`ShopPermission`).
- **Конкурентность:** «посчитал → записал» идёт в транзакции через `AdvisoryLock.AcquireAsync(db, "<ключ>")`. Порядок
  ключей: аккаунт, потом компания. Заказ защищён ещё и `Order.Version`.
- **Единственные писатели:**
  - `BookingEventLog` и `OrderEventLog`/`OrderChangeLog` — журналы;
  - `IdentityRoleSync` — роли, вызывается после `SaveChanges` в той же транзакции;
  - `PhoneVerificationWriter` — подтверждённые телефоны;
  - `GuestDataGateJournal`;
  - `PlatformSettingsWriter`.
- **Телефон — только через `PhoneNormalizer`.** Сопоставление по телефону в контроллерах и сервисах идёт через
  `SubjectScopeResolver` с маркером `// SUBJECT-PHONE-GATE: …`. Это проверяет тест `SubjectPhoneGateInvariantTests`.
- **Файлы — только через `ImageUploadService`** (`ImageProfile`, тип по сигнатуре байт). Хранилища — `FileStorage`
  public/private, у них разные типы результата.
- **Фоновая работа** — только `IScheduledTask` плюс строка регистрации. **Правило retention** — файл в
  `Services/Retention/Rules/` плюс регистрация поимённо.
- **Пагинация** — `PagedResult<T>` + `Pagination.Normalize`.
- **Внешний сервис** оформляется так: своя секция конфигурации, `Provider` с заглушкой по умолчанию, падение на
  нераспознанном значении, проверка в `DeploymentSafetyChecks` (чистые статические методы с юнит-тестами). Секреты
  провайдеров хранятся через `SecretProtector`, секрет в пути запроса маскируется и в приложении, и в nginx.
- **Время, задержки и случайность** в диспетчерах — через `INotificationClock`, `IDispatchDelay`, `IPauseGenerator`.
- **Логи — Serilog.** Телефоны маскирует `LogMasking`/`PhoneMaskingEnricher`. Осознанные 4xx пишутся на уровне Information.
  Сигналы оператору идут через `IGlitchTipSignalService`, вне транзакции.
- **Миграции:** новую создают закреплённым `dotnet-ef` 8.0.11, влитую не редактируют. После мержа поверх чужих миграций
  пересобирают Designer-снимки своих: CI проверяет дрейф снапшота и монотонность Designer-файлов.
- **Комментарии — на английском, развёрнутые, объясняют «почему».** Они ссылаются на `ARCHITECTURE_CYCLE*.md §N`.
- Стиль — `.editorconfig` в корне. `dotnet format` в CI **нет**. Сборка в CI — `-warnaserror`.

### 6.2 Фронтенд (TypeScript/React, оба приложения)
- Функциональные компоненты и **именованные экспорты**. Default export есть только у `App.tsx`/`GoodsApp.tsx`.
- **HTTP — только через модули `src/api/<домен>.ts` и `goods/src/api/<домен>.ts`** поверх общего axios-инстанса
  `src/api/client.ts` (`baseURL: '/api'`, JWT из `authStore`, перехват 401/451). Модуль — объект с методами,
  возвращает `r.data`. Axios напрямую в компонентах не вызывается.
- **Серверное состояние — react-query** (`src/queryClient.ts`: `retry: 1`, `staleTime: 30 с`), ключи — массивы.
  Клиентское — zustand: `authStore` с persist, `legalStore`, `ownerGateStore`. goods своих глобальных сторов не заводит
  (корзина — хук `useCart`).
- **Формы:** react-hook-form, где полей много (6 файлов, в goods один). Остальное — `useState`.
- **Ошибки HTTP → текст** через мапперы `src/utils/*Error.ts` (24 файла) и `goods/src/utils/*Error.ts`. Серверную
  русскую строку показывают дословно.
- **Стили — только Tailwind-утилиты.** Палитра (`cream`, `ink`, `gold`, `line`…) в двух конфигах. UI-примитивы — в
  `src/components/ui/` (`Button`, `Input`, `Modal`, `Icon` — свой SVG-набор, `PhoneInput`, `Pagination`,
  `CityCombobox`…). Эмодзи не используются. Оверлеи закрываются через `useOverlayDismiss`.
- **Общий код двух сайтов живёт в `frontend/src`, goods импортирует его через алиас `@/`.** Обратное направление
  запрещено ESLint. **С цикла 31 разрешённый список ведётся в одном месте — `frontend/goods-shared-sources.js`** (§5.8).
  Сейчас это 8 страниц (`LegalDocumentPage`, `SubjectRequestPage`, `ConsentsPage`, `LoginPage`, `RegisterPage`,
  `NoticesPage`, `BillingPage`, `owner/NotificationsSection`), каталоги `src/components`, `src/utils`, `src/hooks`, «без
  разметки» `src/api`, `src/store`, `src/types` и отдельные файлы `queryClient.ts`, `pages/billingPageHelpers.ts`.
  Общий для двух сайтов компонент кладут в `src/components/**` (компании — в `src/components/company/`), а не в
  `src/pages/`. Новую общую страницу вписывают в этот модуль, а не руками в `tailwind.goods.config.js` или
  `eslint.config.js`: иначе упадёт guard-тест `goods/src/sharedSources.guard.test.ts`.
- ⚠️ **Внутри `frontend/src/**` импорты только относительные.** Алиас `@` объявлен в `tsconfig.json`, `vitest.config.ts`
  и `vite.goods.config.ts`, но **не в `vite.config.ts`**. Импорт через `@/` в `src/` проходит `tsc` и тесты, а
  `vite build` ezbook на нём падает (это и чинил `491406c`).
- **Типы:** прикладные — рукописные (`src/types/index.ts`, `goods/src/types.ts`). Для формы контракта цикла — генерат
  `npm run types:api:cycleNN` из `contracts/cycleNN/openapi.yaml`, руками его не правят. С цикла 31 есть и прецедент
  реэкспорта из генерата в `index.ts` (`SalonCatalogListingDto`).
- **Загрузка нескольких файлов** (прецедент цикла 31) делается без библиотек-аплоадеров: нативный `<input multiple>`,
  последовательная очередь (`usePhotoBatchUpload`), чистое правило отсева (`utils/photoBatch.ts`), прогресс через
  необязательный `onProgress` у метода api-модуля.
- Тесты лежат рядом с кодом (`*.test.ts(x)`), `globals: false`: `describe`/`it`/`expect` импортируются явно.
- **Типы окружения в tsconfig** (с цикла 30): браузерные `tsconfig.json`/`tsconfig.goods.json` — только
  `types: ["vite/client"]`; файл, которому нужны node-API (`fs`, `path`, импорт `.mjs`-скрипта), исключается из обоих и
  вписывается в `include` `tsconfig.node.json` (проверка — `npm run typecheck:node`). Node-типы в браузерный код не тянуть.
- **Картинки goods** — в `frontend/goods/src/assets/…`, импортом из исходников (хеш от Vite), WebP; скриншоты — только
  через `ScreenshotFigure` и манифест `screenshots.json`, размеры и alt не хардкодятся в JSX.
- Service worker'ы не должны ловить `fetch` и трогать Cache API — это проверяет CI.
- Язык интерфейса — русский. Даты — `date-fns` с локалью `ru` и общие утилиты `utils/dateFormat.ts`, деньги — `utils/money.ts`.
- Prettier: без `;`, одинарные кавычки, ширина 120, trailing comma `all`.

### 6.3 Правовые тексты
Правится только `legal-drafts/`, потом `dotnet run --project ServiceBooking.LegalKit -- build` и коммит обоих деревьев
вместе. Побайтную сверку делает CI (`LegalKit check`).
- Плейсхолдеры `{{ИМЯ}}` — по схеме `contracts/cycle11/legal-values.schema.json`.
- Значения, которые подставляются при показе, — атрибуты `data-legal-value|when|unless`.
- Ключ `LegalTextKey` вносится в `All` только после того, как появился текст: иначе fail-fast на старте.
- Живой манифест боя — `/opt/ezbook/app/legal/` на машине 🖥, а не git.

### 6.4 Тесты (функциональные)
- Изоляция — **тест-класс**: `IClassFixture<TestDatabaseFixture>` даёт свою базу, склонированную из шаблона прогона.
- Уникальные данные брать у фикстуры (`fixture.Data.Phone()/Email()/Slug()`). Хост настраивается только через
  `TestHostSettings.Apply`, новая фабрика — со своим `factoryTag`.
- `EnsureDeletedAsync` запрещён. Порядок тестов внутри класса случайный (`RandomTestCaseOrderer`, семя —
  `SERVICEBOOKING_TEST_ORDER_SEED`).
- Кейсы помечаются атрибутом `TestCase` с ID (`CY26-01`…) и описываются в `TEST_CATALOG.md`.
- Подсистеме с fail-closed по секрету нужен тестовый секрет в фабрике.

### 6.5 Git и процесс
- Модель веток: `cycle/NNN-<slug>` → `develop` → (`release-candidate` → `master` + тег для `deploy-production.yml`).
  **Фактически** выкатывается `develop` кнопкой `deploy-staging.yml`. `release-candidate` на origin нет, `master` не
  двигался с июля.
- Коммиты: conventional-стиль `feat(goods): …`/`fix(frontend): …`/`docs(state): …` с id задач (`BE-3`, `FE-2`, `T-25-01`),
  на английском. В документах бывает русский. Трейлер `Co-Authored-By`.
- Документы цикла: корневой `SPEC.md` — спека **текущего** цикла, прошлая архивируется **в корень** с суффиксом
  (`SPEC_CYCLE25_…md`). `ARCHITECTURE_CYCLENN.md`/`API_CONTRACT_CYCLENN.md` лежат в корне. **Папки `docs/history/` нет** —
  архив ведётся суффиксами в корне. Нумерация § в документах сквозная через циклы (цикл 26 — §543–§569), но с цикла 29
  документы нумеруются по номеру цикла (§29.x, §30.x, §31.x).

---

## 7. Тесты, CI и деплой (✔ по конфигам; ничего не запускалось)

### 7.1 Наборы тестов
| Набор | Фреймворк | Объём (✔ подсчёт атрибутов на `364cc2b`) | Последний известный прогон (↪ числа из отчёта цикла 31, переданы при постановке задачи; codebase-analyst сам не запускал) |
|---|---|---|---|
| Юнит бэкенда `ServiceBooking.UnitTests` | xUnit 2.5.3 + FluentAssertions 6.12; без БД и без Docker | 179 файлов, 1 596 `[Fact]/[Theory]` | **2 421** (цикл 30: 2 407) |
| Функциональные API `ServiceBooking.Tests` | xUnit 2.5.3 + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) + **реальный PostgreSQL 16** через Testcontainers 3.10 (или внешний сервер) | 85 файлов тестов, 1 103 `[Fact]/[Theory]` | **1 175** (цикл 30: 1 153) |
| Фронтенд (оба сайта) | Vitest 3.2 + jsdom + Testing Library | 168 файлов `*.test.ts(x)` | **1 255** (цикл 30: 1 194). Гонять с пустой `VITE_SMARTCAPTCHA_SITEKEY`, с локальным `.env` — 8 падений `CartPanel` (§7.2) |

Прогоны различаются по объёму: в прогоне считаются и наборы `[Theory]`, поэтому число прогонов больше числа атрибутов.
Были ли в отчёте цикла 31 зелёные `tsc`, `lint`, `typecheck:node`, `build:release` и сколько тестов упало, в этом
обновлении не проверялось: переданы только общие числа.

**Сквозного браузерного e2e-набора (Playwright, Cypress и т.п.) в проекте НЕТ** ✔. `playwright-core` появился в
devDependencies в цикле 30, но только для скрипта съёмки скриншотов (`frontend/scripts/screenshots/`, §5.7): тестов на
нём нет, в `test:run` и CI он не запускается. «Функциональные» здесь — HTTP-тесты
против хоста в памяти и настоящего Postgres. Ближе всего к e2e — bash-смоуки `deploy/ci/smoke.sh` (живой контейнер API:
health, регистрация, загрузка аватара) и `deploy/ci/smoke-frontend.sh` (собранный `dist`, профили ezbook/goods). Их
запускает CI, а не тест-раннер.

### 7.2 Команды для базового прогона — для qa-engineer (сам codebase-analyst их не запускает)
Базовый функциональный прогон:
```bash
dotnet test ServiceBooking.Tests
```
Предусловие — **запущенный Docker**. Прогон сам поднимает `postgres:16-alpine`, заводит базы `sbtest_<key>_<slot>` и
убирает их. Локальный параллелизм — 4 потока (`ServiceBooking.Tests/xunit.runner.json`), в CI — 2.

Полная последовательность, как в CI:
```bash
dotnet run --project ServiceBooking.TestKit -- doctor   # проверка среды без изменений (0 — ок, 2 — есть проблемы)
dotnet build ServiceBooking.sln -warnaserror
dotnet test ServiceBooking.UnitTests                    # быстрый, без Docker
dotnet test ServiceBooking.Tests                        # функциональный, нужен Docker
cd frontend && npm ci && npm run lint && npx tsc --noEmit && npx tsc --noEmit -p tsconfig.goods.json && npm run typecheck:node && npm run test:run
```
- Фронт-тесты гонять с пустой `VITE_SMARTCAPTCHA_SITEKEY` (как в CI): при локальном `frontend/.env` с ключом краснеют
  8 тестов `CartPanel` (ловушка ниже).
- Скрипты `shots:seed`/`shots:capture` и `stack.sh` — **не тесты**: они поднимают Docker-стенд и пишут в его базу;
  их запускает человек по `frontend/scripts/screenshots/README.md`, а не базовый прогон.
- **macOS + colima** (так на этой машине): перед прогоном выставить
  `DOCKER_HOST=unix://$HOME/.colima/default/docker.sock` и `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock`.
  Без второй переменной падает весь набор, и выглядит это как поломка кода (`docs/testing-isolation.md`, «macOS + colima»).
- Альтернатива — свой Postgres: `SERVICEBOOKING_TEST_CONNECTION='Host=…;Database=postgres;…'` (строка к **серверу**).
  ⚠️ Защита от разрушения работает по имени базы, а не по серверу: боевую строку сюда подставлять нельзя.
- Другие переменные: `SERVICEBOOKING_TEST_MAX_PARALLEL_THREADS`, `SERVICEBOOKING_TEST_RUN_KEY` (8 hex),
  `SERVICEBOOKING_TEST_NO_TEMPLATE`, `SERVICEBOOKING_TEST_ORDER_SEED`. Уборка: `dotnet run --project ServiceBooking.TestKit -- status | sweep [--apply]`.
- **Побочные эффекты прогона** (✔ по фабрикам):
  - окружение `Testing`, фоновые задачи выключены;
  - `Notifications:Provider=logging`, `SmartCaptcha:SecretKey` пуст;
  - где включены `max-bot`/`web-push`, клиенты MAX и `IWebPushSender` подменены записывающими фейками;
  - файлы пишутся во временные каталоги `$TMPDIR/sb-test/…`;
  - сетевых вызовов к внешним провайдерам фабрики не делают.

  Среду всё равно проверяет qa-engineer перед запуском.
- Известные ловушки (↪):
  - два полных прогона параллельно на одном Docker дают ложные падения тестов, чувствительных ко времени;
  - по отчёту цикла 27 полный прогон на colima бывает нестабилен по таймаутам Npgsql;
  - флейк CY24-31 около 03:30–04:05 по времени магазина (C25-9);
  - 8 тестов `CartPanel` краснеют при локальном `frontend/.env` с `VITE_SMARTCAPTCHA_SITEKEY` (в CI зелёные).
- Подмножество: `dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~Cycle26CompanyCardTests"`; цикл 31 —
  `--filter "FullyQualifiedName~Cycle31"` (`Cycle31CatalogListingTests`, `Cycle31GalleryRateLimitTests`; второй поднимает
  отдельный хост с продовыми лимитами).
- `npm run contracts:json` — не тест: скрипт **перезаписывает** `contracts/cycle31/openapi.json`. В базовый прогон не
  входит, в CI запускается как сверка (§7.3).

### 7.3 CI — `.github/workflows/ci.yml` (push в `master`, `release-candidate`, `develop`, `cycle/**`, `fix/**` и любой PR)
- **backend**:
  - сервис `postgres:16`; restore, затем build `-c Release -warnaserror`;
  - юнит-тесты, затем функциональные с `xUnit.MaxParallelThreads=2`;
  - `check-image-pins.sh` — сверка версии Postgres в четырёх местах;
  - дрейф снапшота миграций: `migrations add __DriftProbe` + `check-drift-probe.py`;
  - монотонность Designer-снимков: `check-migration-snapshots.sh`;
  - `LegalKit check`.
- **frontend** (Node 20):
  - `npm ci`;
  - сверка генератов циклов 7, 9, 14 и отдельным шагом 18, 19, 20, 23, 24, 25, 26, 29, 31 (26 и 29 — с цикла 29, 31 — с
    цикла 31);
  - `redocly lint` контрактов 8-invariant, 13, 14, 16, 18, 19, 20, 23, 24, 25, 26, 29, 31;
  - шаг «Contract JSON must match cycle31 yaml» (цикл 31): `npm run contracts:json && git diff --exit-code --
    ../contracts/cycle31/openapi.json`. Сверяется **только** JSON цикла 31, JSON циклов 26 и 29 шаг не трогает (§9.1, C29-1);
  - `npm audit --omit=dev --audit-level=high`;
  - `lint`, `tsc` для ezbook и для goods, `npm run typecheck:node` (шаг «Type-check node (shots)», цикл 30);
  - греп на заглушку правового текста и на `fetch`/Cache в `sw.js`;
  - `test:run`;
  - `build:release` — оба сайта;
  - смоук `dist` и `dist/__goods`;
  - артефакт `frontend-dist-<sha>` для `master`/`release-candidate`/`develop`.
- **docker-build** — сборка образа API и запуск в режиме `Production` с Postgres. Это заодно доказывает полноту
  обязательных env-переменных. Затем `smoke.sh`.
- Контракты 26 и 29 в CI охвачены с цикла 29 (C26-1 закрыт), контракт 31 — с цикла 31 (lint, генерат, JSON).
  Guard-тест общих исходников goods (§5.8) отдельного шага не имеет: он часть `test:run`. JSON-схемы `contracts/cycle30/*.schema.json` в CI **не**
  проверяются: сверку манифеста скриншотов со схемой делает человек при пересъёмке (`ajv-cli`), а форму манифеста в
  каждом прогоне держит vitest T30-15. Скрипты засева и съёмки CI не запускает.

### 7.4 Деплой
- `deploy-staging.yml` — ручной запуск, только ветка `develop`, окружение GitHub `staging`. Шаги: сборка фронта, SSH-ключ,
  `deploy/deploy-remote.sh` на машине. Смоук: health, фронт, `https://$GOODS_HOST/api/goods/catalog`.
- `rollback-staging.yml` — откат на предыдущий релиз.
- `deploy-production.yml` — ручной запуск, тег на `master`, подтверждение словом `deploy`. **Ни разу не использовался**:
  тегов нет.
- `deploy/deploy.sh` — ручной запасной путь через `gh`. SSH-ключ ограничен forced command (`ssh-deploy-wrapper.sh`).
- Прод-стек — `docker-compose.prod.yml`: `postgres:16-alpine`, `api` (`servicebooking-api:latest`, собирается на
  машине), `legal-tools`. Все переключатели — env-переменные с безопасными дефолтами, пример — `.env.production.example`.
- nginx: `ezbook.conf`, `goods.ezbook.conf` (раздаёт `current/__goods`), `errors.ezbook.conf` (GlitchTip).
- Обслуживание: бэкап `deploy/backup/` (systemd-таймер), мониторинг `deploy/monitor/health-alert.*`, SQL-гейты выката
  `deploy/checks/`.
- Подробная инструкция — `DEPLOY.md` (§0–§23); отдельно — `DEPLOY-windows.md`.

---

## 8. (зарезервировано; раздел рисков — §9, документация — §10, как в прежней редакции)

---

## 9. Рискованные и хрупкие места

### 9.1 Найдено или подтверждено в этом сканировании (✔)
**Цикл 31 (✔ по коду и git на `364cc2b`).** Пункты C31-1…C31-4 и C29-1 были записаны перед циклом в ветке 31 (`a8d3337`)
и в `develop` не попали (§ шапка). Здесь указано их состояние после цикла.
- ~~**C31-1. Tailwind goods не сканировал `CompanyPhotosSection.tsx`**~~ (компонент лежал в `src/pages/owner/`, классы
  плитки в goods терялись). **Закрыто:** компонент переехал в `src/components/company/`, `content` генерируется из
  `goods-shared-sources.js`, есть guard-тест. Вид плитки глазами не проверен (M31-01…03 не выполнены).
- ~~**C31-2. Текст `HiddenByOwner` описывал состояние («Показ выключен в настройках» с галочкой)**~~ — **закрыто**: «Показ
  включен в настройках».
- ~~**C31-3. Галерея грузила по одному файлу и делила окно `uploads` 10/мин**~~ — **закрыто**: мультизагрузка и две
  отдельные политики. Остаётся: лимит 10 фото продублирован на фронте и сервере (§5.8). Бюджет обработки изображений на
  пользователя вырос до 30 в минуту — это осознанное ослабление (О-31-3). В закоммиченном `CHANGELOG.md` его ещё нет
  (C31-8).
- ~~**C31-4. Видимость салона в каталоге жила в двух запросах**~~ — **закрыто**: `SalonListingRules` + `SalonListingQuery`.
  Хрупкость перешла в другое место: правило и SQL-двойник надо менять вместе, а согласованность ловит только CY31-05.
  То же относится к `PublicListingQuery` и `SubscriptionResolver`.
- ~~**C31-5. `NoticesPage` импортировался goods, но не сканировался Tailwind goods**~~ — **закрыто** тем же механизмом.
- **C29-1. JSON-копии контрактов — закрыт только частично** (архитектура §31.18 О-31-7 считала его закрытым целиком).
  Скрипт `contracts:json` появился, но `frontend/scripts/contracts-to-json.mjs` обрабатывает только `['cycle31']`, и
  CI-шаг сверяет только `contracts/cycle31/openapi.json`. JSON циклов 26 и 29 — единственные, которые реально читают
  функциональные тесты (`OpenApiContract.Load("cycle26"|"cycle29")`), — по-прежнему без скрипта и без сверки. А JSON
  цикла 31 сверяется, но **ни один тест его не читает**.
- **C31-6. Контрактных тестов цикла 31 нет.** `Cycle31ContractConformanceTests.cs` (CY31-30…34) не написан. Ответы нового
  маршрута `catalog-listing` и галереи с `contracts/cycle31` автоматически не сверяются. Разовую сверку QA описал в
  `TEST_CATALOG.md` словами.
- **C31-7. `API_DOCUMENTATION.md` (строки ~4168–4170) расходится с кодом в двух местах:**
  - в ответе маршрута салона указано поле `planHint`, а в коде и контракте это `notAllowedByPlanText`;
  - в политике `company-photos-edit` упомянут маршрут `PUT /photos/{photoId}/cover`, но такого маршрута нет (обложка
    ставится через `PUT …/photos/order`).
- ~~**C31-8.**~~ **Закрыто в `6a71f50`** (✔ `CHANGELOG.md` строка 19 — раздел «Не выпущено — цикл 31: …», README —
  абзацы «с цикла 31, пока не выкачено»). Прежний текст пункта: **В коммите `364cc2b` `CHANGELOG.md` и `README.md` цикл 31 не описывают** (✔ `git show HEAD:…`, верхний раздел
  CHANGELOG — цикл 30), хотя архитектура (QA-3) требовала раздел. **В рабочем дереве на момент этого обновления оба файла
  изменены и не закоммичены** (правит не codebase-analyst, этот документ их не трогал). В правке есть раздел CHANGELOG
  «Не выпущено — цикл 31: …» с записью об ослаблении лимита загрузок до 30 изображений в минуту и о тексте «Показ
  включен в настройках», счётчик во вступлении — «двадцать семь», а в README — абзацы «с цикла 31, пока не выкачено».
  Пока правку не закоммитят, пункт открыт.
- **C31-9. `contracts:json` зависит от необъявленного пакета.** Скрипт импортирует `js-yaml`, которого нет в
  `frontend/package.json`: пакет поднят в корень `node_modules` транзитивно (ESLint, `@redocly/openapi-core`). Если
  транзитивная зависимость сменит версию или место, CI-шаг сверки JSON упадёт на импорте.
- **C31-10. Отклонения реализации от архитектуры цикла 31** (поведение корректно, но документ цикла описывает другое):
  - явной ветки `OnRejected` для галерейных политик нет (§5.8);
  - `RateLimitTestFactory` не расширен, тест поднимает свой хост;
  - в `CompanyCard` на узком экране слот занимает всю ширину (`col-span-2`), а не колонку под `h1`.
- **Ручные M31-01…10 не выполнены** (вёрстка плитки на 360 px, touch, пакет из 10 фото вживую, шапка `/myasnoy`). Вёрстку
  шапки и плитки автоматически проверяют только наличие классов в vitest. Браузерного набора нет.
- **Мультизагрузка без отмены.** При уходе со страницы текущий запрос не отменяется, а остальные файлы пакета тихо не
  отправляются: после размонтирования очередь останавливается. Пользователь об этом не узнаёт. Так задумано спекой, но
  это место легко принять за баг.
- **Прокрутка вкладок кабинета goods (`474f8be`)** хранится в переменной уровня модуля, общей для всех магазинов и
  вкладок браузера. Тестов нет.

**Добавлено в цикле 30 (✔ по коду и git на `aeed251`, если не сказано иное):**
- **C30-1. `swagger.json` в Development отдаёт 500** (дефект бэкенда, циклом 30 не чинился). Swashbuckle падает на
  одинаковом schemaId у двух разных record'ов `WorkingHoursDto`: `ServiceBooking.API.DTOs.WorkingHours.WorkingHoursDto`
  (`DTOs/WorkingHours/WorkingHoursDto.cs`) и `ServiceBooking.API.DTOs.Shops.WorkingHoursDto`
  (`DTOs/Shops/ShopScheduleDtos.cs:24`); `CustomSchemaIds` в `ApiExtensions` не задан ✔. Следствия: Swagger UI в
  Development открывается, но документ API не грузится; замок стенда съёмки поэтому проверяет `GET /swagger/index.html`
  и разбор тела (`isSwaggerUiHtml`), а не `swagger.json`. Сам 500 в этом обновлении не воспроизводился (↪ отчёт BE-30-1,
  комментарий в `seed-goods-demo.mjs`). На бою Swagger выключен, пользователей не касается.
- **C30-2 (= R-F цикла 30, C24-2). Текст блока «Для покупателей» обещает push, который на бою не включён и на устройствах
  не проверен.** Пункт 3 списка «Как следить за заказом» говорит об уведомлении о смене статуса; на бою провайдер
  `WEBPUSH_STAFFPUSH_PROVIDER` не задан → `logging` 🖥, на реальных Android/iPhone push не проверялся. Ручной кейс «push на
  реальном телефоне» (M30-10 в `ARCHITECTURE_CYCLE30.md §30.14`, M30-09 в `TEST_CATALOG.md`) — **гейт выката этого
  текста**, не гейт мержа; по состоянию на `aeed251` не выполнен.
- **C30-3. `core.hooksPath=.githooks`, а каталога `.githooks` в репозитории нет** ✔ (`git config --get core.hooksPath`,
  локальный конфиг чекаута). Git-хуки в этом чекауте фактически не работают — ни свои, ни стандартные из `.git/hooks`.
- **C30-4. Скриншоты сняты с «грязного» дерева и не проверяются на свежесть.** `screenshots.json` → `sourceCommit:
  "6ea8d4c-dirty"`. Проверки, что кадры соответствуют текущему коду снятых экранов, нет (решение SPEC, риск R-D); список
  «снятых» файлов — только в README скриптов.
- **C30-5. Пересъёмка дорогая по времени.** `stack.sh reset` пересобирает образ API: в замере FE-30-4 это ~20 минут на
  colima под нагрузкой (одна попытка упала на экспорте слоя containerd). Съёмка требует Chrome, интернет (Google Fonts) и
  дневного времени магазина.
- **C30-6. Нумерация ручных кейсов цикла 30 расходится:** в `ARCHITECTURE_CYCLE30.md §30.14` M30-01…12, в
  `TEST_CATALOG.md` M30-01…09 с другими номерами (push — M30-10 против M30-09; M30-11 «пересъёмка ≤ 30 мин» и M30-12
  «замок на `goods.ezbook.ru`» в каталог не перенесены).
- **Ручные кейсы M30 не выполнены** (вердиктов в `TEST_CATALOG.md` нет): вёрстка 360/768/1280, чёткость 2x, ленивая
  загрузка и вес, скринридер. Сквозного браузерного набора нет.
- **`API_CONTRACT_CYCLE30.md §30.26`, проверка «API не изменился»** сформулирована как пустой
  `git diff --stat b9c2a79 -- ServiceBooking.* contracts/cycle2*`; после мерджа цикла 29 этот diff не пустой (цикл 29 менял
  бэкенд и контракты). Базой для этой проверки теперь служит `c19a83c`.
- **`react-router` 6.x — умеренный advisory `npm audit`** (↪ AV7/C25-8, GHSA-wrjc-x8rr-h8h6): цикл 30 зависимостей рантайма
  не менял, CI-порог `--audit-level=high` его пропускает. `playwright-core` и `@types/node` — только dev.

**Прежние пункты:**
- ~~**C26-1. CI не проверяет контракт цикла 26.**~~ **Закрыто в цикле 29** (`d03720b`, ✔ `ci.yml`): `cycle26` и `cycle29`
  в `redocly lint` и в сверке генератов. Шаблон прежний: новый OpenAPI цикла надо дописывать в два шага `ci.yml`.
- **C26-2. Ловушка с алиасом `@` в ezbook-сборке** (§6.2). `tsc` и vitest алиас видят, `vite build` ezbook — нет. Ловит
  только шаг `build:release` в CI, локальные `tsc`/тесты зелёные.
- **C26-3. `ShopManageDto.TimeZoneChangeAllowed = !hasOrders`, а сервер разрешает магазину с заказами переезд в город с
  тем же смещением UTC** (`ShopTimeZoneChangePolicy`). Флаг строже правила. Если UI опирается только на флаг, он запретит
  разрешённый переезд.
- ~~**C26-4. Два одинаковых запроса упорядоченной галереи.**~~ **Закрыто в цикле 29** (✔): `GetPhotosOrderedAsync` удалён,
  карточка салона тоже берёт `CompanyPhotoQueries.OrderedAsync`. Выбор обложки (`CompanyPhotoOrdering`) сортирует
  отдельно, тем же порядком.
- **C26-5. Галерея магазина без счётчика квоты.** `photo-usage` для магазина отвечает 409. Загрузка же идёт через общий
  конвейер и тарифные правила фото: какой лимит объёма фото у линейки «Заказы» действует на деле, в этом сканировании не
  проверялось.
- ~~**C26-6. CHANGELOG и README не описывают цикл 26.**~~ **Закрыто в `b9c2a79`** (✔): в CHANGELOG раздел
  «Не выпущено — цикл 26: …» (строка 34, между циклами 27 и 25), в README цикл 26 описан в блоке goods и в возможностях
  салона (карточка, фото, ссылки на карты).
- **C26-7. Счётчик разделов «Не выпущено» во вступлении CHANGELOG** на `364cc2b` сходится (✔): «двадцать шесть», и
  разделов `## Не выпущено` тоже 26, верхний — цикл 30. Рассинхрон повторяется каждый цикл, и добавление раздела цикла 31
  (C31-8) снова потребует правки счётчика.
- **Hero ezbook `salon-hero.jpg` — PNG на ~1,7 МБ под расширением `.jpg`** (§5.7). По-прежнему самый тяжёлый файл
  фронта, грузится на главной ezbook (на широком экране). Цикл 30 его не трогал; скриншоты goods сделаны отдельно, в WebP
  с бюджетом веса.
- **C27-1. Номера § в документах циклов 26 и 27 совпадают.** Оба цикла стартовали параллельно от `0d41df4`.
  `ARCHITECTURE_CYCLE26.md` — §543–§556, `API_CONTRACT_CYCLE26.md` — §557–§569. `ARCHITECTURE_CYCLE27.md` — снова
  §543–§552, `API_CONTRACT_CYCLE27.md` — §553. Ссылка вида «§548.2» без имени файла неоднозначна. Это продолжение
  ↪ C23-8 (нумерация пересекалась и раньше).
- **C25-10** (подтверждено): `GET /api/companies/{slug}` не проверяет `Kind` и отдаёт `CompanyDto` магазина.
- **C25-6** (подтверждено): `ShopScheduleController` строки 108–109 — `date.AddDays(-1)`/`AddDays(1)` на крайних датах
  бросают исключение, вероятно 500. Доходит ли туда дата вне горизонта, не проверено.
- **C25-7** (подтверждено): `CustomerSearchTerm` оставляет только цифры, без приведения 8→7. Поиск «8916…» не найдёт «7916…».
- **C20-6 — пересмотрено.** `CompaniesController.GetStats` не зовёт `QueryDateTime.ToUtc`, но `CompanyStatsService` сразу
  приводит `from`/`to` к `DateOnly`. Путь к 500 из-за `Kind=Unspecified` по коду не просматривается. Функционально не
  проверялось.
- **C20-5** (подтверждено): в `ServiceBooking.Tests/Tests/LegalPricingGateTests.cs` 8 вхождений `Task.Delay(2500)` —
  ожидание по настенным часам.
- **§9.22** (подтверждено): шаг слотов записи — константа 30 минут.
- **§9.25** (подтверждено): `dotnet format` в CI нет.
- **Заглушка рассылки** (§5.3) выглядит как работающая функция: API отвечает «поставлена в очередь».
- **Крупные файлы, на которых завязано много:**
  - `frontend/src/pages/owner/CompanyManagePage.tsx` — 1 312 строк (на `364cc2b`; цикл 31 убрал чекбокс каталога);
  - `CompaniesController` — около 500 строк, от него зависят оба каталога салонов (с цикла 31 — через `SalonListingQuery`);
  - `NotificationChannelsController` — 845 (не режется по решению цикла 22);
  - `AdminBillingController` — 791, `AdminController` — 675, `BookingsController` — 595;
  - `AppDbContext` — 1 086.
- **Модель релизов разошлась с документами.** Кнопка «staging» фактически выкатывает боевой ezbook.ru, `master`/теги/
  `release-candidate` не используются. CHANGELOG держит 26 разделов «Не выпущено», хотя часть из них на бою (циклы 20–22).

### 9.2 Открытый долг из прежней редакции (↪ не перепроверялся; подробности — `git show 491406c:CURRENT_STATE.md`, §9)
**Эксплуатация и выкат**
- **C25-11.** Состояние машины после деплоя `4739e0b` неизвестно. Миграции `Cycle24…` и `Cycle25…` при выкате применятся
  вместе.
- **C25-1 / C25-2.** MAX персоналу не включён, живого смоука нет. Бот MAX один на подтверждение телефона и на сообщения
  персоналу: бан бота остановит оба.
- **C24-1 / C25-4.** vhost goods на сервере не обновлён: `/sw.js` без no-cache — ручной шаг `DEPLOY.md` §22.2.
- **C24-2.** Push на реальных устройствах не проверен. С цикла 30 на это опирается текст главной goods (C30-2, §9.1).
- **C21-2.** VAPID `sub` проверяется только на непустоту, Apple отвергает заглушки.
- **C19-1.** Гейт `check_retired_limit_options` ни разу не выполнялся на реальной БД.
- **C19-4.** Миграция цикла 19 старше по ID, чем уже применённые миграции циклов 20–22: неидемпотентный
  `migrations script` её пропустит.
- **C18-1.** Тариф «Триал» не сидируется.
- **C20-9.** Правовой манифест в git (`2026-09-29-draft`, `isDraft: true`) ≠ бой (`2026-09-30`, опубликован).

**Данные и корректность**
- **C23-9.** `OrderEditService` полагается на отслеживание EF и `Order.Version`. Здесь уже была ошибка с двойным
  добавлением позиции.
- **C24-10.** `Down` миграции цикла 24 может упасть.
- **C24-11.** Номер заказа меняется при переносе даты выдачи.
- **C24-7.** Лимиты считаются в пределах линейки. Это меняет решение Q5 цикла 23, старые тесты переписаны.
- **C22-5.** Состояние финансирования канала живёт по реальным часам, а не по `INotificationClock`.
- **C22-6 / C22-7 / C22-8.** Цена производительности: админский список каналов, повторное чтение пользователя,
  `masters/clients` в памяти.
- **C17-2 / C17-3 / C17-6.**
  - Коды ответов отмены и переноса расходятся.
  - Клиент может отменить `Completed`.
  - Окно переноса и окно отмены — одно поле.
- **C17-8.** Исторические разрывы Designer-снапшотов вне зоны C15-4, осознанно не чинились.
- **B3.** Приёмочного теста самой опасной миграции биллинга (`AddCoTenancyConstraints`) нет.
- **N9-6.** `DataRetentionTask`: изоляция правил друг от друга. Прежняя запись 🔴, закрыт ли пункт — не проверено.

**Тесты и контракты**
- **C20-4.** Журнал гейта гостевых данных функциональными тестами не покрыт.
- **C25-9.** Флейк CY24-31.
- **AV3 / T8-1.** Ожидания по настенным часам в тестах.
- **T8-5.** Инвариант OpenAPI цикла 8 и schemathesis в CI не запускаются.
- **C17-1.** Генераты циклов 10, 11, 13 и контракты 6, 7, 10, 11, 15, 17 в CI не линтуются.
- **C25-5.** В `contracts/cycle25` не описаны 400 для NUL и для дат вне 2000..2100.
- **C19-6.** Отдельные сценарии цикла 19 не покрыты.
- **§9.18.** Покрытие фронта точечное.

**Безопасность, правовое, зависимости**
- **V1 — принятый риск.** Регистрация на чужой номер возможна. Чтение и удаление чужих гостевых данных закрыто гейтом
  цикла 16, захват идентификатора — нет.
- **AV7 / C25-8.** `npm audit`: умеренные advisories `react-router` (GHSA-wrjc-x8rr-h8h6) и мажорные апгрейды
  `vite`/`vitest`/`esbuild`.
- **C15-8 / C17-4.** Ручное назначение скрытого тарифа (п. 2 ст. 426 ГК) — нет решения заказчика.
- **C-13.** Продажа канала WhatsApp гражданам без статуса — заказчик думает.
- **C20-1.** Callback как второй канал подтверждения телефона не сделан.
- **C20-3.** Действия заказчика вне кода: уведомления РКН (NP5), акты принятия рисков, вычитка живым юристом.
- **NP1 / NP2.** Требования режима НПД — чеки, проверка статуса, автопродление — не реализованы. Сейчас не нужны: списаний нет.
- **LG1…LG9, C19-2, C22-L1.** Открытые правовые вопросы. В частности, версии принятых текстов не попадают в выгрузку
  субъекта.
- **C23-2 / C24-3 / C25-3.** Юрист по текстам goods (L1–L20) не запускался.
- **C24-3.** Галочка мессенджера у гостя работает без подтверждения номера.

**Документация**
- **C19-8 / C23-8.** Руководств по goods в `docs/` нет (цикл 30 добавил шаги покупателя только в интерфейс главной).
  Нумерация § в документах пересекается.
- **C22-4.** Ссылки `Файл.cs:строка` в старых документах исторические.

---

## 10. Что уже существует в документации и тест-кейсах (дополнять существующее, параллельных версий не заводить)

### 10.1 Краткая продуктовая документация
- **`README.md`** (760 строк на `364cc2b`, markdown). Два раздела верхнего уровня:
  - **«О проекте»** (строки 3–612). Абзацы с жирными подзаголовками, а не `###`: что это; где работает («стенд» ezbook.ru);
    отдельный блок «Заказы на самовывоз — goods.ezbook.ru»; возможности по ролям; тарифы; «Чего пока нет»; «Почему это
    стенд, а не запуск».
  - **«Запуск»** (со строки 613) с подразделами `###`: «Локально, всё в Docker», «Локально, без Docker для API»,
    «Переменные окружения и секреты», «Тесты», «CI», «Деплой».
  - Цикл 26 отражён (`b9c2a79`), цикл 30 — тоже (`5b46d62`): главная goods с блоками «Для покупателей» и «Для бизнеса»,
    скриншоты на демо-данных, оговорка про push. **В `364cc2b` цикла 31 в README нет** ✔. Абзацы «с цикла 31, пока не
    выкачено» лежат незакоммиченной правкой в рабочем дереве (C31-8). Инструкция пересъёмки скриншотов — отдельно, `frontend/scripts/screenshots/README.md`
    (§5.7).
- **`CHANGELOG.md`** (4 210 строк на `364cc2b`). Формат по мотивам Keep a Changelog, язык — для пользователей. Номеров
  версий нет. На каждый цикл — раздел `## …`, **самый свежий сверху**. Сверху идут 26 разделов «Не выпущено — <суть>» в
  порядке влития, ниже — датированные разделы (`## 2026-09-17 — сервис впервые развёрнут…`, самый ранний — `2026-09-01`).
  Порядок верхних разделов: цикл 30 (строка 20), цикл 29 (141), цикл 27 (169), цикл 26 (183), далее старые. **Раздела
  цикла 31 в `364cc2b` нет.** Он пишется незакоммиченной правкой сверху, над циклом 30 (C31-8). Внутри крупных разделов сложились подзаголовки `###`: «Что стоит прочитать до
  выката», тематические блоки, «Чего этот цикл не даёт», «Для команды» (итоги тестов и документы цикла). Даты разделов
  «Не выпущено» при выкате не проставлялись. Счётчик во вступлении в `364cc2b` («двадцать шесть») совпадает с фактом
  (C26-7).
- Releases на GitHub и wiki не используются: тегов нет, ссылок на wiki в репозитории нет.
- Внутренние справки: `BRIEF_BRAND_AND_DOMAINS.md` (бренд и домены «EZBOOK Запись» / «EZBOOK Заказы»),
  `BENCHMARK_CYCLE22.md`.

### 10.2 Развёрнутая пользовательская документация
- **`docs/`** (markdown, по ролям, только продукт ezbook — запись на услуги):
  - `README.md` — оглавление с таблицей «кто вы → с чего начать»;
  - `client.md`, `master.md`, `owner.md`, `admin.md`;
  - общие статьи: `accounts.md`, `personal-data.md`, `schedule.md`;
  - `faq.md` — частые вопросы и честный список ограничений.
- Для команды в той же папке: `testing-isolation.md` (запуск тестов, colima, уборка) и `incident-runbook.md` (утечка ПДн).
- **Руководства по goods.ezbook.ru нет.** Магазины упоминаются только в `docs/personal-data.md`.
- **`docs/owner.md` §5 «Настройки компании и витрина» (строки ~142–173) отстаёт от кода** (✔ на `49f0d60`): описывает
  переключатель «Показывать компанию в общем списке» (убран из формы в цикле 31, теперь отдельный блок «Каталог
  ezbook.ru»), не упоминает группы полей (цикл 29), блок «Город и часовой пояс», ссылки на карты и галерею фото салона.
  Строка 40 «четыре вкладки» — в коде пять (есть «Уведомления»).
- Справки внутри приложения: правовые документы и тексты интерфейса отдаются из `/api/legal/*` (исходники —
  `legal-drafts/`) и показываются на `/privacy`, `/terms` и т.д. Отдельной базы знаний или сайта документации нет.
- Эксплуатация: `DEPLOY.md` (2 457 строк, §0–§23, выкат, переменные, ручные шаги по циклам), `DEPLOY-windows.md`,
  `INCIDENT_CHECK_PROCEDURE_CYCLE16.md`. Правовые внутренние документы — `legal-internal/*.md`, `LEGAL_REVIEW*.md`,
  `LEGAL_DECISIONS_CYCLE20.md`, `legal-drafts/README.md`.

### 10.3 Документация API для внешних потребителей
- **`API_DOCUMENTATION.md`** (4 484 строки на `364cc2b`, markdown, по-русски) — основной справочник:
  - §1 «Обзор», §2 «Аутентификация», §3 «Ключевые бизнес-концепции»;
  - §4 «Справочник эндпоинтов» — по доменам, с подразделами «Цикл N (unreleased)», у цикла 26 — строка ~1955; цикл 29
    вписан точечно (`logoUrl` каталога, 400 на битую форму — в §6, ~4384). Цикл 30 API не менял и сюда не вписан.
    Цикл 31 — три пункта-маркера «Цикл 31 (unreleased)» подряд в строках ~4168–4170: маршрут салона `catalog-listing`,
    лимиты галереи, строка для CHANGELOG об ослаблении лимита. **В двух местах текст расходится с кодом** (C31-7, §9.1);
  - §5 «Сквозные сценарии», §6 «Справочник кодов ответа», §7 «Известные ограничения».
  - ↪ C23-8: в §4 есть повторяющиеся номера подразделов.
- **OpenAPI по циклам** — `contracts/cycleN/openapi.yaml` для циклов 7, 9, 10, 11, 13–20, 23–26, 29, 31. У 26, 29 и 31
  есть ещё `openapi.json`. JSON 26 и 29 читает валидатор функциональных тестов `OpenApiContract.cs`, JSON 31 генерирует
  `npm run contracts:json` и сверяет CI, но тесты его не читают (C29-1, C31-6). Кроме того, есть
  `contracts/cycle8/servicebooking-invariant.openapi.yaml`. Каждый файл описывает **дельту своего цикла, а не весь API
  целиком**. Сводного OpenAPI нет.
- Прочие контракты: `contracts/cycle23/goods-routes.json`, `order-money-vectors.json`, `cycle24/pickup-schedule-vectors.json`,
  `cycle11/*.schema.json`, `legal-routes.json`, `contracts/legal/runtime-value-forms.json`, `cycle30/screenshots-manifest.schema.json`
  и `cycle30/seed-state.schema.json` (JSON Schema файлов съёмки, не API). Линт — `contracts/redocly.yaml`.
- Исторический `openapi-cycle6.yaml` лежит в корне.
- Swagger UI — только в Development; `swagger.json` там сейчас отдаёт 500 (C30-1, §9.1).
- Полный перечень маршрутов с атрибутами — `ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt` (§4).
- Postman-коллекции нет. `ServiceBooking.API/ServiceBooking.API.http` — файл запросов IDE.

### 10.4 Описания тест-кейсов (отдельно от кода автотестов)
- **`TEST_CATALOG.md`** (6 463 строки на `364cc2b`, markdown):
  - в начале — «Как устроены ссылки на тесты» и «Префиксы по доменам»;
  - затем разделы по доменам (Auth, Bookings, Companies…);
  - затем разделы по циклам: «Цикл N — … (`CYNN-`, число тестов, файл)», в каждом таблица `ID | US | Что проверяет`.
    Там же ручные кейсы (`M27-…` в ветке 27), вердикты QA и «не покрыто»;
  - порядок разделов в конце: цикл 27 (~6226), цикл 29 (~6240), цикл 30 (~6278), цикл 26 (~6294), **затем цикл 20**:
    разделы идут в порядке влития/написания, а не по номеру. У циклов 27, 29, 30 кроме автотестов есть таблицы ручных
    кейсов (`M27-`, `M29-`, `M30-`) с колонками `Кейс | Шаги | Ожидаемый результат | Критерий` (у M29 ещё `Вердикт`).
    Раздел цикла 30 перечисляет vitest T30-01…19 и QA30-01…10 одной строкой (без таблицы по ID) и M30-01…09 без
    вердиктов; ссылка раздела цикла 29 на спеку исправлена на архив `SPEC_CYCLE29_CYCLE26_FOLLOWUPS.md`.
  - **Раздел цикла 31 — в самом конце файла (строка ~6417), после цикла 20.** В нём:
    - таблица CY31-01…14 и CY31-20…25 с колонками `ID | US | Что проверяет | Тест`;
    - абзац про отложенные контрактные CY31-30…34;
    - vitest разработчиков одной строкой;
    - «Ручные кейсы (T-31-04, Q-31-10)» — таблица M31-01…10 с колонками `Кейс | Шаги | Ожидаемый результат | Критерий |
      Вердикт`, у всех вердикт «не выполнен».
  - ↪ TD16-7: вердикты раздела «Цикл 16» устарели.
- ID кейсов продублированы в коде атрибутом `[TestCase("CY26-01")]` на тестах `ServiceBooking.Tests`.
- Ручные живые проверки выката — чек-листы в `DEPLOY.md` (§16 и шаги циклов).
- Отдельного `TESTPLAN.md` или `docs/testing/` нет. Каталог сценариев — только `TEST_CATALOG.md`.

### 10.5 Документы циклов
- Корень:
  - `SPEC.md` — спека **цикла 31** («Общий блок фото, мультизагрузка, блок „Каталог“ у салона, компактная шапка
    карточки компании»). Следующий цикл заменит её своей, а эту заархивирует как `SPEC_CYCLE31_*.md`. В этом обновлении
    она **не** архивировалась: по конвенции проекта архивирует следующий цикл, и папки `docs/history/` нет;
  - архив спек `SPEC_CYCLE{3..27,29,30}_*.md`: спека 27 — `SPEC_CYCLE27_GOODS_BUSINESS_BLOCK.md`, спека 29 —
    `SPEC_CYCLE29_CYCLE26_FOLLOWUPS.md`, спека 30 — `SPEC_CYCLE30_USER_SECTION_SCREENSHOTS.md` (архивирована в ветке 31).
    Спек 12 и 28 в корне нет (28 — в ветке `origin/cycle/028-…`). Плюс `SPEC_APPENDIX_CHANNELS.md` и
    `SPEC_DEFERRED_NOTIFICATIONS.md`;
  - `ARCHITECTURE_CYCLE{4..27,29,30,31}.md` (у цикла 8 два файла: основной и `_PHASE2`) и
    `API_CONTRACT_CYCLE{4..27,29,30,31}.md`. У 27 — «API не меняется», у 30 — только формы файлов засева и манифеста
    (§30.20–§30.27), у 31 — §31.20–§31.29. Циклы 29–31 нумеруют § по номеру цикла (§29.x, §30.x, §31.x), а не сквозной
    нумерацией. Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — от цикла 3;
  - `LEGAL_REVIEW*.md`.
- `docs/history/` не существует: архивом служат суффиксы в корне.

---

*Конец документа. Раздел 8 оставлен пустым намеренно: номера разделов 9 и 10 совпадают с прежней редакцией, на них
ссылаются другие документы.*
