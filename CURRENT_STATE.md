# CURRENT_STATE — фактическое состояние кодовой базы ServiceBooking

Документ описывает **что есть в репозитории сейчас**, без предложений по развитию.
Все утверждения получены чтением исходников, конфигов и git-истории на коммите `263c661`
(ветка `master`, рабочее дерево чистое). Где чего-то не нашлось — так и написано.

Проверено фактическим запуском (см. §7):
- `dotnet build ServiceBooking.sln` — успешно, 2 предупреждения;
- `npx tsc --noEmit` в `frontend/` — чисто;
- `dotnet test ServiceBooking.Tests` — **230/230 passed, ~1 мин**.

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
| Документация API | Swashbuckle.AspNetCore 6.5.0, Swagger включён **всегда** (без проверки Environment) | `Program.cs` |
| Менеджер пакетов | NuGet, версии зафиксированы в `.csproj` (без `Directory.Packages.props`, без lock-файлов) | — |

### Фронтенд

| Что | Значение | Откуда |
|---|---|---|
| Сборщик | Vite 5.4.x, dev-порт 5173, прокси `/api` и `/uploads` → `http://localhost:5000` | `frontend/vite.config.ts` |
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
- **Хранилище файлов — локальный диск**, `wwwroot/uploads/companies`, раздаётся `app.UseStaticFiles()`.
  Облачного стораджа нет.

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

**Расхождение конфигов:** `ServiceBooking.API/Properties/launchSettings.json` — это нетронутый шаблон
из `dotnet new webapi`: порты `5291/7016` и `launchUrl: "weatherforecast"` (эндпоинта нет).
Vite же проксирует на `5000`. То есть `dotnet run` из Rider по дефолтному профилю поднимет API на
порту, которого фронт не ждёт; докер и прод используют 5000/8080.

---

## 2. Структура репозитория

```
ServiceBooking.sln                  5 проектов
├── ServiceBooking.API/             ← точка входа, вся бизнес-логика веб-слоя
│   ├── Program.cs                  DI, Identity, JWT (+ перечитывание ролей из БД), CORS, Swagger, миграции, сид
│   ├── Controllers/                12 контроллеров (13 классов — в Reviews их два)
│   ├── DTOs/                       Auth / Bookings / Companies / Services / WorkingHours
│   ├── Services/                   SlotService, SubscriptionResolver, CaptchaService, TokenService, AdvisoryLock
│   ├── Dockerfile                  multi-stage, aspnet:8.0, EXPOSE 8080
│   └── appsettings*.json           appsettings.json в git; Development/Production — в .gitignore
├── ServiceBooking.Core/            только сущности и перечисления, зависимость одна — Identity.EFCore
│   ├── Entities/                   15 классов
│   └── Enums/                      BookingStatus, PaymentStatus, UserRole
├── ServiceBooking.Infrastructure/  AppDbContext + 12 миграций EF Core
├── ServiceBooking.Tests/           xUnit, функциональные тесты через WebApplicationFactory
│   ├── Infrastructure/             ApiTestBase, CustomWebApplicationFactory, TestDatabaseFixture, JsonHelpers, TestCaseAttribute
│   └── Tests/                      12 файлов по доменам
├── ServiceBooking/                 ⚠ МЁРТВЫЙ проект (см. ниже)
├── frontend/                       React SPA
│   ├── src/api/                    14 модулей — тонкая обёртка над axios, по одному на домен
│   ├── src/pages/                  страницы; вложенные owner/ и admin/ — вкладки
│   ├── src/components/             booking/, layout/, review/, schedule/, ui/
│   ├── src/store/authStore.ts      единственный zustand-стор
│   ├── src/types/index.ts          общие TS-типы (ручная копия серверных DTO)
│   ├── src/utils/                  3 маппера ошибок HTTP → русский текст
│   └── design_handoff_site_redesign/  HTML-макеты редизайна (*.dc.html) + README, не участвуют в сборке
├── deploy/                         deploy.sh (локально), deploy-remote.sh (на VPS), nginx/ezbook.conf
├── docker-compose.yml              dev: postgres + api
├── docker-compose.prod.yml         prod: postgres (без публикации порта) + api на 127.0.0.1:5000, volume для uploads
├── API_DOCUMENTATION.md            ~130 КБ, подробный справочник эндпоинтов (рус.)
├── TEST_CATALOG.md                 ~128 КБ, человекочитаемый каталог всех тест-кейсов (рус.)
├── DEPLOY.md / DEPLOY-windows.md   runbook'и: reg.ru VPS (Linux+nginx+docker) и VK Cloud Windows (IIS+ARR)
└── .env.production.example, .deploy.env.example, appsettings.Production.json.example
```

### Точка входа и слои

- Единственная точка входа приложения — `ServiceBooking.API/Program.cs`.
- **Бизнес-логика живёт в контроллерах.** Сервисного слоя как такового нет: `Services/` в API содержит
  4 узкоспециализированных помощника (слоты, тарифы, капча, токены) + статический `AdvisoryLock`.
  Правила «кто что может» реализованы приватными методами внутри каждого контроллера
  (`CanManageCompany`, `CanManage`, `CanManageBookingAsync`) и **дублируются между контроллерами**.
- `Core` — анемичные POCO-сущности без поведения. `Infrastructure` — только `AppDbContext` и миграции.
  Репозиториев нет, контроллеры работают с `AppDbContext` напрямую.

### Мёртвый проект `ServiceBooking/`

Blazor Server-шаблон из самого первого коммита (`1535e37`), с тех пор не менялся.
Содержит дефолтные `Components/Pages/Home.razor`, `Error.razor` и один осиротевший класс
`Services/TimeSlot.cs`. `Program.cs` регистрирует Razor Components и не имеет ни БД, ни API.
Проект **входит в solution и собирается** (даёт одно из двух предупреждений сборки), но нигде не
используется — ни в docker-compose, ни в деплой-скриптах. Свой `Dockerfile` есть, но никем не вызывается.

---

## 3. Модель данных

Источник: `ServiceBooking.Core/Entities/*`, конфигурация связей — `ServiceBooking.Infrastructure/Data/AppDbContext.cs`.
Плюс стандартные таблицы ASP.NET Identity (`AspNetUsers` и т.д.) через `IdentityDbContext<AppUser>`.

### Сущности

| Сущность | Ключ | Ключевые поля | Связи |
|---|---|---|---|
| `AppUser : IdentityUser` | string | `FirstName`, `LastName`, `AvatarUrl`, `CommissionPercent`, `CreatedAt` | 1—N: CompanyMemberships, ClientBookings, MasterBookings, MasterServices, WorkingHours |
| `Company` | Guid | `Name`, `Slug` (**уникальный индекс**), `Description`, `LogoUrl`, `Address`, `Phone`, `Email`, `AllowSelfBooking`, `RequirePrepayment`, `ShowInPublicListing`, `IsActive`, `OwnerUserId` | N—1 Owner (`Restrict`), 1—N Members / Services / Bookings |
| `CompanyMember` | Guid | `CompanyId`, `UserId`, `Role: UserRole`, `Bio`, `JoinedAt` | «многие-ко-многим» User↔Company с ролью |
| `Service` | Guid | `CompanyId`, `Name`, `DurationMinutes`, `Price decimal(10,2)`, `ImageUrl`, `IsActive` | 1—N MasterServices, Bookings |
| `MasterService` | Guid | `MasterId`, `ServiceId` | связка «мастер умеет услугу» |
| `WorkingHours` | Guid | `MasterId`, `CompanyId`, **`Date: DateOnly`**, `StartTime`, `EndTime`, `IsWorking` | 1—N `ScheduleBreak` |
| `ScheduleBreak` | Guid | `WorkingHoursId`, `StartTime`, `EndTime` | перерывы внутри дня |
| `WeeklyScheduleTemplate` | Guid | `MasterId`, `CompanyId`, `DayOfWeek` (ISO 1..7), `IsWorking`, `StartTime`, `EndTime`; индекс `(MasterId, CompanyId)` | шаблон, «раскатываемый» в `WorkingHours` |
| `Booking` | Guid | `CompanyId`, `ServiceId`, `MasterId`, `ClientId?`, `GuestName/Phone/Email`, `Date`, `StartTime`, `EndTime`, **`Price` (снимок цены)**, `Status`, `PaymentStatus`, `Notes`, `CancellationReason` | Master `Restrict`, Client `SetNull` |
| `Review` | Guid | `BookingId` (**уникальный индекс** — 1 отзыв на запись), `CompanyId`, `MasterId`, `ClientId?`, `ReviewerName`, `Rating 1..5`, `Comment` | Booking `Cascade` |
| `ClientNote` | Guid | `CompanyId`, `MasterId` (автор), `ClientId?` / `GuestPhone?`, `Note`; индексы `(CompanyId, ClientId)` и `(CompanyId, GuestPhone)` | заметки общие для компании, удалять может только автор |
| `AccountSubscription` | Guid | `OwnerUserId` (**уникальный индекс**), `PlanConfigId?`, `PaidUntil?`, `IsActive` | подписка на **аккаунт владельца**, а не на компанию |
| `SubscriptionPlanConfig` | Guid | `Name`, `PricePerMonth`, `MaxEmployees?`, `MaxCompanies?`, `AllowOnlineBooking`, `AllowMailing`, `AllowAnalytics`, `AllowPublicListing`, `AllowOnlinePayment`, `IsActive`, `NotifyDaysBefore` | справочник тарифов |
| `SubscriptionChangeLog` | Guid | `OwnerUserId` (индекс), `ChangedByUserId`, старые/новые план, `PaidUntil`, `IsActive`, `Comment` | аудит изменений подписки |
| `MailLog` | Guid | `CompanyId`, `Subject`, `Message`, `SentById`, `RecipientCount`, `SentAt` | журнал «рассылок» |

Перечисления: `BookingStatus { Pending, Confirmed, Cancelled, Completed, NoShow }`,
`PaymentStatus { NotRequired, Pending, Paid }`, `UserRole { Client, Master, CompanyOwner, SuperAdmin }`.
Сериализуются как строки (`JsonStringEnumConverter` в `Program.cs`).

### Как это связано смыслово

- Аккаунт = телефон. `UserName == PhoneNumber`, уникальность даётся Identity-индексом. Email опционален.
- Тариф привязан к **владельцу** (`Company.OwnerUserId`), одна подписка покрывает все его компании-филиалы.
  Разрешение тарифа — `SubscriptionResolver`; при отсутствии/неактивности/просрочке падаем в
  `EffectivePlan.Free` = `{OnlineBooking: false, Mailing: false, Analytics: false, PublicListing: true,
  OnlinePayment: false, MaxEmployees: 1, MaxCompanies: 1}`.
- Расписание **датовое**, не по дням недели (миграция `DateBasedSchedule`). `WeeklyScheduleTemplate`
  — только заготовка, которую `POST /api/schedule-template/apply` разворачивает в строки `WorkingHours`.
- `Booking.Price` — снимок `Service.Price` на момент создания; отчёты читают его, а не текущую цену.

### Миграции (12, все в `ServiceBooking.Infrastructure/Migrations/`)

`InitialCreate` → `DateBasedSchedule` → `AddSubscriptionAndCommission` → `AddReviewsTemplatesNotes` →
`AddPromoGiftMailPlans` → `AddPrepaymentSupport` → `LinkSubscriptionsToPlanConfigs` → `RemovePlanKey` →
`AccountLevelSubscriptions` → `AddBookingPriceSnapshot` → `RemovePromoCodesAndGiftCertificates` →
`AddPublicListingAndOnlinePayment` → `AddCompanyIdToClientNote`.

История видна прямо в названиях: промокоды и подарочные сертификаты были добавлены и затем **удалены
целиком** (`RemovePromoCodesAndGiftCertificates`), а подписка переехала с компании на аккаунт владельца
(`AccountLevelSubscriptions`). Остатков этих фич в коде нет.

---

## 4. Что реализовано

Ниже — по функциональным блокам. Все эндпоинты выписаны из атрибутов контроллеров, а не из документации.

### 4.1 Аутентификация и профиль — работает

`ServiceBooking.API/Controllers/AuthController.cs`, `ProfileController.cs`, `Services/TokenService.cs`

| Метод | Путь | Доступ |
|---|---|---|
| POST | `/api/auth/register` | анонимно; выдаёт роль `Client` |
| POST | `/api/auth/login` | анонимно; поиск по `UserName == phone`, lockout после 5 попыток на 15 мин |
| GET / PUT | `/api/profile` | авторизованные |
| POST | `/api/profile/change-password` | авторизованные |
| POST | `/api/profile/change-phone` | авторизованные; требует текущий пароль, через `SetUserNameAsync` |

JWT: HS256, срок **7 дней**, claims `sub/phone/given_name/family_name/jti/role`.
Важная деталь в `Program.cs`: на каждом запросе в `JwtBearerEvents.OnTokenValidated` роли
**перечитываются из БД** и подменяют claim'ы токена — отзыв роли действует немедленно.

`GET /api/profile` дополнительно отдаёт `ProfilePlanDto` для `CompanyOwner` — показывает реальную
строку подписки (в т.ч. просроченную), а не нормализованный Free.

Фронт: `LoginPage.tsx`, `RegisterPage.tsx`, `ProfilePage.tsx`, `store/authStore.ts` (persist в localStorage),
`api/client.ts` — интерцептор на 401 делает `logout()` + редирект на `/login`.

### 4.2 Компании — работает

`Controllers/CompaniesController.cs` (515 строк — самый большой контроллер)

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/companies` | публично; фильтр `ShowInPublicListing && plan.AllowPublicListing` |
| GET | `/api/companies/my` | владелец — свои компании |
| GET | `/api/companies/member` | все компании, где я участник любой роли |
| GET | `/api/companies/{slug}` | публично |
| GET | `/api/companies/{id}/masters?serviceId=` | публично |
| GET | `/api/companies/{id}/members` | владелец/SuperAdmin |
| POST | `/api/companies` | авторизованные; **лимит `MaxCompanies`** под advisory lock, 402 при превышении |
| PUT | `/api/companies/{id}` | владелец |
| POST | `/api/companies/{id}/logo` | владелец; ≤5 МБ, jpeg/png/webp, расширение из Content-Type, старый файл удаляется |
| POST | `/api/companies/{id}/members` | владелец; **лимит `MaxEmployees`** под advisory lock, 402 |
| PUT | `/api/companies/{id}/members/{memberId}/services` | владелец |
| PUT | `/api/companies/{id}/members/{memberId}/commission` | владелец; clamp 0..100 |
| DELETE | `/api/companies/{id}/members/{memberId}` | владелец |
| GET | `/api/companies/{id}/stats?from&to` | владелец; выручка, новые клиенты, топ услуг, по мастерам, по дням |

Автосоздание аккаунта мастера по телефону при `AddMember`: пароль выводится детерминированно —
`"Sb" + последние 6 цифр телефона`, добитый нулями до 8 символов (`CompaniesController.cs:~330`).

Фронт: `pages/owner/CompanyManagePage.tsx` (вкладки «Услуги / Расписание / Сотрудники / Настройки»),
`pages/CabinetPage.tsx` (вкладка «Мои компании» с созданием), `pages/CompanyPage.tsx` (публичная витрина).

### 4.3 Услуги — работает

`Controllers/ServicesController.cs`: `GET /api/services?companyId=` (публично),
`POST`, `PUT /{id}`, `DELETE /{id}` (удаление — мягкое, `IsActive = false`).

### 4.4 Расписание мастеров — работает

`Controllers/WorkingHoursController.cs`: `GET /api/workinghours?masterId&companyId&from&to`,
`PUT /api/workinghours` (upsert дня + полная перезапись перерывов), `DELETE /api/workinghours/{id}`.

`Controllers/ScheduleTemplateController.cs`: `GET`/`PUT /api/schedule-template`,
`POST /api/schedule-template/apply?masterId&companyId&from&to` — раскатка недельного шаблона по датам
(ISO: Пн=1 … Вс=7), существующие дни перезаписываются.

Фронт: `pages/owner/ScheduleTab.tsx` (календарь по месяцам, перерывы, сортировка мастеров с владельцем
первым), `components/schedule/WeeklyTemplateModal.tsx`.

### 4.5 Слоты и бронирование — работает, это ядро продукта

`Controllers/BookingsController.cs` + `Services/SlotService.cs`

| Метод | Путь | Доступ |
|---|---|---|
| GET | `/api/bookings/occupied?masterId&date` | публично |
| GET | `/api/bookings/slots?masterId&serviceId&date&manual` | публично; `manual=true` учитывается **только** для аутентифицированных |
| POST | `/api/bookings` | публично (гость) и авторизованно |
| GET | `/api/bookings/{id}` | владелец записи или персонал |
| GET | `/api/bookings/my` | мои записи как клиента |
| GET | `/api/bookings/client?status=` | то же с фильтром статуса |
| GET | `/api/bookings/master?date&to` | `Master,CompanyOwner` |
| PATCH | `/api/bookings/{id}/complete` \| `/mark-paid` \| `/noshow` | `Master,CompanyOwner,SuperAdmin` |
| PATCH | `/api/bookings/{id}/reschedule` | персонал (advisory lock + проверка конфликта) |
| PATCH | `/api/bookings/{id}/cancel` | клиент записи или персонал |

Логика слотов (`SlotService`): шаг сетки **жёстко 30 минут**, слот занят, если пересекается с бронью
(статус ≠ Cancelled) или перерывом. Без строки `WorkingHours` на дату — пустой список, кроме
`allowWithoutSchedule` (тот самый `manual`-режим из коммита `0c8755d`), где берётся весь день
00:00–24:00 с защитой от переполнения `TimeOnly`.

Гейты в `POST /api/bookings`:
1. аноним → компания должна существовать, `AllowSelfBooking`, капча (если `IsEnforced`), имя+телефон;
2. тариф: `!AllowOnlineBooking && !isStaffManualBooking` → **402**;
3. предоплата: `PaymentStatus = Pending` только если `plan.AllowOnlinePayment && company.RequirePrepayment` и это не ручная запись;
4. проверка конфликта слота внутри транзакции с `pg_advisory_xact_lock` (`Services/AdvisoryLock.cs`) → 409.

Фронт: `components/booking/BookingModal.tsx` (гость/клиент, выбор услуги → мастера → даты → слота, капча),
`ManualBookingModal.tsx` (персонал записывает клиента), `RescheduleModal.tsx`,
`pages/MyBookingsPage.tsx` (персонал), `pages/ClientBookingsPage.tsx` (клиент).

### 4.6 Тарифы и подписки — работает административно

`Services/SubscriptionResolver.cs` (батчевое разрешение планов без N+1), `AdminController` (CRUD планов и
назначение подписок), `DTOs/Companies/CompanyDto.cs` (флаги для UI).

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
Фронт: `components/review/ReviewModal.tsx`, отображение на `CompanyPage.tsx`.

### 4.8 База клиентов мастера — работает

`Controllers/MastersController.cs`: `GET /api/masters/clients?companyId`,
`POST /api/masters/clients/notes`, `DELETE /api/masters/clients/notes/{id}`.
Группировка отдельно по зарегистрированным клиентам и по `GuestPhone`. Контакты (телефон/email)
скрываются, если последний визит завершился более **24 часов** назад. Заметки общие для всей компании,
удаляет только автор. Фронт: `pages/MasterClientsPage.tsx`, вкладка «Клиенты» в `CabinetPage`.

### 4.9 Отчёты и статистика — работает

- `GET /api/reports/masters?companyId&from&to` (`ReportsController`) — выручка, комиссия мастера,
  доля компании; гейт `plan.AllowAnalytics` → 402. Фронт: `ReportsTab` внутри `CabinetPage.tsx`.
- `GET /api/companies/{id}/stats` (`CompaniesController`) — сводка по компании. Фронт: `pages/owner/DashboardTab.tsx`.
- `GET /api/admin/stats` — платформенная сводка.

### 4.10 Админка — работает

`Controllers/AdminController.cs`, все методы `[Authorize(Roles = "SuperAdmin")]`:
`GET /api/admin/stats`, `GET /api/admin/users?search`, `PUT /api/admin/users/{id}/roles`,
`GET /api/admin/companies?search`, `PUT /api/admin/companies/{id}`, `PUT /api/admin/companies/{id}/owner`,
`PUT /api/admin/owners/{ownerUserId}/subscription`, `GET /api/admin/owners/{ownerUserId}/subscription-history`,
`GET /api/admin/bookings` (лимит `Take(500)`), `GET|POST|PUT|DELETE /api/admin/plans[/{id}]` (delete — мягкий).
Фронт: `pages/AdminPage.tsx` (вкладки stats/companies/users/bookings) + `pages/admin/PlansTab.tsx`.

### 4.11 Виджет-встраивание — реализовано частично

Маршрут `/embed/:slug` (`frontend/src/App.tsx:34`) рендерит `pages/EmbedPage.tsx` без навбара — список
услуг компании + `BookingModal`. Страница работает, но **нигде в приложении на неё нет ссылки и нигде
не показывается код для вставки iframe** (grep по `embed` в `frontend/src` даёт только сам роут).

---

## 5. Что реализовано частично, заглушки и несогласованности

Явных маркеров `TODO`/`FIXME`/`HACK` в коде **нет ни одного** (проверено grep'ом по `.cs`, `.ts`, `.tsx`).
Всё ниже выявлено чтением кода.

### 5.1 Настоящие заглушки

1. **Рассылка не отправляет писем.** `Controllers/MailingController.cs` — `POST /api/companies/{id}/mail`
   собирает список email'ов клиентов, пишет `MailLog` и возвращает
   `{ recipientCount, message = "Рассылка поставлена в очередь" }`. **Очереди нет, отправки нет.**
   Отдельно: аккаунты идентифицируются телефоном, email опционален, поэтому список получателей
   на практике может быть почти пустым. Фронт (`pages/owner/MailingTab.tsx`) показывает это как
   рабочую функцию.

2. **Предоплата не проводится.** `RequirePrepayment` / `PaymentStatus.Pending` — организационный флаг.
   Платёж подтверждается вручную (`PATCH /api/bookings/{id}/mark-paid`). Интеграции нет.

3. **Уведомлений нет вообще.** Ни `IHostedService`, ни `BackgroundService`, ни Hangfire/Quartz.
   `SubscriptionPlanConfig.NotifyDaysBefore` сохраняется, редактируется в
   `pages/admin/PlansTab.tsx` и подписан «Уведомление за N дн. до деактивации» — но **никем не читается**
   в бизнес-логике (единственное использование в бэкенде — присваивание в `AdminController.UpdatePlan`).

4. **Аватары пользователей.** `AppUser.AvatarUrl` возвращается в 5 DTO, но **ни один эндпоинт его не
   устанавливает** — загрузки аватара нет (есть только загрузка логотипа компании). Поле всегда `null`.

5. **`Service.ImageUrl`** принимается как строка в `CreateServiceDto`, но загрузки картинки услуги нет;
   в UI поле не заполняется (`CompanyManagePage.tsx` шлёт только name/description/duration/price).

6. **Самостоятельной покупки тарифа нет.** Подписку может выставить только SuperAdmin через
   `PUT /api/admin/owners/{ownerUserId}/subscription`. Экрана «оплатить тариф» на фронте нет —
   `ProfilePage.tsx` только показывает текущий план.

### 5.2 Мёртвый код

| Файл | Состояние |
|---|---|
| `ServiceBooking/` (весь проект) | Blazor-шаблон из первого коммита, никем не используется, но собирается |
| `ServiceBooking/Services/TimeSlot.cs` | сущность-дубль, не связана с `SlotService.TimeSlotResult` |
| `frontend/src/pages/DashboardPage.tsx` (192 стр.) | **не импортируется нигде** — заменён на `CabinetPage`; маршрут `/dashboard` редиректит на `/cabinet` |
| `frontend/src/pages/owner/OwnerPage.tsx` (187 стр.) | **не импортируется нигде** — маршрут `/owner` редиректит на `/cabinet` |
| `frontend/src/pages/MyBookingsPage.tsx` — тип `Booking.price` | см. ниже |
| `ServiceBooking.API/appsettings.Production.json.example` + `.env.production.example` | оба описывают один и тот же прод — два разных способа конфигурации (файл vs env), актуален второй (`docker-compose.prod.yml`) |

Комментарий в `tailwind.config.js`: legacy-шкала `primary`/`accent` намеренно оставлена перекрашенной
в новую палитру, чтобы не мигрировать вручную «ещё не перестилизованные» компоненты — то есть часть
разметки формально ещё на старых токенах.

### 5.3 Несогласованности бэкенда и фронтенда

1. **`BookingDto` не содержит `price` и `companySlug`, а фронтенд их ждёт.**
   `frontend/src/types/index.ts` объявляет `price?: number` и `companySlug?: string`, а
   `ServiceBooking.API/DTOs/Bookings/BookingDto.cs` их не отдаёт. Следствие — две ветки UI,
   которые **никогда не отрисовываются**:
   - `frontend/src/pages/ClientBookingsPage.tsx:126` — блок с ценой записи (`b.price != null`);
   - `frontend/src/pages/ClientBookingsPage.tsx:155` — кнопка перехода на страницу компании (`b.companySlug &&`).
   Поля опциональные, поэтому `tsc` молчит.

2. **`BookingStatus.Pending` фактически недостижим.** Это дефолт сущности, но
   `BookingsController.Create` всегда ставит `Confirmed`. Ни один эндпоинт не выставляет `Pending`.
   При этом статус присутствует в enum, в TS-типах и в фильтрах UI.

3. **`GET /api/companies/{id}/masters` не фильтрует по роли.** Возвращаются **все** `CompanyMembers`
   компании, включая владельца и участников с ролью `Client`, — в UI они попадают в список мастеров
   для записи (`CompaniesController.cs:75-99`).

4. **`ServicesController.CanManageCompany` допускает роль `Master`** — мастер может создавать,
   редактировать и удалять услуги компании. Во всех остальных контроллерах аналогичная проверка
   ограничена `CompanyOwner` (+SuperAdmin). Скорее всего непреднамеренно.

5. **`SubscriptionResolver` не проверяет `SubscriptionPlanConfig.IsActive`.** `DELETE /api/admin/plans/{id}`
   делает мягкое удаление (`IsActive = false`), но владельцы, уже сидящие на этом плане, продолжают
   получать все его возможности — резолвер смотрит только на `AccountSubscription.IsActive` и `PaidUntil`
   (`Services/SubscriptionResolver.cs:75-90`).

6. **`GET /api/workinghours` не проверяет принадлежность.** Требуется только `[Authorize]`; любой
   залогиненный пользователь может прочитать расписание произвольного мастера/компании. `PUT` и `DELETE`
   в том же контроллере проверку `CanManage` имеют. Это же зафиксировано в `API_DOCUMENTATION.md`, §7 п.3.

7. **Аутентифицированный `POST /api/bookings` с несуществующим `CompanyId` даёт 500.** Проверка
   `company is null → NotFound` находится внутри ветки `if (!isAuthenticated)`
   (`BookingsController.cs:57-60`), поэтому для залогиненного пользователя невалидный `CompanyId`
   доходит до `SaveChangesAsync` и падает нарушением внешнего ключа.

8. **Двойной `using ServiceBooking.Core.Entities;`** в `BookingsController.cs` (строки 6 и 10) —
   одно из двух предупреждений сборки (CS0105).

9. **Пароль автосозданного мастера никуда не отправляется.** Генерируется детерминированно из телефона
   и остаётся только в БД в виде хэша; передача пароля мастеру — ручная договорённость владельца.

10. **`launchSettings.json` API** — неотредактированный шаблон (`launchUrl: "weatherforecast"`, порт 5291),
    расходится с портом 5000, на который настроен Vite-прокси и docker-compose.

### 5.4 Документация, которая может быть устаревшей

- `API_DOCUMENTATION.md` (27 июля) в целом соответствует коду, но в разделе про роли ещё пишет, что
  SuperAdmin создаётся из `SuperAdmin:Email` / `SuperAdmin:Password`, тогда как `Program.cs` теперь
  ищет пользователя по `SuperAdmin:Phone`. Упоминается также перечисление `SubscriptionPlan`, которого
  в коде больше нет (удалено миграцией `RemovePlanKey`).
- Раздел §7 «Известные ограничения» этого файла — самый полезный источник по нерешённым вопросам,
  и он согласуется с тем, что видно в коде сегодня.

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
- **Обработка ошибок — без исключений и без middleware.** Глобального `UseExceptionHandler`/
  `ProblemDetails` нет. Контроллеры возвращают результат напрямую:
  `NotFound()`, `Forbid()`, `BadRequest(...)`, `Conflict(...)`, `StatusCode(402, "...")`,
  `NoContent()`. **402 Payment Required — проектная конвенция для «упёрлись в тариф»**
  (лимит компаний, лимит сотрудников, online booking, mailing, analytics).
- **Авторизация — двухуровневая:** атрибут `[Authorize]`/`[Authorize(Roles=...)]` + приватный
  асинхронный предикат внутри контроллера (`CanManageCompany` / `CanManage` / `CanManageBookingAsync`),
  который бьёт в `CompanyMembers`. Паттерн повторяется в 6 контроллерах.
- **Конкурентность:** любое «посчитал → записал» оборачивается в транзакцию +
  `AdvisoryLock.AcquireAsync(db, key)` (`pg_advisory_xact_lock`). Ключи: `booking-slot:{masterId}:{date}`,
  `owner-companies:{userId}`, `company-members:{companyId}`. См. `Services/AdvisoryLock.cs`.
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
- **Ошибки HTTP → текст пользователю** через `src/utils/*Error.ts` (`getBookingErrorMessage`,
  `getCreateCompanyErrorMessage`, `getAddMemberErrorMessage`) — `switch` по `status` с явной обработкой 402/409/403.
  Это устоявшийся паттерн: новый пользовательский сценарий с тарифным гейтом должен получить свой маппер.
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
  (отсюда расхождения из §5.3.1).

### Git

- Сообщения коммитов — на английском, одна строка-заголовок в повелительном наклонении +
  развёрнутое тело с объяснением «почему», трейлер `Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>`.
  Ветка одна — `master`, PR/feature-веток в истории нет.

---

## 7. Тесты

### Что есть

**Один набор — функциональные (интеграционные) тесты бэкенда**, проект `ServiceBooking.Tests`.

- **Фреймворк:** xUnit 2.5.3 + FluentAssertions 6.12.1 + `Microsoft.AspNetCore.Mvc.Testing` 8.0.11,
  `Microsoft.NET.Test.Sdk` 17.8.0, `coverlet.collector` 6.0.0 (покрытие настроено, но нигде не собирается).
- **Характер:** поднимается **реальный HTTP-конвейер** приложения через
  `WebApplicationFactory<Program>` (`Infrastructure/CustomWebApplicationFactory.cs`, окружение `Testing`)
  и **реальная PostgreSQL-база** `servicebooking_test`. Моков нет вообще.
- **Изоляция:** `Infrastructure/TestDatabaseFixture.cs` один раз дропает базу (`EnsureDeletedAsync`),
  затем старт приложения сам накатывает миграции и сидит роли/SuperAdmin. Все тесты — в одной коллекции
  `"Api"`, параллелизм отключён (`AssemblyInfo.cs`: `CollectionBehavior(DisableTestParallelization = true)`).
  Тесты друг за собой не убирают — коллизии исключаются генераторами `Unique*` в `ApiTestBase`.
- **Хелперы:** `Infrastructure/ApiTestBase.cs` (316 строк) — `RegisterAsync`, `LoginAsync`,
  `LoginAsSuperAdminAsync`, `CreateOwnerWithCompanyAsync(allowSelfBooking, requirePrepayment, onlineBooking, attachPlan)`,
  `AddMasterAsync`, сидирование услуг/расписания/подписки. `Infrastructure/JsonHelpers.cs` — обязателен
  для DTO с enum'ами (сервер отдаёт строки).
- **Маркировка:** каждый тест помечен `[Fact, TestCase("PREFIX-NNN")]`
  (`Infrastructure/TestCaseAttribute.cs`) — стабильный ID для перекрёстных ссылок из `TEST_CATALOG.md`.
  Поиск теста по ID: `grep -rn "BK-003" ServiceBooking.Tests/`.

### Состав набора

| Файл | Префикс | Тестов |
|---|---|---|
| `Tests/AuthTests.cs` | `AUTH-` | 10 |
| `Tests/BookingsFlowSmokeTests.cs` | `BK-` | 26 |
| `Tests/CompaniesTests.cs` | `CO-` | 65 |
| `Tests/ServicesTests.cs` | `SVC-` | 12 |
| `Tests/WorkingHoursTests.cs` | `WH-` | 10 |
| `Tests/ScheduleTemplateTests.cs` | `ST-` | 11 |
| `Tests/MastersTests.cs` | `MC-` | 11 |
| `Tests/ReviewsTests.cs` | `RV-` | 12 |
| `Tests/MailingTests.cs` | `MAIL-` | 9 |
| `Tests/ReportsTests.cs` | `RPT-` | 11 |
| `Tests/ProfileTests.cs` | `PROF-` | 15 |
| `Tests/AdminTests.cs` | `ADM-` | 31 |
| **Итого** | | **223 метода / 230 запусков** (4 `[Theory]`, 11 `[InlineData]`) |

Человекочитаемое описание каждого кейса — в `TEST_CATALOG.md` (~128 КБ, на русском), поддерживается
в актуальном состоянии вместе с кодом (последний коммит `263c661` обновил и тесты, и каталог).

### Как запускать (для QA — базовый прогон)

```bash
# Предусловие: локально доступен PostgreSQL на localhost:5432,
# пользователь postgres, ПУСТОЙ пароль (строка подключения захардкожена
# в ServiceBooking.Tests/Infrastructure/CustomWebApplicationFactory.cs и TestDatabaseFixture.cs).
# База servicebooking_test создаётся/пересоздаётся автоматически.

cd /Users/ikolomeets/RiderProjects/ServiceBooking
dotnet test ServiceBooking.Tests
```

Фактический прогон в этом окружении: **`Пройден! не пройдено 0, пройдено 230, всего 230, 1 m 2 s`**.
В выводе шумят предупреждения `RequestSizeLimitFilter ... does not support IHttpRequestBodySizeFeature`
(ожидаемо: `WebApplicationFactory` использует `TestServer`, у которого нет этой фичи; тест на лимит
размера логотипа это учитывает).

Запуск подмножества:
```bash
dotnet test ServiceBooking.Tests --filter "FullyQualifiedName~BookingsFlowSmokeTests"
```

### Чего в тестах НЕТ

- **Нет юнит-тестов.** Ни одного теста на `SlotService`, `SubscriptionResolver`, `CaptchaService`
  в изоляции — только через HTTP.
- **Нет ни одного фронтенд-теста.** В `frontend/package.json` **нет** ни `test`-скрипта, ни
  Vitest/Jest/Testing Library/Playwright/Cypress. Единственная проверка фронта — компиляция
  `tsc` внутри `npm run build`.
- **Нет e2e-тестов через браузер.** «Функциональные» здесь = API-уровень.
- Не покрыты капча-путь с реальным ключом (в Testing `SmartCaptcha:SecretKey` пуст → валидация
  пропускается) и загрузка логотипа на диск в проде.

---

## 8. CI и деплой

### CI — отсутствует

Каталогов `.github/`, `.gitlab-ci.yml`, `azure-pipelines.yml`, `Jenkinsfile` в репозитории **нет**
(проверено). Автоматического прогона тестов, линтера или сборки при push нет. Единственные YAML-файлы
в корне — два docker-compose. Линтера/форматтера как отдельного шага тоже нет: нет `.editorconfig`,
нет ESLint/Prettier-конфигов во `frontend/`.

### Деплой — настроен и задокументирован, ручной по кнопке

**Целевая среда 1 — Linux VPS (reg.ru), домен `ezbook.ru`.** Runbook: `DEPLOY.md`.
- `docker-compose.prod.yml`: `postgres` (порт наружу не публикуется) + `api` на `127.0.0.1:5000`,
  named volume `api_uploads` под `wwwroot/uploads`, все секреты — из `.env` (шаблон `.env.production.example`).
- `deploy/nginx/ezbook.conf`: раздаёт статику из `/var/www/ezbook/dist`, проксирует `/api/`, `/uploads/`
  и `/swagger/` на `127.0.0.1:5000`, `client_max_body_size 6M`. TLS — через `certbot --nginx`.
- `deploy/deploy.sh` (запускается локально/из Rider) → ssh → `git pull` → `deploy/deploy-remote.sh`,
  который делает `docker compose ... up -d --build`, `npm ci && npm run build`, копирует `dist` в
  `/var/www/ezbook/dist`, `restorecon` (SELinux), `nginx -t && systemctl reload nginx`.
  Хост берётся из `.deploy.env` (шаблон `.deploy.env.example`, в примере — `root@31.31.197.39`).

**Целевая среда 2 — Windows (VK Cloud) + IIS.** Runbook: `DEPLOY-windows.md`.
`frontend/public/web.config` попадает в `dist` при сборке и настраивает IIS URL Rewrite + ARR:
прокси `^api/` и `^uploads/` на `localhost:5000` и SPA-fallback на `index.html`.

**Секреты:** `.gitignore` исключает `**/appsettings.*.json` (кроме базового), `.env`, `.env.production`,
`.deploy.env`. В git закоммичен только `ServiceBooking.API/appsettings.json` с плейсхолдерными
значениями. **Но локально на машине разработчика лежат незакоммиченные
`appsettings.Development.json` и `appsettings.Production.json` с настоящими секретами**
(боевой пароль Postgres, JWT-ключ, серверный ключ SmartCaptcha) — их нельзя случайно `git add -f`.

---

## 9. Технический долг и риски (по убыванию приоритета)

**P1 — влияет на безопасность или корректность данных**

1. **Слабые дефолты в закоммиченном `appsettings.json`**: `Jwt:Key = "CHANGE_ME_TO_A_LONG_SECRET_KEY_AT_LEAST_32_CHARS"`,
   `SuperAdmin:Password = "Admin12345"`, `SuperAdmin:Phone = "+70000000000"`. Если прод-конфиг/переменные
   окружения не подставлены, приложение поднимется с предсказуемым ключом подписи JWT и известным
   паролем суперадмина. `docker-compose.prod.yml` переопределяет `Jwt__Key`, но **не** `SuperAdmin__Password`
   (в `.env.production.example` такой переменной нет; о том, что её забыли, есть отдельный коммит `adbe5b3`).
2. **`ServicesController` разрешает мастеру полный CRUD услуг компании** (`ServicesController.cs:82-92`) —
   расходится с моделью прав во всех остальных контроллерах.
3. **`GET /api/workinghours` без проверки принадлежности** — утечка расписания любого мастера любому
   авторизованному пользователю (`WorkingHoursController.cs:18-33`).
4. **Swagger включён безусловно, в том числе в Production** (`Program.cs`: `app.UseSwagger()` вне
   проверки окружения), и nginx-конфиг его проксирует. Полная схема API публично доступна.
5. **Нет глобальной обработки исключений.** Любое необработанное исключение → голый 500 без
   `ProblemDetails` и без корреляции; в Development — developer exception page. Конкретный воспроизводимый
   случай: авторизованный `POST /api/bookings` с несуществующим `CompanyId` (см. §5.3.7).
6. **Полное отсутствие работы с часовыми поясами.** `Booking.Date/StartTime/EndTime` — `DateOnly`/`TimeOnly`
   без TZ; `CreatedAt`/`PaidUntil` — `DateTime.UtcNow`. В `MastersController.GetClients` они смешиваются:
   `lastBooking.Date.ToDateTime(...).ToUniversalTime()` сравнивается с `DateTime.UtcNow.AddHours(-24)`,
   то есть локальное время сервера трактуется как таймзона бизнеса. Для мультирегионального SaaS
   (и для сервера в UTC при клиентах в UTC+7) это источник ошибок «на границе суток».
7. **Rate limiting отсутствует** на `/api/auth/login`, `/api/auth/register` и на гостевом
   `POST /api/bookings`. От перебора логина частично защищает Identity lockout (5 попыток / 15 мин),
   от спама записей — только SmartCaptcha, и только когда ключ задан.

**P2 — код без тестов, на который многое завязано / хрупкие места**

8. **Весь фронтенд не покрыт ничем.** ~4 500 строк TSX, включая три сложных модалки бронирования
   (`BookingModal` 343 стр., `ManualBookingModal` 435 стр., `RescheduleModal` 179 стр.) и календарь
   `ScheduleTab` (410 стр.). Единственная защита — `tsc --noEmit`. Последние 5 коммитов на 60%
   про фронтенд, и проверялись они, судя по всему, руками.
9. **`SlotService` — единственная точка расчёта доступности, покрыта только через HTTP.**
   Шаг сетки 30 минут захардкожен (`SlotService.cs:44`), настраиваемости нет; услуга длительностью
   45 минут будет предлагаться в 30-минутной сетке. Ветка `allowWithoutSchedule` появилась в
   последнем цикле работ (`0c8755d`) и покрыта тремя тестами BK-024..026.
10. **`CompaniesController` — 515 строк и 15 эндпоинтов**, включая логику подписок, загрузку файлов и
    целиком сборку статистики (`GetStats`, ~70 строк агрегаций **в памяти** после `ToListAsync()`).
    При росте числа записей это будет тянуть всю таблицу компании в память.
11. **Логика прав размазана по шести приватным копиям** `CanManageCompany`/`CanManage`. Коммент в
    `BookingsController.CanManageBookingAsync` прямо фиксирует, что расхождение уже случалось однажды
    («cancel used to be missing the CompanyOwner branch»). Риск повторения при добавлении нового контроллера.
12. **Двухуровневая модель ролей (Identity roles ↔ `CompanyMember.Role`) не синхронизируется в обе стороны.**
    `AddMember` добавляет Identity-роль, `RemoveMember` — **не убирает** её. Пользователь, удалённый из
    единственной компании, остаётся с ролью `Master`/`CompanyOwner` в Identity и продолжает проходить
    `[Authorize(Roles = ...)]`-гейты (конкретные проверки принадлежности его уже не пропустят, но
    поверхность шире, чем нужно).
13. **`SubscriptionResolver` игнорирует `PlanConfig.IsActive`** (§5.3.5) — «удалённый» тариф продолжает
    работать у существующих подписчиков.
14. **Перечитывание ролей из БД на каждом запросе** (`Program.cs`, `OnTokenValidated`) — правильное
    решение с точки зрения безопасности, но это **дополнительный запрос к БД на каждый
    аутентифицированный вызов** без кеша.

**P3 — эксплуатация, гигиена, недоделки**

15. **CI нет вообще.** 230 зелёных тестов существуют, но ничто не мешает смёржить красное.
16. **Деплой — bash + ssh + `git pull` на проде.** Нет отката, нет проверки версии, нет health-check
    после рестарта; фронт собирается **на боевом сервере** (`npm ci && npm run build`), т.е. сбой сборки
    оставит систему в промежуточном состоянии.
17. **Мёртвый Blazor-проект `ServiceBooking/`** в solution — собирается, путает при навигации,
    именуется как корневой проект.
18. **Две неотрисовываемые ветки UI** в `ClientBookingsPage.tsx` из-за расхождения DTO (§5.3.1) —
    признак того, что TS-типы фронта дрейфуют от серверных record'ов, и `strict`-режим этого не ловит,
    потому что поля опциональные.
19. **Фичи, выглядящие готовыми в UI, но не работающие по сути:** «Рассылка» (писем нет),
    предоплата (платежей нет), `NotifyDaysBefore` в редакторе тарифов (уведомлений нет).
    Пользователь-владелец компании об этом из интерфейса не узнает.
20. **`/embed/:slug` реализован, но недоступен** — нет ни ссылки, ни генератора кода вставки.
21. **`launchSettings.json` API не отредактирован** (`weatherforecast`, порт 5291 против ожидаемого 5000).
22. **`frontend/design_handoff_site_redesign/`** (10 HTML-файлов макетов) лежит внутри `frontend/`, попадая
    в область `tsconfig`-исключений только за счёт `include: ["src"]`; в сборку не идёт, но и к коду не относится.
23. **Локальные `wwwroot/uploads/companies/*.png`** — реальные загруженные логотипы на машине разработчика,
    в git не попадают (gitignore), но и не воспроизводимы: на чистом клоне ссылки `LogoUrl` из дампа БД
    будут битыми.

---

## 10. Краткий вывод: что логично делать дальше

Не как решение, а как то, что напрашивается из перечисленного выше:

- **Закрыть P1-пункты 1–5** — это точечные правки на десятки строк (переопределить дефолты,
  сузить права в `ServicesController`, добавить `CanManage` в `GET /api/workinghours`, спрятать Swagger
  вне Development, добавить exception-middleware), и каждая из них уже сейчас имеет куда встать в
  существующий тестовый набор.
- **Поднять CI** на то, что уже есть: `dotnet build` + `dotnet test ServiceBooking.Tests` (нужен сервис
  PostgreSQL в пайплайне) + `npm ci && npm run build`. Инфраструктура тестов к этому готова —
  база пересоздаётся сама, параллелизм отключён.
- **Определиться с фронтенд-тестами** — сейчас это единственная крупная непокрытая зона, и именно там
  идёт основная активность последних коммитов.
- **Решить судьбу мёртвого кода**: проект `ServiceBooking/`, `pages/DashboardPage.tsx`,
  `pages/owner/OwnerPage.tsx` — удалить или явно задокументировать, почему остаются.
- **Синхронизировать DTO бэкенда и типы фронтенда** (расхождение `price`/`companySlug`) — либо добавить
  поля в `BookingDto`, либо убрать неработающие ветки UI.
- **Явно обозначить в UI незавершённые фичи** (рассылка, предоплата, `NotifyDaysBefore`), чтобы
  владельцы компаний не считали их рабочими.

Приоритизация и проектные решения по этим пунктам — зона product-analyst и architect; здесь они
перечислены только как прямые следствия зафиксированного состояния.
