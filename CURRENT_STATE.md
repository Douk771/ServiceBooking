# CURRENT_STATE — фактическое состояние кодовой базы ServiceBooking

**Актуально по состоянию на коммит: `7a551eb`, дата: 2026-09-15.**

Документ описывает **что есть в репозитории сейчас**, без предложений по развитию.

Точка отсчёта. Ветка — `sanitation-cycle`, HEAD — `7a551eb`, **рабочее дерево чистое: всё
закоммичено**. Это отличие от прошлой редакции документа, где цикл 2 целиком лежал незакоммиченным и
приходилось оговаривать, что часть описанного в git ещё не попала. Сейчас такой оговорки не нужно.

Диапазон изменений с прошлой редакции (`1c21bca` + рабочее дерево цикла 2): цикл 2 закоммичен
одним коммитом `0492092`, затем выполнен **цикл 3 «готовность к продакшену»** — **26 коммитов**,
`f3adc6e..7a551eb`. Внутри цикла видны два стиля: сначала мелкие коммиты по задачам
(`T-B1`/`T-F1`/… в заголовках), затем шесть укрупнённых (`848d172` бэкенд, `75a4473` тесты,
`45e56af` инфраструктура, `56b0448` фронтенд, `90a69b1` документы цикла, `7a551eb` пользовательская
документация) и отдельный коммит правок по ревью `99a03b0`. Полный список:
`git log --oneline --reverse 0492092..HEAD`.

Что цикл 3 делал: правовой контур (политика, оферта, согласие, 451), права субъекта данных (выгрузка
и удаление аккаунта), rate limiting на вход/регистрацию/гостевую запись/выгрузку, структурированное
логирование с маскированием телефонов и трекер ошибок, health-эндпоинты, синхронизация Identity-ролей,
пагинация четырёх выборок, бэкап/откат/мониторинг и security-заголовки, запуск боевого образа в CI.
**Новых функций для салонов цикл не добавлял.** Главное, что нужно знать дальше: **живого
развёртывания на боевом сервере так и не было** — см. §9, блок P0.

Все утверждения ниже получены чтением исходников, конфигов и git-истории. Где чего-то не нашлось —
так и написано.

**Проверено фактическим запуском** (чистый прогон 2026-09-15 после завершения работ всеми агентами;
автор этого документа работает только на чтение и тесты не запускает — прогон набора пересоздаёт базу
`servicebooking_test`, см. §7):

| Команда | Результат | Было в прошлой редакции |
|---|---|---|
| `dotnet build ServiceBooking.sln -warnaserror` | **0 warnings, 0 errors** | 0 / 0 |
| `dotnet test ServiceBooking.UnitTests` | **204 / 204** | 115 / 115 |
| `dotnet test ServiceBooking.Tests` | **404 / 404** | 344 / 344 |
| `npm run test:run` (в `frontend/`) | **78 / 78** | 35 / 35 |
| `npx tsc --noEmit` (в `frontend/`) | чисто | чисто |
| `npm run build` (в `frontend/`) | успешно | успешно |

Все три числа перепроверены **статическим подсчётом** атрибутов `[Fact]`/`[Theory]`+`[InlineData]`
и вызовов `it(...)` при подготовке этой редакции и сходятся с прогоном точно: 204, 404, 78.

---

## 1. Стек и версии

### Бэкенд

| Что | Значение | Откуда |
|---|---|---|
| Язык / рантайм | C#, `net8.0`, `Nullable=enable`, `ImplicitUsings=enable` | все `*.csproj` |
| SDK на машине | .NET SDK 8.0.203 | `dotnet --version` |
| Фреймворк | ASP.NET Core Web API (контроллеры, minimal hosting в `Program.cs`) | `ServiceBooking.API/Program.cs` |
| ORM | EF Core 8.0.11 + `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.11 | `ServiceBooking.Infrastructure/ServiceBooking.Infrastructure.csproj` |
| СУБД | PostgreSQL (в docker-compose — `postgres:16-alpine`) | `docker-compose.yml`, `docker-compose.prod.yml` |
| Аутентификация | ASP.NET Core Identity (`IdentityDbContext<AppUser>`) + JWT Bearer 8.0.11 | `Program.cs`, `Services/TokenService.cs` |
| Обработка изображений | **SkiaSharp 2.88.8** + `SkiaSharp.NativeAssets.Linux.NoDependencies` (цикл 2) — декод, ориентация по EXIF, ресайз, ре-энкод | `ServiceBooking.API.csproj`, `Services/ImageProcessor.cs` |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` (встроенный в ASP.NET Core 8), ⭐ **пять** именованных политик: `uploads`, `auth-login`, `auth-register`, `booking-create`, `data-export`; глобального лимитера нет | `Program.cs`, секция `RateLimits` |
| **Логирование** ⭐ | **Serilog.AspNetCore 8.0.3** (`CompactJsonFormatter` в stdout и `logs/app-.json`) + маскирование телефонов | `Program.cs`, `Services/LogMasking.cs`, `PhoneMaskingEnricher.cs` |
| **Трекер ошибок** ⭐ | **Sentry.Serilog 4.13.0** — синк включается только при непустом `Sentry:Dsn`; целевой приёмник — self-hosted **GlitchTip** (Sentry-совместимый) | `ServiceBooking.API.csproj`, `docker-compose.glitchtip.yml` |
| **Health-checks** ⭐ | встроенные `Microsoft.Extensions.Diagnostics.HealthChecks`, два анонимных эндпоинта со своим двухполевым ответом | `Program.cs`, `Services/Health/` |
| Фоновые задачи | Свой `BackgroundService` + `IScheduledTask` (цикл 2). Hangfire/Quartz **нет** | `Services/Scheduling/` |
| Документация API | Swashbuckle.AspNetCore 6.5.0, Swagger **только в Development** (цикл 1) | `Program.cs` |
| Правовые документы | ⭐ **файлы на диске** (`App_Data/legal/legal.json` + HTML), снимок в памяти с перечитыванием по mtime; не БД и не внешний сервис | `Services/Legal/LegalDocumentProvider.cs` |
| Стиль кода | ⭐ `.editorconfig` в корне — **описывает** уже сложившийся стиль; `dotnet format` в CI **не подключён** | `.editorconfig` |
| Менеджер пакетов | NuGet, версии зафиксированы в `.csproj` (без `Directory.Packages.props`, без lock-файлов) | — |

### Фронтенд

| Что | Значение | Откуда |
|---|---|---|
| Сборщик | Vite 5.4.x, dev-порт 5173, прокси `/api` и `/uploads` → `process.env.VITE_API_TARGET ?? http://localhost:5000` (порт переопределяется переменной — на macOS 5000 занят AirPlay, коммит `75c5c3c`) | `frontend/vite.config.ts` |
| Тест-раннер | **Vitest 3.2 + jsdom 25 + @testing-library/react 16 + @testing-library/jest-dom + user-event** (появился в цикле 2, US-23); конфиг **отдельный от vite.config.ts** | `frontend/vitest.config.ts`, `frontend/src/test/setup.ts` |
| Библиотека UI | React 18.3 + React DOM 18.3, TypeScript 5.5 (`strict`, `noUnusedLocals`, `noUnusedParameters`) | `frontend/package.json`, `frontend/tsconfig.json` |
| Роутинг | `react-router-dom` 6.26 | `frontend/src/App.tsx` |
| Server state | `@tanstack/react-query` 5.56 (`retry: 1`, `staleTime: 30_000`) | `frontend/src/App.tsx` |
| Client state | `zustand` 4.5 + `persist` (ключ localStorage `auth-store`) | `frontend/src/store/authStore.ts` |
| HTTP-клиент | `axios` 1.7, единственный инстанс с `baseURL: '/api'` | `frontend/src/api/client.ts` |
| Формы | `react-hook-form` 7.53 | `CabinetPage.tsx`, `OwnerPage.tsx`, `CompanyManagePage.tsx` |
| Даты | `date-fns` 3.6 + локаль `ru` | все страницы с датами |
| Стили | Tailwind CSS 3.4 + PostCSS + Autoprefixer, кастомная палитра | `frontend/tailwind.config.js`, `frontend/src/index.css` |
| Линтер / форматтер | ⭐ **ESLint 9** (flat-config, `typescript-eslint` 8, плагины react/react-hooks/react-refresh, `eslint-config-prettier`) + **Prettier 3**; `npm run lint` **входит в CI** | `frontend/eslint.config.js`, `package.json` |
| Менеджер пакетов | npm, есть `package-lock.json`; ⭐ на проде фронт **больше не собирается** — CI выкладывает артефакт `frontend-dist-<sha>` | `.github/workflows/ci.yml`, `deploy/deploy.sh` |

Отдельной библиотеки валидации форм (zod/yup) нет — валидация делается правилами `react-hook-form`
и `[Required]`/`[EmailAddress]`-атрибутами DTO на бэкенде.

### Внешние сервисы

- **Yandex SmartCaptcha** — единственная реальная внешняя интеграция.
  Сервер: `ServiceBooking.API/Services/CaptchaService.cs`, `POST https://smartcaptcha.cloud.yandex.ru/validate`.
  Клиент: `frontend/src/components/booking/SmartCaptcha.tsx`, скрипт `https://smartcaptcha.yandexcloud.net/captcha.js`,
  ключ из `VITE_SMARTCAPTCHA_SITEKEY` (единственная используемая `import.meta.env`-переменная во всём фронтенде).
- ⭐ **GlitchTip (self-hosted, Sentry-совместимый)** — вторая интеграция, но **пока только на бумаге**:
  код (`Sentry.Serilog`) и compose-файл готовы, `Sentry:Dsn` в закоммиченном конфиге пуст, сервер
  ни разу не поднимался (§9.1).
- **Платёжного шлюза нет.** Ни SDK, ни HTTP-вызовов — `PaymentStatus` меняется только вручную
  через `PATCH /api/bookings/{id}/mark-paid`.
- **Почтового провайдера нет.** SMTP/SendGrid/любой другой клиент в коде отсутствует (см. §5).
- **Интеграции с мессенджерами нет.** MAX был исследован и исключён из цикла 2 решением заказчика
  (SPEC §0, приложение А) — в коде нет ничего.
- **Хранилище файлов — локальный диск, но теперь ДВА класса хранения** (цикл 2, `Services/FileStorage.cs`):
  - *публичный* — по умолчанию `wwwroot/uploads/{companies,avatars,services}` (переопределяется
    `Storage:PublicRoot`), метод возвращает URL вида `/uploads/<область>/<guid>.<ext>`;
  - *приватный* — `App_Data/private-uploads/<companyId>/<guid>.jpg` (фото к заметкам о клиентах),
    **никогда не раздаётся статикой**, метод возвращает непрозрачный storage-key, не URL. Отдаётся
    только через `GET /api/client-notes/photos/{id}` с проверкой членства в компании.
  Корни настраиваются `Storage:PublicRoot` / `Storage:PrivateRoot`; в Production `Program.cs` **падает
  на старте**, если приватный корень резолвится внутри `wwwroot`. Облачного стораджа нет.
  ⭐ Цикл sanitation: `Program.cs` больше не вызывает голый `app.UseStaticFiles()` (который резолвит
  `IWebHostEnvironment.WebRootFileProvider`, т.е. буквально `wwwroot`, ОДИН раз при старте хоста —
  на чистом клоне без закоммиченного `wwwroot` это давало пустой провайдер навсегда, даже после того
  как первая загрузка создавала каталог). Теперь раздача идёт через явный `PhysicalFileProvider`,
  построенный поверх того же `FileStorage.PublicRootFullPath`, что использует запись — `Storage:PublicRoot`
  стал единственным источником правды для чтения и записи. Каталог публичного корня создаётся
  (`Directory.CreateDirectory`) непосредственно перед конфигурацией раздачи, так что провайдер никогда
  не строится поверх ещё не существующего каталога. Раздаётся ровно `PublicRootFullPath` под
  `RequestPath = "/uploads"` — не весь `wwwroot` целиком.

### Как собирается и запускается

```bash
# БД (dev)
docker compose up -d postgres          # docker-compose.yml, порт 5432 наружу

# Бэкенд
dotnet run --project ServiceBooking.API        # слушает http://localhost:5000 (профиль из launchSettings — 5291)
# Миграции применяются автоматически на старте: Program.cs -> db.Database.MigrateAsync()
# Там же сидятся роли (Client/Master/CompanyOwner/SuperAdmin) и SuperAdmin из конфига

# Фронтенд
cd frontend && npm install && npm run dev      # http://localhost:5173, /api проксируется на :5000

# Всё сразу в докере (api + postgres, без фронта)
docker compose up --build
```

Прод-сборка фронта: `npm run build` = `tsc && vite build` → `frontend/dist`.
Тесты фронта: `npm test` (watch) / `npm run test:run` (однократно).
Линт и формат: `npm run lint` (ESLint, есть в CI) / `npm run format` (Prettier).
⭐ На боевой сервер `dist` больше не собирается на месте — берётся артефактом из CI (§8).

**Расхождение конфигов устранено (цикл 2).** `ServiceBooking.API/Properties/launchSettings.json`
приведён в порядок: профили `http`/`https` слушают `http://localhost:5000` (https дополнительно 7016),
`launchUrl: "weatherforecast"` удалён, `launchBrowser: false`. Порт совпадает с тем, куда проксирует
Vite и куда мапится docker-compose.

**Конфигурация приложения** (`ServiceBooking.API/appsettings.json`). Секции цикла 2: `Storage`
(`PrivateRoot`, `PublicRoot`, `MinFreeDiskMb: 1024`), `Uploads` (`MaxFileBytes: 5242880`,
`PerUserPerMinute: 10`), `ScheduledTasks` (`Enabled`, `TickSeconds: 60`, и по подсекции на задачу —
сейчас одна, `photo-retention-cleanup`). ⭐ Цикл 3 добавил три: `ForwardedHeaders:TrustedNetworks`
(пустой список в git — в Production **пустой список роняет старт**), `RateLimits` (по подсекции на
политику: `auth-login` 10/мин, `auth-register` 5/час, `booking-create` 120/час и 10/час анонимам,
`data-export` 3/сутки) и `Sentry` (`Dsn`, `Release` — оба пустые). Секция `Legal` (`Root`,
`ReloadSeconds: 30`) в закоммиченном файле **отсутствует** — работают дефолты из `LegalOptions`
(`<ContentRoot>/App_Data/legal`, 30 с). Настроек Serilog в конфиге нет — логгер сконфигурирован кодом.
Отдельно появился **закоммиченный** `appsettings.Testing.json` (исключение из правила
`**/appsettings.*.json` в `.gitignore`): выключает планировщик и поднимает лимит загрузок до 1000/мин,
чтобы функциональные тесты вели себя одинаково у всех и в CI. Секретов не содержит.

---

## 2. Структура репозитория

```
ServiceBooking.sln                  5 проектов (+ папка Solution Items)
├── ServiceBooking.API/             ← точка входа, вся бизнес-логика веб-слоя
│   ├── Program.cs                  593 строки: Serilog, fail-fast прод-конфига (через DeploymentSafetyChecks),
│   │                               DI, Identity, JWT (+ перечитывание ролей, SecurityStamp и claim'ы согласия),
│   │                               CORS, Swagger (только Dev), exception handler, ForwardedHeaders,
│   │                               5 политик rate limiting, глобальный LegalConsentFilter, health-эндпоинты,
│   │                               регистрация фоновых задач, миграции, сид
│   ├── Controllers/                14 контроллеров (15 классов — в Reviews их два); ⭐ добавился LegalController
│   ├── DTOs/                       Auth / Bookings / ClientNotes / ⭐ Common (PagedResult) / Companies / Services / WorkingHours
│   ├── Services/                   SlotService+SlotCalculator, SubscriptionResolver, CaptchaService, TokenService,
│   │   │                           AdvisoryLock, BookingFilters, CompanyMembership, PhoneNormalizer, FileStorage,
│   │   │                           ImageSignature, ImageProcessor, ImageUploadService, PhotoQuota,
│   │   │                           ⭐ DeploymentSafetyChecks, ⭐ IdentityRoleSync, ⭐ LogMasking, ⭐ PhoneMaskingEnricher
│   │   ├── Legal/                  ⭐ LegalDocumentProvider, LegalConsentFilter, LegalOptions, LegalSnapshot
│   │   ├── Health/                 ⭐ DatabaseReadyHealthCheck
│   │   └── Scheduling/             IScheduledTask, ScheduledTaskRunner, ScheduledTaskOptions,
│   │                               ScheduledTaskSchedule, Tasks/PhotoRetentionCleanupTask
│   ├── App_Data/legal/             ⭐ В GIT: манифест legal.json + privacy.html + terms.html (ЧЕРНОВИК);
│   │                               на проде перекрывается bind-mount'ом ./legal с хоста
│   ├── App_Data/private-uploads/   приватный класс хранения (в .gitignore)
│   ├── Dockerfile                  multi-stage, aspnet:8.0, EXPOSE 8080 (комментарий «не менять на -alpine»)
│   └── appsettings*.json           appsettings.json и appsettings.Testing.json в git; Development/Production — в .gitignore
├── ServiceBooking.Core/            только сущности и перечисления, зависимость одна — Identity.EFCore
│   ├── Entities/                   18 классов (⭐ +UserConsent)
│   └── Enums/                      BookingStatus, ⭐ LegalDocumentType, PaymentStatus, PhotoRetention, UserRole
├── ServiceBooking.Infrastructure/  AppDbContext + 26 миграций EF Core
├── ServiceBooking.UnitTests/       xUnit, БЕЗ БД и без HTTP — чистая логика; 15 файлов, 204 запуска
├── ServiceBooking.Tests/           xUnit, функциональные тесты через WebApplicationFactory; 21 файл, 404 запуска
│   ├── Infrastructure/             ApiTestBase, CustomWebApplicationFactory, TestDatabaseFixture, JsonHelpers,
│   │                               TestCaseAttribute, TestImages, ⭐ LegalDocumentsTestFactory, ⭐ RateLimitTestFactory
│   └── Tests/                      21 файл по доменам
├── frontend/                       React SPA
│   ├── src/api/                    16 модулей — тонкая обёртка над axios, по одному на домен (⭐ +legal.ts)
│   ├── src/pages/                  страницы; вложенные owner/ и admin/ — вкладки;
│   │                               ⭐ +LegalDocumentPage.tsx, +DeleteAccountPage.tsx
│   ├── src/components/             booking/, clientNotes/, ⭐ legal/ (ConsentGate, LegalUpdateBanner),
│   │                               layout/, review/, schedule/, ui/ (⭐ +Pagination)
│   ├── src/hooks/                  useOverlayDismiss, useAuthedImage, ⭐ useExportData, ⭐ useDebouncedValue
│   ├── src/store/authStore.ts      единственный zustand-стор
│   ├── src/test/setup.ts           setup Vitest (jest-dom + cleanup Testing Library)
│   ├── src/types/index.ts          общие TS-типы (ручная копия серверных DTO)
│   ├── src/utils/                  мапперы ошибок HTTP → русский текст (⭐ +authError, +legalError) + phone.ts
│   ├── eslint.config.js            ⭐ ESLint 9 flat-config + Prettier
│   └── design_handoff_site_redesign/  HTML-макеты редизайна, не участвуют в сборке
├── .editorconfig                   ⭐ описывает уже сложившийся C#-стиль; в CI НЕ проверяется
├── .github/workflows/ci.yml        CI: три job'а (backend, frontend, docker-build со смоук-прогоном образа)
├── deploy/
│   ├── deploy.sh / deploy-remote.sh / nginx/ezbook.conf
│   ├── ci/smoke.sh                 ⭐ смоук живого контейнера (health, регистрация, загрузка аватара)
│   ├── backup/                     ⭐ backup.sh + systemd .service/.timer (локальный бэкап)
│   ├── monitor/                    ⭐ health-alert.sh + systemd .service/.timer
│   └── rollback.sh                 ⭐ откат одной командой
├── docker-compose.yml              dev: postgres + api
├── docker-compose.prod.yml         prod: postgres + api на 127.0.0.1:5000, два volume + ⭐ bind-mount ./legal
├── docker-compose.glitchtip.yml    ⭐ self-hosted GlitchTip (Sentry-совместимый трекер), отдельный стек
├── README.md                       продуктовое описание + «чего пока нет» + ⭐ запуск/секреты/CI/деплой
├── CHANGELOG.md                    changelog по датам циклов, самая свежая запись сверху
├── docs/                           пользовательская документация по ролям (⭐ +personal-data.md)
├── SPEC.md / ARCHITECTURE.md / API_CONTRACT.md      документы текущего (C) цикла работ
├── SPEC_DEFERRED_NOTIFICATIONS.md / SPEC_APPENDIX_CHANNELS.md  ⭐ спека ОТЛОЖЕННОГО цикла уведомлений
├── API_DOCUMENTATION.md            ~237 КБ, подробный справочник эндпоинтов (рус.)
├── TEST_CATALOG.md                 ~246 КБ, человекочитаемый каталог всех тест-кейсов (рус.)
├── DEPLOY.md / DEPLOY-windows.md   runbook'и: reg.ru VPS (Linux+nginx+docker) и VK Cloud Windows
│                                   (IIS+ARR — контур ВЫВЕДЕН ИЗ СКОУПА цикла 3, но файл не удалён)
└── .env.production.example, .deploy.env.example, appsettings.Production.json.example
```

⭐ — появилось в цикле 3; отметки предыдущих циклов из этой редакции сняты, чтобы значок означал ровно одно.

### Точка входа и слои

- Единственная точка входа приложения — `ServiceBooking.API/Program.cs`. **Второй процесс** в том же
  хосте — `ScheduledTaskRunner` (`BackgroundService`), тикает раз в `ScheduledTasks:TickSeconds` (60 с).
  ⭐ Третий фоновый «житель» — `LegalDocumentProvider`: держит снимок правовых документов в памяти и
  перечитывает манифест с диска по mtime (не чаще раза в `Legal:ReloadSeconds`).
- **Бизнес-логика по-прежнему живёт в контроллерах.** Сервисного слоя как такового нет, но `Services/`
  заметно вырос: к слотам/тарифам/капче/токенам добавились чистые (без БД) помощники — `SlotCalculator`,
  `PhoneNormalizer`, `PhotoQuota`, `ScheduledTaskSchedule`, `BookingFilters`, `ImageSignature`,
  `ImageProcessor` — и статические `AdvisoryLock`, `CompanyMembership`.
  Правила «кто что может» **всё ещё** реализованы приватными методами внутри каждого контроллера
  (`CanManageCompany`, `CanManage`, `CanManageBookingAsync`), но их «членская» половина теперь
  делегируется в единственное место — `CompanyMembership.IsStaffAsync` / `IsOwnerAsync`.
- `Core` — анемичные POCO-сущности без поведения. `Infrastructure` — только `AppDbContext` и миграции.
  Репозиториев нет, контроллеры работают с `AppDbContext` напрямую.
- Выделился слой «чистая логика без инфраструктуры» — именно он покрыт `ServiceBooking.UnitTests`:
  `SlotCalculator`, `SubscriptionResolver.Resolve` (статический), `PhoneNormalizer`, `PhotoQuota`,
  `ScheduledTaskSchedule`, `BookingFilters`, `ImageSignature`, `ImageProcessor`, `FileStorage`, `TokenService`,
  ⭐ `DeploymentSafetyChecks`, `Pagination.Normalize`, `LogMasking`, `LegalDocumentProvider`,
  `LegalConsentFilter`. Цикл 3 **сознательно вытаскивал логику в этот слой ради тестируемости** —
  именно поэтому fail-fast переехал из `Program.cs` в отдельный класс.

### Мёртвый проект `ServiceBooking/` — удалён

Blazor Server-шаблон из первого коммита удалён целиком в цикле 2 (US-22): каталог `ServiceBooking/`
и запись о проекте в `ServiceBooking.sln` больше не существуют. Именно его предупреждение сборки
мешало включить `-warnaserror` в CI — теперь флаг включён.

---

## 3. Модель данных

Источник: `ServiceBooking.Core/Entities/*`, конфигурация связей — `ServiceBooking.Infrastructure/Data/AppDbContext.cs`.
Плюс стандартные таблицы ASP.NET Identity (`AspNetUsers` и т.д.) через `IdentityDbContext<AppUser>`.

### Сущности

| Сущность | Ключ | Ключевые поля | Связи |
|---|---|---|---|
| `AppUser : IdentityUser` | string | `FirstName`, `LastName`, `AvatarUrl`, `CreatedAt`, ⭐ **`DeletedAtUtc?`** (надгробие удалённого аккаунта, цикл 3) | 1—N: CompanyMemberships, ClientBookings, MasterBookings, MasterServices, WorkingHours |
| `Company` | Guid | `Name`, `Slug` (**уникальный индекс**), `Description`, `LogoUrl`, `Address`, `Phone`, `Email`, `AllowSelfBooking`, `RequirePrepayment`, `ShowInPublicListing`, `IsActive`, `OwnerUserId` | N—1 Owner (`Restrict`), 1—N Members / Services / Bookings |
| `CompanyMember` | Guid | `CompanyId`, `UserId`, `Role: UserRole`, `Bio`, **`CommissionPercent`** (переехал сюда с `AppUser` в цикле 1), `JoinedAt`; **уникальный индекс `(CompanyId, UserId)`** | «многие-ко-многим» User↔Company с ролью |
| `Service` | Guid | `CompanyId`, `Name`, `DurationMinutes`, `Price decimal(10,2)`, `ImageUrl`, `IsActive` | 1—N MasterServices, Bookings |
| `MasterService` | Guid | `MasterId`, `ServiceId` | связка «мастер умеет услугу» |
| `WorkingHours` | Guid | `MasterId`, `CompanyId`, **`Date: DateOnly`**, `StartTime`, `EndTime`, `IsWorking`; **уникальный индекс `(MasterId, CompanyId, Date)`** (цикл 1) | 1—N `ScheduleBreak` |
| `ScheduleBreak` | Guid | `WorkingHoursId`, `StartTime`, `EndTime` | перерывы внутри дня |
| `WeeklyScheduleTemplate` | Guid | `MasterId`, `CompanyId`, `DayOfWeek` (ISO 1..7), `IsWorking`, `StartTime`, `EndTime`; индекс `(MasterId, CompanyId)` | шаблон, «раскатываемый» в `WorkingHours` |
| `Booking` | Guid | `CompanyId`, `ServiceId`, `MasterId`, `ClientId?`, `GuestName/Phone/Email`, `Date`, `StartTime`, `EndTime`, **`Price` (снимок цены)**, **`CommissionPercent` (снимок комиссии, цикл 1)**, `Status`, `PaymentStatus`, `Notes`, `CancellationReason`, ⭐ **`ConsentPrivacyVersion?` / `ConsentTermsVersion?` / `ConsentAcceptedAtUtc?`** (снимок согласия, заполняет сервер, в т.ч. для гостя), ⭐ **`ClientDeleted`** | Master `Restrict`, Client `SetNull` |
| `Review` | Guid | `BookingId` (**уникальный индекс** — 1 отзыв на запись), `CompanyId`, `MasterId`, `ClientId?`, `ReviewerName`, `Rating 1..5`, `Comment` | Booking `Cascade` |
| `ClientNote` | Guid | `CompanyId`, `MasterId` (автор), `ClientId?` / `GuestPhone?`, `Note`, **`BookingId?`** (визит, к которому написана заметка; `SetNull`), `CreatedAt`; индексы `(CompanyId, ClientId)` и `(CompanyId, GuestPhone)` | заметки общие для компании; удалять может **автор или владелец компании** (решение Q16); 1—N `ClientNotePhoto` |
| `ClientNotePhoto` | Guid | `ClientNoteId`, `CompanyId` (денормализованная копия), `StoragePath`, `ThumbnailPath`, `ContentType`, `SizeBytes` (полный размер + миниатюра), `Width`, `Height`, `ContentHash` (SHA-256 **обработанных** байт), `UploadedByUserId?` (`SetNull`), `CreatedAt`; индексы `ClientNoteId`, `(CompanyId, CreatedAt)`, **уникальный `(ClientNoteId, ContentHash)`** | фото к заметке; каскад от заметки; ≤5 на заметку |
| **`UserConsent`** ⭐ | Guid | `UserId`, `DocumentType: LegalDocumentType`, `Version`, `AcceptedAtUtc`; **уникальный индекс `(UserId, DocumentType)`** | последняя принятая версия одного документа одним пользователем. **Журнала нет** — строка перезаписывается при повторном принятии (осознанно, SPEC §3.3) |
| `ScheduledTaskState` | string `Name` (PK) | `LastStartedAtUtc?`, `LastFinishedAtUtc?`, `LastSucceeded`, `LastDurationMs`, `LastSummary?`, `LastError?` | состояние периодической задачи, переживающее рестарт |
| `AccountSubscription` | Guid | `OwnerUserId` (**уникальный индекс**), `PlanConfigId?`, `PaidUntil?`, `IsActive` | подписка на **аккаунт владельца**, а не на компанию |
| `SubscriptionPlanConfig` | Guid | `Name`, `PricePerMonth`, `MaxEmployees?`, `MaxCompanies?`, `AllowOnlineBooking`, `AllowMailing`, `AllowAnalytics`, `AllowPublicListing`, `AllowOnlinePayment`, **`PhotoQuotaMb?`** (null = без ограничения, дефолт 100), **`PhotoRetention`**, `IsActive`, `NotifyDaysBefore` | справочник тарифов |
| `SubscriptionChangeLog` | Guid | `OwnerUserId` (индекс), `ChangedByUserId`, старые/новые план, `PaidUntil`, `IsActive`, `Comment` | аудит изменений подписки |
| `MailLog` | Guid | `CompanyId`, `Subject`, `Message`, `SentById`, `RecipientCount`, `SentAt` | журнал «рассылок» |

Перечисления: `BookingStatus { Pending, Confirmed, Cancelled, Completed, NoShow }`,
`PaymentStatus { NotRequired, Pending, Paid }`, `UserRole { Client, Master, CompanyOwner, SuperAdmin }`,
`PhotoRetention { SixMonths = 0, TwelveMonths = 1, Forever = 2 }` (цикл 2),
⭐ **`LegalDocumentType { Privacy, Terms }`** (цикл 3; одноимённый дубль на стороне API был заведён и
удалён внутри цикла, коммит `263eb55` — перечисление живёт только в `Core`).
Сериализуются как строки (`JsonStringEnumConverter` в `Program.cs`).

### Как это связано смыслово

- Аккаунт = телефон **в канонической форме** (цикл 2, US-26). `UserName == PhoneNumber ==` только цифры,
  без `+`, скобок, пробелов и дефисов; 11-значный номер с `8` превращается в `7…`, 10-значный с `9`
  дополняется до `7XXXXXXXXXX`, всё остальное сохраняет цифры как есть (10..15 цифр — E.164).
  Единственная реализация — `Services/PhoneNormalizer.cs` (чистый статический класс). Применяется во
  всех точках входа: register, login, change-phone, AddMember (поиск и автосоздание), гостевая запись,
  поиск в админке, сид SuperAdmin. Уникальность даётся Identity-индексом. Email опционален.
- Тариф привязан к **владельцу** (`Company.OwnerUserId`), одна подписка покрывает все его компании-филиалы.
  Разрешение тарифа — `SubscriptionResolver`; при отсутствии/неактивности/просрочке падаем в
  `EffectivePlan.Free` = `{OnlineBooking: false, Mailing: false, Analytics: false, PublicListing: true,
  OnlinePayment: false, MaxEmployees: 1, MaxCompanies: 1}`.
- Расписание **датовое**, не по дням недели (миграция `DateBasedSchedule`). `WeeklyScheduleTemplate`
  — только заготовка, которую `POST /api/schedule-template/apply` разворачивает в строки `WorkingHours`.
- `Booking.Price` и `Booking.CommissionPercent` — снимки `Service.Price` и `CompanyMember.CommissionPercent`
  на момент создания; отчёты читают их, а не текущие значения (иначе уход мастера из компании
  обнулял бы историческую комиссию).
- **Фото привязаны к заметке, а не к записи.** Заметка опционально ссылается на визит (`BookingId?`);
  фото наследует компанию заметки денормализованным полем — чтобы квота считалась одним
  индексированным агрегатом, а раздача проверялась одной строкой без join'а.
- **Квота и срок хранения фото — свойства тарифа** (`PhotoQuotaMb`, `PhotoRetention`), разрешаются тем
  же `SubscriptionResolver`; на Free-базлайне — 100 МБ и 6 месяцев.
- ⭐ **Согласие хранится в двух видах и с разным сроком жизни.** «Текущее состояние» — `UserConsent`
  (одна строка на пару «пользователь + документ», перезаписывается) и claim'ы в JWT, по которым
  работает гейт без похода в БД. «Исторический факт» — снимок версий **на самой записи** (`Booking`),
  который переживает и смену редакции документа, и удаление аккаунта, и относится в том числе к
  гостю, у которого аккаунта нет.
- ⭐ **Удалённый аккаунт — надгробие, а не отсутствие строки** (`AppUser.DeletedAtUtc`, §4.15).
  Записи такого клиента остаются в истории компании анонимизированными, с флагом
  `Booking.ClientDeleted`.

### Миграции (26, все в `ServiceBooking.Infrastructure/Migrations/`)

Первые 13 — как раньше: `InitialCreate` → `DateBasedSchedule` → `AddSubscriptionAndCommission` →
`AddReviewsTemplatesNotes` → `AddPromoGiftMailPlans` → `AddPrepaymentSupport` →
`LinkSubscriptionsToPlanConfigs` → `RemovePlanKey` → `AccountLevelSubscriptions` →
`AddBookingPriceSnapshot` → `RemovePromoCodesAndGiftCertificates` → `AddPublicListingAndOnlinePayment` →
`AddCompanyIdToClientNote`.

Цикл 1 добавил пять: `DeduplicateWorkingHours` → `AddWorkingHoursUniqueIndex` →
`AddCompanyMemberCommission` → `DeduplicateCompanyMembers` → `AddCompanyMemberUniqueIndex`
(парами: сначала чистка дублей, затем уникальный индекс).

Цикл 2 добавил ещё пять: `AddClientNoteBookingId` → `AddClientNotePhotos` → `AddPlanPhotoLimits` →
`AddScheduledTaskState` → **`NormalizePhoneNumbers`**.

⭐ Цикл 3 добавил четыре: **`ResyncIdentityRoles`** → **`AddLegalConsent`** →
**`AddUserDeletionTombstone`** → **`AddMaxLengthToConsentVersions`**.
`ResyncIdentityRoles` — **миграция данных**: разово пересчитывает Identity-роли по строкам
`CompanyMember` тем же правилом, что `IdentityRoleSync`, и чинит историю «роль осталась после
удаления из компании». Её **`Down` — намеренный no-op**: откатывать пересчёт ролей бессмысленно.
`AddMaxLengthToConsentVersions` — поздняя правка по ревью: ограничение длины строк версий
(до неё колонки были `text` без потолка).

`NormalizePhoneNumbers` — единственная **разрушительная** миграция проекта и сознательно последняя в
цикле. Она приводит уже лежащие в БД номера к канонической форме (ручная транслитерация
`PhoneNormalizer.Normalize` в SQL, строго `[^0-9]`, не `\D`), а при коллизии (два аккаунта сводятся к
одному номеру) — оставляет аккаунт с наибольшим числом связанных строк (записи как клиент + как мастер
+ членства), при равенстве — самый ранний по `CreatedAt`; заметки/отзывы/журналы рассылок проигравшего
**переназначаются** победителю, остальное каскадно удаляется вместе с аккаунтом. Если проигравший
владеет компанией или имеет записи как мастер — миграция **останавливается с описательной ошибкой**,
а не молча меняет данные. Проект не в продакшене, боевых данных нет.

История по-прежнему видна прямо в названиях: промокоды и подарочные сертификаты были добавлены и затем
**удалены целиком**, а подписка переехала с компании на аккаунт владельца. Остатков этих фич в коде нет.

---

## 4. Что реализовано

Ниже — по функциональным блокам. Все эндпоинты выписаны из атрибутов контроллеров, а не из документации.

### 4.1 Аутентификация и профиль — работает

`ServiceBooking.API/Controllers/AuthController.cs`, `ProfileController.cs`, `Services/TokenService.cs`

| Метод | Путь | Доступ |
|---|---|---|
| POST | `/api/auth/register` | анонимно; телефон нормализуется, невалидный → 400; выдаёт роль `Client`; ⭐ **требует `acceptedLegal`** с версиями обоих документов (цикл 3) — без принятия согласия аккаунт не создаётся; лимит `auth-register` 5/час на IP |
| POST | `/api/auth/login` | анонимно; поиск по канонической форме телефона, lockout после 5 попыток на 15 мин; невалидный номер даёт тот же 401, а не 400; ⭐ лимит `auth-login` 10/мин на IP |
| GET / PUT | `/api/profile` | авторизованные |
| POST | `/api/profile/change-password` | авторизованные |
| POST | `/api/profile/change-phone` | авторизованные; требует текущий пароль, через `SetUserNameAsync` |
| POST | `/api/profile/avatar` | авторизованные; свой аватар (id из токена, route-параметра нет), ≤5 МБ, rate limit `uploads`, профиль обработки `Avatar` (512 px, квадратный кроп) |
| **GET** | **`/api/profile/export`** ⭐ | авторизованные; выгрузка своих данных, см. §4.15 |
| **POST** | **`/api/profile/delete-account`** ⭐ | авторизованные; удаление аккаунта, см. §4.15 |

JWT: HS256, срок **7 дней**, claims `sub/phone/given_name/family_name/jti/role` + **`sstamp`**
(хеш `SecurityStamp`, цикл 1) + ⭐ **claim'ы принятых версий правовых документов** (цикл 3) — именно
по ним `LegalConsentFilter` решает, отдавать ли 451, не заглядывая в БД.
Важные детали в `Program.cs` (`JwtBearerEvents.OnTokenValidated`), на каждом запросе:
роли **перечитываются из БД** и подменяют claim'ы токена (отзыв роли действует немедленно), и
**сверяется хеш `SecurityStamp`** — смена пароля или телефона инвалидирует все ранее выданные токены.

`GET /api/profile` дополнительно отдаёт `ProfilePlanDto` для `CompanyOwner` — показывает реальную
строку подписки (в т.ч. просроченную), а не нормализованный Free.

Фронт: `LoginPage.tsx`, `RegisterPage.tsx`, `ProfilePage.tsx`, `store/authStore.ts` (persist в localStorage),
`api/client.ts` — интерцептор на 401 делает `logout()` + редирект на `/login`.

### 4.2 Компании — работает

`Controllers/CompaniesController.cs` (635 строк — самый большой контроллер)

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/companies` | публично; фильтр `ShowInPublicListing && plan.AllowPublicListing` |
| GET | `/api/companies/my` | владелец — свои компании |
| GET | `/api/companies/member` | все компании, где я участник любой роли |
| GET | `/api/companies/{slug}` | публично |
| GET | `/api/companies/{id}/masters?serviceId=` | публично; **фильтр по ролям `Master`/`CompanyOwner`** (цикл 1) |
| GET | `/api/companies/{id}/members` | владелец/SuperAdmin |
| POST | `/api/companies` | авторизованные; **лимит `MaxCompanies`** под advisory lock, 402 при превышении |
| PUT | `/api/companies/{id}` | владелец |
| POST | `/api/companies/{id}/logo` | владелец; ≤5 МБ, rate limit `uploads`, **тип определяется по сигнатуре файла**, ре-энкод профилем `CompanyLogo` (512 px), старый файл удаляется **после** коммита нового URL |
| GET | `/api/companies/{id}/photo-usage` | персонал компании **или SuperAdmin**; занятый объём, число фото, квота, % и срок хранения — единственное место, где SuperAdmin получает цифры по клиентским фото (содержимое ему недоступно) |
| POST | `/api/companies/{id}/members` | владелец; **лимит `MaxEmployees`** под advisory lock, 402; телефон нормализуется; неизвестное имя роли → 400 |
| PUT | `/api/companies/{id}/members/{memberId}/services` | владелец |
| PUT | `/api/companies/{id}/members/{memberId}/commission` | владелец; clamp 0..100; пишет в `CompanyMember.CommissionPercent` |
| DELETE | `/api/companies/{id}/members/{memberId}` | владелец |
| GET | `/api/companies/{id}/stats?from&to` | владелец; выручка, новые клиенты, топ услуг, по мастерам, по дням |

Автосоздание аккаунта мастера по телефону при `AddMember`: пароль выводится детерминированно —
`"Sb" + последние 6 цифр **канонического** телефона`, добитый нулями до 8 символов. После US-26 для
номера, введённого как «8 999…», результат отличается от того, что был до цикла 2.

Фронт: `pages/owner/CompanyManagePage.tsx` (вкладки «Услуги / Расписание / Сотрудники / Настройки»
+ блок кода для встраивания виджета + индикатор занятого под фото места),
`pages/CabinetPage.tsx` (вкладка «Мои компании» с созданием), `pages/CompanyPage.tsx` (публичная витрина).

### 4.3 Услуги — работает

`Controllers/ServicesController.cs`: `GET /api/services?companyId=` (публично),
`POST`, `PUT /{id}`, `DELETE /{id}` (удаление — мягкое, `IsActive = false`),
**`POST /api/services/{id}/image`** ⭐ (загрузка картинки услуги, профиль `ServiceImage` 1200 px,
rate limit `uploads`).

Права ужесточены в цикле 2: `CanManageCompany` здесь теперь — **только `CompanyOwner` (+SuperAdmin)**,
мастер больше не управляет каталогом услуг. `CreateServiceDto` **не принимает `ImageUrl`** — единственный
способ задать картинку — специальный эндпоинт загрузки (иначе клиент мог указать произвольный путь
`/uploads/...` и удалить чужой файл при замене). Есть валидация: `Name` 1..200,
`DurationMinutes` 1..1440, `Price` 0..1 000 000.

### 4.4 Расписание мастеров — работает

`Controllers/WorkingHoursController.cs`: `GET /api/workinghours?masterId&companyId&from&to`
(**теперь с проверкой `CanManage`** — цикл 1 закрыл утечку чужого расписания),
`PUT /api/workinghours` (upsert дня + полная перезапись перерывов), `DELETE /api/workinghours/{id}`.

`Controllers/ScheduleTemplateController.cs`: `GET`/`PUT /api/schedule-template`,
`POST /api/schedule-template/apply?masterId&companyId&from&to` — раскатка недельного шаблона по датам
(ISO: Пн=1 … Вс=7), существующие дни перезаписываются.
Цикл 1 добавил здесь: транзакцию + advisory lock на `PUT` (delete-then-insert стал атомарным) и на
`apply` (find-or-create больше не даёт дублей против нового уникального индекса), а также валидацию
диапазона — `to >= from` и не более **366** дней за один вызов (366, а не 365, чтобы високосный год
раскатывался одним запросом).

Фронт: `pages/owner/ScheduleTab.tsx` (календарь по месяцам, перерывы, сортировка мастеров с владельцем
первым), `components/schedule/WeeklyTemplateModal.tsx`.

### 4.5 Слоты и бронирование — работает, это ядро продукта

`Controllers/BookingsController.cs` + `Services/SlotService.cs`

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/bookings/occupied?masterId&date` | **`[Authorize]`** (цикл 1): SuperAdmin, сам мастер или персонал компании, где этот мастер тоже состоит. Занятость намеренно **не** скоупится по компании — один человек занят у всех работодателей |
| GET | `/api/bookings/slots?companyId&masterId&serviceId&date&manual` | публично; **`companyId` обязателен** (цикл 1), проверяется тройка «услуга принадлежит компании» + «мастер работает в компании»; `manual=true` учитывается **только для персонала этой компании** |
| POST | `/api/bookings` | публично (гость) и авторизованно |
| GET | `/api/bookings/{id}` | владелец записи или персонал |
| GET | `/api/bookings/client?status=` | мои записи как клиента, с фильтром статуса |
| GET | `/api/bookings/master?date&to` | `Master,CompanyOwner` |
| PATCH | `/api/bookings/{id}/complete` \| `/mark-paid` \| `/noshow` | `Master,CompanyOwner,SuperAdmin` |
| PATCH | `/api/bookings/{id}/reschedule` | персонал (advisory lock + проверка конфликта) |
| PATCH | `/api/bookings/{id}/cancel` | клиент записи или персонал; принимает причину отмены, которую видит вторая сторона |

`GET /api/bookings/my` **удалён** в цикле 2 как мёртвый дубль `/api/bookings/client`.

Логика слотов вынесена в чистый `Services/SlotCalculator.cs` (без БД и EF), `SlotService` остался
тонкой обёрткой над запросами. Шаг сетки **по-прежнему жёстко 30 минут** (`SlotCalculator.StepMinutes`),
слот занят, если пересекается с бронью (статус ≠ Cancelled) или перерывом. Без строки `WorkingHours`
на дату — пустой список, кроме `allowWithoutSchedule` (`manual`-режим), где берётся весь день
00:00–24:00 с защитой от переполнения `TimeOnly`. Проверка при самой записи (`IsSlotAllowed`) построена
**поверх той же `Calculate`**, а не отдельным предикатом — чтобы выдача слотов и их валидация не разошлись.

Гейты в `POST /api/bookings` (цикл 1 их существенно перебрал):
1. **компания должна существовать — для любого вызывающего**, а не только для гостя (раньше
   аутентифицированный запрос с несуществующим `CompanyId` падал 500 из-за нарушения FK);
2. «ручная запись персонала» = аутентифицированный вызывающий, **реально работающий в этой компании**,
   передавший `GuestName`. Все остальные — гостевой путь, даже если у них есть аккаунт (раньше одного
   `guestName` хватало, чтобы обойти четыре гейта);
3. `AllowSelfBooking` проверяется **для всех непереонала**, включая залогиненного клиента (раньше жил
   внутри гостевой ветки);
4. гостевой путь → капча (если `IsEnforced`), имя+телефон, телефон нормализуется;
5. тариф: `!AllowOnlineBooking && !isStaffManualBooking` → **402**;
6. предоплата: `PaymentStatus = Pending` только если `plan.AllowOnlinePayment && company.RequirePrepayment` и это не ручная запись;
7. проверка конфликта слота внутри транзакции с `pg_advisory_xact_lock` (`Services/AdvisoryLock.cs`) → 409;
8. снимаются `Price` и `CommissionPercent`.

Фронт: `components/booking/BookingModal.tsx` (гость/клиент, выбор услуги → мастера → даты → слота, капча),
`ManualBookingModal.tsx` (персонал записывает клиента), `RescheduleModal.tsx`,
`pages/MyBookingsPage.tsx` (персонал; под записью раскрывается панель с историей клиента и заметками),
`pages/ClientBookingsPage.tsx` (клиент; маршрут `/my-visits` теперь доступен **всем**
аутентифицированным ролям, включая мастера и владельца).

### 4.6 Тарифы и подписки — работает административно

`Services/SubscriptionResolver.cs` (батчевое разрешение планов без N+1), `AdminController` (CRUD планов и
назначение подписок), `DTOs/Companies/CompanyDto.cs` (флаги для UI).

`SubscriptionResolver.Resolve` — чистая статическая функция (`sub`, `nowUtc` → `EffectivePlan`),
покрыта юнит-тестами. `EffectivePlan` расширен полями `PhotoQuotaMb` и `PhotoRetention`.
**Деактивированный тариф (`SubscriptionPlanConfig.IsActive == false`) теперь роняет владельца во Free**
(цикл 1) — раньше «удалённый» план продолжал раздавать возможности уже подписанным.

`CompanyDto` отдаёт три уровня флагов, и это осознанно (см. комментарии в файле):
- собственные тумблеры владельца: `AllowSelfBooking`, `RequirePrepayment`, `ShowInPublicListing`;
- вычисленные «реально работает»: `OnlineBookingEnabled`, `PrepaymentEnabled`, `PublicListingEnabled`;
- «сырые» возможности тарифа: `PlanAllowsOnlineBooking`, `PlanAllowsOnlinePayment`, `PlanAllowsPublicListing`, `MaxEmployees`.

⭐ Цикл 3 добавил в `CompanyDto` поля `AverageRating` (`double?`) и `ReviewCount` — **агрегат считается
в БД**, а не подтягиванием отзывов на клиент; витрина компании показывает рейтинг без отдельного запроса.

Последняя группа добавлена коммитами `c062dd5` и `bc34db3` — чтобы UI гасил тумблер/кнопку заранее,
а не ловил 402 после заполнения формы. Фронт: `CompanyManagePage.tsx` (SettingsTab, MembersTab),
`CabinetPage.tsx` (скрывает вкладки «Отчёты»/«Рассылка» по `allowAnalytics`/`allowMailing`).

### 4.7 Отзывы — работает

`Controllers/ReviewsController.cs`: `POST /api/reviews` (только по завершённой записи, рейтинг 1..5,
один отзыв на бронь), `GET /api/reviews/can-review` (список ID записей, ждущих отзыва),
`GET /api/companies/{companyId}/reviews` (публично, класс `CompanyReviewsController` в том же файле;
⭐ **с цикла 3 отдаёт `PagedResult<ReviewDto>`, а не массив** — ломающее изменение).
Цикл 1 закрыл дыру: проверка авторства стала строгой (`booking.ClientId != userId` → 403) — раньше
условие `ClientId != null && …` полностью пропускало **гостевые** записи, и любой, кто узнал
`bookingId` (а он возвращается гостю при создании), мог оставить отзыв чужому бизнесу от своего имени.
Как следствие отзыв по гостевой записи теперь невозможен вовсе, и ветка с `GuestName` в
`ReviewerName` удалена как мёртвая.
Фронт: `components/review/ReviewModal.tsx`, отображение на `CompanyPage.tsx`.

### 4.8 База клиентов мастера и заметки — работает, переработано в цикле 2

`Controllers/MastersController.cs`: `GET /api/masters/clients?companyId&search&page&pageSize`
(⭐ цикл 3: **серверный поиск** — эвристика «похоже на телефон» та же, что в `AdminController.GetUsers`,
и **`PagedResult<MasterClientDto>` вместо массива**, ломающее изменение; фильтрация и нарезка страницы
делаются **в памяти**, см. §5.3.5), `POST /api/masters/clients/notes`,
`DELETE /api/masters/clients/notes/{id}`.
Доступ — только персонал компании (`CompanyMembership.IsStaffAsync`), участник с ролью `Client` больше
не проходит. Группировка отдельно по зарегистрированным клиентам и по `GuestPhone`.

Что изменилось:
- **правило «контакты скрыты через 24 часа после визита» удалено** (цикл 2): оно было
  полуреализовано (не пряталось обратно, не было способа раскрыть раньше) и ничего не защищало;
- **заметка стала объектом**: `ClientNoteDto { id, note, createdAt, authorId, authorName, bookingId?,
  bookingDate?, bookingServiceName?, canDelete, photos[] }` — раньше на фронт уезжал `string[]`;
- удалять заметку может **автор или владелец компании** (решение Q16);
- выборка заметок ограничена **50 на клиента** и делается оконной функцией `ROW_NUMBER()` в SQL
  (`FromSqlInterpolated`), а не подтягиванием всей истории компании в память;
- заметка опционально ссылается на визит (`bookingId`), и тогда в карточке видно, к какой услуге и
  дате она относится.

Фронт: `pages/MasterClientsPage.tsx`, вкладка «Клиенты» в `CabinetPage`, раскрывающаяся панель под
записью в `MyBookingsPage.tsx`, компоненты `components/clientNotes/{NoteCard, NotePhotoUploader,
PhotoGallery, PhotoViewerModal}.tsx`.

### 4.9 Отчёты и статистика — работает

- `GET /api/reports/masters?companyId&from&to` (`ReportsController`) — выручка, комиссия мастера,
  доля компании; гейт `plan.AllowAnalytics` → 402. Цикл 1 исправил основу расчёта: фильтр идёт
  **по дате визита** (`Booking.Date`), а комиссия берётся из **снимка на записи**
  (`Booking.CommissionPercent`), а не из текущей строки `CompanyMembers` — уход мастера или смена
  ставки больше не переписывают закрытый период. Фронт: `ReportsTab` внутри `CabinetPage.tsx`.
- `GET /api/companies/{id}/stats` (`CompaniesController`) — сводка по компании. Фронт: `pages/owner/DashboardTab.tsx`.
- `GET /api/admin/stats` — платформенная сводка.

### 4.10 Админка — работает

`Controllers/AdminController.cs`, все методы `[Authorize(Roles = "SuperAdmin")]`:
`GET /api/admin/stats`, `GET /api/admin/users?search&page&pageSize` (поиск по телефону в любом
формате — строка запроса нормализуется; ⭐ цикл 3: `PagedResult<AdminUserDto>` **и починенный N+1** —
роли больше не догружаются по пользователю в цикле), `PUT /api/admin/users/{id}/roles`,
`GET /api/admin/companies?search&page&pageSize` (⭐ тоже `PagedResult<T>`), `PUT /api/admin/companies/{id}` (тело — `Name`, `IsActive`,
`AllowSelfBooking`; блокировка/разблокировка компании доступна из UI, US-04),
`PUT /api/admin/companies/{id}/owner`,
`PUT /api/admin/owners/{ownerUserId}/subscription`, `GET /api/admin/owners/{ownerUserId}/subscription-history`,
`GET /api/admin/bookings` (лимит `Take(500)`), `GET|POST|PUT|DELETE /api/admin/plans[/{id}]`
(delete — мягкий, `IsActive = false`; **вернуть тариф в продажу теперь можно через `PUT`**, US-05),
`GET /api/admin/scheduled-tasks` (цикл 2, см. §4.13).
Из `AdminUserDto` убран `CommissionPercent` (комиссия стала per-company).
Фронт: `pages/AdminPage.tsx` (вкладки stats/companies/users/bookings) + `pages/admin/PlansTab.tsx`
(в редакторе тарифа появились квота на фото и срок хранения).

### 4.11 Виджет-встраивание — работает целиком

Маршрут `/embed/:slug` (`frontend/src/App.tsx`) рендерит `pages/EmbedPage.tsx` без навбара — список
услуг компании + `BookingModal`. В цикле 2 (US-03) в настройках компании
(`pages/owner/CompanyManagePage.tsx`) появился блок с готовым `<iframe …>`-сниппетом, ссылкой, кнопкой
«Скопировать» и предпросмотром; имя компании экранируется для атрибута `title`.

### 4.12 Фото к заметкам о клиентах — функция цикла 2

`Controllers/ClientNotePhotosController.cs` (195 строк), `Services/{ImageUploadService, ImageProcessor,
ImageSignature, FileStorage}.cs`.

| Метод | Путь | Доступ |
|---|---|---|
| POST | `/api/client-notes/{noteId}/photos` | персонал компании заметки; `[EnableRateLimiting("uploads")]`, `[RequestSizeLimit(5 МБ)]` |
| GET | `/api/client-notes/photos/{id}` | персонал **этой** компании; чужому — 404 (существование чужого фото не подтверждается); **SuperAdmin получает 403** — единственное место в продукте, где ему отказано |
| GET | `/api/client-notes/photos/{id}/thumb` | то же, миниатюра |
| DELETE | `/api/client-notes/photos/{id}` | автор заметки, загрузивший это фото, или владелец компании |

Конвейер загрузки (общий для всех **четырёх** точек загрузки изображений — фото заметки, аватар,
картинка услуги, логотип): наличие файла → лимит 5 МБ → **определение типа по сигнатуре байт**
(Content-Type и имя файла не используются нигде) → проверка свободного места на диске
(`Storage:MinFreeDiskMb`) → защита от «бомбы» (`MaxPixels = 50 млн`) → декод/поворот по EXIF/ресайз/
ре-энкод SkiaSharp по профилю (`ClientNotePhoto` 1600 px, `ClientNotePhotoThumb` 320 px, `Avatar` 512 px
с квадратным кропом, `ServiceImage` 1200 px, `CompanyLogo` 512 px) — метаданные, включая геолокацию,
не переносятся. Далее — специфика вызывающего: права, квота, запись.

Специфика фото заметки: ≤5 фото на заметку; идемпотентность по SHA-256 **обработанных** байт
(уникальный индекс `(ClientNoteId, ContentHash)` — повторный клик не тратит квоту дважды); квота
компании считается суммой `SizeBytes` под `pg_advisory_xact_lock("company-photo-quota:{companyId}")`,
файлы пишутся **внутри** удерживаемого лока; при превышении — 400 с текстом «сколько из скольки занято».
Порядок операций на удаление — «сначала строка БД, потом файл»; на замену публичного файла — «сначала
новый файл и коммит, потом удаление старого»: худший исход — осиротевший файл, который подметёт
фоновая уборка.

Отдача приватных фото: `ClientNotePhotoDto.Url` — это путь к **защищённому API**, а не значение для
`<img src>` (браузер не приложит `Authorization`). Фронт грузит их как blob через тот же axios
(`hooks/useAuthedImage.ts`, `components/ui/AuthedImage.tsx`), с `IntersectionObserver` для ленивой
загрузки и `staleTime: Infinity` в react-query.

### 4.13 Периодические фоновые задачи — первый фоновый процесс в продукте (цикл 2)

`Services/Scheduling/`: `IScheduledTask` (контракт: `Name`, `DefaultPeriod`, `ExecuteAsync` →
`ScheduledTaskOutcome{Scanned, Affected, BytesFreed, Summary}`), `ScheduledTaskRunner`
(единственный `BackgroundService`), `ScheduledTaskOptions` (читает `ScheduledTasks:{Name}:{Enabled,
PeriodMinutes, MaxRunMinutes}`), `ScheduledTaskSchedule` (чистые `IsDue`/`IsOverdue`).

Как устроено: раннер **не знает ни одной задачи по имени** — перечисляет всё, что зарегистрировано как
`IScheduledTask`. Добавление второй задачи = новый класс + одна строка регистрации в `Program.cs`.
«Пора ли» читается из БД (`ScheduledTaskState`), а не из памяти — рестарт не приводит к раннему
повторному запуску. Параллельный запуск на двух инстансах исключён неблокирующим
`pg_try_advisory_xact_lock("scheduled-task:{Name}")`; лок держится отдельным соединением, чтобы
промежуточные коммиты задачи его не отпускали. Есть бюджет времени (`MaxRunMinutes`, дефолт 10):
исчерпание — **штатный** исход, уже закоммиченное сохраняется, остальное доедет следующим запуском.
Упавшая задача помечается неуспешной и повторяется **по обычному расписанию**, без backoff.

Единственная задача сейчас — `photo-retention-cleanup` (период по умолчанию — сутки): удаляет фото,
пережившие срок хранения своего тарифа (`Forever` не трогается), батчами по 200; затем подметает
осиротевшие файлы на диске старше 24 часов.

`GET /api/admin/scheduled-tasks` (SuperAdmin) отдаёт по каждой задаче: `enabled`, `periodMinutes`,
`lastStartedAt`, `lastFinishedAt`, `lastDurationMs`, `lastSucceeded`, `lastSummary`, `lastError`,
`isOverdue` (не финишировала дольше двух своих периодов). Зависимости резолвятся `[FromServices]` в
самом действии, а не в primary-конструкторе контроллера, — чтобы остальные админские вызовы за это не платили.
В окружении `Testing` планировщик **выключен** (`appsettings.Testing.json`), функциональные тесты
дёргают задачу напрямую.

### 4.14 Правовой контур: документы, согласие, 451 ⭐ — новое в цикле 3

`Controllers/LegalController.cs`, `Services/Legal/{LegalDocumentProvider, LegalConsentFilter,
LegalOptions, LegalSnapshot}.cs`, `Core/Enums/LegalDocumentType.cs`, сущность `UserConsent`.

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/legal/documents` | публично; только метаданные обоих документов (тип, заголовок, версия, дата вступления, `isDraft`, `changeKind`) — без HTML, чтобы подвал и форма регистрации не тянули текст |
| GET | `/api/legal/documents/{type}` | публично; метаданные **и** HTML одного документа; `Cache-Control: public, max-age=300` |
| GET | `/api/legal/consent-status` | авторизованные; `{ requiresAcceptance, showBanner, documents[] }`, считается **из claim'ов токена и снимка в памяти, без запроса в БД** |
| POST | `/api/legal/accept` | авторизованные; тело — версии обоих документов; версии **сверяются с текущим снимком** (409, если оператор успел заменить текст ещё раз), запись в `UserConsent` под транзакцией + advisory lock |

Как это устроено:

- **Документы — файлы, а не строки в БД.** `App_Data/legal/` содержит манифест `legal.json`
  (`type`, `version`, `effectiveFrom`, `isDraft`, `changeKind`, `title`, `file`) и по HTML-файлу на
  документ. `LegalDocumentProvider` держит снимок в памяти и перечитывает манифест по mtime не чаще
  раза в `Legal:ReloadSeconds` (по умолчанию 30 с). На проде каталог **bind-mount'ится с хоста**
  поверх запечённого в образ черновика — замена текста после юридической вычитки это
  **эксплуатационная операция, а не релиз** (`DEPLOY.md` §2.1).
- **`changeKind` решает, блокировать ли пользователя.** `Material` — блокирующий экран и **451
  Unavailable For Legal Reasons** на всех защищённых вызовах; `Editorial` — только баннер.
  `Material` перебивает `Editorial`, если ожидают оба документа.
- **Гейт — глобальный MVC-фильтр** `LegalConsentFilter` с allow-list'ом (сами правовые эндпоинты,
  принятие согласия и **выгрузка данных** — иначе заблокированный пользователь не смог бы ни узнать,
  чего от него хотят, ни забрать свои данные). Анонимные запросы фильтр не трогает.
- **Снимок согласия на записи.** У `Booking` появились `ConsentPrivacyVersion`, `ConsentTermsVersion`,
  `ConsentAcceptedAtUtc` — их заполняет **сервер** из актуальных документов, в том числе для гостевой
  записи и виджета, где аккаунта нет вовсе.
- **Тексты черновые** (`isDraft: true`), fail-fast на это намеренно нет — §9.4.

Фронт: страницы `/privacy` и `/terms` (`pages/LegalDocumentPage.tsx`), `components/legal/ConsentGate.tsx`
(полноэкранный блокирующий экран на `Material`; из него **достижима выгрузка данных** — коммит
`a115aad`), `components/legal/LegalUpdateBanner.tsx` (на `Editorial`), чекбокс со ссылками в форме
регистрации и в форме записи, ссылки в подвале, `api/legal.ts`, `utils/legalError.ts`.

### 4.15 Права субъекта данных: выгрузка и удаление аккаунта ⭐ — новое в цикле 3

`Controllers/ProfileController.cs`.

- **`GET /api/profile/export`** (лимит `data-export` — 3 раза в сутки) отдаёт JSON с
  `Content-Disposition: attachment`: профиль, история согласий, членства в компаниях, **записи**
  (и как клиента, и **гостевые по каноническому телефону** — визиты, сделанные до регистрации),
  отзывы, **метаданные** заметок и фото о себе (компания, дата, размер/количество).
  В файл **намеренно не входят** тексты заметок сотрудников и содержимое фотографий — они признаны
  результатом работы салона; в самом JSON лежит поле с объяснением этого пользователю.
- **`POST /api/profile/delete-account`** (POST, а не DELETE — нужно тело с текущим паролем).
  Два гейта: текущий пароль и **«за вами числится компания» → 409** (владелец обязан сначала передать
  компанию). Дальше, одной транзакцией: согласия удаляются; заметки и фото **о** пользователе
  удаляются (файлы — после коммита); записи **анонимизируются, а не удаляются** (`ClientId = null`,
  гостевые поля и заметки очищаются, ставится `ClientDeleted = true`) — выручка и комиссия салона
  должны уцелеть; отзывы деперсонализируются (`ReviewerName = "Удалённый пользователь"`); членства
  снимаются, роли пересчитываются `IdentityRoleSync` под теми же advisory-локами; сам аккаунт
  становится **надгробием** — поля затираются, `PasswordHash` очищается, ставится вечный lockout,
  **телефон освобождается** для повторной регистрации, проставляется `AppUser.DeletedAtUtc`.
- **Почему надгробие, а не `DELETE` строки:** четыре FK на `Restrict` (`Booking.Master`,
  `Review.Master`, `Company.Owner`, `MailLog.SentBy`) физическое удаление просто не пропустят, а
  пятый — `ClientNote.Master` на `Cascade` — молча снёс бы заметки этого человека **о других
  клиентах**, то есть данные компании. Причина зафиксирована в `ARCHITECTURE.md` §19.2 и
  комментарием на самом поле `DeletedAtUtc`.

Фронт: `pages/DeleteAccountPage.tsx` (маршрут `/profile/delete`), `hooks/useExportData.ts`
(скачивание blob'ом через тот же axios + `a[download]`), кнопки в `ProfilePage.tsx` и в `ConsentGate`.
Пользовательское описание — `docs/personal-data.md`.

### 4.16 Эксплуатационная обвязка: health, логи, лимиты, роли ⭐ — новое в цикле 3

- **Health-эндпоинты** (`Services/Health/DatabaseReadyHealthCheck.cs`): `GET /api/health/live` —
  анонимный, **не касается БД** вовсе (`Predicate = _ => false`), и `GET /api/health/ready` —
  анонимный, проверяет соединение и применённые миграции. У обоих **свой ResponseWriter на два поля**
  (`status`, `failed`): стандартный ответ фреймворка вложил бы текст исключения и слил бы кусок
  строки подключения на публичный эндпоинт. Rate limiting к ним намеренно **не применён** — мониторинг
  не должен уметь залочить сам себя. `docker-compose.prod.yml` смотрит на `live`, скрипты деплоя
  ждут `ready`.
- **Логирование:** Serilog заменяет хост-логгер целиком, `CompactJsonFormatter` в stdout и
  `logs/app-.json`; телефоны маскируются (`Services/LogMasking.cs`, `PhoneMaskingEnricher`), в том
  числе в строке запроса (`?search=<телефон>`); синк в GlitchTip по протоколу Sentry
  (`Sentry.Serilog`) включается только при непустом `Sentry:Dsn`.
- **Rate limiting** (§6): `auth-login`, `auth-register`, `booking-create` (разные лимиты для
  авторизованных и анонимов), `data-export` — плюс существовавший `uploads`. IP берётся после
  `UseForwardedHeaders` со списком доверенных сетей, отсутствие которого в Production роняет старт.
- **`Services/IdentityRoleSync.cs`** — единственный пересчёт ролей `Master`/`CompanyOwner` из строк
  `CompanyMember`; вызывается из пяти операций (§6). Закрывает дыру цикла 2: `RemoveMember` не снимал
  Identity-роль, и человек, удалённый из единственной компании, продолжал проходить
  `[Authorize(Roles = …)]`. Историю почистила миграция данных `ResyncIdentityRoles`.
- **`Services/DeploymentSafetyChecks.cs`** — fail-fast прод-конфига, вынесенный из `Program.cs` в
  чистые статические методы **ради тестируемости** (28 юнит-тестов), см. §8.

---

## 5. Что реализовано частично, заглушки и несогласованности

Явных маркеров `TODO`/`FIXME`/`HACK` в коде **нет ни одного** (перепроверено grep'ом по `.cs`, `.ts`,
`.tsx`, включая тестовые проекты). Всё ниже выявлено чтением кода.

### 5.1 Настоящие заглушки

1. **Рассылка не отправляет писем.** `Controllers/MailingController.cs` — `POST /api/companies/{id}/mail`
   собирает список email'ов клиентов, пишет `MailLog` и возвращает
   `{ recipientCount, message = "Рассылка поставлена в очередь" }`; `GET` того же маршрута отдаёт
   журнал. **Очереди нет, отправки нет.** Отдельно: аккаунты идентифицируются телефоном, email
   опционален, поэтому список получателей на практике может быть почти пустым. Фронт
   (`pages/owner/MailingTab.tsx`) показывает это как рабочую функцию — **решением Q9 текст оставлен
   как есть**, это сознательный выбор, а не упущение.

2. **Предоплата не проводится.** `RequirePrepayment` / `PaymentStatus.Pending` — организационный флаг.
   Платёж подтверждается вручную (`PATCH /api/bookings/{id}/mark-paid`). Интеграции нет.

3. **Уведомлений по-прежнему нет.** Инфраструктура периодических задач появилась (§4.13), но
   уведомлений на ней не построено: ни email, ни SMS, ни мессенджеров.
   `SubscriptionPlanConfig.NotifyDaysBefore` сохраняется, редактируется в
   `pages/admin/PlansTab.tsx` и подписан «Уведомление за N дн. до деактивации» — но **никем не читается**
   в бизнес-логике (единственное использование в бэкенде — присваивание в `AdminController.UpdatePlan`;
   проверено grep'ом заново).

4. **Самостоятельной покупки тарифа нет.** Подписку может выставить только SuperAdmin через
   `PUT /api/admin/owners/{ownerUserId}/subscription`. Экрана «оплатить тариф» на фронте нет —
   `ProfilePage.tsx` только показывает текущий план.

5. **Клиент не видит своих фото.** Фотофиксация работает только внутрь салона: у клиента нет ни
   эндпоинта, ни экрана. Осознанное решение Q5, а не пробел.

6. **Согласия клиента на фотосъёмку в интерфейсе по-прежнему нет.** Ни чекбокса, ни дисклеймера, ни
   хранения факта согласия. Цикл 3 построил общий правовой контур (§4.14), но фотосъёмку он
   **не покрывает**: согласие даётся на политику и оферту, отдельного согласия на съёмку нет.
   Решение Q6 цикла 2 остаётся в силе, README и `docs/faq.md` про это пишут прямо.

7. ⭐ **Правовые документы — черновик, и приложение это никак не проверяет.** `legal.json` помечен
   `"isDraft": true`, версии — `2026-09-08-draft`. Fail-fast на `isDraft` в Production **намеренно
   отсутствует** (решение заказчика: старт с черновиком разрешён), видимость обеспечивается только
   плашкой в UI и текстом самого документа.

8. ⭐ **`Booking.ClientDeleted` не доведён до интерфейса.** Бэкенд проставляет флаг, он приезжает в
   DTO и объявлен в TS-типах — и там же заканчивается (см. §5.2).

**Закрыто в цикле 2** (эти пункты из прошлой редакции больше не актуальны): загрузка аватара
(`POST /api/profile/avatar`) и картинки услуги (`POST /api/services/{id}/image`) реализованы —
`AppUser.AvatarUrl` и `Service.ImageUrl` теперь заполняются, а не висят пустыми.

### 5.2 Мёртвый код

Почти весь мёртвый код прошлой редакции удалён в цикле 2 (US-22):

| Что было | Что стало |
|---|---|
| `ServiceBooking/` (весь Blazor-проект, включая `Services/TimeSlot.cs` и свой `Dockerfile`) | **удалён**, из `.sln` тоже |
| `frontend/src/pages/DashboardPage.tsx` (192 стр.) | **удалён**; редирект `/dashboard` → `/cabinet` в `App.tsx` оставлен |
| `frontend/src/pages/owner/OwnerPage.tsx` (187 стр.) | **удалён**; редирект `/owner` → `/cabinet` оставлен |
| `GET /api/bookings/my` | **удалён** как дубль `/api/bookings/client` |
| `AppUser.CommissionPercent`, `ProfileDto.CommissionPercent`, `AdminUserDto.CommissionPercent` | **удалены** (комиссия живёт на `CompanyMember`) |
| расхождение `BookingDto` и TS-типов по `price`/`companySlug` | **закрыто**: поля добавлены в DTO, ветки UI ожили (решение Q12) |

Что осталось:

| Файл | Состояние |
|---|---|
| `ServiceBooking.API/appsettings.Production.json.example` + `.env.production.example` | оба описывают один и тот же прод — два разных способа конфигурации (файл vs env), актуален второй (`docker-compose.prod.yml`) |
| `frontend/design_handoff_site_redesign/` | 10 HTML-макетов редизайна, в сборку не идут |
| `components/auth/` | пустой каталог (не убран и в цикле 3) |
| ⭐ `Booking.ClientDeleted` во фронтенде | **фактически мёртвый флаг**: единственное упоминание во всём `frontend/src` — объявление поля в `types/index.ts:103` (проверено grep'ом). Ни одна страница его не показывает |

Комментарий в `tailwind.config.js`: legacy-шкала `primary`/`accent` намеренно оставлена перекрашенной
в новую палитру, чтобы не мигрировать вручную «ещё не перестилизованные» компоненты — то есть часть
разметки формально ещё на старых токенах.

### 5.3 Несогласованности бэкенда и фронтенда

Из десяти пунктов позапрошлой редакции восемь закрыл цикл 1/2; цикл 3 добавил два новых. Что есть сейчас:

1. **`BookingStatus.Pending` фактически недостижим.** Это дефолт сущности, но
   `BookingsController.Create` всегда ставит `Confirmed`. Ни один эндпоинт не выставляет `Pending`.
   Статус присутствует в enum, в TS-типах и в фильтрах UI. **Решение Q10: оставлено намеренно** —
   сценарий «запись ждёт подтверждения салона» проектируется отдельной историей позже.

2. **Пароль автосозданного мастера никуда не отправляется.** Генерируется детерминированно из
   канонического телефона и остаётся только в БД в виде хэша; передача пароля мастеру — ручная
   договорённость владельца. Схема слабая, но осознанная и задокументированная.

3. **`MailingController.CanManageCompany` — единственная копия проверки прав, не перешедшая на
   `CompanyMembership`.** Инлайн-`AnyAsync` по `CompanyMembers` с ролью `CompanyOwner`; логика та же,
   но код продублирован.

4. **Типы фронта по-прежнему копируются вручную** (`frontend/src/types/index.ts`), генерации из
   OpenAPI нет. Именно так и возник разрыв `price`/`companySlug`, который цикл 2 закрыл, и так же
   «повис» `clientDeleted` (п. выше).

5. ⭐ **Пагинация `GET /api/masters/clients` — единственная из четырёх, которая считается в памяти.**
   Контроллер материализует весь список клиентов компании, применяет `search` и режет `Skip/Take`
   там же. Признано приемлемым ревьюером и **прокомментировано в коде**; три остальные выборки
   (`admin/users`, `admin/companies`, публичные отзывы) пагинируются в SQL.

6. ⭐ **`docs/schedule.md` — единственный файл пользовательской документации, не тронутый циклом 3**
   (последняя правка — 1 сентября). Расписание цикл 3 не менял, так что расхождения не видно, но
   стилистически он отстал от остальных семи файлов.

Закрыто (для истории, чтобы не искать заново): фильтр ролей в `GET /api/companies/{id}/masters`;
права мастера на CRUD услуг; `SubscriptionResolver` теперь проверяет `PlanConfig.IsActive`;
`GET /api/workinghours` проверяет принадлежность; 500 на несуществующем `CompanyId` в
`POST /api/bookings`; дубль `using` в `BookingsController`; `launchSettings.json`;
расхождение `BookingDto` и TS-типов.

### 5.4 Документация, которая может быть устаревшей

- `API_DOCUMENTATION.md` (~237 КБ) **обновлён в цикле 3** коммитом `90a69b1`: в нём есть `/api/legal/*`,
  `/api/profile/export`, `/api/profile/delete-account`, `/api/health/*`, `acceptedLegal` в регистрации
  и новый §3.11 про конверт `PagedResult<T>`. Отдельно в нём есть §7 «Известные ограничения» — раздел,
  который стоит перечитывать вместе с §9 этого документа.
- ⚠️ ⭐ **`TEST_CATALOG.md` содержит устаревшее замечание о самом себе.** Его последний раздел
  («Документация, не обновлённая вместе с кодом») утверждает, что `API_DOCUMENTATION.md` в цикле 3 не
  тронут ни одной строкой. На момент написания это было правдой — документ обновили **позже**, тем
  самым `90a69b1`, а замечание не убрали. Единственное найденное расхождение документации с кодом.
- `SPEC.md`, `ARCHITECTURE.md`, `API_CONTRACT.md` в корне — документы **цикла 3**, а не постоянные
  справочники: следующая задача их перезапишет. Соглашения об архиве (`docs/history/`) в репозитории
  **нет** — предыдущие редакции живут только в git-истории (SPEC цикла 2 — `git show 0492092:SPEC.md`,
  цикла 1 — `e6b746c`, ещё более ранняя — `7c86ca2`).
- ⭐ `SPEC_DEFERRED_NOTIFICATIONS.md` и `SPEC_APPENDIX_CHANNELS.md` — **не документы цикла 3**, а
  сохранённая спека **отложенной** темы уведомлений (MAX/SMS). Тема отложена, не отменена; на неё
  ссылается код (`ProfileController.ChangePhone`).
- Преамбула `SPEC.md` предупреждает, что писалась против **прошлой** редакции `CURRENT_STATE.md`
  (`1c21bca` + цикл 2); после настоящего обновления это предупреждение устарело.

---

## 6. Конвенции проекта

Соблюдаются последовательно; ниже — то, чему стоит следовать, а не изобретать рядом своё.

### Бэкенд (C#)

- **Primary constructors** для всех контроллеров и сервисов:
  `public class BookingsController(AppDbContext db, SlotService slotService, ...) : ControllerBase`.
  Приватных полей `_db` нет нигде.
- **File-scoped namespaces**, `ImplicitUsings`, `Nullable` включены во всех проектах.
- **DTO — только `record`** с позиционными параметрами. Никаких классов-DTO, никакого AutoMapper:
  маппинг руками, обычно приватным статическим методом `MapToDto` в контроллере
  (`CompaniesController.MapToDto`, `BookingsController.MapToDto`) — намеренно «единственный источник
  истины», о чём есть комментарий в коде.
- **Именование:** сущности — `ServiceBooking.Core/Entities/<Name>.cs`, DTO — `DTOs/<Домен>/<Name>Dto.cs`,
  контроллеры — `<Домен>Controller`. Маршруты: `[Route("api/[controller]")]` там, где имя совпадает
  (`auth`, `bookings`, `companies`, `services`, `workinghours`), и явная строка там, где нет
  (`api/admin`, `api/masters`, `api/profile`, `api/reports`, `api/reviews`, `api/schedule-template`,
  `api/companies/{id:guid}/mail`).
- **Мелкие DTO живут в конце файла контроллера** (`AdminController.cs`, `ProfileController.cs`,
  `ReportsController.cs`, `MastersController.cs`, `ScheduleTemplateController.cs`), крупные вынесены в `DTOs/`.
- **Обработка ошибок — двухрежимная.** Осознанные 4xx контроллеры возвращают напрямую и **plain text**:
  `NotFound()`, `Forbid()`, `BadRequest("...")`, `Conflict(...)`, `StatusCode(402, "...")`,
  `NoContent()` — фронтовые мапперы (`src/utils/*Error.ts`) читают `response.data` как строку, поэтому
  ValidationProblemDetails там, где нужен свой текст, намеренно не используется.
  **Необработанные исключения** (цикл 1) вне Development ловит `app.UseExceptionHandler` и отдаёт
  `application/problem+json` с `traceId`; в Development работает developer exception page.
  **402 Payment Required — проектная конвенция для «упёрлись в тариф»** (лимит компаний, лимит
  сотрудников, online booking, mailing, analytics). **429** — загрузки и четыре политики цикла 3
  (см. ниже). **451 Unavailable For Legal Reasons** — «требуется принять новую редакцию документов»
  (цикл 3, `LegalConsentFilter`); фронт обрабатывает его отдельно, разлогинивать пользователя нельзя.
- **Авторизация — двухуровневая:** атрибут `[Authorize]`/`[Authorize(Roles=...)]` + приватный
  асинхронный предикат внутри контроллера (`CanManageCompany` / `CanManage` / `CanManageBookingAsync`).
  Приватные предикаты остались конвенцией, но их «членская» половина **обязана** идти через
  `Services/CompanyMembership.cs` (`IsStaffAsync` / `IsOwnerAsync`) — единственное SQL-определение
  «этот человек действительно работает в этой компании». Своих `AnyAsync` по `CompanyMembers` в новом
  коде быть не должно (единственное оставшееся исключение — `MailingController`).
- **Конкурентность:** любое «посчитал → записал» оборачивается в транзакцию +
  `AdvisoryLock.AcquireAsync(db, key)` (`pg_advisory_xact_lock`). Ключи: `booking-slot:{masterId}:{date}`,
  `owner-companies:{userId}`, `company-members:{companyId}`, `schedule-template:{masterId}:{companyId}`,
  `working-hours:{masterId}:{companyId}`, `company-photo-quota:{companyId}`,
  `scheduled-task:{name}`. Для фоновых задач есть неблокирующий `TryAcquireAsync`
  (`pg_try_advisory_xact_lock`): «кто-то уже делает» означает «пропустить запуск», а не «встать в очередь».
- **Загрузка файлов — только через `ImageUploadService`.** Четыре точки загрузки в продукте, одна
  реализация конвейера. Новая точка загрузки = новый `ImageProfile` + вызов `ReadAndProcessAsync`,
  а не свой код чтения `IFormFile`. Тип файла определяется **исключительно по сигнатуре байт**
  (`ImageSignature`), `Content-Type` и имя файла не используются нигде.
- **Два класса хранения — два разных типа возвращаемого значения** (`FileStorage`): публичный отдаёт
  URL, приватный — непрозрачный ключ, из которого нет пути обратно к URL. Флага `isPublic` намеренно
  нет: ошибка «положил приватное в публичное» должна быть ошибкой компиляции, а не находкой ревью.
- **Порядок операций с файлами:** на замену — «новый файл → коммит строки → удаление старого»;
  на удаление — «строка БД → файл». Худший исход в обоих случаях — осиротевший файл, который подметёт
  фоновая уборка; строка, указывающая в никуда, недопустима.
- **Фоновая работа — только через `IScheduledTask`.** Новая периодическая задача = класс + одна строка
  `AddScoped<IScheduledTask, …>` в `Program.cs`. Свои `BackgroundService`/таймеры заводить не нужно.
- ⭐ **Пагинация — только через `DTOs/Common/PagedResult.cs`.** Новый список наружу отдаётся конвертом
  `PagedResult<T>(Items, Page, PageSize, Total, HasNext)`, параметры нормализуются
  `Pagination.Normalize(page, pageSize)` (`pageSize` по умолчанию 20, потолок 100 — **клампится, а не
  400**; `page` клампится и снизу, и **сверху**, чтобы `(page-1)*pageSize` не переполнил `int`).
  Свои `?page=`-раскладки в контроллерах писать нельзя: четыре существующие выборки специально сведены
  в одну точку.
- ⭐ **Identity-роли пересчитываются только `Services/IdentityRoleSync.cs`.** Любое изменение
  `CompanyMember` завершается вызовом `IdentityRoleSync.SyncAsync(db, userManager, userId)` **внутри
  той же транзакции и строго после `SaveChangesAsync`** (порядок объяснён в XML-комментарии класса).
  Функция владеет **только** ролями `Master`/`CompanyOwner`; `Client` и `SuperAdmin` не трогает
  никогда. Сейчас вызывается из пяти операций (`Create`, `AddMember`, `RemoveMember`,
  `UpdateCompanyOwner` — там дважды, для нового и старого владельца, — и `DeleteAccount`), итого шесть
  вызовов.
- ⭐ **Правовой гейт — глобальный фильтр с allow-list, а не атрибут на действии.**
  `LegalConsentFilter` зарегистрирован как MVC-фильтр на **все** действия; он молчит для анонимных
  запросов и для явного allow-list (сами правовые эндпоинты, принятие согласия, выгрузка данных).
  Новый эндпоинт по умолчанию **закрыт** гейтом — это намеренное направление ошибки.
- ⭐ **Логирование — Serilog, и телефоны в него не попадают.** Хост-логгер заменён целиком
  (`builder.Host.UseSerilog`), формат — `CompactJsonFormatter` в stdout и в `logs/app-.json`.
  Маскирование — `Services/LogMasking.cs` + `PhoneMaskingEnricher`; `UseSerilogRequestLogging`
  отдельно позаботился о том, чтобы `?search=<телефон>` не утёк в строку запроса. Синк в GlitchTip —
  `Sentry.Serilog`, включается **только** при непустом `Sentry:Dsn`. Осознанные 4xx (400/402/403/404/
  409/429/451) логируются на уровне Information, а не Warning/Error.
- ⭐ **Проверки прод-конфигурации — в `Services/DeploymentSafetyChecks.cs`, чистыми статическими
  методами.** Новую обязательную настройку добавляют туда (и покрывают юнит-тестом), а не отдельным
  `if` в `Program.cs`.
- **Телефон — только через `PhoneNormalizer`.** Любая новая точка входа, принимающая номер, обязана
  нормализовать его до записи и до поиска.
- ⭐ **Rate limiting — именованные политики в `Program.cs` + `[EnableRateLimiting("…")]` на действии.**
  Глобального лимитера **нет** (`app.UseRateLimiter()` — no-op без атрибута), поэтому «не навесил
  атрибут» = «лимита нет»; на health-эндпоинтах это сделано намеренно. Пять политик:
  `uploads` (10/мин на пользователя, четыре точки загрузки), `auth-login` (10/мин на IP),
  `auth-register` (5/час на IP), `booking-create` (120/час авторизованным, 10/час анонимным),
  `data-export` (3/сутки). Числа читаются из секции `RateLimits` конфигурации; IP берётся **после**
  `UseForwardedHeaders` с явным списком `ForwardedHeaders:TrustedNetworks`. Тело ответа 429 пишет
  `OnRejected` — иначе фронтовым мапперам `*Error.ts` нечего было бы показать.
- **Комментарии — развёрнутые, объясняющие «почему», на английском.** Это заметная черта кодовой базы:
  почти каждое неочевидное решение прокомментировано абзацем (`Program.cs` про `OnTokenValidated`,
  `SlotService` про `allowWithoutSchedule`, `EffectivePlan.Free` про Free-базлайн). XML-документация
  включена (`GenerateDocumentationFile`) и подхватывается Swagger'ом.
- **Локализация:** сообщения об ошибках API — **в основном английские** (`"Time slot is no longer
  available"`, `"Company limit reached for the current tariff plan."`), но есть русские вкрапления
  в `ProfileController` (`"Неверный текущий пароль"`) и `MailingController`. Единой конвенции нет.
- **Миграции:** EF Core Code First, применяются автоматически при старте
  (`await db.Database.MigrateAsync()` в `Program.cs`). Имена — `PascalCase` описанием изменения.
  Скриптов отката/сидов данных (кроме ролей и SuperAdmin) нет.

### Фронтенд (TypeScript/React)

- **Функциональные компоненты, именованный экспорт** (`export function CabinetPage()`); дефолтный экспорт
  только у `App.tsx`.
- **Слой API — `src/api/<домен>.ts`,** объект-неймспейс с методами, всегда `.then(r => r.data)`:
  ```ts
  export const companiesApi = { getAll: () => api.get<Company[]>('/companies').then(r => r.data), ... }
  ```
  Компоненты **никогда не дёргают axios напрямую** — только через эти модули.
- **Данные с сервера — исключительно react-query** (`useQuery` / `useMutation` + `queryClient.invalidateQueries`).
  Ключи — массивы вида `['my-companies']`, `['company', slug]`, `['services', companyId]`.
- **Клиентское состояние — только `authStore`** (zustand + persist). Другого глобального стора нет,
  локальное состояние — `useState`.
- **Формы:** `react-hook-form` там, где полей много (создание компании, услуги), иначе — управляемые `useState`.
- **Ошибки HTTP → текст пользователю** через `src/utils/*Error.ts` — сейчас их восемь
  (`bookingError`, `cancelError`, `companyError`, `companyAdminError`, `companyManageError`,
  `memberError`, `planError`, `scheduleError`, `uploadError`) — `switch` по `status` с явной обработкой
  402/403/409/429. Это устоявшийся паттерн: новый пользовательский сценарий с гейтом должен получить
  свой маппер, а не строить текст на месте.
- **Приватные изображения грузятся как blob,** а не через `<img src>`: `hooks/useAuthedImage.ts` +
  `components/ui/AuthedImage.tsx`, `IntersectionObserver` для ленивой загрузки, `staleTime: Infinity`
  в react-query. Публичные картинки (логотип, аватар, картинка услуги) — обычный `<img src>`.
- ⭐ **Пагинированные списки** рисует общий `components/ui/Pagination.tsx`, поиск по серверу
  дебаунсится общим `hooks/useDebouncedValue.ts` (см. `MasterClientsPage.tsx` как образец связки
  «дебаунс → серверный `search` → `PagedResult`»). Скачивание файлов из защищённого API —
  `hooks/useExportData.ts` (blob + `a[download]`), по тому же принципу, что и приватные картинки.
- ⭐ **Линтер и форматтер есть:** ESLint 9 (flat-конфиг `frontend/eslint.config.js`,
  `typescript-eslint`, `eslint-plugin-react`/`react-hooks`/`react-refresh`, `eslint-config-prettier`)
  и Prettier 3. Команды — `npm run lint` и `npm run format`; `npm run lint` **входит в CI**.
  На бэкенде аналог — `.editorconfig` в корне (описывает уже сложившийся стиль; в CI не проверяется,
  §9.20).
- **Тесты фронта** лежат рядом с кодом (`src/utils/phone.test.ts`,
  `src/components/clientNotes/PhotoGallery.test.tsx`), а не в отдельном каталоге.
  `globals: false` — `describe`/`it`/`expect` импортируются явно, в тон конвенции именованных экспортов.
  `vitest.config.ts` намеренно отделён от `vite.config.ts`.
- **Стили — только Tailwind-утилиты в JSX,** с произвольными значениями (`text-[13.5px]`, `rounded-[18px]`)
  под макет. Токены палитры (`cream`, `ink`, `gold`, `line`, `muted`, `success/danger/warning/info`) — в
  `tailwind.config.js`. CSS-модулей нет, `index.css` содержит только base-слой и `line-clamp-2`.
- **UI-примитивы** в `src/components/ui/`: `Button`, `Input`, `Card`, `Modal`, `Badge`, `Icon`
  (свой SVG-набор из ~40 иконок, эмодзи не используются). Оверлеи закрываются через общий хук
  `src/hooks/useOverlayDismiss.ts`.
- **Вкладочные страницы** оформлены как локальные компоненты в одном файле (`AdminPage.tsx`,
  `CabinetPage.tsx`) либо вынесены в подпапку (`pages/owner/`, `pages/admin/`).
- **Язык интерфейса — русский**, форматирование чисел `toLocaleString('ru-RU')`, дат — `date-fns` с локалью `ru`.
- **Типы дублируются вручную** в `src/types/index.ts` — генерации из OpenAPI нет
  (отсюда риск расхождений, см. §5.3.4).

### Git

- Сообщения коммитов — на английском, одна строка-заголовок в повелительном наклонении +
  развёрнутое тело с объяснением «почему», трейлер `Co-Authored-By: Claude …` (в цикле 3 —
  `Claude Opus 5`, раньше `Claude Sonnet 5`).
- Работа идёт в ветке **`sanitation-cycle`** (циклы 1, 2 и 3), `master` — предыдущее состояние;
  в `master` циклы **не вливались**. CI триггерится на push в обе ветки и на любой pull request.
- Цикл 3 — **26 коммитов** (`f3adc6e..7a551eb`), в отличие от цикла 2, уехавшего одним коммитом
  `0492092`. Заголовки мелких коммитов несут идентификатор задачи из ARCHITECTURE (`T-B1`, `T-F5`, …)
  и историю из SPEC (`US-42`), ломающие изменения помечены прямо в заголовке (`BREAKING #1`,
  `BREAKING #2`). Рабочее дерево чистое.

---

## 7. Тесты

### Что есть — три набора

| Набор | Проект/каталог | Нужна БД? | Команда | Объём |
|---|---|---|---|---|
| Юнит-тесты бэкенда | `ServiceBooking.UnitTests` | нет | `dotnet test ServiceBooking.UnitTests` | **204** запуска (было 115) |
| **Функциональные (API) тесты** | `ServiceBooking.Tests` | **да, PostgreSQL** | `dotnet test ServiceBooking.Tests` | **404** запуска (было 344) |
| Тесты фронтенда | `frontend/src/**/*.test.ts(x)` | нет | `npm run test:run` (в `frontend/`) | **78** тестов (было 35) |

Количества посчитаны статически по атрибутам `[Fact]`/`[Theory]`+`[InlineData]` и вызовам `it(...)`
и совпадают с фактическим прогоном 2026-09-15.

### 7.1 Юнит-тесты бэкенда — `ServiceBooking.UnitTests`

**Ни БД, ни HTTP, ни моков** — только чистые функции.

- **Фреймворк:** xUnit 2.5.3 + FluentAssertions 6.12.1, `Microsoft.NET.Test.Sdk` 17.8.0,
  `coverlet.collector` 6.0.0. Ссылается напрямую на `ServiceBooking.API`.
- `ServiceBooking.API.csproj` содержит `<InternalsVisibleTo Include="ServiceBooking.UnitTests" />`.
- Атрибут `[TestCase(...)]` здесь **не используется** — стабильные ID есть только у функционального набора.

| Файл | Что покрывает | `[Fact]` + `[InlineData]` |
|---|---|---|
| **`DeploymentSafetyChecksTests.cs`** ⭐ | fail-fast прод-конфига: Jwt-ключ, пароль SuperAdmin, приватный корень внутри `wwwroot`, доверенные сети | 6 + 22 = 28 |
| **`LegalDocumentProviderTests.cs`** ⭐ | чтение и перечитывание `legal.json`, версии, `isDraft`, `changeKind` | 9 + 11 = 20 |
| **`PaginationTests.cs`** ⭐ | `Pagination.Normalize`: кламп снизу и **сверху** (переполнение `(page-1)*pageSize`), `HasNext` | 7 + 14 = 21 |
| **`LogMaskingTests.cs`** ⭐ | маскирование телефонов в логах | 5 + 11 = 16 |
| **`LegalConsentFilterTests.cs`** ⭐ | решение фильтра «требуется ли новое согласие» | 4 |
| `BookingFiltersTests.cs` | разбор `?status=`, предикат `Upcoming` | 11 + 6 = 17 |
| `SlotCalculatorTests.cs` | сетка слотов, перерывы, `allowWithoutSchedule`, граница суток | 15 |
| `ImageProcessorTests.cs` | ресайз, кроп, EXIF, формат вывода | 15 |
| `PhoneNormalizerTests.cs` | каноническая форма, валидность | 10 + 12 = 22 |
| `ScheduledTaskScheduleTests.cs` | `IsDue` / `IsOverdue` | 10 |
| `SubscriptionResolverRulesTests.cs` | правило разрешения тарифа, включая `PlanConfig.IsActive` | 10 |
| `PhotoQuotaTests.cs` | окно хранения, `Forever` | 8 + 2 = 10 |
| `ImageSignatureTests.cs` | определение JPEG/PNG/WEBP по байтам | 7 |
| `TokenServiceTests.cs` | claims, хеш `SecurityStamp` | 5 |
| `FileStorageTests.cs` | containment-проверка путей, ключи vs URL | 4 |
| **Итого** | | **204 запуска** |

### 7.2 Функциональные (API) тесты — `ServiceBooking.Tests`

**Это тот набор, который QA прогоняет как базовый.**

- **Фреймворк:** xUnit 2.5.3 + FluentAssertions 6.12.1 + `Microsoft.AspNetCore.Mvc.Testing` 8.0.11,
  `Microsoft.NET.Test.Sdk` 17.8.0, `coverlet.collector` 6.0.0 (покрытие настроено, но нигде не собирается).
- **Характер:** поднимается **реальный HTTP-конвейер** приложения через
  `WebApplicationFactory<Program>` (`Infrastructure/CustomWebApplicationFactory.cs`, окружение `Testing`)
  и **реальная PostgreSQL-база** `servicebooking_test`. Моков нет вообще.
- **Изоляция:** `Infrastructure/TestDatabaseFixture.cs` один раз дропает базу (`EnsureDeletedAsync`),
  затем старт приложения сам накатывает миграции и сидит роли/SuperAdmin. Основная коллекция — `"Api"`,
  параллелизм отключён (`AssemblyInfo.cs`).
- **Две дополнительные фабрики цикла 3** (каждая поднимает **свой** хост поверх той же базы):
  - ⭐ `Infrastructure/RateLimitTestFactory.cs` — хост с жёсткими лимитами. Основная фабрика в
    `Testing` поднимает все `RateLimits:*` до 10000/мин именно для того, чтобы остальные сотни тестов
    никогда не упирались в лимит; тесты `SEC-` про 429 нуждаются в обратном.
  - ⭐ `Infrastructure/LegalDocumentsTestFactory.cs` — хост, смотрящий на **одноразовую копию**
    `App_Data/legal`, чтобы `LEG-`-тесты переписывали `legal.json`/HTML прямо на диске (существенная
    vs редакционная правка, «подменили файл — без пересборки»), не мешая остальным. `ReloadSeconds: 1`,
    чтобы не спать 30 секунд на каждую смену версии.
- **Строка подключения:** переменная окружения `SERVICEBOOKING_TEST_CONNECTION`, при её отсутствии —
  литерал `Host=localhost;Database=servicebooking_test;Username=postgres;Password=` (пустой пароль).
- **Окружение `Testing`** читает закоммиченный `appsettings.Testing.json`: планировщик фоновых задач
  **выключен**, лимиты загрузок и rate limiting подняты.
- **Хелперы:** `Infrastructure/ApiTestBase.cs`, `JsonHelpers.cs`, `TestImages.cs`.
- **Маркировка:** каждый тест помечен `[Fact, TestCase("PREFIX-NNN")]` — стабильный ID для
  перекрёстных ссылок из `TEST_CATALOG.md`. Поиск теста по ID: `grep -rn "BK-003" ServiceBooking.Tests/`.

| Файл | Префикс | `[Fact]` + `[InlineData]` |
|---|---|---|
| `Tests/CompaniesTests.cs` | `CO-` | 77 + 3 = 80 |
| `Tests/BookingsFlowSmokeTests.cs` | `BK-` | 54 |
| `Tests/AdminTests.cs` | `ADM-` | 42 |
| `Tests/ServicesTests.cs` | `SVC-` | 19 |
| `Tests/MastersTests.cs` | `MC-` | 19 |
| `Tests/ClientNotePhotosTests.cs` | `MC-` (продолжает нумерацию) | 19 |
| `Tests/ProfileTests.cs` | `PROF-` | 18 |
| `Tests/WorkingHoursTests.cs` | `WH-` | 17 |
| `Tests/ScheduleTemplateTests.cs` | `ST-` | 15 |
| **`Tests/LegalConsentTests.cs`** ⭐ | `LEG-` | 14 |
| `Tests/AuthTests.cs` | `AUTH-` | 12 + 2 = 14 |
| `Tests/ReportsTests.cs` | `RPT-` | 13 |
| `Tests/ReviewsTests.cs` | `RV-` | 11 + 6 = 17 |
| **`Tests/DataRightsTests.cs`** ⭐ | `LEG-` (выгрузка и удаление аккаунта) | 10 |
| `Tests/MailingTests.cs` | `MAIL-` | 9 |
| `Tests/SchedulerTests.cs` | `SCH-` | 9 |
| **`Tests/PaginationTests.cs`** ⭐ | `PAG-` | 9 + 5 = 14 |
| **`Tests/LegalConsentVersionChangeTests.cs`** ⭐ | `LEG-` (смена редакции на живом хосте) | 7 |
| **`Tests/RateLimitingTests.cs`** ⭐ | `SEC-` | 6 |
| **`Tests/HealthTests.cs`** ⭐ | `OPS-` | 4 |
| **`Tests/IdentityRoleSyncTests.cs`** ⭐ | `SEC-` | 4 |
| **Итого** | | **404 запуска** |

Человекочитаемое описание каждого кейса — в `TEST_CATALOG.md` (~246 КБ, русский), §10.4.
**Оговорка про `LEG-036`** (гонка одновременного принятия согласия): сторож **вероятностный** —
красноту подтверждали на 20 итерациях, в репозиторий закоммичен одиночный прогон (см. §9.10).

### Как запускать (для QA — базовый прогон)

```bash
# Предусловие: доступен PostgreSQL на localhost:5432, пользователь postgres, ПУСТОЙ пароль.
# Иначе — задать SERVICEBOOKING_TEST_CONNECTION со своей строкой подключения.
# База servicebooking_test создаётся/пересоздаётся автоматически (EnsureDeletedAsync на старте).

cd /Users/ikolomeets/RiderProjects/ServiceBooking
dotnet build ServiceBooking.sln -warnaserror   # так же, как в CI
dotnet test ServiceBooking.UnitTests     # быстрый, без БД — прогонять первым
dotnet test ServiceBooking.Tests         # основной функциональный набор, нужна БД

cd frontend && npm ci && npm run lint && npx tsc --noEmit && npm run test:run
```

Числа последнего фактического прогона (2026-09-15, выполнял не автор этого документа):
`dotnet build … -warnaserror` — **0 warnings / 0 errors**; `ServiceBooking.UnitTests` — **204/204**;
`ServiceBooking.Tests` — **404/404**; `npm run test:run` — **78/78**; `tsc --noEmit` — чисто;
`npm run build` — успешно.

Ожидаемый шум в выводе функционального набора, не являющийся сбоем:
`RequestSizeLimitFilter ... does not support IHttpRequestBodySizeFeature` (у `TestServer` нет этой
фичи — тесты на лимит размера это учитывают) и намеренный `DbUpdateException`/FK-нарушение из теста
на обработку ошибок.

Запуск подмножества:
```bash
dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~LegalConsentTests"
```

### 7.3 Тесты фронтенда — Vitest

- **Раннер:** Vitest 3.2, окружение `jsdom` 25, `@testing-library/react` 16 + `jest-dom` + `user-event`.
- **Конфиг:** `frontend/vitest.config.ts` (намеренно отдельный от `vite.config.ts`),
  `globals: false` (явные импорты `describe`/`it`/`expect`), setup — `src/test/setup.ts`.
- **Что покрыто (78):** `utils/uploadError` (14), `utils/phone` (10), `utils/authError` (7) ⭐,
  `utils/cancelError` (6), `utils/legalError` (6) ⭐, `pages/MasterClientsPage` (6) ⭐,
  `pages/LegalDocumentPage` (5) ⭐, `components/clientNotes/PhotoGallery` (5),
  `components/ui/Pagination` (4) ⭐, `components/legal/ConsentGate` (4) ⭐,
  `components/legal/LegalUpdateBanner` (4) ⭐, `hooks/useDebouncedValue` (4) ⭐,
  `pages/CompanyPage` (3) ⭐.
- Впервые появились тесты на **страницы**, а не только на утилиты.

### Чего в тестах НЕТ

- **Нет e2e-тестов через браузер.** Ни Playwright, ни Cypress. «Функциональные» здесь = API-уровень.
  Ближайшее к e2e — `deploy/ci/smoke.sh`: bash + curl против **живого контейнера** (health, регистрация,
  загрузка аватара), запускается CI-джобом `docker-build`, а не тест-раннером.
- **Покрытие фронтенда остаётся точечным** — см. §9.13.
- **Нет ручного стендового чек-листа** как документа (CSP на скачивании выгрузки, живой GlitchTip,
  «ошибка → письмо») — см. §9 и §10.4.
- **Нет шага `dotnet format`** (`.editorconfig` есть, ESLint в CI есть) — см. §9.20.
- Не покрыты: капча с реальным ключом (в `Testing` `SmartCaptcha:SecretKey` пуст → валидация
  пропускается), миграции `NormalizePhoneNumbers`/`ResyncIdentityRoles` на боевом объёме данных,
  сами скрипты `deploy/backup/*`, `deploy/rollback.sh`, `deploy/monitor/*` (bash, тестов нет и
  вживую не запускались).

---

## 8. CI и деплой

### CI — есть (`.github/workflows/ci.yml`)

Появился в цикле 1, расширен в 2 и 3. Триггеры: push в `master`, `release-candidate`, `develop`
и любую ветку по маске `cycle/**`, плюс **любой**
pull request. `concurrency` с `cancel-in-progress`, у каждого job'а `timeout-minutes: 15`.

**Три независимых job'а:**

| Job | Что делает |
|---|---|
| `backend` | сервис-контейнер `postgres:16` c health-check; `SERVICEBOOKING_TEST_CONNECTION` указывает на него; кеш `~/.nuget/packages`; `dotnet restore` → **`dotnet build … -c Release -warnaserror`** → `dotnet test ServiceBooking.UnitTests` (быстрый, без БД, идёт первым) → `dotnet test ServiceBooking.Tests` |
| `frontend` | Node 20 c npm-кешем; `npm ci` → ⭐ **`npm run lint`** (ESLint) → `npx tsc --noEmit` → `npm run test:run` → `npm run build` (с `VITE_SMARTCAPTCHA_SITEKEY` из **переменной репозитория**, не секрета — site-ключ публичен) → ⭐ **выгрузка артефакта `frontend-dist-<sha>`** (только для `master`/`sanitation-cycle`, retention 30 дней) |
| `docker-build` | ⭐ теперь **не только собирает, но и запускает**: `docker build` → поднимает `postgres:16-alpine` в отдельной docker-сети → запускает образ с `ASPNETCORE_ENVIRONMENT=Production` и полным набором переменных из `DEPLOY.md` → `deploy/ci/smoke.sh` → `docker logs` при любом исходе |

Про `docker-build` важны две вещи, обе записаны комментариями прямо в workflow:
- **Запуск в `Production` — намеренный.** Это одновременно проверка, что fail-fast
  (`DeploymentSafetyChecks`) удовлетворяется **ровно тем** набором переменных, который описан в
  `DEPLOY.md` и `.env.production.example`: добавили новую обязательную переменную и забыли про
  документацию — job краснеет.
- **`deploy/ci/smoke.sh` гоняет реальную загрузку изображения** по HTTP в живой контейнер, то есть
  проверяет, что `SkiaSharp.NativeAssets.Linux.NoDependencies` действительно грузится на glibc-базе
  и что файл потом реально отдаётся. Ни `dotnet build`, ни `dotnet run` этого поймать не могут.
  Скрипт запускается и руками: `BASE_URL=http://localhost:5000 deploy/ci/smoke.sh`.
  В самом `ServiceBooking.API/Dockerfile` вверху стоит предупреждение: **не менять тег на `-alpine`**.

Чего в CI нет: `dotnet format --verify-no-changes` (см. §9), сбора покрытия, автодеплоя —
CI только проверяет и складывает артефакт фронта, деплой запускает человек.

### Деплой — настроен, задокументирован, с откатом; **вживую не выполнялся**

**Целевая среда — Linux VPS (reg.ru), домен `ezbook.ru`.** Runbook: `DEPLOY.md` (~50 КБ, 13 разделов
+ «Если что-то пошло не так»). Windows/IIS-контур (`DEPLOY-windows.md`) **выведен из скоупа цикла 3**
решением заказчика, но из репозитория не удалён (§9.8).

- `docker-compose.prod.yml`: `postgres` (порт наружу не публикуется) + `api` на `127.0.0.1:5000`,
  два named volume — `api_uploads` → `/app/wwwroot/uploads` и `api_private_uploads` →
  `/app/private-uploads` (**персональные данные, бэкапить отдельно**), плюс ⭐ **bind-mount
  `./legal:/app/App_Data/legal:ro`** поверх черновика, запечённого в образ: правовые тексты меняются
  на хосте **без пересборки и без релиза** (`DEPLOY.md` §2.1). Health-check контейнера смотрит на
  `/api/health/live`, готовность (`ready`) проверяет скрипт деплоя.
- `deploy/nginx/ezbook.conf`: статика из `/var/www/ezbook/current` (симлинк на релиз), прокси `/api/`
  и `/uploads/` на `127.0.0.1:5000`, `client_max_body_size 6M`, TLS через `certbot --nginx`.
  `/swagger/` не проксируется. Заголовки: на уровне `server` — `Strict-Transport-Security`,
  `X-Content-Type-Options`, `Referrer-Policy`; в `location /` они **повторены намеренно** (nginx не
  наследует `add_header` между уровнями — об этом есть комментарий прямо в конфиге) плюс
  `X-Frame-Options: DENY` и полный `Content-Security-Policy` с исключениями под SmartCaptcha и
  `img-src … blob:` под приватные фото и выгрузку. `location /embed/` вынесен **отдельно и намеренно
  без анти-фрейминга** — виджет для того и существует, чтобы его встраивали.
- ⭐ **Деплой больше не собирает фронт на боевом сервере.** `deploy/deploy.sh` (на машине
  разработчика, требует `gh auth login`) скачивает артефакт `frontend-dist-<sha>`, который CI собрал
  **для этого же коммита**, кладёт его на VPS новым каталогом `/var/www/ezbook/releases/<ts>/` и
  вызывает `deploy/deploy-remote.sh`. Тот тегирует текущий образ API как `previous`, запоминает
  текущий релиз, **атомарно переключает симлинк `current`**, пересобирает и перезапускает контейнер,
  **ждёт `/api/health/ready`** (а не «контейнер стартовал»), и если готовность не наступила —
  печатает следующей строкой готовую команду отката.
- ⭐ **Откат одной командой без аргументов:** `bash deploy/rollback.sh` — возвращает фронт на
  предыдущий релиз (или на указанный timestamp) и образ API на тег `previous`. Прямо в шапке скрипта
  записано, чего он **не** делает: **не откатывает миграцию БД** (они применяются на старте и
  необратимы) — на этот случай в `DEPLOY.md` §8 есть отдельный раздел.
- ⭐ **Бэкап:** `deploy/backup/backup.sh` + `servicebooking-backup.{service,timer}` — ежесуточный
  `pg_dump` и снимок обоих файловых хранилищ, **с хоста, а не из контейнера** (должен работать, когда
  приложение лежит), в `/var/backups/servicebooking` — **вне** docker-томов, чтобы
  `docker compose down -v` не унёс копии вместе с оригиналом. Хранение: 7 суточных + 4 недельных
  (воскресные). Перед стартом проверяет свободное место (1,5× от прошлого набора).
  **Копия локальная, внешней нет** — см. §9.2.
- ⭐ **Мониторинг:** `deploy/monitor/health-alert.{sh,service,timer}` — systemd-таймер на том же
  хосте дёргает `/api/health/ready`, после трёх подряд неудач шлёт письмо.
  `docker-compose.glitchtip.yml` — self-hosted **GlitchTip** (говорит по протоколу Sentry, поэтому
  синк `Sentry.Serilog` в API менять не нужно): четыре контейнера, отдельный стек со своими
  postgres/redis, наружу только через nginx. Выбран вместо self-hosted Sentry осознанно (~20
  контейнеров и 16 ГБ RAM против ~1 ГБ) — обоснование в шапке файла. **Вживую не поднимался.**

**Fail-fast прод-конфигурации** живёт теперь в `Services/DeploymentSafetyChecks.cs` (вынесен из
`Program.cs` ради тестируемости — чистые статические методы, 28 юнит-тестов). В окружении Production
приложение **не стартует**, если `Jwt:Key` пуст/короче 32 символов/равен плейсхолдеру; если
`SuperAdmin:Password` пуст или равен `Admin12345`/`CHANGE_ME`; если `Storage:PrivateRoot` резолвится
внутри `wwwroot`; ⭐ если не настроен `ForwardedHeaders:TrustedNetworks` (иначе rate limiting по IP
считал бы всех за один адрес docker-бриджа). Предупреждение без падения — `SuperAdmin:Phone` по
умолчанию. **На `isDraft` в `legal.json` fail-fast намеренно нет** (§9.4).
`.dockerignore` исключает `appsettings.Development.json` и `appsettings.Production.json`.

**Секреты:** `.gitignore` исключает `**/appsettings.*.json` (кроме базового и `Testing`), `.env`,
`.env.production`, `.deploy.env` и ⭐ `/legal/` (каталог оператора на VPS; в git лежит только
черновик `ServiceBooking.API/App_Data/legal/`). В git закоммичен только
`ServiceBooking.API/appsettings.json` с плейсхолдерами. **Но локально на машине разработчика лежат
незакоммиченные `appsettings.Development.json` и `appsettings.Production.json` с настоящими
секретами** (боевой пароль Postgres, JWT-ключ, серверный ключ SmartCaptcha) — их нельзя случайно
`git add -f`.

---

## 9. Технический долг и риски (по убыванию приоритета)

Из 22 пунктов прошлой редакции цикл 3 закрыл **семь** полностью и три — частично; список закрытого
приведён в конце раздела, чтобы никто не переоткрывал их заново. Пункты, появившиеся или
переформулированные в цикле 3, помечены ⭐.

**P0 — выглядит готовым, но вживую не проверялось**

Это главный разрыв текущего состояния: цикл 3 сделал запуск возможным, но **не выполнил** его.

1. ⭐ **Живого деплоя на VPS не было ни разу.** Весь эксплуатационный контур цикла 3 —
   `deploy/deploy.sh` (скачивание артефакта CI вместо сборки на сервере), `deploy/rollback.sh`,
   `deploy/backup/*`, `deploy/monitor/*`, `docker-compose.glitchtip.yml`, security-заголовки
   `deploy/nginx/ezbook.conf` — написан, вычитан на трёх кругах ревью и задокументирован в `DEPLOY.md`,
   но **на боевом сервере не выполнялся**. **GlitchTip вживую не поднимался**, цепочка
   «ошибка в приложении → событие в GlitchTip → письмо» end-to-end **не проверялась**. Единственное,
   что проверено машинно, — CI собирает образ, запускает его в `Production` и гоняет `deploy/ci/smoke.sh`.
2. ⭐ **Бэкап только локальный — на той же машине, что и данные.** `deploy/backup/backup.sh` кладёт
   дампы в `/var/backups/servicebooking` на самом VPS. Это защищает от порчи данных и ошибки
   оператора и **не защищает от потери или блокировки самого VPS**. Внешняя копия отложена решением
   заказчика (SPEC R13), ограничение честно записано в самом скрипте, `DEPLOY.md` §10.3 и `README.md`.
   Тем же свойством страдает алерт: `deploy/monitor/health-alert.sh` крутится на проверяемой машине,
   и если VPS лёг целиком, алерт не придёт.
3. ⭐ **CSP на сценарии скачивания выгрузки живьём не проверялся.** `GET /api/profile/export` фронт
   скачивает как blob с `a[download]` (`hooks/useExportData.ts`), а `location /` в nginx отдаёт
   строгий `Content-Security-Policy`. Что эта пара уживается, подтверждено только **статическим
   разбором QA** и согласием ревьюера — ни разу не открывалось в браузере через настоящий nginx.
   Кандидат №1 в стендовый чек-лист (которого пока нет, §10.4).
4. ⭐ **Правовые тексты — ЧЕРНОВАЯ редакция, юрист их не вычитывал.**
   `App_Data/legal/legal.json` содержит `"isDraft": true` и версии `2026-09-08-draft`. Старт с
   черновиком разрешён решением заказчика **осознанно**, поэтому fail-fast на `isDraft` в
   Production **намеренно отсутствует** — приложение поднимется с черновиком и молча. Видимость
   черновика обеспечена только плашкой в UI и первым абзацем самого текста.

**P1 — влияет на безопасность или корректность данных**

5. ⭐ **Смена номера телефона не подтверждается ничем, кроме текущего пароля.**
   `POST /api/profile/change-phone` сразу присваивает новый номер. Риск конкретный: гостевые визиты
   и заметки о клиенте ищутся по номеру, поэтому, указав чужой номер, можно прочитать чужую
   гостевую историю. Отложено **до появления SMS-канала**; решение описано в
   `SPEC_DEFERRED_NOTIFICATIONS.md` (пункт Д-1) и комментарием в `ProfileController.ChangePhone`
   (строка 151).
6. **Слабые дефолты в закоммиченном `appsettings.json` никуда не делись**
   (`Jwt:Key = "CHANGE_ME_…"`, `SuperAdmin:Password = "Admin12345"`, `SuperAdmin:Phone = "+70000000000"`).
   В цикле 3 fail-fast вынесен из `Program.cs` в чистый `Services/DeploymentSafetyChecks.cs`
   (`ValidateSecrets`, `ValidateTrustedNetworksConfigured`) и **покрыт юнит-тестами** (28 запусков),
   а CI-джоб `docker-build` стартует образ именно в `Production` — то есть проверка теперь сама под
   тестом. Остаточный риск **прежний**: в **не**-Production окружениях (стенд с
   `ASPNETCORE_ENVIRONMENT=Staging`) проверка не срабатывает вовсе.
7. **Полное отсутствие работы с часовыми поясами — подтверждено как решение, а не как упущение.**
   `Booking.Date/StartTime/EndTime` — `DateOnly`/`TimeOnly` без TZ, всё остальное — `DateTime.UtcNow`.
   Часовые пояса **осознанно не вводились** в цикле 3 (US-51); обоснование — в SPEC US-51,
   эксплуатационное следствие («все контейнеры в UTC») — в `DEPLOY.md` §13 и `README.md`.
   Для мультирегионального SaaS это остаётся источником ошибок «на границе суток».
8. **Security-заголовки: Linux-контур закрыт, Windows-контур — нет.** `deploy/nginx/ezbook.conf`
   теперь отдаёт `Strict-Transport-Security`, `X-Content-Type-Options`, `Referrer-Policy`,
   `X-Frame-Options: DENY` и полноценный `Content-Security-Policy` (с явными исключениями под
   SmartCaptcha и `blob:` для приватных фото), причём `location /embed/` намеренно оставлен без
   анти-фрейминга. В **Windows/IIS-контуре** (`frontend/public/web.config`, `DEPLOY-windows.md`)
   заголовков нет вообще — этот контур **выведен из скоупа цикла 3 решением заказчика**, но
   `DEPLOY-windows.md` из репозитория не удалён, и по нему всё ещё можно развернуть систему без защиты.
9. **Юридический риск фотофиксации принят, но не снят.** Общий правовой контур появился (§4.14), но
   **согласия клиента на фотосъёмку в интерфейсе по-прежнему нет** — ни чекбокса, ни дисклеймера, ни
   хранения факта: решение Q6 цикла 2 остаётся в силе. Согласие на политику и оферту,
   которое даёт клиент, фотосъёмку **не покрывает**. Ответственность переложена на компанию-салон
   текстом в README/`docs/faq.md`.

**P2 — код без тестов, на который многое завязано / хрупкие места**

10. ⭐ **Сторож гонки согласия — вероятностный.** Тест `LEG-036` (`LegalConsentTests.cs`) ловит гонку
    «одновременное принятие согласия», и его красноту подтверждали **на 20 итерациях**, а в репозиторий
    закоммичен **одиночный прогон**. То есть зелёный LEG-036 в CI не доказывает отсутствия регрессии —
    он лишь не поймал её в этот раз.
11. ⭐ **`Booking.ClientDeleted` фактически мёртв в UI.** Бэкенд проставляет флаг при удалении
    аккаунта, он доезжает до фронта и объявлен в `frontend/src/types/index.ts` (строка 103) — и это
    **единственное** его упоминание во всём фронтенде (проверено grep'ом). Ни одна страница его не
    отображает: персонал не видит, что клиент удалился.
12. ⭐ **Пагинация `GET /api/masters/clients` работает в памяти.** Контроллер материализует весь
    список клиентов компании, фильтрует по `search` и режет `Skip/Take` там же, а не в SQL.
    Признано приемлемым ревьюером (список ограничен одной компанией) и **задокументировано
    комментарием в коде** — но с ростом базы клиентов это первый кандидат на деградацию.
    Остальные три выборки (`admin/users`, `admin/companies`, публичные отзывы) пагинируются в БД.
13. **Фронтенд покрыт точечно.** 78 тестов Vitest на ~8 000 строк TSX. Покрыты мапперы ошибок,
    `formatPhone`, `PhotoGallery`, `Pagination`, `useDebouncedValue`, правовой контур
    (`ConsentGate`, `LegalUpdateBanner`, `LegalDocumentPage`) и по одному тесту на `CompanyPage` и
    `MasterClientsPage`. **Не покрыты**: `BookingModal`, `ManualBookingModal`, `RescheduleModal`,
    календарь `ScheduleTab`, вкладочные страницы кабинета и админки, `DeleteAccountPage`,
    `useExportData`, `useAuthedImage`.
14. **`CompaniesController` — 635 строк** (было 571) и 16 эндпоинтов, включая логику подписок,
    загрузку файлов, квоту фото и целиком сборку статистики (`GetStats`, ~70 строк агрегаций
    **в памяти** после `ToListAsync()`). Контроллер продолжает расти.
15. **Логика прав по-прежнему размазана по приватным копиям** `CanManageCompany`/`CanManage`, хотя их
    «членская» половина унифицирована через `CompanyMembership`. `MailingController` **до сих пор**
    не переведён на общий хелпер — единственное оставшееся исключение.
16. **Перечитывание ролей и сверка `SecurityStamp` на каждом запросе** (`Program.cs`,
    `OnTokenValidated`) — дополнительный запрос к БД на каждый аутентифицированный вызов без кеша.
    Цикл 3 добавил туда же чтение состояния согласия (claim'ы `consent_*` сверяются с актуальной
    версией документа), то есть путь на каждом запросе стал длиннее, а не короче.
17. **Шаг сетки слотов по-прежнему захардкожен 30 минутами** (`SlotCalculator.StepMinutes`).
18. **Миграция `NormalizePhoneNumbers` необратима и не проверялась на реальных данных.** Сейчас это
    безопасно (боевых данных нет), но повторно применить её к живой базе будет нельзя.
    Рядом появилась вторая миграция с данными — `ResyncIdentityRoles`, у неё **`Down` — no-op**
    (осознанно: откат пересчёта ролей бессмыслен).
19. **Ограничитель `PermitLimit` читается из конфигурации на каждый запрос** через
    `ctx.RequestServices.GetRequiredService<IConfiguration>()` — приём из цикла 2 сохранён и
    распространён на четыре новые политики.

**P3 — эксплуатация, гигиена, недоделки**

20. ⭐ **Шага `dotnet format` в CI нет.** `.editorconfig` появился (US-50) и **описывает уже
    существующий стиль**, а не задаёт новый, но `dotnet format --verify-no-changes` в CI не добавлен:
    сухой прогон даёт несколько сотен предсуществующих расхождений по переносам (в основном в
    `ServiceBooking.Tests`). Массовое переформатирование отложено отдельным коммитом — причина
    записана комментарием в шапке самого `.editorconfig`. ESLint в CI, наоборот, **добавлен**
    (`npm run lint` отдельным шагом).
21. **Устаревшие зависимости фронта с известными уязвимостями (`axios`, `form-data`, `react-router`)
    не закрыты.** Версии в `frontend/package.json` те же, что и до цикла 3 (`axios ^1.7.7`,
    `react-router-dom ^6.26.2`); `npm audit fix` закрывает часть без мажора, `react-router` требует
    мажорного апгрейда.
22. **Фичи, выглядящие готовыми в UI, но не работающие по сути:** «Рассылка» (писем нет, текст
    «Рассылка поставлена в очередь» **осознанно оставлен вводящим в заблуждение**, решение Q9),
    предоплата (платежей нет), `NotifyDaysBefore` в редакторе тарифов (уведомлений нет), обещание
    «напоминание накануне визита» на `HomePage.tsx` (Q8). README и `docs/faq.md` про это пишут
    честно — интерфейс нет.
23. **Локальные загруженные файлы не воспроизводимы на чистом клоне.** `wwwroot/uploads/**` и
    `App_Data/private-uploads/**` — в `.gitignore`; на машине разработчика в приватном каталоге лежат
    ~36 папок компаний с реальными JPEG. На свежем клоне ссылки из дампа БД будут битыми.
24. **Валидация DTO неполна** (`Slug`, `Bio`, `Comment`, `SendMailDto.Message`), нет запрета удалять
    последнего владельца, нет проверки статуса в `MarkPaid` — явно отложено как некритичное.
25. **`frontend/design_handoff_site_redesign/`** (10 HTML-макетов) лежит внутри `frontend/`, в сборку
    не идёт; **пустой каталог `frontend/src/components/auth/`** всё ещё на месте; два `.example`-файла
    прод-конфига (`ServiceBooking.API/appsettings.Production.json.example` и `.env.production.example`)
    описывают один и тот же прод двумя способами, актуален второй.

**Закрыто циклом 3** (не переоткрывать без причины):
отсутствие политики конфиденциальности, пользовательского соглашения и записи согласия;
отсутствие прав субъекта данных (выгрузка и удаление аккаунта);
rate limiting только на загрузках — теперь закрыты вход, регистрация, гостевая запись и выгрузка;
доверие `X-Forwarded-For` без списка доверенных сетей;
отсутствие структурированного логирования и трекера ошибок (Serilog + маскирование телефонов +
Sentry-совместимый синк) и отсутствие health-эндпоинтов;
односторонняя синхронизация Identity-ролей (`RemoveMember` не снимал роль) — закрыто
`IdentityRoleSync` + миграцией данных;
отсутствие пагинации на четырёх выборках и N+1 по ролям в `admin/users`;
отсутствие бэкапа, отката и security-заголовков в Linux-контуре как таковых;
«образ ни разу не запускался» — CI-джоб `docker-build` теперь **запускает** контейнер и гоняет
`deploy/ci/smoke.sh`, включая реальную загрузку изображения (то есть загрузку SkiaSharp внутри образа);
сборка фронта на боевом сервере — перенесена в CI-артефакт;
отсутствие `.editorconfig` и линтера на фронте.

**Закрыто циклами 1 и 2** (для истории): права мастера на CRUD услуг;
`GET /api/workinghours` без проверки принадлежности; публичный `GET /api/bookings/occupied`;
обход гейтов через `guestName` и `manual=true`; Swagger в Production; отсутствие глобальной обработки
исключений; `SubscriptionResolver` и `PlanConfig.IsActive`; комиссия, привязанная к аккаунту, а не к
компании; основа расчёта отчётов; загрузка логотипа по `Content-Type`; отсутствие CI; мёртвый
Blazor-проект и мёртвые страницы фронта; расхождение `BookingDto` и TS-типов; недостижимый `/embed`;
`launchSettings.json`; отсутствие тестов фронтенда; отсутствие юнит-тестов; двоение аккаунтов по
формату телефона.

---

## 10. Что уже существует в документации и тест-кейсах

Раздел нужен, чтобы следующие агенты **дополняли существующее, а не заводили параллельные версии**.
Всё перечисленное лежит в репозитории и обновлялось в цикле 3.

### 10.1 Краткая продуктовая документация

| Что | Путь | Формат | Структура |
|---|---|---|---|
| Обзор продукта | `README.md` (~19 КБ) | Markdown, русский | `## О проекте` (внутри жирными врезками «Для кого», «Роли», **«Что умеет»**, **«Чего пока нет»** — честный список отсутствующего: платежи, письма, уведомления, самостоятельная оплата тарифа, клиентский просмотр фото, согласие на съёмку) → **`## Запуск`** (`### Локально, всё в Docker`, `### Локально, без Docker для API`, `### Переменные окружения и секреты`, `### CI`, `### Деплой`). В цикле 3 README вырос эксплуатационной половиной: врезки «Бэкапы» (и прямо — что копия локальная) и «Часовые пояса» (UTC везде) |
| Changelog | `CHANGELOG.md` (~49 КБ) | Markdown, русский, по мотивам Keep a Changelog | **По датам завершения цикла, самая свежая запись сверху**; номеров версий в проекте нет. Верхняя запись — `## 2026-09-15 — подготовка к запуску для реальных людей`, внутри подразделы «Появилось новое», «Изменилось» и т.п. Запись открыто говорит, что сервис **не запущен** и живого развёртывания не было |

GitHub Releases / wiki в проекте не используются.

### 10.2 Развёрнутая пользовательская документация

Каталог **`docs/`**, Markdown, русский, **разбита по ролям**, точка входа — `docs/README.md`
с таблицей «кто вы → с чего начать» и разделом «Что нового» со ссылкой на `CHANGELOG.md`.

| Файл | О чём |
|---|---|
| `docs/README.md` | оглавление, роли в двух словах, ссылка на CHANGELOG |
| `docs/client.md` | запись на услугу — для клиента |
| `docs/master.md` | кабинет мастера (включая заметки и фото) |
| `docs/owner.md` | кабинет владельца компании |
| `docs/admin.md` | администрирование платформы |
| `docs/accounts.md` | общая: аккаунт, телефон как логин, смена пароля/номера |
| **`docs/personal-data.md`** ⭐ (~16 КБ, цикл 3) | общая: правовые документы и **плашка «Черновая редакция»**, согласие при регистрации, что происходит при редакционной и существенной правке, «Скачать свои данные», «Удалить аккаунт» (что удаляется, что остаётся, освобождение телефона, почему владелец компании так удалиться не может, что делать, если аккаунта нет) |
| `docs/schedule.md` | общая: расписание, перерывы, расчёт свободного времени (единственный файл `docs/`, не тронутый циклом 3) |
| `docs/faq.md` | частые вопросы **и честный список ограничений** |

Отдельного сайта документации и справочного раздела внутри приложения **нет**. Внутри приложения
пользователю доступны только сами правовые документы — страницы `/privacy` и `/terms`.

### 10.3 Документация API для внешних потребителей

| Что | Путь | Формат | Структура |
|---|---|---|---|
| Справочник эндпоинтов | `API_DOCUMENTATION.md` (~237 КБ) | Markdown, русский | §1 Обзор → §2 Аутентификация → §3 Ключевые бизнес-концепции (появился **§3.11 «Конверт `PagedResult<T>`»**) → **§4 Справочник эндпоинтов** (основной объём) → §5 Сквозные сценарии (curl-рецепты) → §6 Справочник кодов ответа → **§7 Известные ограничения**. Обновлён в цикле 3 коммитом `90a69b1`: `/api/legal/*`, `/api/profile/export`, `/api/profile/delete-account`, `/api/health/*`, `acceptedLegal` в регистрации, пагинация |
| Контракт текущего цикла | `API_CONTRACT.md` (~51 КБ) | Markdown, русский | документ **цикла 3**: контракт новых/изменённых эндпоинтов, коды ошибок (включая 451), тела ответов |

**OpenAPI/Swagger-файла в репозитории нет** — схема генерируется Swashbuckle во время работы и
доступна только в Development (`/swagger`). Postman-коллекции нет. Генерации TS-типов из схемы нет.

### 10.4 Описания тест-кейсов

**`TEST_CATALOG.md`** (~246 КБ), Markdown, русский — человекочитаемое описание **каждого**
автоматизированного кейса, отдельно от самого кода тестов.

- Структура: «Как устроены ссылки на тесты» → «Префиксы по доменам» → **«Юнит-тесты (без БД)»** →
  далее раздел на домен (`Auth`, `Bookings`, `Companies`, `Services`, `WorkingHours`,
  `ScheduleTemplate`, `Reviews`, `Mailing`, `Masters`, `ClientNotePhotos (цикл 2)`, `Scheduler (цикл 2)`,
  `Admin`, `Profile`, `Reports`) и разделы цикла 3: **`Legal (US-36…US-39)`**,
  **`Security / Rate limiting (US-42, US-46)`**, **`Health (US-43)`**, **`Pagination (US-49)`**,
  **«Регрессии продукта, найденные QA при разборе следствий пагинации — ЗАКРЫТЫ»**.
- Разделы цикла 3 помечены «найдено QA» — функциональные тесты правового контура, rate limiting,
  health и пагинации писал QA, а не разработчики.
- Связь с кодом — через стабильный ID из атрибута `[TestCase("PREFIX-NNN")]`.
  Поиск кейса по ID: `grep -rn "BK-003" ServiceBooking.Tests/`.
- ⚠️ **Последний раздел каталога — «Документация, не обновлённая вместе с кодом» — устарел.** Он
  утверждает, что `API_DOCUMENTATION.md` не тронут в цикле 3 ни одной строкой. На момент написания
  раздела это было правдой; документ обновили позже, коммитом `90a69b1` (см. §10.3). Само замечание
  из каталога не убрали.

Отдельного `TESTPLAN.md`, каталога `docs/testing/` или ручных сценариев вне `TEST_CATALOG.md`
в проекте **не найдено**. Ручной стендовый чек-лист (CSP на скачивании выгрузки, живой GlitchTip,
цепочка «ошибка → письмо») как документ **не существует** — эти проверки перечислены только в §9
этого файла и в отчёте QA.

### 10.5 Документы цикла работ

| Документ | Размер | Что это |
|---|---|---|
| `SPEC.md` | ~139 КБ | **цикл 3** «готовность к продакшену», редакция 2 (решения заказчика внесены в §0). Истории US-36…US-52, §2.3 сквозной порядок работ, §2.4 порядок урезания |
| `ARCHITECTURE.md` | ~135 КБ | **цикл 3**. Ключевые ссылки, на которые ссылается код: §4 правовые документы, §5 модель согласий, §6.3 451 и claim'ы, §7.3/§7.4/**§19.2** (почему удаление аккаунта — надгробие, а не `DELETE`), §8 IdentityRoleSync, §9 ForwardedHeaders, §10 health, §11 логирование и GlitchTip, §12 деплой/бэкап/откат, §15.1 пагинация, §17.1/§18.2 стиль |
| `API_CONTRACT.md` | ~51 КБ | **цикл 3**, см. §10.3 |
| **`SPEC_DEFERRED_NOTIFICATIONS.md`** ⭐ | ~90 КБ | SPEC **отложенного** цикла уведомлений клиенту по телефону (MAX/SMS). Тема **отложена решением заказчика, не отменена**. Здесь же живёт пункт **Д-1 «Подтверждение нового номера телефона при смене»**, на который ссылается комментарий в `ProfileController.ChangePhone` |
| **`SPEC_APPENDIX_CHANNELS.md`** ⭐ | ~57 КБ | приложение к нему: исследование каналов доставки |

**Соглашения об архиве (`docs/history/`) в репозитории по-прежнему нет**, каталога такого нет, в
README оно не описано — поэтому документы цикла 3 **оставлены в корне**, а не перенесены.
Предыдущие редакции живут только в git-истории: SPEC цикла 2 — `git show 0492092:SPEC.md`,
цикла 1 — `git show e6b746c:SPEC.md`, ещё более ранняя — `7c86ca2`.
