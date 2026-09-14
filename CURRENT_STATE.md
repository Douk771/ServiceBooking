# CURRENT_STATE — фактическое состояние кодовой базы ServiceBooking

**Актуально по состоянию на коммит: `1c21bca` + незакоммиченное рабочее дерево цикла B, дата: 2026-09-08.**

Документ описывает **что есть в репозитории сейчас**, без предложений по развитию.

Важная оговорка про точку отсчёта. Ветка — `sanitation-cycle`, HEAD — `1c21bca`. Но **часть описанного
здесь состояния ещё не закоммичена**: цикл B (доводка + фотофиксация работы мастера + периодические
фоновые задачи + каноническая форма телефона) полностью реализован, прошёл три раунда code-review и
приёмку QA, однако лежит в рабочем дереве (`git status`: 85 изменённых/удалённых файлов и 47 новых,
часть последних — каталоги).
Для целей этого документа цикл B считается частью текущего состояния. Диапазон изменений с прошлой
редакции документа: `263c661..1c21bca` (цикл A, закоммичен) + рабочее дерево (цикл B).

Все утверждения ниже получены чтением исходников, конфигов и git-истории. Где чего-то не нашлось —
так и написано.

**Проверено фактическим запуском** (прогонял devops-engineer отдельным шагом; автор этого документа
работает только на чтение и тесты не запускает — прогон набора пересоздаёт базу `servicebooking_test`,
см. §7):

| Команда | Результат | Время |
|---|---|---|
| `dotnet build ServiceBooking.sln` | **0 warnings, 0 errors** (в прошлой редакции было 2 предупреждения) | ~8 с |
| `dotnet test ServiceBooking.UnitTests` | **115 / 115** | ~2 с |
| `dotnet test ServiceBooking.Tests` | **344 / 344** (в прошлой редакции — 230/230) | ~65 с |
| `npm run test:run` (в `frontend/`) | **35 / 35** — тест-раннера фронта раньше не существовало вовсе | ~1,5 с |
| `npx tsc --noEmit` (в `frontend/`) | чисто | ~2,5 с |
| `npm run build` (в `frontend/`) | успешно | ~3,7 с |

**Базовый прогон QA (перед началом нового цикла, 2026-09-08).** Независимо перепроверено QA той же
командой на локальном PostgreSQL 16 (Homebrew, `servicebooking_test`, пустой пароль `postgres`,
тестовое окружение — `appsettings.Testing.json` подтверждает выключенный планировщик, боевых данных
не затронуто). Результат идентичен таблице выше: `dotnet build` — 0/0 за ~8 с; `ServiceBooking.UnitTests`
115/115 за ~0,5 с; `ServiceBooking.Tests` 344/344 за ~65 с (в логе шумной трассой проходит намеренный
`DbUpdateException`/FK-нарушение из теста на обработку ошибок — это не сбой, итог теста зелёный);
`npm run test:run` 35/35 за ~1,5 с; `tsc --noEmit` чисто; `npm run build` успешно. Предсуществующих
падений нет — чистый baseline, регрессий фиксировать не от чего.

Эти же числа подтверждены статическим подсчётом атрибутов `[Fact]`/`[Theory]`+`[InlineData]` и вызовов
`it(...)`. Оба предупреждения сборки из прошлой редакции устранены физически: Blazor-проект
`ServiceBooking/` удалён из solution, дубль `using` в `BookingsController` убран — поэтому в CI
включён `-warnaserror`.

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
| Обработка изображений | **SkiaSharp 2.88.8** + `SkiaSharp.NativeAssets.Linux.NoDependencies` (цикл B) — декод, ориентация по EXIF, ресайз, ре-энкод | `ServiceBooking.API.csproj`, `Services/ImageProcessor.cs` |
| Rate limiting | **`Microsoft.AspNetCore.RateLimiting`** (встроенный в ASP.NET Core 8), одна именованная политика `uploads` (цикл B) | `Program.cs` |
| Фоновые задачи | Свой `BackgroundService` + `IScheduledTask` (цикл B). Hangfire/Quartz **нет** | `Services/Scheduling/` |
| Документация API | Swashbuckle.AspNetCore 6.5.0, Swagger **только в Development** (цикл A) | `Program.cs` |
| Менеджер пакетов | NuGet, версии зафиксированы в `.csproj` (без `Directory.Packages.props`, без lock-файлов) | — |

### Фронтенд

| Что | Значение | Откуда |
|---|---|---|
| Сборщик | Vite 5.4.x, dev-порт 5173, прокси `/api` и `/uploads` → `process.env.VITE_API_TARGET ?? http://localhost:5000` (порт переопределяется переменной — на macOS 5000 занят AirPlay, коммит `75c5c3c`) | `frontend/vite.config.ts` |
| Тест-раннер | **Vitest 3.2 + jsdom 25 + @testing-library/react 16 + @testing-library/jest-dom + user-event** (появился в цикле B, US-23); конфиг **отдельный от vite.config.ts** | `frontend/vitest.config.ts`, `frontend/src/test/setup.ts` |
| Библиотека UI | React 18.3 + React DOM 18.3, TypeScript 5.5 (`strict`, `noUnusedLocals`, `noUnusedParameters`) | `frontend/package.json`, `frontend/tsconfig.json` |
| Роутинг | `react-router-dom` 6.26 | `frontend/src/App.tsx` |
| Server state | `@tanstack/react-query` 5.56 (`retry: 1`, `staleTime: 30_000`) | `frontend/src/App.tsx` |
| Client state | `zustand` 4.5 + `persist` (ключ localStorage `auth-store`) | `frontend/src/store/authStore.ts` |
| HTTP-клиент | `axios` 1.7, единственный инстанс с `baseURL: '/api'` | `frontend/src/api/client.ts` |
| Формы | `react-hook-form` 7.53 | `CabinetPage.tsx`, `OwnerPage.tsx`, `CompanyManagePage.tsx` |
| Даты | `date-fns` 3.6 + локаль `ru` | все страницы с датами |
| Стили | Tailwind CSS 3.4 + PostCSS + Autoprefixer, кастомная палитра | `frontend/tailwind.config.js`, `frontend/src/index.css` |
| Менеджер пакетов | npm, есть `package-lock.json`, на проде — `npm ci` | `deploy/deploy-remote.sh` |

Отдельной библиотеки валидации форм (zod/yup) нет — валидация делается правилами `react-hook-form`
и `[Required]`/`[EmailAddress]`-атрибутами DTO на бэкенде.

### Внешние сервисы

- **Yandex SmartCaptcha** — единственная реальная внешняя интеграция.
  Сервер: `ServiceBooking.API/Services/CaptchaService.cs`, `POST https://smartcaptcha.cloud.yandex.ru/validate`.
  Клиент: `frontend/src/components/booking/SmartCaptcha.tsx`, скрипт `https://smartcaptcha.yandexcloud.net/captcha.js`,
  ключ из `VITE_SMARTCAPTCHA_SITEKEY` (единственная используемая `import.meta.env`-переменная во всём фронтенде).
- **Платёжного шлюза нет.** Ни SDK, ни HTTP-вызовов — `PaymentStatus` меняется только вручную
  через `PATCH /api/bookings/{id}/mark-paid`.
- **Почтового провайдера нет.** SMTP/SendGrid/любой другой клиент в коде отсутствует (см. §5).
- **Интеграции с мессенджерами нет.** MAX был исследован и исключён из цикла B решением заказчика
  (SPEC §0, приложение А) — в коде нет ничего.
- **Хранилище файлов — локальный диск, но теперь ДВА класса хранения** (цикл B, `Services/FileStorage.cs`):
  - *публичный* — `wwwroot/uploads/{companies,avatars,services}`, раздаётся `app.UseStaticFiles()`,
    метод возвращает URL вида `/uploads/<область>/<guid>.<ext>`;
  - *приватный* — `App_Data/private-uploads/<companyId>/<guid>.jpg` (фото к заметкам о клиентах),
    **никогда не раздаётся статикой**, метод возвращает непрозрачный storage-key, не URL. Отдаётся
    только через `GET /api/client-notes/photos/{id}` с проверкой членства в компании.
  Корни настраиваются `Storage:PublicRoot` / `Storage:PrivateRoot`; в Production `Program.cs` **падает
  на старте**, если приватный корень резолвится внутри `wwwroot`. Облачного стораджа нет.

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

**Расхождение конфигов устранено (цикл B).** `ServiceBooking.API/Properties/launchSettings.json`
приведён в порядок: профили `http`/`https` слушают `http://localhost:5000` (https дополнительно 7016),
`launchUrl: "weatherforecast"` удалён, `launchBrowser: false`. Порт совпадает с тем, куда проксирует
Vite и куда мапится docker-compose.

**Конфигурация приложения** (`ServiceBooking.API/appsettings.json`) выросла тремя секциями цикла B:
`Storage` (`PrivateRoot`, `PublicRoot`, `MinFreeDiskMb: 1024`), `Uploads` (`MaxFileBytes: 5242880`,
`PerUserPerMinute: 10`), `ScheduledTasks` (`Enabled`, `TickSeconds: 60`, и по подсекции на задачу —
сейчас одна, `photo-retention-cleanup`).
Отдельно появился **закоммиченный** `appsettings.Testing.json` (исключение из правила
`**/appsettings.*.json` в `.gitignore`): выключает планировщик и поднимает лимит загрузок до 1000/мин,
чтобы функциональные тесты вели себя одинаково у всех и в CI. Секретов не содержит.

---

## 2. Структура репозитория

```
ServiceBooking.sln                  5 проектов (Blazor-проект удалён, добавлен UnitTests)
├── ServiceBooking.API/             ← точка входа, вся бизнес-логика веб-слоя
│   ├── Program.cs                  fail-fast прод-конфига, DI, Identity, JWT (+ перечитывание ролей и проверка
│   │                               SecurityStamp), CORS, Swagger (только Dev), exception handler, rate limiter,
│   │                               регистрация фоновых задач, миграции, сид
│   ├── Controllers/                13 контроллеров (14 классов — в Reviews их два)
│   ├── DTOs/                       Auth / Bookings / ClientNotes / Companies / Services / WorkingHours
│   ├── Services/                   SlotService+SlotCalculator, SubscriptionResolver, CaptchaService, TokenService,
│   │                               AdvisoryLock, BookingFilters, CompanyMembership, PhoneNormalizer,
│   │                               FileStorage, ImageSignature, ImageProcessor, ImageUploadService, PhotoQuota
│   │   └── Scheduling/             IScheduledTask, ScheduledTaskRunner, ScheduledTaskOptions,
│   │                               ScheduledTaskSchedule, Tasks/PhotoRetentionCleanupTask
│   ├── App_Data/private-uploads/   приватный класс хранения (в .gitignore, локальные фото разработчика)
│   ├── Dockerfile                  multi-stage, aspnet:8.0, EXPOSE 8080
│   └── appsettings*.json           appsettings.json и appsettings.Testing.json в git; Development/Production — в .gitignore
├── ServiceBooking.Core/            только сущности и перечисления, зависимость одна — Identity.EFCore
│   ├── Entities/                   17 классов
│   └── Enums/                      BookingStatus, PaymentStatus, PhotoRetention, UserRole
├── ServiceBooking.Infrastructure/  AppDbContext + 22 миграции EF Core
├── ServiceBooking.UnitTests/       ⭐ xUnit, БЕЗ БД и без HTTP — чистая логика (появился в цикле A, вырос в B)
├── ServiceBooking.Tests/           xUnit, функциональные тесты через WebApplicationFactory
│   ├── Infrastructure/             ApiTestBase, CustomWebApplicationFactory, TestDatabaseFixture, JsonHelpers,
│   │                               TestCaseAttribute, TestImages
│   └── Tests/                      14 файлов по доменам
├── frontend/                       React SPA
│   ├── src/api/                    15 модулей — тонкая обёртка над axios, по одному на домен
│   ├── src/pages/                  страницы; вложенные owner/ и admin/ — вкладки
│   ├── src/components/             booking/, clientNotes/, layout/, review/, schedule/, ui/
│   ├── src/hooks/                  useOverlayDismiss, useAuthedImage
│   ├── src/store/authStore.ts      единственный zustand-стор
│   ├── src/test/setup.ts           setup Vitest (jest-dom + cleanup Testing Library)
│   ├── src/types/index.ts          общие TS-типы (ручная копия серверных DTO)
│   ├── src/utils/                  10 модулей: 8 мапперов ошибок HTTP → русский текст + phone.ts; рядом *.test.ts
│   └── design_handoff_site_redesign/  HTML-макеты редизайна (*.dc.html) + README, не участвуют в сборке
├── .github/workflows/ci.yml        ⭐ CI: два job'а (backend с сервисом postgres, frontend)
├── deploy/                         deploy.sh (локально), deploy-remote.sh (на VPS), nginx/ezbook.conf
├── docker-compose.yml              dev: postgres + api
├── docker-compose.prod.yml         prod: postgres (без публикации порта) + api на 127.0.0.1:5000,
│                                   два volume: api_uploads (публичный) и api_private_uploads (приватный)
├── README.md                       ⭐ продуктовое описание + «чего пока нет» + ссылка на docs/
├── CHANGELOG.md                    ⭐ changelog по датам циклов, самая свежая запись сверху
├── docs/                           ⭐ пользовательская документация по ролям (см. §10)
├── SPEC.md / ARCHITECTURE.md / API_CONTRACT.md   документы текущего (B) цикла работ
├── API_DOCUMENTATION.md            ~208 КБ, подробный справочник эндпоинтов (рус.)
├── TEST_CATALOG.md                 ~209 КБ, человекочитаемый каталог всех тест-кейсов (рус.)
├── DEPLOY.md / DEPLOY-windows.md   runbook'и: reg.ru VPS (Linux+nginx+docker) и VK Cloud Windows (IIS+ARR)
└── .env.production.example, .deploy.env.example, appsettings.Production.json.example
```

⭐ — появилось в циклах A/B, в прошлой редакции документа этого не было.

### Точка входа и слои

- Единственная точка входа приложения — `ServiceBooking.API/Program.cs`. **Второй процесс** в том же
  хосте — `ScheduledTaskRunner` (`BackgroundService`), тикает раз в `ScheduledTasks:TickSeconds` (60 с).
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
  `ScheduledTaskSchedule`, `BookingFilters`, `ImageSignature`, `ImageProcessor`, `FileStorage`, `TokenService`.

### Мёртвый проект `ServiceBooking/` — удалён

Blazor Server-шаблон из первого коммита удалён целиком в цикле B (US-22): каталог `ServiceBooking/`
и запись о проекте в `ServiceBooking.sln` больше не существуют. Именно его предупреждение сборки
мешало включить `-warnaserror` в CI — теперь флаг включён.

---

## 3. Модель данных

Источник: `ServiceBooking.Core/Entities/*`, конфигурация связей — `ServiceBooking.Infrastructure/Data/AppDbContext.cs`.
Плюс стандартные таблицы ASP.NET Identity (`AspNetUsers` и т.д.) через `IdentityDbContext<AppUser>`.

### Сущности

| Сущность | Ключ | Ключевые поля | Связи |
|---|---|---|---|
| `AppUser : IdentityUser` | string | `FirstName`, `LastName`, `AvatarUrl`, `CreatedAt` (**`CommissionPercent` удалён** в цикле B) | 1—N: CompanyMemberships, ClientBookings, MasterBookings, MasterServices, WorkingHours |
| `Company` | Guid | `Name`, `Slug` (**уникальный индекс**), `Description`, `LogoUrl`, `Address`, `Phone`, `Email`, `AllowSelfBooking`, `RequirePrepayment`, `ShowInPublicListing`, `IsActive`, `OwnerUserId` | N—1 Owner (`Restrict`), 1—N Members / Services / Bookings |
| `CompanyMember` | Guid | `CompanyId`, `UserId`, `Role: UserRole`, `Bio`, **`CommissionPercent`** (переехал сюда с `AppUser` в цикле A), `JoinedAt`; **уникальный индекс `(CompanyId, UserId)`** | «многие-ко-многим» User↔Company с ролью |
| `Service` | Guid | `CompanyId`, `Name`, `DurationMinutes`, `Price decimal(10,2)`, `ImageUrl`, `IsActive` | 1—N MasterServices, Bookings |
| `MasterService` | Guid | `MasterId`, `ServiceId` | связка «мастер умеет услугу» |
| `WorkingHours` | Guid | `MasterId`, `CompanyId`, **`Date: DateOnly`**, `StartTime`, `EndTime`, `IsWorking`; **уникальный индекс `(MasterId, CompanyId, Date)`** (цикл A) | 1—N `ScheduleBreak` |
| `ScheduleBreak` | Guid | `WorkingHoursId`, `StartTime`, `EndTime` | перерывы внутри дня |
| `WeeklyScheduleTemplate` | Guid | `MasterId`, `CompanyId`, `DayOfWeek` (ISO 1..7), `IsWorking`, `StartTime`, `EndTime`; индекс `(MasterId, CompanyId)` | шаблон, «раскатываемый» в `WorkingHours` |
| `Booking` | Guid | `CompanyId`, `ServiceId`, `MasterId`, `ClientId?`, `GuestName/Phone/Email`, `Date`, `StartTime`, `EndTime`, **`Price` (снимок цены)**, **`CommissionPercent` (снимок комиссии, цикл A)**, `Status`, `PaymentStatus`, `Notes`, `CancellationReason` | Master `Restrict`, Client `SetNull` |
| `Review` | Guid | `BookingId` (**уникальный индекс** — 1 отзыв на запись), `CompanyId`, `MasterId`, `ClientId?`, `ReviewerName`, `Rating 1..5`, `Comment` | Booking `Cascade` |
| `ClientNote` | Guid | `CompanyId`, `MasterId` (автор), `ClientId?` / `GuestPhone?`, `Note`, **`BookingId?`** (визит, к которому написана заметка; `SetNull`), `CreatedAt`; индексы `(CompanyId, ClientId)` и `(CompanyId, GuestPhone)` | заметки общие для компании; удалять может **автор или владелец компании** (решение Q16); 1—N `ClientNotePhoto` |
| **`ClientNotePhoto`** ⭐ | Guid | `ClientNoteId`, `CompanyId` (денормализованная копия), `StoragePath`, `ThumbnailPath`, `ContentType`, `SizeBytes` (полный размер + миниатюра), `Width`, `Height`, `ContentHash` (SHA-256 **обработанных** байт), `UploadedByUserId?` (`SetNull`), `CreatedAt`; индексы `ClientNoteId`, `(CompanyId, CreatedAt)`, **уникальный `(ClientNoteId, ContentHash)`** | фото к заметке; каскад от заметки; ≤5 на заметку |
| **`ScheduledTaskState`** ⭐ | string `Name` (PK) | `LastStartedAtUtc?`, `LastFinishedAtUtc?`, `LastSucceeded`, `LastDurationMs`, `LastSummary?`, `LastError?` | состояние периодической задачи, переживающее рестарт |
| `AccountSubscription` | Guid | `OwnerUserId` (**уникальный индекс**), `PlanConfigId?`, `PaidUntil?`, `IsActive` | подписка на **аккаунт владельца**, а не на компанию |
| `SubscriptionPlanConfig` | Guid | `Name`, `PricePerMonth`, `MaxEmployees?`, `MaxCompanies?`, `AllowOnlineBooking`, `AllowMailing`, `AllowAnalytics`, `AllowPublicListing`, `AllowOnlinePayment`, **`PhotoQuotaMb?`** (null = без ограничения, дефолт 100), **`PhotoRetention`**, `IsActive`, `NotifyDaysBefore` | справочник тарифов |
| `SubscriptionChangeLog` | Guid | `OwnerUserId` (индекс), `ChangedByUserId`, старые/новые план, `PaidUntil`, `IsActive`, `Comment` | аудит изменений подписки |
| `MailLog` | Guid | `CompanyId`, `Subject`, `Message`, `SentById`, `RecipientCount`, `SentAt` | журнал «рассылок» |

Перечисления: `BookingStatus { Pending, Confirmed, Cancelled, Completed, NoShow }`,
`PaymentStatus { NotRequired, Pending, Paid }`, `UserRole { Client, Master, CompanyOwner, SuperAdmin }`,
**`PhotoRetention { SixMonths = 0, TwelveMonths = 1, Forever = 2 }`** (цикл B).
Сериализуются как строки (`JsonStringEnumConverter` в `Program.cs`).

### Как это связано смыслово

- Аккаунт = телефон **в канонической форме** (цикл B, US-26). `UserName == PhoneNumber ==` только цифры,
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

### Миграции (22, все в `ServiceBooking.Infrastructure/Migrations/`)

Первые 13 — как раньше: `InitialCreate` → `DateBasedSchedule` → `AddSubscriptionAndCommission` →
`AddReviewsTemplatesNotes` → `AddPromoGiftMailPlans` → `AddPrepaymentSupport` →
`LinkSubscriptionsToPlanConfigs` → `RemovePlanKey` → `AccountLevelSubscriptions` →
`AddBookingPriceSnapshot` → `RemovePromoCodesAndGiftCertificates` → `AddPublicListingAndOnlinePayment` →
`AddCompanyIdToClientNote`.

Цикл A добавил пять: `DeduplicateWorkingHours` → `AddWorkingHoursUniqueIndex` →
`AddCompanyMemberCommission` → `DeduplicateCompanyMembers` → `AddCompanyMemberUniqueIndex`
(парами: сначала чистка дублей, затем уникальный индекс).

Цикл B добавил ещё пять: `AddClientNoteBookingId` → `AddClientNotePhotos` → `AddPlanPhotoLimits` →
`AddScheduledTaskState` → **`NormalizePhoneNumbers`**.

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
| POST | `/api/auth/register` | анонимно; телефон нормализуется, невалидный → 400; выдаёт роль `Client` |
| POST | `/api/auth/login` | анонимно; поиск по канонической форме телефона, lockout после 5 попыток на 15 мин; невалидный номер даёт тот же 401, а не 400 |
| GET / PUT | `/api/profile` | авторизованные |
| POST | `/api/profile/change-password` | авторизованные |
| POST | `/api/profile/change-phone` | авторизованные; требует текущий пароль, через `SetUserNameAsync` |
| **POST** | **`/api/profile/avatar`** ⭐ | авторизованные; свой аватар (id из токена, route-параметра нет), ≤5 МБ, rate limit `uploads`, профиль обработки `Avatar` (512 px, квадратный кроп) |

JWT: HS256, срок **7 дней**, claims `sub/phone/given_name/family_name/jti/role` + **`sstamp`**
(хеш `SecurityStamp`, цикл A).
Важные детали в `Program.cs` (`JwtBearerEvents.OnTokenValidated`), на каждом запросе:
роли **перечитываются из БД** и подменяют claim'ы токена (отзыв роли действует немедленно), и
**сверяется хеш `SecurityStamp`** — смена пароля или телефона инвалидирует все ранее выданные токены.

`GET /api/profile` дополнительно отдаёт `ProfilePlanDto` для `CompanyOwner` — показывает реальную
строку подписки (в т.ч. просроченную), а не нормализованный Free.

Фронт: `LoginPage.tsx`, `RegisterPage.tsx`, `ProfilePage.tsx`, `store/authStore.ts` (persist в localStorage),
`api/client.ts` — интерцептор на 401 делает `logout()` + редирект на `/login`.

### 4.2 Компании — работает

`Controllers/CompaniesController.cs` (571 строка — самый большой контроллер)

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/companies` | публично; фильтр `ShowInPublicListing && plan.AllowPublicListing` |
| GET | `/api/companies/my` | владелец — свои компании |
| GET | `/api/companies/member` | все компании, где я участник любой роли |
| GET | `/api/companies/{slug}` | публично |
| GET | `/api/companies/{id}/masters?serviceId=` | публично; **фильтр по ролям `Master`/`CompanyOwner`** (цикл A) |
| GET | `/api/companies/{id}/members` | владелец/SuperAdmin |
| POST | `/api/companies` | авторизованные; **лимит `MaxCompanies`** под advisory lock, 402 при превышении |
| PUT | `/api/companies/{id}` | владелец |
| POST | `/api/companies/{id}/logo` | владелец; ≤5 МБ, rate limit `uploads`, **тип определяется по сигнатуре файла**, ре-энкод профилем `CompanyLogo` (512 px), старый файл удаляется **после** коммита нового URL |
| **GET** | **`/api/companies/{id}/photo-usage`** ⭐ | персонал компании **или SuperAdmin**; занятый объём, число фото, квота, % и срок хранения — единственное место, где SuperAdmin получает цифры по клиентским фото (содержимое ему недоступно) |
| POST | `/api/companies/{id}/members` | владелец; **лимит `MaxEmployees`** под advisory lock, 402; телефон нормализуется; неизвестное имя роли → 400 |
| PUT | `/api/companies/{id}/members/{memberId}/services` | владелец |
| PUT | `/api/companies/{id}/members/{memberId}/commission` | владелец; clamp 0..100; пишет в `CompanyMember.CommissionPercent` |
| DELETE | `/api/companies/{id}/members/{memberId}` | владелец |
| GET | `/api/companies/{id}/stats?from&to` | владелец; выручка, новые клиенты, топ услуг, по мастерам, по дням |

Автосоздание аккаунта мастера по телефону при `AddMember`: пароль выводится детерминированно —
`"Sb" + последние 6 цифр **канонического** телефона`, добитый нулями до 8 символов. После US-26 для
номера, введённого как «8 999…», результат отличается от того, что был до цикла B.

Фронт: `pages/owner/CompanyManagePage.tsx` (вкладки «Услуги / Расписание / Сотрудники / Настройки»
+ блок кода для встраивания виджета + индикатор занятого под фото места),
`pages/CabinetPage.tsx` (вкладка «Мои компании» с созданием), `pages/CompanyPage.tsx` (публичная витрина).

### 4.3 Услуги — работает

`Controllers/ServicesController.cs`: `GET /api/services?companyId=` (публично),
`POST`, `PUT /{id}`, `DELETE /{id}` (удаление — мягкое, `IsActive = false`),
**`POST /api/services/{id}/image`** ⭐ (загрузка картинки услуги, профиль `ServiceImage` 1200 px,
rate limit `uploads`).

Права ужесточены в цикле B: `CanManageCompany` здесь теперь — **только `CompanyOwner` (+SuperAdmin)**,
мастер больше не управляет каталогом услуг. `CreateServiceDto` **не принимает `ImageUrl`** — единственный
способ задать картинку — специальный эндпоинт загрузки (иначе клиент мог указать произвольный путь
`/uploads/...` и удалить чужой файл при замене). Есть валидация: `Name` 1..200,
`DurationMinutes` 1..1440, `Price` 0..1 000 000.

### 4.4 Расписание мастеров — работает

`Controllers/WorkingHoursController.cs`: `GET /api/workinghours?masterId&companyId&from&to`
(**теперь с проверкой `CanManage`** — цикл A закрыл утечку чужого расписания),
`PUT /api/workinghours` (upsert дня + полная перезапись перерывов), `DELETE /api/workinghours/{id}`.

`Controllers/ScheduleTemplateController.cs`: `GET`/`PUT /api/schedule-template`,
`POST /api/schedule-template/apply?masterId&companyId&from&to` — раскатка недельного шаблона по датам
(ISO: Пн=1 … Вс=7), существующие дни перезаписываются.
Цикл A добавил здесь: транзакцию + advisory lock на `PUT` (delete-then-insert стал атомарным) и на
`apply` (find-or-create больше не даёт дублей против нового уникального индекса), а также валидацию
диапазона — `to >= from` и не более **366** дней за один вызов (366, а не 365, чтобы високосный год
раскатывался одним запросом).

Фронт: `pages/owner/ScheduleTab.tsx` (календарь по месяцам, перерывы, сортировка мастеров с владельцем
первым), `components/schedule/WeeklyTemplateModal.tsx`.

### 4.5 Слоты и бронирование — работает, это ядро продукта

`Controllers/BookingsController.cs` + `Services/SlotService.cs`

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/bookings/occupied?masterId&date` | **`[Authorize]`** (цикл A): SuperAdmin, сам мастер или персонал компании, где этот мастер тоже состоит. Занятость намеренно **не** скоупится по компании — один человек занят у всех работодателей |
| GET | `/api/bookings/slots?companyId&masterId&serviceId&date&manual` | публично; **`companyId` обязателен** (цикл A), проверяется тройка «услуга принадлежит компании» + «мастер работает в компании»; `manual=true` учитывается **только для персонала этой компании** |
| POST | `/api/bookings` | публично (гость) и авторизованно |
| GET | `/api/bookings/{id}` | владелец записи или персонал |
| GET | `/api/bookings/client?status=` | мои записи как клиента, с фильтром статуса |
| GET | `/api/bookings/master?date&to` | `Master,CompanyOwner` |
| PATCH | `/api/bookings/{id}/complete` \| `/mark-paid` \| `/noshow` | `Master,CompanyOwner,SuperAdmin` |
| PATCH | `/api/bookings/{id}/reschedule` | персонал (advisory lock + проверка конфликта) |
| PATCH | `/api/bookings/{id}/cancel` | клиент записи или персонал; принимает причину отмены, которую видит вторая сторона |

`GET /api/bookings/my` **удалён** в цикле B как мёртвый дубль `/api/bookings/client`.

Логика слотов вынесена в чистый `Services/SlotCalculator.cs` (без БД и EF), `SlotService` остался
тонкой обёрткой над запросами. Шаг сетки **по-прежнему жёстко 30 минут** (`SlotCalculator.StepMinutes`),
слот занят, если пересекается с бронью (статус ≠ Cancelled) или перерывом. Без строки `WorkingHours`
на дату — пустой список, кроме `allowWithoutSchedule` (`manual`-режим), где берётся весь день
00:00–24:00 с защитой от переполнения `TimeOnly`. Проверка при самой записи (`IsSlotAllowed`) построена
**поверх той же `Calculate`**, а не отдельным предикатом — чтобы выдача слотов и их валидация не разошлись.

Гейты в `POST /api/bookings` (цикл A их существенно перебрал):
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
(цикл A) — раньше «удалённый» план продолжал раздавать возможности уже подписанным.

`CompanyDto` отдаёт три уровня флагов, и это осознанно (см. комментарии в файле):
- собственные тумблеры владельца: `AllowSelfBooking`, `RequirePrepayment`, `ShowInPublicListing`;
- вычисленные «реально работает»: `OnlineBookingEnabled`, `PrepaymentEnabled`, `PublicListingEnabled`;
- «сырые» возможности тарифа: `PlanAllowsOnlineBooking`, `PlanAllowsOnlinePayment`, `PlanAllowsPublicListing`, `MaxEmployees`.

Последняя группа добавлена коммитами `c062dd5` и `bc34db3` — чтобы UI гасил тумблер/кнопку заранее,
а не ловил 402 после заполнения формы. Фронт: `CompanyManagePage.tsx` (SettingsTab, MembersTab),
`CabinetPage.tsx` (скрывает вкладки «Отчёты»/«Рассылка» по `allowAnalytics`/`allowMailing`).

### 4.7 Отзывы — работает

`Controllers/ReviewsController.cs`: `POST /api/reviews` (только по завершённой записи, рейтинг 1..5,
один отзыв на бронь), `GET /api/reviews/can-review` (список ID записей, ждущих отзыва),
`GET /api/companies/{companyId}/reviews` (публично, класс `CompanyReviewsController` в том же файле).
Цикл A закрыл дыру: проверка авторства стала строгой (`booking.ClientId != userId` → 403) — раньше
условие `ClientId != null && …` полностью пропускало **гостевые** записи, и любой, кто узнал
`bookingId` (а он возвращается гостю при создании), мог оставить отзыв чужому бизнесу от своего имени.
Как следствие отзыв по гостевой записи теперь невозможен вовсе, и ветка с `GuestName` в
`ReviewerName` удалена как мёртвая.
Фронт: `components/review/ReviewModal.tsx`, отображение на `CompanyPage.tsx`.

### 4.8 База клиентов мастера и заметки — работает, переработано в цикле B

`Controllers/MastersController.cs`: `GET /api/masters/clients?companyId`,
`POST /api/masters/clients/notes`, `DELETE /api/masters/clients/notes/{id}`.
Доступ — только персонал компании (`CompanyMembership.IsStaffAsync`), участник с ролью `Client` больше
не проходит. Группировка отдельно по зарегистрированным клиентам и по `GuestPhone`.

Что изменилось:
- **правило «контакты скрыты через 24 часа после визита» удалено** (цикл B): оно было
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
  доля компании; гейт `plan.AllowAnalytics` → 402. Цикл A исправил основу расчёта: фильтр идёт
  **по дате визита** (`Booking.Date`), а комиссия берётся из **снимка на записи**
  (`Booking.CommissionPercent`), а не из текущей строки `CompanyMembers` — уход мастера или смена
  ставки больше не переписывают закрытый период. Фронт: `ReportsTab` внутри `CabinetPage.tsx`.
- `GET /api/companies/{id}/stats` (`CompaniesController`) — сводка по компании. Фронт: `pages/owner/DashboardTab.tsx`.
- `GET /api/admin/stats` — платформенная сводка.

### 4.10 Админка — работает

`Controllers/AdminController.cs`, все методы `[Authorize(Roles = "SuperAdmin")]`:
`GET /api/admin/stats`, `GET /api/admin/users?search` (поиск по телефону в любом формате —
строка запроса нормализуется), `PUT /api/admin/users/{id}/roles`,
`GET /api/admin/companies?search`, `PUT /api/admin/companies/{id}` (тело — `Name`, `IsActive`,
`AllowSelfBooking`; блокировка/разблокировка компании доступна из UI, US-04),
`PUT /api/admin/companies/{id}/owner`,
`PUT /api/admin/owners/{ownerUserId}/subscription`, `GET /api/admin/owners/{ownerUserId}/subscription-history`,
`GET /api/admin/bookings` (лимит `Take(500)`), `GET|POST|PUT|DELETE /api/admin/plans[/{id}]`
(delete — мягкий, `IsActive = false`; **вернуть тариф в продажу теперь можно через `PUT`**, US-05),
**`GET /api/admin/scheduled-tasks`** ⭐ (см. §4.13).
Из `AdminUserDto` убран `CommissionPercent` (комиссия стала per-company).
Фронт: `pages/AdminPage.tsx` (вкладки stats/companies/users/bookings) + `pages/admin/PlansTab.tsx`
(в редакторе тарифа появились квота на фото и срок хранения).

### 4.11 Виджет-встраивание — работает целиком

Маршрут `/embed/:slug` (`frontend/src/App.tsx`) рендерит `pages/EmbedPage.tsx` без навбара — список
услуг компании + `BookingModal`. В цикле B (US-03) в настройках компании
(`pages/owner/CompanyManagePage.tsx`) появился блок с готовым `<iframe …>`-сниппетом, ссылкой, кнопкой
«Скопировать» и предпросмотром; имя компании экранируется для атрибута `title`.

### 4.12 Фото к заметкам о клиентах ⭐ — новая функция цикла B

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

### 4.13 Периодические фоновые задачи ⭐ — первый фоновый процесс в продукте

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

---

## 5. Что реализовано частично, заглушки и несогласованности

Явных маркеров `TODO`/`FIXME`/`HACK` в коде **нет ни одного** (проверено grep'ом по `.cs`, `.ts`, `.tsx`).
Всё ниже выявлено чтением кода.

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

6. **Согласия клиента на фотосъёмку в интерфейсе нет.** Ни чекбокса, ни дисклеймера, ни хранения
   факта согласия. Решение Q6 принято заказчиком осознанно, юридический риск зафиксирован в SPEC;
   README и `docs/faq.md` про это пишут прямо.

**Закрыто в цикле B** (эти пункты из прошлой редакции больше не актуальны): загрузка аватара
(`POST /api/profile/avatar`) и картинки услуги (`POST /api/services/{id}/image`) реализованы —
`AppUser.AvatarUrl` и `Service.ImageUrl` теперь заполняются, а не висят пустыми.

### 5.2 Мёртвый код

Почти весь мёртвый код прошлой редакции удалён в цикле B (US-22):

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
| `components/auth/` | пустой каталог |

Комментарий в `tailwind.config.js`: legacy-шкала `primary`/`accent` намеренно оставлена перекрашенной
в новую палитру, чтобы не мигрировать вручную «ещё не перестилизованные» компоненты — то есть часть
разметки формально ещё на старых токенах.

### 5.3 Несогласованности бэкенда и фронтенда

Из десяти пунктов прошлой редакции **восемь закрыты** циклами A и B. Что осталось:

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
   OpenAPI нет. Именно так и возник разрыв `price`/`companySlug`, который цикл B закрыл.

Закрыто (для истории, чтобы не искать заново): фильтр ролей в `GET /api/companies/{id}/masters`;
права мастера на CRUD услуг; `SubscriptionResolver` теперь проверяет `PlanConfig.IsActive`;
`GET /api/workinghours` проверяет принадлежность; 500 на несуществующем `CompanyId` в
`POST /api/bookings`; дубль `using` в `BookingsController`; `launchSettings.json`;
расхождение `BookingDto` и TS-типов.

### 5.4 Документация, которая может быть устаревшей

- `API_DOCUMENTATION.md` (~208 КБ) обновлялся в цикле B (последняя правка 2026-09-07) и включает
  разделы по фото, планировщику и телефону. Отдельно в нём есть §7 «Известные ограничения» — раздел,
  который стоит перечитывать вместе с §9 этого документа.
- `SPEC.md`, `ARCHITECTURE.md`, `API_CONTRACT.md` в корне — документы **текущего (B) цикла**, а не
  постоянные справочники: следующая задача их перезапишет. Соглашения об архиве
  (`docs/history/`) в репозитории **нет** — предыдущие редакции живут только в git-истории
  (SPEC цикла A — `git show e6b746c:SPEC.md`, ещё более ранняя — `7c86ca2`).
- Преамбула `SPEC.md` предупреждает, что писалась против **прошлой** редакции `CURRENT_STATE.md`;
  после настоящего обновления это предупреждение устарело.

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
  **Необработанные исключения** (цикл A) вне Development ловит `app.UseExceptionHandler` и отдаёт
  `application/problem+json` с `traceId`; в Development работает developer exception page.
  **402 Payment Required — проектная конвенция для «упёрлись в тариф»** (лимит компаний, лимит
  сотрудников, online booking, mailing, analytics). **429** — только на четырёх точках загрузки.
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
- **Телефон — только через `PhoneNormalizer`.** Любая новая точка входа, принимающая номер, обязана
  нормализовать его до записи и до поиска.
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
  развёрнутое тело с объяснением «почему», трейлер `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
- Появилась вторая ветка: **`sanitation-cycle`** (циклы A и B), `master` — предыдущее состояние.
  CI триггерится на push в обе и на любой pull request.

---

## 7. Тесты

### Что есть — три набора

В прошлой редакции документа набор был **один**. Сейчас их три, и это разные инструменты
с разными предусловиями:

| Набор | Проект/каталог | Нужна БД? | Команда | Объём |
|---|---|---|---|---|
| Юнит-тесты бэкенда | `ServiceBooking.UnitTests` | нет | `dotnet test ServiceBooking.UnitTests` | **115** запусков |
| **Функциональные (API) тесты** | `ServiceBooking.Tests` | **да, PostgreSQL** | `dotnet test ServiceBooking.Tests` | **344** запуска |
| Тесты фронтенда | `frontend/src/**/*.test.ts(x)` | нет | `npm run test:run` (в `frontend/`) | **35** тестов |

Количества посчитаны статически по атрибутам `[Fact]`/`[Theory]`+`[InlineData]` и вызовам `it(...)`.

### 7.1 Юнит-тесты бэкенда — `ServiceBooking.UnitTests`

Появился в цикле A, вырос в цикле B. **Ни БД, ни HTTP, ни моков** — только чистые функции.

- **Фреймворк:** xUnit 2.5.3 + FluentAssertions 6.12.1, `Microsoft.NET.Test.Sdk` 17.8.0,
  `coverlet.collector` 6.0.0. Ссылается напрямую на `ServiceBooking.API`.
- `ServiceBooking.API.csproj` содержит `<InternalsVisibleTo Include="ServiceBooking.UnitTests" />` —
  чтобы внутренние чистые помощники `ImageProcessor` (ориентация, кроп, ресайз) тестировались
  синтетическими битмапами, а не подделкой EXIF-байт.

| Файл | Что покрывает | `[Fact]` / `[Theory]`(`InlineData`) |
|---|---|---|
| `SlotCalculatorTests.cs` | сетка слотов, перерывы, `allowWithoutSchedule`, граница суток | 15 |
| `ImageProcessorTests.cs` | ресайз, кроп, EXIF, формат вывода | 15 |
| `BookingFiltersTests.cs` | разбор `?status=`, предикат `Upcoming` | 11 / 2 (6) |
| `PhoneNormalizerTests.cs` | каноническая форма, валидность | 10 / 2 (12) |
| `ScheduledTaskScheduleTests.cs` | `IsDue` / `IsOverdue` | 10 |
| `SubscriptionResolverRulesTests.cs` | правило разрешения тарифа, включая `PlanConfig.IsActive` | 10 |
| `PhotoQuotaTests.cs` | окно хранения, `Forever` | 8 / 1 (2) |
| `ImageSignatureTests.cs` | определение JPEG/PNG/WEBP по байтам | 7 |
| `TokenServiceTests.cs` | claims, хеш `SecurityStamp` | 5 |
| `FileStorageTests.cs` | containment-проверка путей, ключи vs URL | 4 |
| **Итого** | | **95 + 5 (20) = 115 запусков** |

### 7.2 Функциональные (API) тесты — `ServiceBooking.Tests`

**Это тот набор, который QA прогоняет как базовый.**

- **Фреймворк:** xUnit 2.5.3 + FluentAssertions 6.12.1 + `Microsoft.AspNetCore.Mvc.Testing` 8.0.11,
  `Microsoft.NET.Test.Sdk` 17.8.0, `coverlet.collector` 6.0.0 (покрытие настроено, но нигде не собирается).
- **Характер:** поднимается **реальный HTTP-конвейер** приложения через
  `WebApplicationFactory<Program>` (`Infrastructure/CustomWebApplicationFactory.cs`, окружение `Testing`)
  и **реальная PostgreSQL-база** `servicebooking_test`. Моков нет вообще.
- **Изоляция:** `Infrastructure/TestDatabaseFixture.cs` один раз дропает базу (`EnsureDeletedAsync`),
  затем старт приложения сам накатывает миграции и сидит роли/SuperAdmin. Все тесты — в одной коллекции
  `"Api"`, параллелизм отключён (`AssemblyInfo.cs`: `CollectionBehavior(DisableTestParallelization = true)`).
  Тесты друг за собой не убирают — коллизии исключаются генераторами `Unique*` в `ApiTestBase`.
- **Строка подключения больше не захардкожена:** берётся из переменной окружения
  `SERVICEBOOKING_TEST_CONNECTION`, а при её отсутствии — из прежнего литерала
  `Host=localhost;Database=servicebooking_test;Username=postgres;Password=` (пустой пароль).
  Именно эту переменную задаёт CI.
- **Окружение `Testing`** читает закоммиченный `appsettings.Testing.json`: планировщик фоновых задач
  **выключен**, лимит загрузок поднят до 1000/мин. Тесты планировщика вызывают задачу напрямую.
- **Хелперы:** `Infrastructure/ApiTestBase.cs` — `RegisterAsync`, `LoginAsync`,
  `LoginAsSuperAdminAsync`, `CreateOwnerWithCompanyAsync(...)`, `AddMasterAsync`, сидирование
  услуг/расписания/подписки. `Infrastructure/JsonHelpers.cs` — обязателен для DTO с enum'ами
  (сервер отдаёт строки). **`Infrastructure/TestImages.cs`** ⭐ — генерация валидных JPEG/PNG/WEBP и
  заведомо битых байт для тестов загрузки.
- **Маркировка:** каждый тест помечен `[Fact, TestCase("PREFIX-NNN")]`
  (`Infrastructure/TestCaseAttribute.cs`) — стабильный ID для перекрёстных ссылок из `TEST_CATALOG.md`.
  Поиск теста по ID: `grep -rn "BK-003" ServiceBooking.Tests/`.

| Файл | Префикс | `[Fact]` / `[Theory]`(`InlineData`) |
|---|---|---|
| `Tests/AuthTests.cs` | `AUTH-` | 12 / 1 (2) |
| `Tests/BookingsFlowSmokeTests.cs` | `BK-` | 54 |
| `Tests/CompaniesTests.cs` | `CO-` | 77 / 1 (3) |
| `Tests/ServicesTests.cs` | `SVC-` | 19 |
| `Tests/WorkingHoursTests.cs` | `WH-` | 17 |
| `Tests/ScheduleTemplateTests.cs` | `ST-` | 15 |
| `Tests/MastersTests.cs` | `MC-` | 19 |
| **`Tests/ClientNotePhotosTests.cs`** ⭐ | `MC-` (продолжает нумерацию `MastersTests`) | 19 |
| **`Tests/SchedulerTests.cs`** ⭐ | `SCH-` | 9 |
| `Tests/ReviewsTests.cs` | `RV-` | 11 / 2 (6) |
| `Tests/MailingTests.cs` | `MAIL-` | 9 |
| `Tests/ReportsTests.cs` | `RPT-` | 13 |
| `Tests/ProfileTests.cs` | `PROF-` | 18 |
| `Tests/AdminTests.cs` | `ADM-` | 41 |
| **Итого** | | **333 + 4 (11) = 344 запуска** |

Человекочитаемое описание каждого кейса — в `TEST_CATALOG.md` (~209 КБ, на русском), поддерживается
в актуальном состоянии вместе с кодом; в нём есть отдельные разделы «Юнит-тесты (без БД)»,
«ClientNotePhotos (US-17…US-20, цикл B)» и «Scheduler (US-21, цикл B)».

### Как запускать (для QA — базовый прогон)

```bash
# Предусловие: доступен PostgreSQL на localhost:5432, пользователь postgres, ПУСТОЙ пароль.
# Иначе — задать SERVICEBOOKING_TEST_CONNECTION со своей строкой подключения.
# База servicebooking_test создаётся/пересоздаётся автоматически (EnsureDeletedAsync на старте).

cd /Users/ikolomeets/RiderProjects/ServiceBooking

dotnet test ServiceBooking.UnitTests     # быстрый, без БД — прогонять первым
dotnet test ServiceBooking.Tests         # основной функциональный набор, нужна БД

cd frontend && npm run test:run          # тесты фронтенда
```

Числа последнего фактического прогона (выполнял devops-engineer, не автор этого документа):
`dotnet build` — **0 warnings / 0 errors**, ~8 с; `ServiceBooking.UnitTests` — **115/115**, ~2 с;
`ServiceBooking.Tests` — **344/344**, ~65 с; `npm run test:run` — **35/35**, ~1,5 с;
`tsc --noEmit` — ~2,5 с; `npm run build` — ~3,7 с.

В выводе функционального набора шумят предупреждения
`RequestSizeLimitFilter ... does not support IHttpRequestBodySizeFeature` — ожидаемо:
`WebApplicationFactory` использует `TestServer`, у которого нет этой фичи; тесты на лимит размера
файла это учитывают.

Запуск подмножества:
```bash
dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~BookingsFlowSmokeTests"
```

### 7.3 Тесты фронтенда — Vitest ⭐

Появились в цикле B (US-23) — до этого фронт не был покрыт вообще ничем, кроме `tsc`.

- **Раннер:** Vitest 3.2, окружение `jsdom` 25, `@testing-library/react` 16 + `jest-dom` + `user-event`.
- **Конфиг:** `frontend/vitest.config.ts` (намеренно отдельный от `vite.config.ts`),
  `globals: false` (явные импорты `describe`/`it`/`expect`), setup — `src/test/setup.ts`
  (подключает `jest-dom/vitest` и вручную вызывает `cleanup()` в `afterEach`, т.к. авто-cleanup
  Testing Library полагается на глобальный `afterEach`, которого при `globals: false` нет).
- **Что покрыто:** `src/utils/uploadError.test.ts` (14), `src/utils/phone.test.ts` (10),
  `src/utils/cancelError.test.ts` (6), `src/components/clientNotes/PhotoGallery.test.tsx` (5).
  Настройка `coverage.include` ограничена `src/utils/**` и `src/components/clientNotes/**` — то есть
  покрытие меряется только для того, что цикл B и писал.

### Чего в тестах НЕТ

- **Покрытие фронтенда — точечное.** 35 тестов на ~4 500+ строк TSX: покрыты мапперы ошибок,
  форматирование телефона и одна галерея фото. Сложные модалки бронирования, календарь расписания,
  вкладочные страницы кабинета — по-прежнему без тестов.
- **Нет e2e-тестов через браузер.** Ни Playwright, ни Cypress. «Функциональные» здесь = API-уровень.
- **Нет линтера/форматтера** ни на фронте (ESLint/Prettier), ни на бэке (`.editorconfig` отсутствует).
- Не покрыты: капча с реальным ключом (в `Testing` `SmartCaptcha:SecretKey` пуст → валидация
  пропускается), реальная загрузка файла на диск в контейнере (см. §9), миграция
  `NormalizePhoneNumbers` на боевом объёме данных.

---

## 8. CI и деплой

### CI — есть ⭐ (`.github/workflows/ci.yml`)

Появился в цикле A, расширен в цикле B. Триггеры: push в `master` и `sanitation-cycle`, **любой**
pull request. `concurrency` с `cancel-in-progress` — новый push отменяет предыдущий прогон той же ветки.

**Три независимых job'а:**

| Job | Что делает |
|---|---|
| `backend` | сервис-контейнер `postgres:16` c health-check; `SERVICEBOOKING_TEST_CONNECTION` указывает на него; кеш `~/.nuget/packages`; `dotnet restore` → **`dotnet build … -c Release -warnaserror`** → `dotnet test ServiceBooking.UnitTests` (быстрый, без БД, идёт первым) → `dotnet test ServiceBooking.Tests` |
| `frontend` | Node 20 c npm-кешем; `npm ci` → `npx tsc --noEmit` → `npm run test:run` → `npm run build` |
| **`docker-build`** ⭐ | `docker build -f ServiceBooking.API/Dockerfile -t servicebooking-api:ci .` — образ **не запускается и никуда не пушится**, проверяется только сборка |

Зачем отдельный `docker-build`: `SkiaSharp.NativeAssets.Linux.NoDependencies` загружается только на
glibc-базе. Откат Dockerfile на `-alpine` или непубликация нативного ассета прошли бы мимо
`dotnet build`/`dotnet run` и упали бы только в настоящем контейнере. В самом `ServiceBooking.API/Dockerfile`
теперь стоит комментарий-предупреждение вверху файла: **не менять тег на `-alpine`** (musl vs glibc),
а если Alpine всё же понадобится — сначала поменять пакет нативных ассетов SkiaSharp.

`-warnaserror` включён именно потому, что удалён Blazor-проект, который приносил своё предупреждение.

Чего в CI нет: линтера/форматтера (их нет и в проекте), сбора покрытия, публикации артефактов,
автодеплоя — CI только проверяет, деплой остаётся ручным.

### Деплой — настроен и задокументирован, ручной по кнопке

**Целевая среда 1 — Linux VPS (reg.ru), домен `ezbook.ru`.** Runbook: `DEPLOY.md`.
- `docker-compose.prod.yml`: `postgres` (порт наружу не публикуется) + `api` на `127.0.0.1:5000`,
  **два** named volume: `api_uploads` → `/app/wwwroot/uploads` (публичный класс) и
  `api_private_uploads` → `/app/private-uploads` (приватный класс, **персональные данные — бэкапить
  отдельно**), `Storage__PrivateRoot=/app/private-uploads` задан прямо в compose.
  Остальные секреты — из `.env` (шаблон `.env.production.example`).
- `deploy/nginx/ezbook.conf`: раздаёт статику из `/var/www/ezbook/dist`, проксирует `/api/` и
  `/uploads/` на `127.0.0.1:5000`, `client_max_body_size 6M`. TLS — через `certbot --nginx`.
  - `/swagger/` **больше не проксируется** — Swagger доступен только в Development, запрос туда
    падает в SPA-fallback и даёт 404 от React Router.
  - ⭐ **Анти-clickjacking:** `location /` отдаёт `X-Frame-Options: DENY` и
    `Content-Security-Policy: frame-ancestors 'none'` (оба `always`), а `location /embed/` вынесен
    **отдельно и намеренно без этих заголовков** — виджет записи для того и существует, чтобы его
    встраивали в чужой сайт. Раньше security-заголовков не было вовсе.
- `deploy/deploy.sh` (запускается локально/из Rider) → ssh → `git pull` → `deploy/deploy-remote.sh`,
  который делает `docker compose ... up -d --build`, `npm ci && npm run build`, копирует `dist` в
  `/var/www/ezbook/dist`, `restorecon` (SELinux), `nginx -t && systemctl reload nginx`.
  Хост берётся из `.deploy.env` (шаблон `.deploy.env.example`, в примере — `root@31.31.197.39`).

**Целевая среда 2 — Windows (VK Cloud) + IIS.** Runbook: `DEPLOY-windows.md`.
`frontend/public/web.config` попадает в `dist` при сборке и настраивает IIS URL Rewrite + ARR:
прокси `^api/` и `^uploads/` на `localhost:5000` и SPA-fallback на `index.html`.
**Анти-iframe-заголовков, которые получил nginx, в Windows-контуре нет** — см. §9.
Приватный корень хранения на Windows опирается на то, что IIS по умолчанию не отдаёт `App_Data`
(имя каталога выбрано именно ради паритета двух контуров).

**Fail-fast прод-конфигурации** (цикл B, `Program.cs`, до `builder.Build()`): в окружении Production
приложение **не стартует**, если `Jwt:Key` пуст, короче 32 символов или равен плейсхолдеру; если
`SuperAdmin:Password` пуст или равен `Admin12345` / `CHANGE_ME`; если `Storage:PrivateRoot` резолвится
внутри `wwwroot`. Отдельно печатается предупреждение (не падение), если `SuperAdmin:Phone` остался
`+70000000000`. `.dockerignore` дополнительно исключает `appsettings.Development.json` и
`appsettings.Production.json`, чтобы локальная сборка образа не запекла в него реальные секреты.

**Секреты:** `.gitignore` исключает `**/appsettings.*.json` (кроме базового), `.env`, `.env.production`,
`.deploy.env`. В git закоммичен только `ServiceBooking.API/appsettings.json` с плейсхолдерными
значениями. **Но локально на машине разработчика лежат незакоммиченные
`appsettings.Development.json` и `appsettings.Production.json` с настоящими секретами**
(боевой пароль Postgres, JWT-ключ, серверный ключ SmartCaptcha) — их нельзя случайно `git add -f`.

---

## 9. Технический долг и риски (по убыванию приоритета)

Из 23 пунктов прошлой редакции **13 закрыто** циклами A и B — список закрытого приведён в конце
раздела, чтобы никто не переоткрывал их заново.

**P1 — влияет на безопасность или корректность данных**

1. **Слабые дефолты в закоммиченном `appsettings.json` никуда не делись**
   (`Jwt:Key = "CHANGE_ME_…"`, `SuperAdmin:Password = "Admin12345"`, `SuperAdmin:Phone = "+70000000000"`),
   **но теперь они не могут утечь в прод незамеченными**: `Program.cs` в окружении Production
   падает на старте на каждом из них (кроме телефона — там предупреждение).
   Остаточный риск: в **не**-Production окружениях (например, стенд, поднятый с
   `ASPNETCORE_ENVIRONMENT=Staging`) проверка не срабатывает вовсе.
2. **Полное отсутствие работы с часовыми поясами.** `Booking.Date/StartTime/EndTime` — `DateOnly`/`TimeOnly`
   без TZ; `CreatedAt`/`PaidUntil`/`ScheduledTaskState.*` — `DateTime.UtcNow`. Сроки хранения фото и
   окна расписания считаются в UTC. `Company.TimeZoneId` **осознанно не заводился** в цикле B (SPEC §8.1):
   он нужен под напоминания, которых нет. Для мультирегионального SaaS это остаётся источником ошибок
   «на границе суток».
3. **Rate limiting покрывает только загрузки.** Политика `uploads` (10/мин на пользователя) висит на
   четырёх эндпоинтах загрузки изображений. На `/api/auth/login`, `/api/auth/register` и гостевом
   `POST /api/bookings` — по-прежнему **ничего**: от перебора логина частично защищает Identity lockout
   (5 попыток / 15 мин), от спама записей — только SmartCaptcha, и только когда ключ задан.
   Ограничитель `PermitLimit` читается из конфигурации **на каждый запрос** через
   `ctx.RequestServices.GetRequiredService<IConfiguration>()`.
4. **Security-заголовки закрыты только частично и только на nginx.** `X-Frame-Options`/`CSP frame-ancestors`
   добавлены для основного приложения (и намеренно сняты для `/embed/`), но **HSTS, `X-Content-Type-Options`,
   `Referrer-Policy` и полноценный CSP по-прежнему отсутствуют**. В **Windows/IIS-контуре**
   (`DEPLOY-windows.md`, `frontend/public/web.config`) анти-iframe-заголовков нет вообще — если этот
   контур используется в проде, виджет-защита там не работает.
5. **Юридический риск фотофиксации принят, но не снят.** Фото клиента — персональные данные, фото лица
   при ряде условий трактуется как биометрия. Согласия в интерфейсе нет (решение Q6), факт согласия не
   хранится, ответственность переложена на компанию-салон текстом в README/`docs/faq.md`.
   SPEC явно помечает пункт как «стоит вернуть, если продукт пойдёт в продакшен».

**P2 — код без тестов, на который многое завязано / хрупкие места**

6. **Фронтенд покрыт точечно.** 35 тестов Vitest на ~7 000 строк TSX + ~1 150 строк TS. Покрыты мапперы ошибок,
   `formatPhone` и `PhotoGallery`. **Не покрыты**: `BookingModal`, `ManualBookingModal`,
   `RescheduleModal`, календарь `ScheduleTab`, вкладочные страницы кабинета и админки,
   `useAuthedImage` (блобы, `IntersectionObserver`, `revokeObjectURL`).
7. **Docker-образ ни разу не собирался и не запускался вручную в этой среде** (Docker локально нет) —
   единственная проверка сборки образа сейчас это новый CI-джоб `docker-build`.
   **Реальная загрузка фото внутри контейнера не проверялась** ни разу: нет smoke-теста
   «в запущенном контейнере upload возвращает 201». Это существенно именно из-за SkiaSharp — нативная
   библиотека, которая либо загрузится на glibc-базе, либо нет; юнит- и функциональные тесты идут
   на хосте разработчика/раннера, а не в образе.
8. **`CompaniesController` — 571 строка и 16 эндпоинтов**, включая логику подписок, загрузку файлов,
   квоту фото и целиком сборку статистики (`GetStats`, ~70 строк агрегаций **в памяти** после
   `ToListAsync()`). Контроллер стал больше, а не меньше.
9. **Логика прав по-прежнему размазана по приватным копиям** `CanManageCompany`/`CanManage`, хотя их
   «членская» половина унифицирована через `CompanyMembership`. Коммент в
   `BookingsController.CanManageBookingAsync` фиксирует, что расхождение уже случалось однажды.
   `MailingController` до сих пор не переведён на общий хелпер.
10. **Двухуровневая модель ролей (Identity roles ↔ `CompanyMember.Role`) не синхронизируется в обе стороны.**
    `AddMember` добавляет Identity-роль, `RemoveMember` — **не убирает** её. Пользователь, удалённый из
    единственной компании, остаётся с ролью `Master`/`CompanyOwner` в Identity и продолжает проходить
    `[Authorize(Roles = ...)]`-гейты. Явно оставлено вне цикла B (SPEC §8.2).
11. **Перечитывание ролей и сверка `SecurityStamp` на каждом запросе** (`Program.cs`, `OnTokenValidated`) —
    правильное решение с точки зрения безопасности, но это **дополнительный запрос к БД на каждый
    аутентифицированный вызов** без кеша. Проверка стемпа расход не увеличила (пользователь уже загружен),
    но сам запрос остался.
12. **Шаг сетки слотов по-прежнему захардкожен 30 минутами** (`SlotCalculator.StepMinutes`).
    Логика стала чистой и покрыта юнит-тестами, но настраиваемости нет: услуга длительностью 45 минут
    предлагается в 30-минутной сетке.
13. **Пагинации нет нигде.** `admin/users` (с N+1 по ролям), `admin/companies`, публичные отзывы
    компании, база клиентов мастера. Заметки ограничены 50 на клиента, `admin/bookings` — `Take(500)`,
    фото — 5 на заметку, но это точечные заглушки, а не общее решение. Явно отложено (SPEC §8.2).
14. **Миграция `NormalizePhoneNumbers` необратима и не проверялась на реальных данных.** Она удаляет
    аккаунты-дубли и переназначает их заметки/отзывы/журналы. Сейчас это безопасно (боевых данных нет),
    но повторно применить её к живой базе будет нельзя.

**P3 — эксплуатация, гигиена, недоделки**

15. **Деплой — bash + ssh + `git pull` на проде.** Нет отката, нет проверки версии, нет health-check
    после рестарта; фронт собирается **на боевом сервере** (`npm ci && npm run build`), т.е. сбой сборки
    оставит систему в промежуточном состоянии. CI ничего не деплоит.
16. **Фичи, выглядящие готовыми в UI, но не работающие по сути:** «Рассылка» (писем нет — и текст
    «Рассылка поставлена в очередь» **осознанно оставлен вводящим в заблуждение**, решение Q9),
    предоплата (платежей нет), `NotifyDaysBefore` в редакторе тарифов (уведомлений нет).
    Обещание «напоминание накануне визита» на главной (`HomePage.tsx`) тоже оставлено намеренно (Q8).
    README и `docs/faq.md` про это пишут честно — интерфейс нет.
17. **Устаревшие зависимости фронта** (axios, form-data, react-router с известными уязвимостями) —
    обновление явно отложено (SPEC §8.2): `react-router` требует мажорного апгрейда.
18. **Нет линтера и форматтера.** Ни `.editorconfig`, ни ESLint/Prettier, ни шага в CI. Стиль держится
    на дисциплине и code-review.
19. **`frontend/design_handoff_site_redesign/`** (10 HTML-файлов макетов) лежит внутри `frontend/`, попадая
    в область `tsconfig`-исключений только за счёт `include: ["src"]`; в сборку не идёт, но и к коду не относится.
20. **Локальные загруженные файлы не воспроизводимы на чистом клоне.** `wwwroot/uploads/**` и
    `App_Data/private-uploads/**` — в `.gitignore`; на машине разработчика в приватном каталоге лежат
    ~36 папок компаний с реальными JPEG. На свежем клоне ссылки из дампа БД будут битыми.
21. **Валидация DTO неполна** (`Slug`, `Bio`, `Comment`, `SendMailDto.Message`), нет запрета удалять
    последнего владельца, нет проверки статуса в `MarkPaid` — явно отложено как некритичное (SPEC §8.2).
22. **Пустой каталог `frontend/src/components/auth/`** и два `.example`-файла прод-конфига, описывающих
    один и тот же прод двумя способами.

**Закрыто циклами A и B** (не переоткрывать без причины): права мастера на CRUD услуг;
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
Всё перечисленное лежит в репозитории и обновлялось в цикле B.

### 10.1 Краткая продуктовая документация

| Что | Путь | Формат | Структура |
|---|---|---|---|
| Обзор продукта | `README.md` | Markdown, русский | «О проекте» → «Для кого» / «Роли» → **«Что умеет»** (маркированный список функций) → **«Чего пока нет»** (честный список отсутствующего: платежи, письма, уведомления, самостоятельная оплата тарифа, клиентский просмотр фото, согласие на съёмку) → ссылка на `docs/README.md` |
| Changelog | `CHANGELOG.md` | Markdown, русский, по мотивам Keep a Changelog | **По датам завершения цикла, самая свежая запись сверху**; номеров версий в проекте нет. Верхняя запись — `## 2026-09-07 — фото к работе мастера и доводка начатого`, внутри подразделы «Появилось новое», «Изменилось», и т.п. |

GitHub Releases / wiki в проекте не используются.

### 10.2 Развёрнутая пользовательская документация

Каталог **`docs/`**, Markdown, русский, **разбита по ролям**, точка входа — `docs/README.md`
с таблицей «кто вы → с чего начать».

| Файл | О чём |
|---|---|
| `docs/README.md` | оглавление, роли в двух словах, ссылка на CHANGELOG |
| `docs/client.md` | запись на услугу — для клиента |
| `docs/master.md` | кабинет мастера (включая заметки и фото) |
| `docs/owner.md` | кабинет владельца компании |
| `docs/admin.md` | администрирование платформы |
| `docs/accounts.md` | общая: аккаунт, телефон как логин, смена пароля/номера |
| `docs/schedule.md` | общая: расписание, перерывы, расчёт свободного времени |
| `docs/faq.md` | частые вопросы **и честный список ограничений** |

Отдельного сайта документации и справочного раздела внутри приложения **нет**.

### 10.3 Документация API для внешних потребителей

| Что | Путь | Формат | Структура |
|---|---|---|---|
| Справочник эндпоинтов | `API_DOCUMENTATION.md` (~208 КБ) | Markdown, русский | §1 Обзор → §2 Аутентификация → §3 Ключевые бизнес-концепции → **§4 Справочник эндпоинтов** (основной объём) → §5 Сквозные сценарии (curl-рецепты) → §6 … → **§7 Известные ограничения** |
| Контракт текущего цикла | `API_CONTRACT.md` (~61 КБ) | Markdown, русский | документ **цикла B**: контракт новых/изменённых эндпоинтов, коды ошибок, тела ответов |

**OpenAPI/Swagger-файла в репозитории нет** — схема генерируется Swashbuckle во время работы и
доступна только в Development (`/swagger`). Postman-коллекции нет. Генерации TS-типов из схемы нет.

### 10.4 Описания тест-кейсов

**`TEST_CATALOG.md`** (~209 КБ), Markdown, русский — человекочитаемое описание **каждого**
автоматизированного кейса, отдельно от самого кода тестов.

- Структура: «Как устроены ссылки на тесты» → «Префиксы по доменам» → **«Юнит-тесты (без БД)»** →
  далее раздел на домен (`Auth`, `Bookings`, `Companies`, `Services`, `WorkingHours`,
  `ScheduleTemplate`, `Reviews`, `Mailing`, `Masters`, **`ClientNotePhotos (US-17…US-20, цикл B)`**,
  **`Scheduler (US-21, цикл B)`**, `Admin`, `Profile`, `Reports`).
- Связь с кодом — через стабильный ID из атрибута `[TestCase("PREFIX-NNN")]`.
  Поиск кейса по ID: `grep -rn "BK-003" ServiceBooking.Tests/`.
- Каталог поддерживается в актуальном состоянии **вместе с кодом**: последняя правка — 2026-09-07.

Отдельного `TESTPLAN.md`, каталога `docs/testing/` или ручных сценариев вне `TEST_CATALOG.md`
в проекте **не найдено**.

### 10.5 Документы цикла работ

`SPEC.md` (~147 КБ), `ARCHITECTURE.md` (~138 КБ), `API_CONTRACT.md` — **документы текущего (B) цикла**,
не постоянные справочники. `SPEC.md` содержит §0 «Решения заказчика» (Q5–Q18), §4–§7 истории с
критериями приёмки, §8 границу цикла (в т.ч. §8.2 «что откладывается и почему») и приложение А
(целиком сохранённое исследование по MAX).

**Соглашения об архиве (`docs/history/`) в репозитории нет**, каталога такого нет.
Предыдущие редакции живут только в git-истории: SPEC цикла A — `git show e6b746c:SPEC.md`,
ещё более ранняя редакция — `7c86ca2`.
