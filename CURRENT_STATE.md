# CURRENT_STATE — фактическое состояние кодовой базы ServiceBooking

**Актуально по состоянию на коммит: `aeed251` (HEAD ветки `cycle/030-user-section-screenshots` в конце цикла 30; ветка уже содержит влитый `develop` = `origin/develop` = `c19a83c` с циклом 29, мердж `2100fb3`), дата: 2026-09-30.**
**Режим: обновление поверх полного сканирования на `1837373`** (прежние точечные обновления — на `b9c2a79`).
Сверено по git (✔): `git diff b9c2a79..c19a83c` — цикл 29 (кратко в §5.5; в этот файл цикл 29 сам вписал только числа
прогонов), `git diff origin/develop..HEAD` — цикл 30 (37 файлов, только фронт goods, скрипты съёмки, `tsconfig`, CI-шаг,
`contracts/cycle30`, документы; бэкенд, миграции и API цикл 30 **не** менял). Цикл 30 описан в §5.7; обновлены также §0,
§1 (фронт-зависимости), §2, §3–§4 (строки про цикл 29), §5.4, §5.5, §6.2, §7.1–§7.3, §9.1–§9.2, §10.1, §10.3–§10.5. Документ обновлён в ветке цикла 30 до её влития в
`develop`. **Следующий diff отсчитывайте от `aeed251`** (после влития ветки — от мерджа её в `develop`; коммиты между
ними — только документы этого обновления).

**Дополнение цикла 28 (проходы A и B, ветка `cycle/028-showcase-data-demo-stand`, backend ✔ по коду и `dotnet test`; правил вручную, полный пересчёт — за codebase-analyst):** §5.5 (блоки «Цикл 28, проход A» и «Цикл 28, проход B — демо-стенд»),
§5.6 (задачи `showcase-reseed` и `demo-reset`), §9.3 (долг C28-1…C28-14) и строка `Migrations/` в §2. Форма API — `contracts/cycle28/openapi.yaml`, описание — `API_DOCUMENTATION.md` §4.21 (проход B — §4.21.8).

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
- `origin/develop` = `c19a83c` — в нём влит **цикл 29** «Доделки цикла 26» (последний коммит цикла — `c19a83c`,
  «docs(cycle29): CHANGELOG, статус ручных кейсов 360 px»). Кратко — §5.5.
- `cycle/030-user-section-screenshots` — ветка **цикла 30** (блок «Для покупателей» и скриншоты на главной goods), в
  `develop` ещё не влита; `origin/cycle/030-…` = `aeed251`. Впереди `origin/develop` на 14 коммитов, позади на 0:
  13 коммитов цикла 30 (`712032e`…`aeed251`, включая прежнее обновление этого файла) и мердж `2100fb3`
  (`origin/develop` с циклом 29 влит в ветку). Что в ней — §5.7.
- `origin/cycle/029-cycle26-followups` — ветка цикла 29, влита в `develop`.
- `origin/cycle/028-showcase-data-demo-stand` — на 10 коммитов впереди и на 13 позади `origin/develop` (✔ на `c19a83c`;
  циклы 26–27 в неё уже влиты, `d23a7a3`). Только документы: спека цикла 28, бриф «витрина и демо-стенд», черновик
  тарифной сетки, архитектура и контракт цикла 28. Когда эта ветка будет вливаться, её `SPEC.md` (спека 28) столкнётся
  с корневым `SPEC.md`, который к тому моменту — спека цикла 30 (§10.5). Если она тронет CURRENT_STATE.md, конфликт разрешает codebase-analyst,
  сводя обе версии по смыслу; выбирать файл целиком нельзя.
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
| Контракты | openapi-typescript 7.13 (генерация типов), @redocly/cli 2.54 (линт OpenAPI) |
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
| `ServiceBooking.Infrastructure/` | `Data/AppDbContext.cs` (1 086 строк, вся Fluent-конфигурация) и `Migrations/` (79 миграций + снапшот; последняя — `Cycle28ShowcaseMarks`, цикл 28) |
| `ServiceBooking.API/` | Веб-приложение, в нём же **вся бизнес-логика** (отдельного слоя Application нет) |
| `ServiceBooking.API/Program.cs` | **Точка входа**, 48 строк — только порядок вызовов расширений |
| `ServiceBooking.API/Startup/` | Инфраструктура запуска: `ApiExtensions` (MVC/JSON/CORS/Swagger/обработчик 500/раздача загрузок), `AuthenticationExtensions` (JWT, `OnTokenValidated`), `ApplicationServicesExtensions` (DI прикладных сервисов, фоновые задачи, подтверждение телефона), `NotificationServicesExtensions` (транспорты, реестры, Web Push), `RateLimitingExtensions`, `DeploymentValidationExtensions` (fail-fast проверки прод-конфигурации), `HealthEndpointsExtensions` (`/api/health/live`, `/api/health/ready`), `LoggingExtensions`, `StartupSeedingExtensions` (миграции, роли, суперадмин) |
| `ServiceBooking.API/Controllers/` | 52 контроллера и 2 помощника (`BookingEndpointHelpers`, `AdminAccountDtoBuilder`). Мелкие DTO часто лежат в конце файла контроллера |
| `ServiceBooking.API/DTOs/<Домен>/` | Крупные DTO (`record`), `DTOs/Common/` — `PagedResult<T>`, `Optional<T>` |
| `ServiceBooking.API/Services/` | Бизнес-логика. В корне 45 файлов общего назначения (слоты, доступ, телефоны, файлы, изображения, проверки деплоя…) и предметные подкаталоги: `Billing/` (36), `Bookings/` (7), `Companies/` (8), `Legal/` (20), `Notifications/` (30 + `GreenApi/`, `GreenApiMax/`, `WebPush/`), `Orders/` (23 + `Notifications/`, `Reports/`), `Shops/` (30), `PhoneVerification/` (13 + `Max/` 16), `StaffMax/` (7), `Retention/` (+ `Rules/` 27 правил), `Scheduling/` (+ `Tasks/` 10 задач), `Subjects/` (права субъекта ПДн), `Signals/` (GlitchTip), `PublicSites/`, `Health/`, `Hosting/` |
| `ServiceBooking.API/App_Data/legal/` | **Собранный** артефакт правовых текстов (18 файлов, `legal.json`). Лежит в git, руками не правится |
| `ServiceBooking.LegalKit/` | CLI `build/check/links/publish/rollback/status` для правовых текстов. Ссылается на API как на библиотеку |
| `ServiceBooking.TestKit/` | Изоляция тестовых БД (Testcontainers, лизинг баз `sbtest_<key>_<slot>`), CLI `status/sweep/doctor` |
| `ServiceBooking.UnitTests/` | 177 файлов `.cs`, 1 581 атрибут `[Fact]`/`[Theory]` |
| `ServiceBooking.Tests/` | Функциональные тесты API: `Infrastructure/` (27 файлов: фабрики хоста, фикстуры), `Tests/` (81 файл, 1 063 атрибута `[Fact]`/`[Theory]`) |
| `frontend/src/` | Приложение **ezbook.ru** (запись на услуги). Точка входа — `main.tsx` → `App.tsx` |
| `frontend/goods/` | Приложение **goods.ezbook.ru** (заказы на самовывоз). Точка входа — `goods/src/main.tsx` → `GoodsApp.tsx`; `goods/index.html`, `goods/public/` (свой `sw.js`, манифест) |
| `frontend/scripts/merge-goods-dist.mjs` | Кладёт сборку goods в `dist/__goods/`, чтобы оба сайта уезжали одним релизом |
| `frontend/scripts/screenshots/` | Цикл 30: локальный стенд `sb-shots` (`stack.sh`), засев демо-данных goods через HTTP API (`seed-goods-demo.mjs`, `demo-data.mjs`), съёмка кадров (`capture-goods-screenshots.mjs`), `README.md` пересъёмки. В сборку и CI не входят (§5.7) |
| `frontend/goods/src/assets/screenshots/` | Цикл 30: 5 WebP-кадров главной goods, манифест `screenshots.json`, модуль подключения `shots.ts` |
| `contracts/` | Машиночитаемые контракты по циклам (`cycleN/openapi.yaml`, JSON-схемы и векторы), `redocly.yaml`. Цикл 30 — только JSON-схемы `cycle30/screenshots-manifest.schema.json` и `seed-state.schema.json`, OpenAPI нет |
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
последняя — `20260930004926_Cycle25OrdersInsightsMax`. **Циклы 26, 27, 29 и 30 миграций не добавляли** (✔ на `aeed251`).

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

**264 маршрута в контроллерах плюс 2 health (`/api/health/live`, `/api/health/ready`) — итого 266.** Эталон со всеми
атрибутами — `ServiceBooking.Tests/Tests/Cycle22RouteTable.golden.txt` (266 строк: авторизация, rate limit, фильтры
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
- Галерея — auth: `POST /api/companies/{id}/photos`, `DELETE /api/companies/{id}/photos/{photoId}` (`?reason=DepictedPersonRequest`
  только для SA), `PUT /api/companies/{id}/photos/order`.
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
  (42 места в контроллерах), глобального лимитера нет.
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
- `frontend/src/types/api-cycle*.generated.ts` для циклов 7, 9, 14, 18, 19, 20, 23, 24, 25, 26, 29 — сверочные генераты
  контрактов. Часть из них ни один модуль не импортирует, их держит только CI-сверка. Прикладные типы — рукописные
  `src/types/index.ts` и `goods/src/types.ts`.
- `openapi-cycle6.yaml` в корне и `contracts/cycle{10,11,15,17}/openapi.yaml` — исторические контракты, в CI не линтуются.
- Корневые `ARCHITECTURE.md` и `API_CONTRACT.md` — это документы **цикла 3** (последняя правка — 2026-09-17), а не текущая
  архитектура.
- ↪ C22-1: восемь навигационных свойств EF без ссылок в коде.

### 5.5 Что сделали последние циклы

**Цикл 30 — блок «Для покупателей» и скриншоты на главной goods** — в ветке `cycle/030-…`, в `develop` пока не влит.
Подробно — §5.7.

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

**Цикл 28, проход A — тарифы «Записи», витринные данные (✔ backend; ветка `cycle/028-showcase-data-demo-stand`, в `develop` не влит, на бою нет).** Документы: `ARCHITECTURE_CYCLE28.md` §570–§583,
`API_CONTRACT_CYCLE28.md` §590–§603, `contracts/cycle28/openapi.yaml`. Проход B (демо-стенд: `/api/demo/*`, `DemoMode`, сброс, `[DemoForbidden]`) — описан в следующем блоке («Цикл 28, проход B»).
- **Одна миграция `Cycle28ShowcaseMarks`** (только добавления): `AspNetUsers.IsShowcase`, `BillingAccounts.IsShowcase`, `Companies.IsShowcase` и `ShowcaseBookingOpen`, `Bookings.ShowcaseKind`
  (`ShowcaseBookingKind`: 0 обычная, 1 генератор, 2 посетитель), CHECK `CK_Companies_ShowcaseBookingOpen`, четыре частичных индекса. Enum'ы только дописаны: `NotificationReason.ShowcaseSuppressed`,
  `PublicArea.Showcase`, `LegalTextKey.ShowcaseNotice/ShowcaseBookingClosed/DemoBanner` (вне `All`).
- **Команды оператора внутри образа API** (`Program.cs`: `OpsCommandLine.Parse(args)`, первый аргумент `ops`; Serilog в режиме `ops` пишет в stderr, файл не трогает): `Services/Ops/`. HTTP-маршрута нет.
  `ops tariffs plan|apply` (`Services/Showcase/Tariffs/`: `ZapisTariffCatalog`, `TariffCatalogSeeder`) — только создаёт недостающее; выравнивает системный бесплатный тариф в «Старт» по полям, совпадающим со значением сида цикла 7.
  `ops showcase plan|create|recreate|delete [--yes]` (`Services/Showcase/`: `ShowcaseDataset` — чистая функция `(профиль, now)`, `ShowcaseRandom` xorshift64\*, `ShowcaseIds` UUIDv5, `ShowcasePhones` блок `72005550000–72005559999`,
  `ShowcaseGenerator`, `ShowcaseEraser` + `ShowcaseOwnership` — порядок физического удаления, `ShowcaseAssetStore` + `ServiceBooking.API/ShowcaseAssets/`, `ShowcaseCommands` — одна транзакция под advisory-lock `ops:showcase`).
  На тестовой БД: создание 9 компаний, 158 пользователей, ~9 тыс. записей — 4–13 с; `recreate` даёт тот же хеш записей на одну дату.
- **Запрет смешивания** — `ShowcaseMixingGuard` (члены, смена ответственного, перенос, служебный тариф, зарезервированный слаг `primer-`); `AuthController.Login` отказывает витринным учёткам тем же 401; выборки «по номеру»
  (`SubjectDataExporter`, `AccountDeletionService`, `ProfileConsentsController`, `GuestBookingLookup`) исключают витрину, маркеры `SUBJECT-PHONE-GATE` сохранены.
- **Исходящие** — `ShowcaseOutboundGuard`: `NotificationScheduler.BuildContextAsync`, `StaffPushScheduler` (2 входа), `MailingController.SendMail` (409); страховка в `NotificationDispatchTask` и `StaffPushDispatchTask` (`Skipped` / `ShowcaseSuppressed`).
- **Запись** — `BookingCreationService`: после `AllowSelfBooking` 409 JSON `ShowcaseBookingClosed` для закрытой витрины (не для персонала); запись в витринной компании помечается `Visitor`; правило ретенции
  `ShowcaseVisitorBookingRule` (24 ч, `Retention:ShowcaseVisitorBookingHours`); `InactiveAccountRule` пропускает витрину. `CompanyDto` +`isShowcase`/`showcaseBookingOpen`, `BookingDto` +`companyIsShowcase`.
- **Админка** — параметр `showcase=all|only|exclude`, поля `isShowcase`, `stats` без витрины + три счётчика.
- **Решения заказчика Q28-1…Q28-4 в коде:** `EffectivePlan.Free` теперь `AllowOnlineBooking=true`, `AccountMaxEmployees=2` (**меняет поведение всех существующих бесплатных компаний**); `TrialTermsRegistry` — новая редакция `2026-09-30`
  без обещания рассылок и без параметра `{3}` (`RenderCurrent(plan, days, endsAt)`), редакция `2026-09-26` сохранена; `TrialActivationService` не шлёт сигнал, если у пробного тарифа `AllowNotificationChannel = false`.
- Тесты: unit +180 (`OpsCommandLineTests`, `ZapisTariffCatalogTests`, `Showcase*Tests`, `TrialMailingRulePolicyTests`, …); в функциональных обновлены 6 тестов под новый бесплатный тариф (`CompaniesTests`, `AdminTests`).
  Функциональных тестов цикла 28 backend не писал — их пишет QA (`CY28-*`).

**Цикл 28, проход B — демо-стенд (✔ backend: код и прогон на локальной БД; ветка `cycle/028-showcase-data-demo-stand`, в `develop` не влит, на бою нет, выкат DO-2 не выполнялся).** Документы: `ARCHITECTURE_CYCLE28.md` §579–§581,
`API_CONTRACT_CYCLE28.md` §597–§600a, `API_DOCUMENTATION.md` §4.21.8. Новых миграций и зависимостей нет. Всё ниже **инертно при `DemoMode:Enabled=false`** (по умолчанию): маршруты отвечают 404 без тела, middleware и фильтр проходят насквозь,
сброс отказывает кодом 2, задача `demo-reset` не регистрируется.
- **Настройки** `DemoMode` (`DemoModeOptions`): `Enabled`, `ResetLocalTime` (`04:00`), `TimeZoneId` (`Europe/Moscow`), `MaintenanceFlagPath` (`App_Data/state/demo-resetting`); лимит `RateLimits:demo-login` 30/мин на IP (вне демо лимитер «не включается», чтобы 404 не превращался в 429).
- **Два замка** (`Services/Demo/`): `DeploymentSafetyChecks.ValidateDemoMode` (конфигурация, до `Build()`, любое окружение: хосты `demo.*`, БД `*_demo`, `Jwt:Issuer` `*.Demo`, провайдеры `logging`/`stub`, StaffMax и пересев витрины выключены; нарушения собираются в одно сообщение)
  и `DemoInstanceGuard` (данные; вызывается из `StartupSeedingExtensions` сразу после `MigrateAsync`: метка `PlatformSettings` `instance.kind = demo`, на пустой БД пишется, на БД с невитринной компанией или записью `ShowcaseKind = None` — отказ старта).
- **Маршруты** `DemoController` (`[DemoOnly]` — `IResourceFilter`, 404 до действия): `GET /api/demo/status` (`DemoStatusDto`), `POST /api/demo/login` (`DemoLoginService`: учётка роли по стабильному Id из `ShowcaseDemoRoles`, `TokenService.GenerateToken(…, demo: true)` — claim `sb_demo = 1`
  и текущие версии документов; 400/409/404/429 по контракту). Эталон `Cycle22RouteTable.golden.txt`: +2 маршрута и атрибут `[DemoForbidden]` на семи существующих действиях (`change-password`, `change-phone`, `delete-account`, `subscription/request`, `trial`, `transfer`, `PUT …/owner`).
- **Конвейер:** `DemoResponseHeadersMiddleware` (`X-Robots-Tag: noindex, nofollow`) и `DemoMaintenanceMiddleware` (503 с `Retry-After` и `X-Demo-Resetting`; исключения `/api/health/*` и `GET /api/demo/status`) стоят первыми после `UseForwardedHeaders`, проверяют `DemoMode:Enabled` на каждом запросе.
  `DemoForbiddenFilter` — глобальный `IAsyncResourceFilter` (403 `text/plain` + `X-Demo-Restricted`; срабатывает до привязки модели, после авторизации). Заголовки `X-Demo-*` и `Retry-After` объявлены в CORS (`WithExposedHeaders`).
  Флаг обслуживания — файл (`DemoMaintenanceFlag`, singleton, кеш 1 с, зависший старше 10 минут игнорируется с `LogError`); общий для API и процесса `ops`.
- **Сброс** (`DemoResetService`, одна процедура для `ops demo reset [--yes]` и фоновой `DemoResetTask`): оба замка → advisory-lock `ops:showcase` (код 4) → флаг → **одна транзакция**: `TRUNCATE` всех таблиц модели кроме списка разрешённых (`DemoResetTables`: строится из модели EF; 66 таблиц; разрешены
  `__EFMigrationsHistory`, `Cities`, `AspNetRoles`, `SubscriptionPlanConfigs`, `SubscriptionOptions`, `PlanOptionRules`, `PlatformSettings`, **`ScheduledTaskStates`** — добавлена к списку архитектора), `TariffCatalogSeeder.ApplyAsync` (внутри внешней транзакции), генератор с профилем `demo`, `pricing.public-enabled = true`; коммит;
  после коммита — `FileStorage.ClearAllFiles` (оба корня хранилища, кроме картинок новых данных; отказывается трогать корень приложения и корень файловой системы), `SuperAdminSeeder` (вынесен из `StartupSeedingExtensions` без изменения поведения; сам открывает транзакции, поэтому не может быть внутри общей), метка `demo.last-reset-utc`, снятие флага.
  Сбой до коммита откатывает всё (TRUNCATE транзакционен): вчерашнее демо цело. `TRUNCATE` без `CASCADE` (отклонение от §580: ошибка на FK лучше тихой очистки каталога тарифов). Ночная задача `demo-reset` (период 10 мин, `DemoResetSchedule`) **не** делает первый сброс: без метки `demo.last-reset-utc` она ждёт команды оператора.
  Измерено на локальной Postgres 16 без картинок (`ShowcaseAssets/` пуст): сброс 6–7 с, 66 таблиц, 159 пользователей, ~9,3 тыс. записей, ~19,5 тыс. событий.
- **Профиль «demo»** (`ShowcaseProfile.Demo` — флаги `DemoRoles`, `Reviews`, `ClientNotes`, `RichHistory`, `RescheduleChance = 0,18`; `ShowcaseDataset`): тот же набор, что у `prod`, плюс три роли (`ShowcaseDemoRoles`: владелец «Лаванды» на публичном тарифе «Салон», `PaidUntil` = сегодня + 30; мастер `lavanda:m0`; клиент «Мария Климова» —
  визиты берутся из существующих гостевых слотов в «Лаванде», «Жемчуге» и «Взгляде»: 3–4 прошлых и 1–2 будущих в каждой), отзывы (~30 % завершённых визитов зарегистрированных клиентов; средний рейтинг 4,0–4,4 по компаниям; у демо-клиента остаются неоценённые визиты),
  заметки мастеров (нейтральные, без здоровья и фото; больше у демо-мастера), история переносов (часть записей переносится дважды). Профиль `prod` не изменился ни на байт (хеш графа совпал с состоянием до правки).
- **Тесты:** unit +117 (`DemoModeValidationTests`, `DemoMaintenanceTests`, `DemoResetTests`, `DemoInstanceAndStorageTests`, `ShowcaseDemoProfileTests`; правка `ShowcaseDatasetTests` — списки отзывов/заметок пусты у `prod`); полный прогон `ServiceBooking.Tests` зелёный (1197/1198, 1 пропущен, прежний), эталон маршрутов обновлён.
  **Функциональных тестов цикла 28 прохода B backend не писал** — их пишет QA (`CY28-23…30`).
- **Вручную проверено на локальной БД** (не заменяет функциональные тесты): статус и вход под тремя ролями, 403 с заголовком (в том числе с пустым телом), 409 до первого сброса, 503 во время сброса, токен до сброса жив после него, файлы очищены, зарегистрированный посетитель стёрт, отказ старта на «боевой» конфигурации и на БД с реальными данными, код 2 у `ops demo reset` вне демо, ночная задача (стартовала по устаревшей метке, затем «not due»), 404 без тела и без 429 на 40 запросах вне демо.

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
Одиннадцать `IScheduledTask` (с цикла 28), регистрируются поимённо; двенадцатая, `demo-reset`, — только при `DemoMode:Enabled` (проход B):

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
| `showcase-reseed` | main | (цикл 28) раз в неделю пересоздаёт **уже существующую** витрину; период 1 ч; **выключена** (`Showcase:Reseed:Enabled=false`), в `Testing` выключена. Время последнего пересева — `PlatformSettings` `showcase.last-reseed-utc` |
| `demo-reset` | main | (цикл 28, проход B) **регистрируется только в демо-режиме**; период 10 мин; ночной сброс демо (`DemoResetService`), когда локальное время прошло `DemoMode:ResetLocalTime`, а `PlatformSettings` `demo.last-reset-utc` старше сегодняшнего слота. Без метки не делает ничего (первый сброс — команда оператора). `MaxRunMinutes=5`, в `Testing` выключена |

Тик `main` — 10 с, тик `realtime` — 1 с. В окружении `Testing` все задачи выключены (`appsettings.Testing.json`).

### 5.7 Главная goods, путь покупателя, доска заказов, изображения во фронте — цикл 30 (✔ по коду на `aeed251`)
Раздел описывает состояние **после цикла 30** («блок „Для покупателей“ и настоящие скриншоты на главной goods»), ветка
`cycle/030-user-section-screenshots`, в `develop` пока не влита. Документы цикла: `SPEC.md` (корневой),
`ARCHITECTURE_CYCLE30.md` (§30.0–§30.15), `API_CONTRACT_CYCLE30.md` (§30.20–§30.27; «API не меняется», только формы
файлов засева и манифеста). Бэкенд, миграции, маршруты и OpenAPI цикл 30 не менял ✔ (`git diff origin/develop..HEAD`).

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

---

## 6. Конвенции проекта (✔ выборочно по коду; им следовать, а не вводить рядом свои)

### 6.1 Бэкенд (C#)
- **Primary constructors** у контроллеров и сервисов, file-scoped namespaces. Полей `_db` нет.
- **DTO — позиционные `record`.** AutoMapper нет, маппинг ручной (`*Mapper`, `*Assembler`, `MapToDto`). Новое поле
  дописывается **в конец** record'а с дефолтом, чтобы позиционные вызовы продолжали компилироваться. Атрибуты валидации
  ставятся на **параметры** конструктора: `[property: Required]` даёт 500.
- **Чистые правила — статические классы без EF и HTTP** в `Services/<Домен>/`: `*Rules`, `*Policy`, `*Texts`,
  `*Calculator`, `*StateMachine`. Они и покрываются юнит-тестами. Контроллер держит права, транзакцию и advisory-lock.
  Поднимать `AppDbContext` в юнит-тестах в проекте не принято, логика с БД проверяется функционально.
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
  запрещено ESLint. goods может импортировать из ezbook-страниц только разрешённый список: `LegalDocumentPage`,
  `SubjectRequestPage`, `ConsentsPage`, `LoginPage`, `RegisterPage`, `NoticesPage`, `BillingPage`,
  `owner/NotificationsSection`, `owner/CompanyPhotosSection`.
- ⚠️ **Внутри `frontend/src/**` импорты только относительные.** Алиас `@` объявлен в `tsconfig.json`, `vitest.config.ts`
  и `vite.goods.config.ts`, но **не в `vite.config.ts`**. Импорт через `@/` в `src/` проходит `tsc` и тесты, а
  `vite build` ezbook на нём падает (это и чинил `491406c`).
- **Типы:** прикладные — рукописные (`src/types/index.ts`, `goods/src/types.ts`). Для формы контракта цикла — генерат
  `npm run types:api:cycleNN` из `contracts/cycleNN/openapi.yaml`, руками его не правят.
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
  архив ведётся суффиксами в корне. Нумерация § в документах сквозная через циклы (цикл 26 — §543–§569).

---

## 7. Тесты, CI и деплой (✔ по конфигам; ничего не запускалось)

### 7.1 Наборы тестов
| Набор | Фреймворк | Объём (✔ подсчёт атрибутов) | Последний известный прогон (↪ из отчёта QA цикла 30, после мерджа цикла 29, 30.09.2026) |
|---|---|---|---|
| Юнит бэкенда `ServiceBooking.UnitTests` | xUnit 2.5.3 + FluentAssertions 6.12; без БД и без Docker | 177 файлов, 1 581 `[Fact]/[Theory]` (на `1837373`; цикл 29 добавил тесты, заново не пересчитывалось) | 2 407/2 407 (так же на `b23c13c`, цикл 29) |
| Функциональные API `ServiceBooking.Tests` | xUnit 2.5.3 + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) + **реальный PostgreSQL 16** через Testcontainers 3.10 (или внешний сервер) | 81 файл тестов, 1 063 `[Fact]/[Theory]` (на `1837373`; цикл 29 добавил 3 файла, заново не пересчитывалось) | 1 153/1 153 (так же на `b23c13c`) |
| Фронтенд (оба сайта) | Vitest 3.2 + jsdom + Testing Library | 162 файла `*.test.ts(x)` ✔ на `aeed251` | 1 194/1 194 на ветке цикла 30 (с пустой `VITE_SMARTCAPTCHA_SITEKEY`; с локальным `.env` — 8 падений `CartPanel`, §7.2); на `b23c13c` было 1 157/1 157 в 157 файлах |

Там же после мерджа зелёные `tsc` (ezbook, goods), `lint`, `build:release` (↪ отчёт; `typecheck:node` в отчёте отдельно
не назван).

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
- Подмножество: `dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~Cycle26CompanyCardTests"`.

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
  - сверка генератов циклов 7, 9, 14 и отдельным шагом 18, 19, 20, 23, 24, 25, 26, 29 (26 и 29 — с цикла 29);
  - `redocly lint` контрактов 8-invariant, 13, 14, 16, 18, 19, 20, 23, 24, 25, 26, 29;
  - `npm audit --omit=dev --audit-level=high`;
  - `lint`, `tsc` для ezbook и для goods, `npm run typecheck:node` (шаг «Type-check node (shots)», цикл 30);
  - греп на заглушку правового текста и на `fetch`/Cache в `sw.js`;
  - `test:run`;
  - `build:release` — оба сайта;
  - смоук `dist` и `dist/__goods`;
  - артефакт `frontend-dist-<sha>` для `master`/`release-candidate`/`develop`.
- **docker-build** — сборка образа API и запуск в режиме `Production` с Postgres. Это заодно доказывает полноту
  обязательных env-переменных. Затем `smoke.sh`.
- Контракты 26 и 29 в CI охвачены с цикла 29 (C26-1 закрыт). JSON-схемы `contracts/cycle30/*.schema.json` в CI **не**
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
- **C26-7. Счётчик разделов «Не выпущено» во вступлении CHANGELOG снова отстаёт** (✔ на `aeed251`): в тексте «двадцать
  три», по факту разделов `## Не выпущено` — 25 (с циклом 29). Мелочь, но повторяется каждый цикл.
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
  - `frontend/src/pages/owner/CompanyManagePage.tsx` — 1 327 строк (на `aeed251`, после групп полей цикла 29);
  - `NotificationChannelsController` — 845 (не режется по решению цикла 22);
  - `AdminBillingController` — 791, `AdminController` — 675, `BookingsController` — 595;
  - `AppDbContext` — 1 086.
- **Модель релизов разошлась с документами.** Кнопка «staging» фактически выкатывает боевой ezbook.ru, `master`/теги/
  `release-candidate` не используются. CHANGELOG держит 25 разделов «Не выпущено», хотя часть из них на бою (циклы 20–22).

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

### 9.3 Долг цикла 28 (проход A)
- **C28-1 / L28-1.** Тексты `ShowcaseNotice`, `ShowcaseBookingClosed` — нейтральные заглушки без юриста (вне `LegalTextKey.All`, запасные тексты на фронте и в `ShowcaseTexts`). Закрыть: текст в живой `legal.json`, ключ в `All`.
- **C28-2 / L28-2.** `/pricing` с ценами включается без вычитки оферты (НДС, оплата, возврат). `pricing.legal-notice` пуст.
- **C28-2b / L28-2b.** Условия пробного периода (редакция `2026-09-30`, Q28-4) правились без юриста. Кроме того, текст статуса окна рассылок пробного периода (`TrialLegalNotices.TrialMailingWindowNotStarted`, показывается в «Ваша подписка»)
  всё ещё говорит «рассылки клиентам входят в пробный период»: он не менялся, это то же противоречие.
- **C28-3 / L28-3.** Демо-баннер (`DemoBanner`, текст вне `LegalTextKey.All`, запасной на фронте) — заглушка без юриста. Редакция правовых документов для демо **остаётся долгом**: демо монтирует те же опубликованные документы боя (`legal/` только для чтения); срок хранения данных на демо — ≤ 24 ч.
- **C28-4 / L28-4.** Изображения витрины: каталог `ShowcaseAssets/` пуст (манифест без записей) до задачи CT-1; заключение по лицензиям и людям на фото не получено. Пока файлов нет, витрина создаётся без картинок, пункт US-28-03 «3–6 фото» не выполнен.
- **C28-5.** Пул клиентов витрины (120 + 300) на ~9 тыс. записей даёт ~20 визитов на клиента за 60 дней — данные правдоподобны в отчётах, но не в карточке отдельного клиента. Размер пула — параметры `ShowcaseProfile`.
- **C28-6.** Отображаемое имя бесплатного тарифа в кабинете владельца без подписки и в тексте 402 при лимите мест — зашитое «Бесплатный» (`OwnerSubscriptionService`, `CompanyMembersController`, `CompanyTransferService`, `AdminAccountDtoBuilder`), на `/pricing` после `ops tariffs apply` — «Старт».
- **C28-7.** Диапазон `+7 (200)` — проверка по реестру Россвязи (открытые CSV) не выполнена (шаг выката); фильтры §574.5 работают независимо от диапазона.
- **C28-8.** Хвосты генератора: ~~вход сброса (`ops demo reset`) отвечает кодом 2~~ — **закрыто проходом B** (команда реализована, код 2 остался только вне демо); правила выдачи прав `Master` для владельца-одиночки не заводятся (только `CompanyOwner`, как в продукте).

### 9.4 Долг цикла 28 (проход B — демо-стенд)
- **C28-9.** Сброс демо не измерен с картинками и на машине заказчика: измерение 6–7 с сделано на локальной Postgres без файлов (`ShowcaseAssets/` пуст, CT-1 не выполнена). Бюджет ≤ 3 мин проверяет QA (`CY28-28`); память стенда (§581.2, оценка 250–400 МБ) — замер через сутки после выката: команды и таблица для записи цифр готовы в `DEPLOY.md` §25.10, **замер остаётся ручным шагом** (стенд на машине не развёрнут).
- **C28-10.** Отклонения от §580 (записаны здесь, контракт не менялся): (1) `ScheduledTaskStates` добавлена в список разрешённых таблиц — иначе ночной сброс стирал бы состояние всех задач, и все они срабатывали бы разом; (2) `TRUNCATE` без `CASCADE`;
  (3) `SuperAdmin` создаётся **после** коммита общей транзакции (его сид открывает собственные транзакции): при сбое именно этого шага демо остаётся без администратора, код выхода 1, флаг снимается, а метка `demo.last-reset-utc` не ставится — следующий тик повторит сброс.
- **C28-11.** Первый сброс демо — только командой оператора (`ops demo reset --yes` после первого запуска API, который ставит метку `instance.kind`): ночная задача без метки `demo.last-reset-utc` ничего не делает. Если стереть `PlatformSettings` руками, нужно снова запустить API и сделать сброс командой.
- **C28-12.** Функциональному тесту демо-режима нужна БД с именем на `_demo` (замок 1) — слот тестовой БД `sbtest_<ключ>_demo` подходит под шаблон `TestDatabaseNaming`; `ServicesBaseUrl`, `AllowedOrigins`, `Jwt:Issuer` и провайдеры тест задаёт сам. Замок нельзя обойти переменной: проверка идёт в любом окружении.
- **C28-13.** Кеши в памяти после сброса: при сбросе командой `ops` (другой процесс) кеш каталога цен API живёт до 60 с, а кеш ответа флага обслуживания — 1 с (сброс ждёт 1,3 с после поднятия флага, чтобы API его увидел). Посетитель может на эту минуту увидеть устаревший каталог цен.
- **C28-15.** Пробный запуск демо с `ASPNETCORE_ENVIRONMENT=Production` (проверено вручную) требует переменных, которых нет в перечне `.env.demo.example` из §581.1 архитектора: `Trial__PhoneKeyHmac` и `Trial__PhoneKeyId` (`ValidateTrialSecrets`, иначе старт падает), плюс `ForwardedHeaders__TrustedNetworks__0` и `SuperAdmin__Password` без заглушки (они в перечне есть). **Закрыто DO-4 (заготовка):** все четыре переменные есть в `.env.demo.example` и `docker-compose.demo.yml` (`DEMO_TRIAL_PHONEKEY_HMAC`/`_ID`, `DEMO_FORWARDEDHEADERS__TRUSTEDNETWORKS__0` с фиксированной подсетью проекта, `DEMO_SUPERADMIN_PASSWORD` — обязательные, без значений по умолчанию); проверено `docker compose config`, но на машине стенд ещё не запускался.
- **C28-14.** Запись посетителей демо в открытые витринные компании по-прежнему ограничена капчей: хост `demo.visit.ezbook.ru` нужно добавить в консоль SmartCaptcha (ручной шаг `DEPLOY.md` §25.6, заказчик); без него гостевая запись на демо отвечает «Invalid captcha», запись под ролью клиента работает.

---

## 10. Что уже существует в документации и тест-кейсах (дополнять существующее, параллельных версий не заводить)

### 10.1 Краткая продуктовая документация
- **`README.md`** (743 строки на `b9c2a79`, markdown). Два раздела верхнего уровня:
  - **«О проекте»** (строки 3–580). Абзацы с жирными подзаголовками, а не `###`: что это; где работает («стенд» ezbook.ru);
    отдельный блок «Заказы на самовывоз — goods.ezbook.ru»; возможности по ролям; тарифы; «Чего пока нет»; «Почему это
    стенд, а не запуск».
  - **«Запуск»** (со строки 581) с подразделами `###`: «Локально, всё в Docker», «Локально, без Docker для API»,
    «Переменные окружения и секреты», «Тесты», «CI», «Деплой».
  - Цикл 26 в README отражён (`b9c2a79`): профиль и фото магазина, смена города, общая карточка салона/магазина.
    Логотип/первая буква в карточках упомянуты. **Блок «Для покупателей» и скриншоты главной goods (цикл 30) в README
    на `aeed251` не описаны** ✔ (grep «Для покупателей»/«скриншот» — пусто). Инструкция пересъёмки для команды лежит
    отдельно — `frontend/scripts/screenshots/README.md` (§5.7).
- **`CHANGELOG.md`** (4 089 строк на `aeed251`). Формат по мотивам Keep a Changelog, язык — для пользователей. Номеров версий нет.
  Раздел `## …` на каждый цикл, **самый свежий сверху**. Сверху идут 25 разделов «Не выпущено — <суть>» в порядке влития,
  ниже — датированные разделы (`## 2026-09-17 — сервис впервые развёрнут…`, самый ранний — `2026-09-01`). Верхний раздел
  сейчас — цикл 29 (строка 20), под ним цикл 27 (строка 48), цикл 26 (строка 62), затем цикл 25. **Раздела цикла 30 на
  `aeed251` нет.** Внутри крупных
  разделов сложились подзаголовки `###`: «Что стоит прочитать до выката», тематические блоки, «Чего этот цикл не даёт»,
  «Для команды» (итоги тестов и документы цикла). Даты «Не выпущено» при выкате не проставлялись. Счётчик во вступлении
  («двадцать три») отстаёт от факта (25) — C26-7.
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
- Справки внутри приложения: правовые документы и тексты интерфейса отдаются из `/api/legal/*` (исходники —
  `legal-drafts/`) и показываются на `/privacy`, `/terms` и т.д. Отдельной базы знаний или сайта документации нет.
- Эксплуатация: `DEPLOY.md` (2 457 строк, §0–§23, выкат, переменные, ручные шаги по циклам), `DEPLOY-windows.md`,
  `INCIDENT_CHECK_PROCEDURE_CYCLE16.md`. Правовые внутренние документы — `legal-internal/*.md`, `LEGAL_REVIEW*.md`,
  `LEGAL_DECISIONS_CYCLE20.md`, `legal-drafts/README.md`.

### 10.3 Документация API для внешних потребителей
- **`API_DOCUMENTATION.md`** (4 481 строка, markdown, по-русски) — основной справочник:
  - §1 «Обзор», §2 «Аутентификация», §3 «Ключевые бизнес-концепции»;
  - §4 «Справочник эндпоинтов» — по доменам, с подразделами «Цикл N (unreleased)», у цикла 26 — строка ~1955; цикл 29
    вписан точечно (`logoUrl` каталога — ~4168, 400 на битую форму — в §6, ~4384). Цикл 30 API не менял и сюда не вписан;
  - §5 «Сквозные сценарии», §6 «Справочник кодов ответа», §7 «Известные ограничения».
  - ↪ C23-8: в §4 есть повторяющиеся номера подразделов.
- **OpenAPI по циклам** — `contracts/cycleN/openapi.yaml` для циклов 7, 9, 10, 11, 13–20, 23–26, 29 (у 26 и 29 есть ещё
  `openapi.json` — для валидатора функциональных тестов `OpenApiContract.cs`) и
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
- **`TEST_CATALOG.md`** (6 415 строк на `aeed251`, markdown):
  - в начале — «Как устроены ссылки на тесты» и «Префиксы по доменам»;
  - затем разделы по доменам (Auth, Bookings, Companies…);
  - затем разделы по циклам: «Цикл N — … (`CYNN-`, число тестов, файл)», в каждом таблица `ID | US | Что проверяет`.
    Там же ручные кейсы (`M27-…` в ветке 27), вердикты QA и «не покрыто»;
  - порядок разделов в конце: цикл 27 (~6226), цикл 29 (~6240), цикл 30 (~6278), цикл 26 (~6294), **затем цикл 20**:
    разделы идут в порядке влития/написания, а не по номеру. У циклов 27, 29, 30 кроме автотестов есть таблицы ручных
    кейсов (`M27-`, `M29-`, `M30-`) с колонками `Кейс | Шаги | Ожидаемый результат | Критерий` (у M29 ещё `Вердикт`).
    Раздел цикла 30 перечисляет vitest T30-01…19 и QA30-01…10 одной строкой (без таблицы по ID) и M30-01…09 без
    вердиктов; ссылка раздела цикла 29 на спеку исправлена на архив `SPEC_CYCLE29_CYCLE26_FOLLOWUPS.md`.
  - ↪ TD16-7: вердикты раздела «Цикл 16» устарели.
- ID кейсов продублированы в коде атрибутом `[TestCase("CY26-01")]` на тестах `ServiceBooking.Tests`.
- Ручные живые проверки выката — чек-листы в `DEPLOY.md` (§16 и шаги циклов).
- Отдельного `TESTPLAN.md` или `docs/testing/` нет. Каталог сценариев — только `TEST_CATALOG.md`.

### 10.5 Документы циклов
- Корень:
  - `SPEC.md` — спека **цикла 30** (блок «Для покупателей» и скриншоты). Следующий цикл заменит её своей, а эту
    заархивирует как `SPEC_CYCLE30_*.md`;
  - архив спек `SPEC_CYCLE{3..27,29}_*.md`: спека 27 — `SPEC_CYCLE27_GOODS_BUSINESS_BLOCK.md` (архивирована в цикле 29 и
    повторно в ветке 30, `e633cbb`), спека 29 — `SPEC_CYCLE29_CYCLE26_FOLLOWUPS.md` (появилась в
    ветке 30 при мердже `2100fb3`, в `origin/develop` = `c19a83c` корневой `SPEC.md` — ещё спека 29); спеки 12 и 28 в корне нет (28 — в
    ветке `origin/cycle/028-…`). Плюс `SPEC_APPENDIX_CHANNELS.md` и `SPEC_DEFERRED_NOTIFICATIONS.md`;
  - `ARCHITECTURE_CYCLE{4..27,29,30}.md` (у цикла 8 два файла: основной и `_PHASE2`) и `API_CONTRACT_CYCLE{4..27,29,30}.md`
    (у 27 — «API не меняется», у 30 — только формы файлов засева/манифеста, §30.20–§30.27). Циклы 29 и 30 нумеруют § по
    номеру цикла (§29.x, §30.x), а не сквозной нумерацией. Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — от цикла 3;
  - `LEGAL_REVIEW*.md`.
- `docs/history/` не существует: архивом служат суффиксы в корне.

---

*Конец документа. Раздел 8 оставлен пустым намеренно: номера разделов 9 и 10 совпадают с прежней редакцией, на них
ссылаются другие документы.*
