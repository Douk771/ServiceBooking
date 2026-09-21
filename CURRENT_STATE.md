# CURRENT_STATE — фактическое состояние кодовой базы ServiceBooking

**Актуально по состоянию на коммит: `7ab28b2`, дата: 2026-09-22.**

Документ описывает **что есть в репозитории сейчас**, без предложений по развитию.

Точка отсчёта. Ветка — **`develop`**, HEAD — `7ab28b2` (`origin/develop` указывает туда же),
рабочее дерево чистое.
Модель веток прежняя: `master` ← `release-candidate` ← `develop` ← `cycle/NN-<слаг>`
(см. §0.0 DEPLOY.md). `develop` — интеграционный ствол и одновременно **та самая ветка, которая
развёрнута на боевой машине**: `master` отстаёт, тегов нет, релиз формально не объявлен.

**Сверка от `7a36543` к `7ab28b2`: изменений в коде нет.** Единственный коммит диапазона —
`7ab28b2` «Write down that the feature is built and still cannot ship», и он трогает **только три
markdown-файла**: `CHANGELOG.md` (+107 строк), `CURRENT_STATE.md` (прошлая редакция) и `README.md`
(+33/−11). Ни одного файла `*.cs`, `frontend/src/**`, миграции, конфига, CI или deploy-скрипта в
диапазоне не изменено (`git diff --stat 7a36543..7ab28b2`). Поэтому все разделы ниже, описывающие
код (§1–§9), **перенесены без изменений** — обновлены только шапка и §10 (документация), которую
этот коммит и менял.

Состояние веток на момент правки: локально выписана **`cycle/06-booking-fixes`**, но и она, и
`cycle/05-legal-compliance` указывают на тот же коммит `7ab28b2`, что и `develop`, — **уникальных
коммитов в них нет** (`git log cycle/06-booking-fixes ^develop` пуст). То есть ветки заведены
заранее, работа по циклам 5 и 6 ещё не начиналась, и всё описанное здесь целиком лежит на `develop`.

Диапазон изменений предыдущей редакции (`0e61369..7a36543`): **18 коммитов**, полный
список — `git log --oneline 0e61369..HEAD`. Это **весь цикл 4** (ветка `cycle/04-notifications-whatsapp`,
смёржена коммитом `2a11d07`), плюс два коммита починки CI уже на `develop` и один коммит
документации (`d4137dd`), закрывший расхождение README/CHANGELOG с фактом развёртывания.
Объём: **+25 082 / −1 197** строк, 176 файлов.

**Главное отличие от прошлой редакции: в продукте впервые появился канал сообщений наружу —
уведомления клиенту о записи в WhatsApp, отправляемые от имени салона с его собственного номера
через GREEN-API.** Сюда же входят: справочник городов и часовые пояса компаний, первое в проекте
шифрование чужих секретов (AES-GCM), две новые фоновые задачи, админский контур оплаты канала и
параметров платформы, раздел «Уведомления» в кабинете владельца.

⚠️ **Функция не выпущена наружу и после выката никому не предлагается** — тарифный флаг
`AllowNotificationChannel` выключен у всех планов, цена опции не задана. Это **ожидаемое состояние,
а не дефект** (подробнее — §9, блок P0). Провайдер по умолчанию — `logging`, то есть в закоммиченной
конфигурации приложение не делает ни одного сетевого вызова к GREEN-API.

Все утверждения ниже получены чтением исходников, конфигов и git-истории. Где чего-то не нашлось —
так и написано.

**Числа прогонов на `7a36543`** (на `7ab28b2` остаются в силе — код между этими коммитами не
менялся, см. сверку выше; автор этого документа работает только на чтение и тесты не
запускает — прогон функционального набора пересоздаёт базу `servicebooking_test`, см. §7; числа
получены от исполнявшего прогон, зафиксированы в теле мерж-коммита `2a11d07`):

| Команда | Результат | Было в прошлой редакции |
|---|---|---|
| `dotnet build ServiceBooking.sln -warnaserror` | **0 warnings, 0 errors** | 0 / 0 |
| `dotnet test ServiceBooking.UnitTests` | **488 / 488** | 215 / 215 |
| `dotnet test ServiceBooking.Tests` | **449 / 449** | 407 / 407 |
| `npm run test:run` (в `frontend/`) | **100 / 100** | 78 / 78 |
| `npx tsc --noEmit` (в `frontend/`) | чисто | чисто |
| `npm run build` (в `frontend/`) | успешно | успешно |

Числа перепроверены **статическим подсчётом** при подготовке этой редакции и сходятся точно:
`[Fact]`+`[InlineData]` — **488** в `ServiceBooking.UnitTests`, **449** в `ServiceBooking.Tests`
(там атрибуты идут парой `[Fact, TestCase("ID")]`), вызовов `it(...)` в `frontend/src` — **100**.
Функциональный набор прогонялся **дважды подряд без пересоздания базы** — идемпотентность
подтверждена (запись в `2a11d07`).

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
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` (встроенный в ASP.NET Core 8), 🆕 **шесть** именованных политик: `uploads`, `auth-login`, `auth-register`, `booking-create`, `data-export`, 🆕 `notifications-webhook` (600/мин на IP); глобального лимитера нет | `Program.cs`, секция `RateLimits` |
| 🆕 **Шифрование секретов** | **AES-GCM напрямую** (`System.Security.Cryptography`), мастер-ключ из конфигурации (`Notifications:EncryptionKey`, 32 байта base64). ASP.NET Core Data Protection **отвергнут осознанно** — его key ring в контейнере эфемерен и молча перестал бы читать ранее зашифрованные токены после передеплоя. Формат шифротекста `v1.<keyId>.<base64(nonce12‖tag16‖ct)>`, AAD — id канала | `Services/Notifications/SecretProtector.cs`, `ChannelKeyFingerprint.cs` |
| 🆕 **HTTP-клиент наружу** | `IHttpClientFactory`, именованный клиент `green-api` с keep-alive и **IPv4-first `ConnectCallback`**; логирование этого клиента заглушено на уровне категорий (токен в URL) | `Program.cs`, `Services/Notifications/GreenApi/GreenApiHandlerFactory.cs`, `PreferIPv4.cs` |
| 🆕 Кеш в памяти | `IMemoryCache` (`AddMemoryCache`) — кеш QR-ответа и 60-секундный кеш `PlatformSettings` | `Program.cs` |
| **Логирование** ⭐ | **Serilog.AspNetCore 8.0.3** (`CompactJsonFormatter` в stdout и `logs/app-.json`) + маскирование телефонов | `Program.cs`, `Services/LogMasking.cs`, `PhoneMaskingEnricher.cs` |
| **Трекер ошибок** ⭐ | **Sentry.Serilog 4.13.0** — синк включается только при непустом `Sentry:Dsn`; целевой приёмник — self-hosted **GlitchTip** (Sentry-совместимый) | `ServiceBooking.API.csproj`, `docker-compose.glitchtip.yml` |
| **Health-checks** ⭐ | встроенные `Microsoft.Extensions.Diagnostics.HealthChecks`, два анонимных эндпоинта со своим двухполевым ответом | `Program.cs`, `Services/Health/` |
| Фоновые задачи | Свой `BackgroundService` + `IScheduledTask` (цикл 2). Hangfire/Quartz **нет**. 🆕 Задач стало **три** (добавились `notification-dispatch` и `channel-health`), сам раннер при этом не менялся | `Services/Scheduling/` |
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

- 🆕 **GREEN-API (WhatsApp)** — третья реальная интеграция, **появилась в цикле 4 и в закоммиченной
  конфигурации выключена**. `Notifications:Provider` по умолчанию `"logging"` — транспорт-заглушка,
  которая не делает ни одного сетевого вызова (`Services/Notifications/LoggingNotificationTransport.cs`,
  `NoopChannelProvisioning`). Реальный адаптер включается значением `"green-api"`; **нераспознанное
  значение роняет старт**, чтобы прод не мог тихо «отправлять» в никуда.
  Код адаптера — `Services/Notifications/GreenApi/` (`GreenApiTransport`, `GreenApiProvisioning`,
  `GreenApiUrls`, `GreenApiResultClassifier`, `GreenApiWebhookParser`, `GreenApiStateInstanceParser`,
  `GreenApiHandlerFactory`), базовый адрес `https://api.green-api.com`.
  Модель: **один экземпляр провайдера = один номер = одна оплата**, номер принадлежит салону,
  платформа платит провайдеру партнёрским токеном. **Партнёрского аккаунта GREEN-API пока нет** (§9).
- **Yandex SmartCaptcha** — вторая реальная внешняя интеграция.
  Сервер: `ServiceBooking.API/Services/CaptchaService.cs`, `POST https://smartcaptcha.cloud.yandex.ru/validate`.
  Клиент: `frontend/src/components/booking/SmartCaptcha.tsx`, скрипт `https://smartcaptcha.yandexcloud.net/captcha.js`,
  ключ из `VITE_SMARTCAPTCHA_SITEKEY` (единственная используемая `import.meta.env`-переменная во всём фронтенде).
- **GlitchTip (self-hosted, Sentry-совместимый)** — ещё одна реальная интеграция, ⭐ **поднята и
  работает**: `https://errors.ezbook.ru` (nginx + TLS от certbot + basic-auth перед собственной
  формой логина GlitchTip), стек `docker-compose.glitchtip.yml` с явным `name: glitchtip`.
  `Sentry:Dsn` в **закоммиченном** конфиге по-прежнему пуст — боевое значение живёт только в `.env`
  на машине. Цепочка «ошибка в приложении → issue в трекере → письмо» проверена end-to-end
  2026-09-17 (`DEPLOY.md` §16). Тем же `SENTRY_DSN` пользуются `deploy/backup/backup.sh` и
  `deploy/monitor/health-alert.sh` — один DSN даёт три канала алертов.
- **Платёжного шлюза нет.** Ни SDK, ни HTTP-вызовов — `PaymentStatus` меняется только вручную
  через `PATCH /api/bookings/{id}/mark-paid`.
- **Почтового провайдера нет.** SMTP/SendGrid/любой другой клиент в коде отсутствует (см. §5).
- **Почтового провайдера по-прежнему нет.** Письмо владельцу о разрыве канала было в спеке цикла 4
  и **вырезано решением заказчика** (SPEC §0, редакция 6) — единственный канал оповещения владельца
  это плашка в кабинете. SMTP-клиента в коде нет.
- ~~Интеграции с мессенджерами нет~~ — закрыто циклом 4 (см. GREEN-API выше). MAX, исследованный в
  цикле 2, так и не реализован; выбран WhatsApp.
- **Хранилище файлов — локальный диск, но теперь ДВА класса хранения** (цикл 2, `Services/FileStorage.cs`):
  - *публичный* — по умолчанию `wwwroot/uploads/{companies,avatars,services}` (переопределяется
    `Storage:PublicRoot`), метод возвращает URL вида `/uploads/<область>/<guid>.<ext>`;
  - *приватный* — `App_Data/private-uploads/<companyId>/<guid>.jpg` (фото к заметкам о клиентах),
    **никогда не раздаётся статикой**, метод возвращает непрозрачный storage-key, не URL. Отдаётся
    только через `GET /api/client-notes/photos/{id}` с проверкой членства в компании.
  Корни настраиваются `Storage:PublicRoot` / `Storage:PrivateRoot`; в Production `Program.cs` **падает
  на старте**, если приватный корень резолвится внутри `wwwroot`, ⭐ если приватный корень резолвится
  внутри фактического `Storage:PublicRoot` (или совпадает с ним — раздача теперь следует за этой
  настройкой, а не жёстко за `wwwroot`, см. ниже), ⭐ и если сам `Storage:PublicRoot` резолвится в
  content root приложения или выше него (иначе `/uploads/...` отдавал бы `appsettings.Production.json`,
  DLL-ки и `App_Data/legal/...` кому угодно). Облачного стораджа нет.
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
🆕 Цикл 4 добавил туда же явное выключение двух новых задач
(`ScheduledTasks:notification-dispatch:Enabled=false`, `channel-health:Enabled=false`) — поверх общего
`ScheduledTasks:Enabled=false`, чтобы специализированная фабрика могла включать ровно одну из них.

🆕 **Секция `Notifications` (цикл 4)** — самая большая новая секция `appsettings.json`:
`Provider` (`logging` по умолчанию), `EncryptionKey` (пусто в git), `KeyRotationAck`,
`KeyFingerprintPath` (`App_Data/state/.notifications-key-fingerprint`), `PartnerToken`,
`WebhookToken`, `UnsubscribeKey`, подсекция `GreenApi` (`ApiUrl`, `TimeoutSeconds: 15`,
`ConnectPreference: IPv4First`, `ConnectTimeoutSeconds: 5`, `PerAddressConnectTimeoutSeconds: 2`),
подсекция `Dispatch` (`BatchSize: 200`, `BudgetSeconds: 50`, `MaxParallelChannels: 8`,
`PauseMinMs: 5000`, `PauseMaxMs: 15000`, `InFlightGraceMinutes: 5`, `MaxAttempts: 5`),
а также `ReminderJitterMinutes: 15`, `UnauthorizedInstanceTimeoutMinutes: 15`,
`TestMessageCooldownMinutes: 5`, `ConsecutiveFailureThreshold: 5`, `AllowedRecipients: []`
(белый список получателей для обкатки). Все секреты этой секции в git **пустые** — боевые значения
живут только в `.env` на машине (§8).

---

## 2. Структура репозитория

```
ServiceBooking.sln                  5 проектов (+ папка Solution Items)
├── ServiceBooking.API/             ← точка входа, вся бизнес-логика веб-слоя
│   ├── Program.cs                  🆕 784 строки: Serilog, fail-fast прод-конфига (через DeploymentSafetyChecks),
│   │                               DI, Identity, JWT (+ перечитывание ролей, SecurityStamp и claim'ы согласия),
│   │                               CORS, Swagger (только Dev), exception handler, ForwardedHeaders,
│   │                               5 политик rate limiting, глобальный LegalConsentFilter, health-эндпоинты,
│   │                               регистрация фоновых задач, миграции, сид
│   ├── Controllers/                🆕 18 контроллеров (19 классов — в Reviews их два); цикл 4 добавил
│   │                               CitiesController, NotificationChannelsController, NotificationsController,
│   │                               CompanyNotificationsController
│   ├── DTOs/                       Auth / Bookings / ⭐ Common (PagedResult, 🆕 Optional<T>) / 🆕 Cities /
│   │                               ClientNotes / Companies / 🆕 Notifications (Channel/Log/Settings/Template/Unsubscribe) /
│   │                               Services / WorkingHours
│   ├── Services/                   SlotService+SlotCalculator, SubscriptionResolver, CaptchaService, TokenService,
│   │   │                           AdvisoryLock, BookingFilters, CompanyMembership, PhoneNormalizer, FileStorage,
│   │   │                           ImageSignature, ImageProcessor, ImageUploadService, PhotoQuota,
│   │   │                           ⭐ DeploymentSafetyChecks, ⭐ IdentityRoleSync, ⭐ LogMasking, ⭐ PhoneMaskingEnricher,
│   │   │                           🆕 NotificationScheduler (постановка в очередь), 🆕 NotificationTexts,
│   │   │                           🆕 NotificationRiskText, 🆕 ChannelPresentation, 🆕 CompanyTimeZone,
│   │   │                           🆕 CitySearch, 🆕 PhoneDisplayMask, 🆕 UnsubscribeTokens,
│   │   │                           🆕 PlatformSettingsWriter, 🆕 ProviderWebhookParsing
│   │   ├── Legal/                  ⭐ LegalDocumentProvider, LegalConsentFilter, LegalOptions, LegalSnapshot
│   │   ├── Health/                 ⭐ DatabaseReadyHealthCheck
│   │   ├── Notifications/          🆕 SecretProtector, ChannelKeyFingerprint, NotificationOptions, NotificationGate,
│   │   │   │                       NotificationTiming, NotificationTemplateRenderer/Validator, DefaultTemplates,
│   │   │   │                       ChannelStateMapper/Transition, ChannelIdleCalculator, ChannelPaymentState,
│   │   │   │                       PlatformSettings, PauseGenerator, PreferIPv4, ProviderCallback,
│   │   │   │                       INotificationTransport/IChannelProvisioning/INotificationClock/IDispatchDelay,
│   │   │   │                       LoggingNotificationTransport
│   │   │   └── GreenApi/           🆕 адаптер провайдера: Transport, Provisioning, Urls, ResultClassifier,
│   │   │                           WebhookParser, StateInstanceParser, HandlerFactory
│   │   └── Scheduling/             IScheduledTask, ScheduledTaskRunner, ScheduledTaskOptions,
│   │                               ScheduledTaskSchedule, Tasks/{PhotoRetentionCleanupTask,
│   │                               🆕 NotificationDispatchTask, 🆕 ChannelHealthTask}
│   ├── App_Data/legal/             ⭐ В GIT: манифест legal.json + privacy.html + terms.html (ЧЕРНОВИК);
│   │                               на проде перекрывается bind-mount'ом ./legal с хоста
│   ├── App_Data/private-uploads/   приватный класс хранения (в .gitignore)
│   ├── Dockerfile                  multi-stage, aspnet:8.0, EXPOSE 8080 (комментарий «не менять на -alpine»)
│   └── appsettings*.json           appsettings.json и appsettings.Testing.json в git; Development/Production — в .gitignore
├── ServiceBooking.Core/            только сущности и перечисления, зависимость одна — Identity.EFCore
│   ├── Entities/                   🆕 30 классов (⭐ +UserConsent; 🆕 +12 сущностей цикла 4, см. §3)
│   └── Enums/                      BookingStatus, ⭐ LegalDocumentType, PaymentStatus, PhotoRetention, UserRole,
│                                   🆕 +7: ChannelState, ChannelStateReason, ChannelPaymentStatus,
│                                   NotificationStatus, NotificationReason, NotificationType,
│                                   NotificationTransport, OptOutSource
├── ServiceBooking.Infrastructure/  AppDbContext + 🆕 28 миграций EF Core
├── ServiceBooking.UnitTests/       xUnit, БЕЗ БД и без HTTP — чистая логика; 🆕 37 файлов, 488 запусков
├── ServiceBooking.Tests/           xUnit, функциональные тесты через WebApplicationFactory; 🆕 28 файлов, 449 запусков
│   ├── Infrastructure/             ApiTestBase, CustomWebApplicationFactory, TestDatabaseFixture, JsonHelpers,
│   │                               TestCaseAttribute, TestImages, ⭐ LegalDocumentsTestFactory, ⭐ RateLimitTestFactory,
│   │                               🆕 NotificationTestBase, NotificationTestFactory, NotificationDispatchTestFactory
│   └── Tests/                      🆕 28 файлов по доменам
├── frontend/                       React SPA
│   ├── src/api/                    🆕 20 модулей — тонкая обёртка над axios, по одному на домен
│   │                               (⭐ +legal.ts; 🆕 +cities.ts, +notifications.ts, +notificationChannels.ts,
│   │                               +platformSettings.ts)
│   ├── src/pages/                  страницы; вложенные owner/ и admin/ — вкладки;
│   │                               ⭐ +LegalDocumentPage.tsx, +DeleteAccountPage.tsx;
│   │                               🆕 +UnsubscribePage.tsx, owner/{NotificationsSection, NotificationSettingsTab,
│   │                               NotificationTemplatesTab, NotificationLogTab}.tsx, admin/NotificationsAdminTab.tsx
│   ├── src/components/             booking/, clientNotes/, ⭐ legal/ (ConsentGate, LegalUpdateBanner),
│   │                               layout/, review/, schedule/, ui/ (⭐ +Pagination, 🆕 +CityCombobox),
│   │                               🆕 notifications/ (QrModal, AssignCompanyDialog, RiskAcceptanceModal,
│   │                               ChannelBreachBanner)
│   ├── src/hooks/                  useOverlayDismiss, useAuthedImage, ⭐ useExportData, ⭐ useDebouncedValue
│   ├── src/store/authStore.ts      единственный zustand-стор
│   ├── src/test/setup.ts           setup Vitest (jest-dom + cleanup Testing Library)
│   ├── src/types/index.ts          общие TS-типы (ручная копия серверных DTO)
│   ├── src/utils/                  мапперы ошибок HTTP → русский текст (⭐ +authError, +legalError,
│   │                               🆕 +notificationError) + phone.ts, 🆕 timezone.ts, 🆕 channelBanner.ts
│   ├── eslint.config.js            ⭐ ESLint 9 flat-config + Prettier
│   └── design_handoff_site_redesign/  HTML-макеты редизайна, не участвуют в сборке
├── .editorconfig                   ⭐ описывает уже сложившийся C#-стиль; в CI НЕ проверяется
├── .github/workflows/
│   ├── ci.yml                      CI: три job'а (backend, frontend, docker-build со смоук-прогоном образа)
│   ├── deploy-staging.yml          🚀 деплой стенда по кнопке (ветка develop), environment `staging`
│   └── deploy-production.yml       🚀 деплой прода по кнопке (только тег на master + ввод слова `deploy`),
│                                   environment `production` с required reviewer
├── deploy/
│   ├── deploy.sh / deploy-remote.sh
│   ├── ssh-deploy-wrapper.sh       🚀 форс-команда в authorized_keys пользователя ezbookdeploy:
│   │                               закрытый allowlist upload-release/deploy/rollback/health
│   ├── nginx/ezbook.conf           основной vhost (ezbook.ru)
│   ├── nginx/errors.ezbook.conf    🚀 vhost трекера ошибок (errors.ezbook.ru, basic-auth)
│   ├── ci/smoke.sh                 смоук живого контейнера (health, регистрация, загрузка аватара)
│   ├── backup/                     backup.sh + systemd .service/.timer (локальный бэкап, включая .env)
│   ├── monitor/                    health-alert.sh + systemd .service/.timer
│   └── rollback.sh                 откат одной командой
├── docker-compose.yml              dev: postgres + api
├── docker-compose.prod.yml         prod: postgres + api на 127.0.0.1:5000, два volume + ⭐ bind-mount ./legal
│                                   + 🆕 ЗАПИСЫВАЕМЫЙ bind-mount ./state (отпечаток ключа шифрования)
├── docker-compose.glitchtip.yml    ⭐ self-hosted GlitchTip (Sentry-совместимый трекер), отдельный стек
├── README.md                       продуктовое описание + «чего пока нет» + ⭐ запуск/секреты/CI/деплой
├── CHANGELOG.md                    changelog по датам циклов, самая свежая запись сверху
├── docs/                           пользовательская документация по ролям (⭐ +personal-data.md)
├── SPEC.md                         🆕 ~276 КБ, спека ЦИКЛА 4 (уведомления WhatsApp), редакция 6
├── ARCHITECTURE.md / API_CONTRACT.md                документы ЦИКЛА 3 (не перезаписаны!)
├── ARCHITECTURE_CYCLE4.md / API_CONTRACT_CYCLE4.md  🆕 документы цикла 4 — ПРОДОЛЖЕНИЯ прежних
│                                   (разделы 21–40 и 19–37, нумерация не пересекается)
├── SPEC_CYCLE3_PRODUCTION.md       🆕 сохранённая спека цикла 3 (SPEC.md занят циклом 4)
├── SPEC_DEFERRED_NOTIFICATIONS.md / SPEC_APPENDIX_CHANNELS.md  ⭐ спека ОТЛОЖЕННОГО цикла уведомлений
├── API_DOCUMENTATION.md            ~247 КБ, подробный справочник эндпоинтов (рус.)
├── TEST_CATALOG.md                 ~270 КБ, человекочитаемый каталог всех тест-кейсов (рус.)
├── DEPLOY.md                       🚀 ~111 КБ, ПЕРЕПИСАН ЦЕЛИКОМ под фактическую машину
│                                   (Ubuntu 24.04 desktop, не VPS): 16 разделов + «Почему так сделано»
│                                   + §16 чек-лист первого запуска с датами и результатами
├── DEPLOY-windows.md               VK Cloud Windows (IIS+ARR) — контур ВЫВЕДЕН ИЗ СКОУПА, файл не удалён
└── .env.production.example, .deploy.env.example, appsettings.Production.json.example
```

⭐ — появилось в цикле 3. 🚀 — появилось/изменилось при первом реальном развёртывании
(диапазон `6369266..0e61369`). 🆕 — появилось в **цикле 4** (диапазон `0e61369..7a36543`).

### Точка входа и слои

- Единственная точка входа приложения — `ServiceBooking.API/Program.cs` (🆕 вырос до **784 строк**).
  **Второй процесс** в том же
  хосте — `ScheduledTaskRunner` (`BackgroundService`), тикает раз в `ScheduledTasks:TickSeconds` (60 с);
  🆕 задач в нём теперь три, но сам раннер цикл 4 **не менял** — это прямое подтверждение, что
  расширение через `IScheduledTask` работает как задумано.
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
  🆕 **Цикл 4 продолжил эту линию агрессивнее всех предыдущих:** почти вся его логика — чистые
  классы, покрытые юнит-тестами (`SecretProtector`, `ChannelKeyFingerprint`, `NotificationGate`,
  `NotificationTiming`, `NotificationTemplateRenderer/Validator`, `ChannelStateMapper`,
  `ChannelIdleCalculator`, `ChannelPaymentState`, `ChannelPresentation`, `CompanyTimeZoneResolver`,
  `CitySearch`, `PhoneDisplayMask`, `UnsubscribeTokens`, `PauseGenerator`, `PreferIPv4`,
  `GreenApiUrls/ResultClassifier/WebhookParser/StateInstanceParser`, `NotificationTexts`,
  `Optional<T>`). Именно поэтому юнит-набор вырос с 215 до 488, а функциональный — только на 42.
- 🆕 **Файлы цикла 4 поделены между двумя бэкенд-разработчиками:** `Services/Notifications/**` —
  один, `Services/Notification*.cs` в корне `Services/` (`NotificationScheduler`, `NotificationTexts`,
  `NotificationRiskText`) — другой. Это объясняет, почему презентационный код лежит не рядом с
  кодами причин, которые он переводит (объяснено комментарием в самих файлах).

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
| `Company` | Guid | `Name`, `Slug` (**уникальный индекс**), `Description`, `LogoUrl`, `Address`, `Phone`, `Email`, `AllowSelfBooking`, `RequirePrepayment`, `ShowInPublicListing`, `IsActive`, `OwnerUserId`, 🆕 **`CityId?`** (FK `Restrict`), 🆕 **`TimeZoneId`** (IANA, дефолт `Europe/Moscow`), 🆕 **`TimeZoneIsManual`** | N—1 Owner (`Restrict`), 🆕 N—1 City (`Restrict`), 1—N Members / Services / Bookings |
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
| `SubscriptionPlanConfig` | — | 🆕 **`AllowNotificationChannel`** (по умолчанию `false` **у всех планов, включая новые** — решение заказчика Q1) | см. выше |

#### 🆕 Сущности цикла 4 (12 новых)

| Сущность | Ключ | Ключевые поля | Смысл |
|---|---|---|---|
| **`City`** | int | `Name`, `Region`, `TimeZoneId` (IANA), `IsActive`, `SearchName` (нормализованное: строчные, ё→е, без дефисов/пробелов) | справочник городов, **91 строка сидится миграцией**. Поиск — обычный `LIKE` по `SearchName`, без full-text и trigram: на таком объёме seq scan дешевле, и это **явно закомментированное решение**, а не недосмотр |
| **`NotificationChannel`** | Guid | `OwnerUserId` (владелец **аккаунта**, тот же ключ, что у `AccountSubscription`), `Transport`, `State`, `PhoneNumber?` (канонический), `ProviderInstanceId?` (**уникальный среди непустых**), `ProviderSecretCiphertext?` + `ProviderSecretKeyId?`, `OrphanedInstanceId?`, `RequestedAtUtc?`, `ContactEmail?` (**зарезервировано, в цикле 4 не используется — письма вырезаны**), `PaidFromUtc?`/`PaidUntilUtc?`, `IsSuspendedByAdmin`, `IdleSinceUtc?`/`IdleWarningSentAtUtc?`, `InstanceCreatedAtUtc?`, `ConnectedAtUtc?`, `LastStateCheckAtUtc?`, `LastStateReason?`, `ConsecutiveSendFailures`, `LastTestMessageAtUtc?`, `DisruptionNotifiedAtUtc?`, `RiskAcceptedAtUtc?`/`RiskAcceptedVersion?`, `ReplacedByChannelId?` | **один экземпляр провайдера = один номер = одна оплата**. Канал принадлежит аккаунту владельца, а **не** компании |
| **`ChannelCompanyAssignment`** | Guid | `ChannelId`, `CompanyId` (**уникальный индекс** — компания не может быть на двух каналах), `AssignedAtUtc`, `AssignedByUserId` | назначение компаний на канал; правило держится **индексом**, а не проверкой в коде |
| **`ChannelStateEvent`** | Guid | `ChannelId`, `FromState`, `ToState`, `Reason`, `Detail?`, `OccurredAtUtc` | история переходов состояния. `Detail` — техническая заметка, **никогда не секрет провайдера** |
| **`ChannelPaymentLog`** | Guid | `ChannelId`, `ChangedByUserId`, `OldPaidUntil?`/`NewPaidUntil?`, `Amount?`, `Comment?`, `ChangedAtUtc` | журнал оплат канала суперадмином, по форме — копия `SubscriptionChangeLog` |
| **`CompanyNotificationSettings`** | **`CompanyId` (PK)** | `EnabledTypeMask` (битовая маска по `NotificationType`, дефолт — все биты), `ReminderLeadMinutes` (60..4320, дефолт 1440), `MinLeadMinutes` (0..720, дефолт 120), `UpdatedAt`, `UpdatedByUserId?` | настройки принадлежат **компании, а не каналу**. Отсутствие строки = дефолты, поэтому backfill существующим компаниям не нужен |
| **`NotificationTemplate`** | Guid | `CompanyId`, `Type`, `Body` (≤1000), `UpdatedAt`, `UpdatedByUserId?` | переопределение платформенного текста. Нет строки или пустой `Body` = платформенный дефолт |
| **`NotificationTemplateHistory`** | Guid | `CompanyId`, `Type`, `PreviousBody`, `ChangedByUserId`, `ChangedAtUtc` | снимок **предыдущего** текста при каждой правке |
| **`OutboundNotification`** | Guid | `CompanyId`, `ChannelId?`, `BookingId?`, `Type`, `RecipientPhone`/`RecipientName?`/`RecipientUserId?`, `Body` (**снимок отрендеренного текста на момент постановки**), `DueAtUtc`, `VisitStartUtc` (денормализовано), `Status`, `Reason?`, `ReasonDetail?`, `AttemptCount`, `LastAttemptAtUtc?` (**он же маркер «в полёте»**), `NextAttemptAtUtc?`, `ProviderMessageId?`, `SentAtUtc?`/`DeliveredAtUtc?`/`ReadAtUtc?`, `Generation` (поколение переносов), `IdempotencyKey` | **одна строка — и очередь, и вечный журнал**: строки никогда не удаляются, меняется только статус |
| **`NotificationOptOut`** | Guid | `Phone` (канонический, **уникальный**), `UserId?` (информационно), `OptedOutAtUtc`, `Source` | одна таблица на всю платформу **по номеру**, а не флаг на `AppUser` + таблица для гостей. Следствие **намеренное**: отписка принадлежит номеру, смена телефона её с собой не уносит |
| **`PlatformSetting`** | **`Key` (PK, ≤100)** | `Value` (≤200), `UpdatedAt`, `UpdatedByUserId?` | параметры платформы, правимые суперадмином без пересборки и с журналом. Ключи цикла 4: `notifications.channel.price-per-month`, `notifications.channel.idle-days`. **Отсутствие ключа цены = опция не предлагается**, а не «цена 0» |
| **`PlatformSettingChangeLog`** | Guid | `Key`, `OldValue?`, `NewValue?`, `ChangedByUserId`, `ChangedAtUtc`, `Comment?` | журнал правок параметров платформы |

Перечисления: `BookingStatus { Pending, Confirmed, Cancelled, Completed, NoShow }`,
`PaymentStatus { NotRequired, Pending, Paid }`, `UserRole { Client, Master, CompanyOwner, SuperAdmin }`,
`PhotoRetention { SixMonths = 0, TwelveMonths = 1, Forever = 2 }` (цикл 2),
⭐ **`LegalDocumentType { Privacy, Terms }`** (цикл 3; одноимённый дубль на стороне API был заведён и
удалён внутри цикла, коммит `263eb55` — перечисление живёт только в `Core`).
Сериализуются как строки (`JsonStringEnumConverter` в `Program.cs`).

🆕 **Семь перечислений цикла 4:**

| Перечисление | Члены | Важное |
|---|---|---|
| `ChannelState` | `NotConnected, Connecting, Connected, Disconnected, Blocked, DisabledByOwner, NeedsReconnect, Replaced` | первые семь — ровно то, что видит владелец по SPEC; **восьмое (`Replaced`) — сознательное отступление от буквы спеки**, задокументировано в `ARCHITECTURE_CYCLE4.md` §38.1 |
| `ChannelStateReason` | 11 членов (`Authorized`, `ProviderReportsUnauthorized`, `ProviderReportsBlocked`, `ConsecutiveSendFailuresExceeded`, `DisconnectedByOwner`, `SuspendedByAdmin`, `UnauthorizedInstanceTimedOut`, `IdleInstanceDeleted`, `ReplacedAfterBan`, `SecretUnavailable`) | русский текст для владельца собирается **на сервере** из этого кода, в одном месте |
| `ChannelPaymentStatus` | `NotPaid, Paid, Suspended` | **никогда не хранится** — вычисляется `ChannelPaymentState.Of` |
| `NotificationStatus` | `Pending = 0, Sent, Delivered, Failed, Expired, Skipped, Cancelled` | ⚠️ **`Pending` обязан остаться 0**: частичный индекс диспетчера объявлен сырым SQL-фильтром `"Status" = 0`. Перестановка членов **молча** ломает индекс — он остаётся, но перестаёт совпадать с запросом, и тот сваливается в full scan. Добавлять только в конец |
| `NotificationType` | `BookingConfirmed, Reminder, BookingCancelled, BookingRescheduled, StaffBookingCreated, StaffBookingCancelled` | используется как **позиция бита** в `EnabledTypeMask` → значения тоже append-only. Два последних члена **никем не ставятся в очередь** — US-34 не реализована (§5.1) |
| `NotificationTransport` | `WhatsApp` | один член; заведено enum'ом заранее, чтобы второй транспорт был новым членом + адаптером, а не миграцией |
| `NotificationReason` | 11 членов (`Delivered`, `RecipientHasNoWhatsApp`, `RejectedByProvider`, `RetriesExhausted`, `VisitAlreadyStarted`, `RecipientOptedOut`, `NotOnPaidPlan`, `NoUsableChannel`, `TypeDisabledByCompany`, `BelowMinimumLeadTime`, `BookingOrAssignmentCancelled`) | русский текст журнала доставки собирает сервер (`NotificationTexts`) |

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
- 🆕 **Канал принадлежит аккаунту владельца, компания — только назначается на него.** Это та же
  ось, что у подписки (`AccountSubscription.OwnerUserId`): один номер обслуживает все филиалы
  владельца. Компания при этом может быть назначена **максимум на один канал** (уникальный индекс).
- 🆕 **Часовой пояс компании выводится из города, но может быть перебит вручную.**
  `TimeZoneIsManual` существует ровно для того, чтобы последующее сохранение города **не сбрасывало**
  ручную правку обратно на зону города. Логика — чистый `CompanyTimeZoneResolver`
  (`ForNewCompany` / `ForUpdate`).
- 🆕 **`OutboundNotification` — очередь и журнал в одной таблице.** Идемпотентность даёт
  `IdempotencyKey` с участием `Generation` (поколение переносов), так что перенос записи может
  поставить новое напоминание, не столкнувшись со старым. `LastAttemptAtUtc` пишется **до** исходящего
  HTTP-вызова и работает маркером «в полёте» на `InFlightGraceMinutes`.
- 🆕 **Отписка живёт по номеру телефона, а не по человеку** — см. комментарий на `NotificationOptOut`.

### Миграции (🆕 28, все в `ServiceBooking.Infrastructure/Migrations/`)

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

🆕 **Цикл 4 добавил две:**

1. **`20260918051815_AddNotificationChannels`** — вся схема цикла одним файлом, **собранным блоками**:
   таблица `Cities` + сид **91 города** (Калининград → Камчатка, с IANA-зонами) → колонки
   `CityId`/`TimeZoneId`/`TimeZoneIsManual` на `Companies` → **backfill существующих компаний на
   Барнаул / `Asia/Barnaul`** (UTC+7, **отдельная зона, не новосибирская**) → одиннадцать таблиц
   уведомлений и каналов → `AllowNotificationChannel` на планах → индексы, включая **частичный
   `IX_OutboundNotifications_Dispatch` с сырым фильтром `"Status" = 0`**.
2. **`20260921082526_AddChannelLastStateReason`** — поздняя правка по ревью: колонка
   `NotificationChannels.LastStateReason` (кеш последней причины перехода, чтобы список каналов не
   делал второй запрос на строку).

⚠️ **Правило, действующее с момента мёржа цикла 4:** первую из этих двух миграций **не редактировать**.
Она уже применена, и всё новое оформляется **новыми** миграциями — как это и сделала вторая.

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
| POST | `/api/companies` | авторизованные; **лимит `MaxCompanies`** под advisory lock, 402 при превышении. 🆕 **ЛОМАЮЩЕЕ: `cityId` обязателен** — без него 400. Опционально `timeZoneId` (перебивает зону города и ставит `TimeZoneIsManual`) |
| PUT | `/api/companies/{id}` | владелец; 🆕 принимает `cityId` и `timeZoneId`, разрешает их через `CompanyTimeZoneResolver.ForUpdate` (ручная зона переживает смену города) |
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

🆕 **Задач теперь три** (раннер при этом не менялся ни строкой):

1. `photo-retention-cleanup` (период — сутки): удаляет фото, пережившие срок хранения своего тарифа
   (`Forever` не трогается), батчами по 200; затем подметает осиротевшие файлы на диске старше 24 часов.
2. 🆕 **`notification-dispatch`** (период — **1 минута**, `MaxRunMinutes: 2`) — отправщик очереди,
   `Services/Scheduling/Tasks/NotificationDispatchTask.cs` (~464 строки, «главный архитектурный вопрос
   цикла»). Один проход: выборка батча по частичному индексу → классификация по гейтам/таймингам
   **одним `SaveChanges`** → отправка, сгруппированная по каналам, с ограниченным параллелизмом
   **между** каналами и строго последовательно **внутри** канала, с паузой 5–15 с между отправками.
   Собственный бюджет = `min(MaxRunTime − 10 с, Dispatch:BudgetSeconds)` — задача останавливает себя
   раньше, чем сработает жёсткая отмена раннера. Backoff повторов — `[1, 5, 15, 60, 180]` минут,
   потолок `MaxAttempts: 5`. **Пауза принципиально не может держать транзакцию** — это и было главным
   ограничением дизайна.
3. 🆕 **`channel-health`** (период — **15 минут**, `MaxRunMinutes: 10`) —
   `Services/Scheduling/Tasks/ChannelHealthTask.cs` (~405 строк), четыре работы в одном проходе, каждая
   одним батч-запросом: опрос состояния каналов (`Connecting`/`Connected`/`Disconnected`), детекция
   простоя и удаление простаивающего экземпляра, таймаут неавторизованного экземпляра, повтор удаления
   «осиротевших» экземпляров у провайдера (`OrphanedInstanceId`).

`GET /api/admin/scheduled-tasks` (SuperAdmin) отдаёт по каждой задаче: `enabled`, `periodMinutes`,
`lastStartedAt`, `lastFinishedAt`, `lastDurationMs`, `lastSucceeded`, `lastSummary`, `lastError`,
`isOverdue` (не финишировала дольше двух своих периодов). Зависимости резолвятся `[FromServices]` в
самом действии, а не в primary-конструкторе контроллера, — чтобы остальные админские вызовы за это не платили.
В окружении `Testing` планировщик **выключен** (`appsettings.Testing.json`), функциональные тесты
дёргают задачу напрямую. 🆕 Исключение — специализированная фабрика `NotificationDispatchTestFactory`,
которая поднимает **свой** хост с реально тикающим раннером (§7.2).

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
- **Тексты черновые** (`isDraft: true`), fail-fast на это намеренно нет — §9.7.

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
  чистые статические методы **ради тестируемости** (🆕 теперь **59 юнит-тестов**), см. §8.

### 4.17 🆕 Уведомления клиенту в WhatsApp — функция цикла 4, код есть, наружу не выпущена

Самый крупный блок цикла. **Код реализован, протестирован и смёржен, но выключен тарифно** — см.
предупреждение в преамбуле и §9.

**Контроллеры и эндпоинты** (выписаны из атрибутов):

`Controllers/NotificationChannelsController.cs` (~652 строки, `[Route("api/notification-channels")]`,
весь класс под `[Authorize]`):

| Метод | Путь | Что делает |
|---|---|---|
| GET | `/api/notification-channels` | список каналов владельца (`ChannelListDto`) |
| GET | `/api/notification-channels/offer` | что вообще предлагается: цена из `PlatformSetting`, тариф, текст риска и его версия. **Если цена не задана или тариф не разрешает — опция не предлагается** |
| POST | `/api/notification-channels` | заявка на канал (`RequestedAtUtc`), оплату проставляет суперадмин |
| GET | `/api/notification-channels/{id}` | карточка канала |
| POST | `/api/notification-channels/{id}/accept-risk` | принятие текста о рисках; версия сверяется с `NotificationRiskText.CurrentVersion`, устаревшая → 400 |
| POST | `/api/notification-channels/{id}/connect` | создание экземпляра у провайдера, переход в `Connecting` |
| GET | `/api/notification-channels/{id}/qr` | QR для привязки номера (ответ кешируется в `IMemoryCache`); при успешной авторизации фиксирует `Authorized` и заполняет номер из `wid` |
| POST | `/api/notification-channels/{id}/test-message` | тестовое сообщение, кулдаун `TestMessageCooldownMinutes` |
| DELETE | `/api/notification-channels/{id}` | отключение канала владельцем |
| POST | `/api/notification-channels/{id}/replace` | замена номера после бана в том же оплаченном периоде (старый канал → `Replaced`) |
| POST | `/api/notification-channels/{id}/companies` | назначить компанию на канал |
| DELETE | `/api/notification-channels/{id}/companies/{companyId}` | снять компанию с канала |

`Controllers/CompanyNotificationsController.cs` (`[Route("api/companies/{companyId:guid}")]`, `[Authorize]`):
`GET|PUT /notification-settings`, `GET /notification-templates`, `PUT /notification-templates/{type}`,
`POST /notification-templates/{type}/preview`, `GET /notifications` (журнал доставки,
`PagedResult<NotificationLogItemDto>`), `GET /notifications/summary?days=`.

`Controllers/NotificationsController.cs` (`[Route("api/notifications")]`, класс **без** `[Authorize]`):
`GET|PUT /preferences` (`[Authorize]` — переключатель у клиента в профиле),
`GET|POST /unsubscribe/{token}` (**анонимно**, токен подписан `UnsubscribeKey`),
`POST /provider-webhook/{token}` (**анонимно**, политика лимита `notifications-webhook`, парсер
резолвится через `[FromServices]`).

`Controllers/CitiesController.cs`: `GET /api/cities?search=&take=` — **публично**, справочник городов.

`Controllers/AdminController.cs` (всё под `SuperAdmin`): 🆕 `GET /api/admin/notification-channels`
(`PagedResult<AdminChannelDto>`), `GET /api/admin/notification-channels/summary`,
`POST /api/admin/notification-channels/{id}/payment` (проставить оплаченный период + сумму + комментарий),
`POST .../{id}/suspend`, `POST .../{id}/resume`, `GET|PUT /api/admin/platform-settings`
(цена канала и число дней простоя).

**Шифрование чужих секретов — впервые в проекте** (`Services/Notifications/SecretProtector.cs`,
`ChannelKeyFingerprint.cs`): AES-GCM напрямую, мастер-ключ из `.env`
(`NOTIFICATIONS_ENCRYPTION_KEY`, 32 байта base64), формат `v1.<keyId>.<base64(nonce12‖tag16‖ct)>`,
AAD — id канала (шифротекст, перенесённый в чужую строку, не расшифруется), **новый случайный nonce
на каждый вызов**. Отпечаток ключа пишется в файл рядом с `.env`
(`App_Data/state/.notifications-key-fingerprint`, записываемый bind-mount `./state`), и при
расхождении **приложение не стартует**. Невозможность расшифровать секрет — отдельная причина
`ChannelStateReason.SecretUnavailable` (это инцидент платформы, а не действие владельца), экземпляр
при этом выводится из эксплуатации, и Connect открывается заново.

**Постановка в очередь** — `Services/NotificationScheduler.cs`, вызывается **напрямую из
`BookingsController`** на создание / отмену / перенос записи. Решение «слать или нет» — чистый
`NotificationGate`; когда слать — чистый `NotificationTiming` (с джиттером `ReminderJitterMinutes`).

**Три рубежа против попадания токена в лог:** заглушено логирование HTTP-клиента `green-api` на
уровне категорий; `GreenApiUrls.SafeLabel` для того, что адаптер логирует сам; маскирование
токена в access-логе nginx (§8).

Фронт: раздел «Уведомления» в кабинете владельца (`pages/owner/NotificationsSection.tsx` +
вкладки `NotificationSettingsTab` / `NotificationTemplatesTab` / `NotificationLogTab`), привязка по
QR (`components/notifications/QrModal.tsx`), назначение компаний (`AssignCompanyDialog`), принятие
рисков (`RiskAcceptanceModal`), плашка о разрыве (`ChannelBreachBanner`), админский экран
(`pages/admin/NotificationsAdminTab.tsx`), выбор города (`components/ui/CityCombobox.tsx`),
публичная страница отписки `/u/:token` (`pages/UnsubscribePage.tsx` — **добавлена в
`CONSENT_GATE_BYPASS_PATHS`**, иначе заблокированный согласием пользователь не смог бы отписаться),
переключатель уведомлений в `ProfilePage.tsx`.

---

## 5. Что реализовано частично, заглушки и несогласованности

Явных маркеров `TODO`/`FIXME`/`HACK` в коде **нет ни одного** (🆕 перепроверено grep'ом заново на
`7a36543` по всем четырём проектам и `frontend/src`). Всё ниже выявлено чтением кода.

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

3. 🆕 **Уведомления клиенту реализованы, но выключены; уведомлений персоналу нет вовсе.**
   - Клиентские уведомления в WhatsApp (§4.17) **написаны и покрыты тестами**, но
     `AllowNotificationChannel = false` у всех тарифов и цена опции не задана — после выката опция
     **никому не предлагается**. Провайдер по умолчанию `logging` — сетевых вызовов нет.
   - **US-34 «уведомления персоналу» не реализована** — отложена осознанно, была первой в порядке
     урезания цикла. Следы в коде есть и они **мёртвые**: члены `NotificationType.StaffBookingCreated`
     / `StaffBookingCancelled` существуют, для них есть тексты (`NotificationTexts`) и дефолтные
     шаблоны (`DefaultTemplates`), но **ни одна строка кода их в очередь не ставит** (проверено
     grep'ом). Эндпоинт `PUT /api/companies/{id}/members/{memberId}/notifications` из
     `API_CONTRACT_CYCLE4.md` §35 **не существует**.
   - Email/SMS по-прежнему нет. Письмо владельцу о разрыве канала **вырезано решением заказчика**,
     единственный канал оповещения владельца — плашка в кабинете.
   - `SubscriptionPlanConfig.NotifyDaysBefore` сохраняется, редактируется в
     `pages/admin/PlansTab.tsx` и подписан «Уведомление за N дн. до деактивации» — но **никем не читается**
     в бизнес-логике (единственное использование в бэкенде — присваивание в `AdminController.UpdatePlan`).

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

9. 🆕 **Текст о рисках подключения WhatsApp — «рыба».** `Services/NotificationRiskText.cs`,
   единственная константа `CurrentVersion = "2026-09-18-draft"`, содержательный текст ждёт вычитки
   юристом. Механика при этом **настоящая**: владелец обязан принять текст, версия сохраняется в
   `NotificationChannel.RiskAcceptedVersion`, устаревшая версия отвергается 400. Полноценного
   провайдера документов (как в цикле 3) для одного абзаца заводить не стали — это осознанно.
   Правовые документы `App_Data/legal/` при этом **не менялись** и остаются на `2026-09-08-draft`.

10. 🆕 **`NotificationChannel.ContactEmail` — зарезервированное, сознательно неиспользуемое поле.**
    Сбор email при оплате канала и письмо о разрыве вырезаны решением заказчика; колонка оставлена в
    схеме, чтобы возврат функции не требовал миграции. Ни в одном DTO не отдаётся.

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
   «повис» `clientDeleted` (п. выше). 🆕 Цикл 4 добавил в этот файл **+177 строк** ручных копий
   (каналы, состояния, шаблоны, журнал, города) — поверхность расхождения выросла заметнее, чем в
   любом предыдущем цикле.

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

- 🆕 **Расхождение README/CHANGELOG с фактом развёртывания, бывшее «самым крупным» в прошлой
  редакции, ЗАКРЫТО** коммитом `d4137dd` (в начале этого диапазона): README теперь пишет, где сервис
  работает и почему это стенд, а в CHANGELOG появился верхний раздел **«Не выпущено»** — то, что
  принято командой, но ещё не выкачено. Содержимое цикла 4 в оба файла вносится **параллельно
  product-analyst'ом**, этим документом не описывается.
- 🆕 **`ARCHITECTURE.md` и `API_CONTRACT.md` в корне — это документы ЦИКЛА 3, а не текущего.**
  Цикл 4 их **не перезаписывал**: он положил рядом `ARCHITECTURE_CYCLE4.md` (разделы **21–40**) и
  `API_CONTRACT_CYCLE4.md` (разделы **19–37**) — продолжения с непересекающейся нумерацией. Ссылка
  вида «§26» без указания файла **неоднозначна**: смотреть надо на номер (≥21 / ≥19 — цикл 4).
  `SPEC.md`, наоборот, **перезаписан** циклом 4, а прошлая спека сохранена как
  `SPEC_CYCLE3_PRODUCTION.md`.
- 🆕 **`API_CONTRACT_CYCLE4.md` §35 описывает эндпоинт, которого нет** —
  `PUT /api/companies/{id}/members/{memberId}/notifications` (US-34). В самом контракте он помечен
  «режется первым», и он действительно был срезан; раздел из документа не убран.
- `API_DOCUMENTATION.md` (🆕 ~247 КБ) **обновлён в цикле 3** коммитом `90a69b1`: в нём есть `/api/legal/*`,
  `/api/profile/export`, `/api/profile/delete-account`, `/api/health/*`, `acceptedLegal` в регистрации
  и новый §3.11 про конверт `PagedResult<T>`. Отдельно в нём есть §7 «Известные ограничения» — раздел,
  который стоит перечитывать вместе с §9 этого документа.
- ⚠️ ⭐ **`TEST_CATALOG.md` содержит устаревшее замечание о самом себе.** Его последний раздел
  («Документация, не обновлённая вместе с кодом») утверждает, что `API_DOCUMENTATION.md` в цикле 3 не
  тронут ни одной строкой. На момент написания это было правдой — документ обновили **позже**, тем
  самым `90a69b1`, а замечание не убрали.
- 🆕 **`DEPLOY.md` дополнен в цикле 4** (+192 строки): раздел «Инвентарь секретов» (на который раньше
  ссылался `backup.sh` вникуда) появился, `.env` в бэкапе описан, рядом — процедура ротации ключа
  шифрования и создание каталога `state/` до первого `docker compose up`. Прежняя претензия
  «§11.1 перечисляет шесть шагов из семи» в этой редакции **снята**.
- 🚀 Преамбула `ARCHITECTURE.md` (цикл 3) называет baseline **цикла 3** — исторические числа на
  момент начала того цикла, а не текущие (`488`/`449`/`100`, §7). Преамбула
  `ARCHITECTURE_CYCLE4.md` аналогично называет baseline цикла 4 (`215`/`407`/`78`).
- 🆕 Соглашения об архиве (`docs/history/`) в репозитории **по-прежнему нет**, каталога такого нет,
  в README оно не описано. Цикл 4 решил проблему иначе — **суффиксом в имени файла**
  (`*_CYCLE4.md`, `SPEC_CYCLE3_PRODUCTION.md`), а не переносом в архив. Поэтому документы двух
  последних циклов **одновременно лежат в корне**. Более ранние редакции живут только в git-истории
  (SPEC цикла 2 — `git show 0492092:SPEC.md`, цикла 1 — `e6b746c`, ещё более ранняя — `7c86ca2`).
- ⭐ `SPEC_DEFERRED_NOTIFICATIONS.md` и `SPEC_APPENDIX_CHANNELS.md` — сохранённая спека **отложенной**
  темы уведомлений по телефону (MAX/SMS). 🆕 Цикл 4 закрыл эту тему **другим каналом** (WhatsApp), но
  файлы остались: на них ссылается код (`ProfileController.ChangePhone` — пункт Д-1 о подтверждении
  номера, он **не реализован и сейчас**), и исследование каналов доставки из приложения сохраняет
  ценность. Читать их как «план работ» уже нельзя.

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
- 🆕 **Секреты чужих аккаунтов — только через `SecretProtector`.** Никакого хранения токена
  провайдера в открытом виде, никакого Data Protection, никакого своего AES. Нарушение расшифровки
  бросает `ChannelSecretUnavailableException`, и вызывающий обязан перевести это в «каналу нужно
  переподключение», а **не** дать `CryptographicException` или его текст дойти до лога/ответа.
- 🆕 **Русский текст статусов и причин собирает сервер, в одном месте.** `ChannelStateReason` и
  `NotificationReason` — машиночитаемые коды; формулировки живут в `Services/NotificationTexts.cs` и
  `Services/ChannelPresentation.cs`. Фронт их **не сочиняет** и не собирает из кусочков.
- 🆕 **Параметры платформы, правимые суперадмином, — таблица `PlatformSetting`, а не `appsettings`
  и не поле на тарифе.** Чтение — `Services/Notifications/PlatformSettings` (кеш 60 с), запись —
  `Services/PlatformSettingsWriter` (пишет журнал). **Отсутствие ключа ≠ значение по умолчанию:**
  отсутствие цены означает «опция не предлагается», а не «бесплатно».
- 🆕 **«Поле не прислали» и «прислали null» различаются через `DTOs/Common/Optional<T>`** (+ свой
  `OptionalJsonConverterFactory`, зарегистрирован в `Program.cs`). Для частичных обновлений
  (например, снять ручной часовой пояс) писать отдельные флаги `xxxSpecified` не нужно.
- 🆕 **Абстракции над временем, задержкой и случайностью обязательны для всего, что «ждёт»:**
  `INotificationClock`, `IDispatchDelay`, `IPauseGenerator`. Прода это не меняет, но делает
  отправщик тестируемым мгновенно (§7.2). `DateTime.UtcNow` и `Task.Delay` прямо в такой логике —
  регресс.
- 🆕 **Внешний провайдер — за интерфейсом с заглушкой по умолчанию.** `INotificationTransport` /
  `IChannelProvisioning`, дефолт — `logging`/no-op, реальный адаптер включается строкой конфигурации,
  **нераспознанное значение роняет старт** (тихий откат на заглушку в проде недопустим).
- 🆕 **Миграции: первую миграцию смёрженного цикла не редактируют.** Всё новое — новой миграцией
  (образец — `AddChannelLastStateReason`, добавленная поверх по итогам ревью).
- 🆕 **Значения `enum`, участвующие в SQL-фильтрах индексов или в битовых масках, — append-only.**
  Касается `NotificationStatus` (частичный индекс диспетчера с сырым фильтром `"Status" = 0`) и
  `NotificationType` (позиции битов в `EnabledTypeMask`). Перестановка ломает **молча**.
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
- **Ошибки HTTP → текст пользователю** через `src/utils/*Error.ts` — 🆕 сейчас их двенадцать
  (`authError`, `bookingError`, `cancelError`, `companyError`, `companyAdminError`, `companyManageError`,
  `legalError`, `memberError`, `planError`, `scheduleError`, `uploadError`, 🆕 `notificationError`)
  — `switch` по `status` с явной обработкой
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
  §9.25).
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
- Работа идёт в ветке **`develop`** — интеграционном стволе модели `master` ← `release-candidate`
  ← `develop` ← `cycle/NN-<слаг>` (§0.0 DEPLOY.md). `master` — предыдущее состояние, циклы в него
  **не вливались**: релиза не было, на боевую машину ничего не выкатывалось. Ветки завершённых
  циклов размечены задним числом (`cycle/01-cleanup`, `cycle/02-photos-scheduler`,
  `cycle/03-production-readiness`), `sanitation-cycle` сохранён и дублирует третью из них.
  CI триггерится на push в `master`, `release-candidate`, `develop`, любую ветку по маске
  `cycle/**` и на любой pull request.
  🆕 **Цикл 4 — первый, прошедший модель веток целиком по назначению:** работа шла в
  `cycle/04-notifications-whatsapp`, влилась в `develop` мерж-коммитом `2a11d07` **с сохранением
  истории ветки** (не squash), и в теле мержа записан результат приёмки: числа прогонов, два
  пройденных ревью и **явный список того, почему релиз не готов**. Это удобная точка отсчёта —
  `git log --oneline 0e61369..2a11d07` показывает весь цикл, а два коммита после мержа
  (`aae3541`, `7a36543`) — только починку CI.
- 🆕 **Ветки циклов 5 и 6 уже заведены, но пусты:** `cycle/05-legal-compliance` и
  `cycle/06-booking-fixes` (обе есть и локально, и на `origin`) указывают ровно на `7ab28b2`, то есть
  на текущий тип `develop`; `git log cycle/05-legal-compliance ^develop` и то же для `cycle/06`
  не дают ни одного коммита. Не принимать имя ветки за выполненную работу: правового цикла 5 и
  починок записи цикла 6 в коде нет.
- 🆕 Цикл 4 — **15 коммитов** в ветке цикла (`560526c..85ad781`) плюс мерж и две починки CI.
  Заголовки — в том же повествовательном стиле, что и раньше; тело объясняет «почему», трейлер
  `Co-Authored-By: Claude Opus 5` (у мерж-коммита — `Claude Sonnet 5`).
- Цикл 3 — **26 коммитов** (`f3adc6e..7a551eb`), в отличие от цикла 2, уехавшего одним коммитом
  `0492092`. Заголовки мелких коммитов несут идентификатор задачи из ARCHITECTURE (`T-B1`, `T-F5`, …)
  и историю из SPEC (`US-42`), ломающие изменения помечены прямо в заголовке (`BREAKING #1`,
  `BREAKING #2`). Рабочее дерево чистое.

---

## 7. Тесты

### Что есть — три набора

| Набор | Проект/каталог | Нужна БД? | Команда | Объём |
|---|---|---|---|---|
| Юнит-тесты бэкенда | `ServiceBooking.UnitTests` | нет | `dotnet test ServiceBooking.UnitTests` | 🆕 **488** запусков (было 215) |
| **Функциональные (API) тесты** | `ServiceBooking.Tests` | **да, PostgreSQL** | `dotnet test ServiceBooking.Tests` | 🆕 **449** запусков (было 407) |
| Тесты фронтенда | `frontend/src/**/*.test.ts(x)` | нет | `npm run test:run` (в `frontend/`) | 🆕 **100** тестов (было 78) |

Количества посчитаны статически по атрибутам `[Fact]`/`[Theory]`+`[InlineData]` и вызовам `it(...)`
и совпадают с фактическим прогоном на `7a36543`. В `ServiceBooking.Tests` атрибуты идут парой
`[Fact, TestCase("ID")]` — голого `[Fact]` там не встретить, искать надо `[Fact`.

🆕 **Пропорция роста важна сама по себе:** юнит-набор вырос более чем вдвое (+273), функциональный —
на 42. Цикл 4 писался «чистой логикой наружу», и основная проверка его правил живёт **без БД**.

### 7.1 Юнит-тесты бэкенда — `ServiceBooking.UnitTests`

**Ни БД, ни HTTP, ни моков** — только чистые функции.

- **Фреймворк:** xUnit 2.5.3 + FluentAssertions 6.12.1, `Microsoft.NET.Test.Sdk` 17.8.0,
  `coverlet.collector` 6.0.0. Ссылается напрямую на `ServiceBooking.API`.
- `ServiceBooking.API.csproj` содержит `<InternalsVisibleTo Include="ServiceBooking.UnitTests" />`.
- Атрибут `[TestCase(...)]` здесь **не используется** — стабильные ID есть только у функционального набора.

🆕 **Цикл 4 добавил 22 файла** (перечислены ниже отдельным блоком) — юнит-набор стал основным
носителем правил этого цикла.

| Файл | Что покрывает | `[Fact]` + `[InlineData]` |
|---|---|---|
| **`DeploymentSafetyChecksTests.cs`** ⭐🆕 | fail-fast прод-конфига: Jwt-ключ, пароль SuperAdmin, приватный корень внутри `wwwroot`, доверенные сети, 🆕 секреты уведомлений, отпечаток ключа, наличие tzdata | **59** (было 28) |
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
| `FileStorageTests.cs` | containment-проверка путей, ключи vs URL | 8 |

🆕 **Файлы цикла 4** (22 новых, суммарно ~273 запуска):

| Файл | Что покрывает | Запусков |
|---|---|---|
| `ChannelKeyFingerprintTests.cs` | отпечаток мастер-ключа, расхождение, подтверждение ротации | 18 |
| `NotificationGateTests.cs` | решение «слать или нет»: тариф, канал, тип, отписка, минимальный запас | 18 |
| `SecretProtectorTests.cs` | AES-GCM, AAD на id канала, порча шифротекста, чужой ключ | 15 |
| `GreenApiResultClassifierTests.cs` | постоянная ошибка vs временная, «нет WhatsApp» | 15 |
| `CompanyTimeZoneResolverTests.cs` | вывод зоны из города, ручное переопределение, смена города | 15 |
| `NotificationTemplateTests.cs` | рендер и валидация шаблонов, плейсхолдеры | 23 |
| `ChannelPresentationTests.cs` | русские формулировки состояния канала | 23 |
| `NotificationTimingTests.cs` | когда ставить напоминание, джиттер, границы | 19 |
| `NotificationTextsTests.cs` | русские формулировки журнала доставки | 12 |
| `GreenApiUrlsTests.cs` | сборка URL и `SafeLabel` (токен не должен попасть в лог) | 11 |
| `GreenApiWebhookParserTests.cs` | разбор вебхука статусов | 11 |
| `ChannelStateMapperTests.cs` | состояние провайдера → `ChannelState` | 10 |
| `CitySearchTests.cs` | нормализация поисковой строки (ё→е, дефисы) | 9 |
| `ChannelIdleCalculatorTests.cs` | расчёт простоя канала | 8 |
| `GreenApiStateInstanceParserTests.cs` | разбор ответа о состоянии экземпляра | 8 |
| `UnsubscribeTokensTests.cs` | подпись и разбор токена отписки | 8 |
| `ChannelPaymentStateTests.cs` | вычисление `ChannelPaymentStatus` | 5 |
| `PreferIPv4Tests.cs` | упорядочивание адресов, IPv4 первым | 5 |
| `ScheduledTaskOptionsTests.cs` | чтение подсекций новых задач | 5 |
| `PauseGeneratorTests.cs` | пауза 5–15 с между отправками | 4 |
| `PhoneDisplayMaskTests.cs` | маска номера для показа владельцу | 4 |
| `OptionalTests.cs` | «не прислали» vs «прислали null» | 3 |

| **Итого** | | 🆕 **488 запусков** |

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
- **Дополнительные фабрики** (каждая поднимает **свой** хост поверх той же базы):
  - ⭐ `Infrastructure/RateLimitTestFactory.cs` — хост с жёсткими лимитами. Основная фабрика в
    `Testing` поднимает все `RateLimits:*` до 10000/мин именно для того, чтобы остальные сотни тестов
    никогда не упирались в лимит; тесты `SEC-` про 429 нуждаются в обратном.
  - ⭐ `Infrastructure/LegalDocumentsTestFactory.cs` — хост, смотрящий на **одноразовую копию**
    `App_Data/legal`, чтобы `LEG-`-тесты переписывали `legal.json`/HTML прямо на диске (существенная
    vs редакционная правка, «подменили файл — без пересборки»), не мешая остальным. `ReloadSeconds: 1`,
    чтобы не спать 30 секунд на каждую смену версии. 🆕 **Получила собственного суперадмина**
    (коммит `7a36543`) — см. §9 про урок общих ресурсов.
  - 🆕 `Infrastructure/NotificationTestFactory.cs` — хост на тест для тестов каналов.
  - 🆕 `Infrastructure/NotificationTestBase.cs` — общая база в **той же** коллекции `"Api"`.
  - 🆕 **`Infrastructure/NotificationDispatchTestFactory.cs` — первая в проекте тестовая
    инфраструктура с РЕАЛЬНО тикающим `ScheduledTaskRunner`.** До цикла 4 раннер в тестах был выключен
    целиком, и его собственное поведение (advisory lock, бюджет, частичный проход) проверить было
    нечем. Фабрика включает его обратно на своём хосте и подменяет три абстракции на
    записывающие/фейковые: `IDispatchDelay` → `RecordingDelay`, `INotificationClock` → `FakeClock`,
    `INotificationTransport` → `RecordingTransport` (регистрируются **после** `Program.cs` — побеждает
    последняя). Экземпляр **на каждый тест**, не общий. Реальный сетевой вызов через этот хост
    невозможен и без подмены — `Notifications:Provider=logging` уже это гарантирует; подмена нужна
    для наблюдаемости и мгновенности, а не для безопасности.
    Тесты этой фабрики вынесены в **отдельную коллекцию** `"NotificationDispatch"` с
    `DisableParallelization = true`.
- **Строка подключения:** переменная окружения `SERVICEBOOKING_TEST_CONNECTION`, при её отсутствии —
  литерал `Host=localhost;Database=servicebooking_test;Username=postgres;Password=` (пустой пароль).
- 🆕 ⚠️ **ЗАДАВАЙТЕ `SERVICEBOOKING_TEST_CONNECTION` ЯВНО, С ИМЕНЕМ БАЗЫ ПО НОМЕРУ ЦИКЛА.**
  Дефолт выше — **разделяемый ресурс между всеми чекаутами репозитория на машине**, а их здесь
  несколько (`ServiceBooking`, `ServiceBooking2`, `ServiceBooking3`, плюс временные git-worktree).
  Прогон стартует с `EnsureDeletedAsync` + `MigrateAsync`, поэтому **чужой прогон дропает и
  пересоздаёт базу посреди вашего**. Наблюдаемые последствия: невоспроизводимые падения на случайных
  тестах, `Npgsql.PostgresException: 42P01: table "..." does not exist` при накате миграций, и —
  самое дорогое — **недостоверные числа прогона**, по которым потом считают регрессии цикла.

  Как надо:
  ```bash
  SERVICEBOOKING_TEST_CONNECTION="Host=localhost;Database=servicebooking_test_cycleNN;Username=postgres;Password=postgres" \
    dotnet test ServiceBooking.Tests
  ```
  В базе уже лежат `servicebooking_test_cycle07`, `servicebooking_test_cycle07_fix` — конвенция
  применялась и раньше, но нигде не была записана, поэтому следующий человек про неё не знал.

  **История, из-за которой это здесь появилось (цикл 6).** Базовая линия цикла была снята как
  «449/449 зелёный». Воспроизвести её не удалось ни разу: повторные прогоны давали 444/449 и
  447/449, состав падений менялся между запусками, а одно падение (500 на анонимном запросе
  приватного фото) не воспроизводилось в изоляции вовсе и было ошибочно принято за регрессию
  цикла. Регрессии не было: продуктовый код оказался ни при чём во всех случаях. На изолированной
  базе набор даёт **451/451 дважды подряд**.

  Это тот же класс дефекта, что урок P0-E из §9 (общий ресурс между тестами внутри процесса), но
  **на уровень выше** — общий ресурс между независимыми рабочими копиями на одной машине. Урок
  P0-E был записан и не сработал, потому что описывал только внутрипроцессный случай.
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
| `Tests/UploadsStaticFilesTests.cs` | — | 2 |
| 🆕 **`Tests/NotificationChannelsTests.cs`** | `NTF-C001…C018` (свой `NotificationTestFactory` на тест) | 18 |
| 🆕 **`Tests/NotificationWebhookUnsubscribeTests.cs`** | `NTF-W*`, `NTF-U*`, `NTF-L*` | 9 |
| 🆕 **`Tests/NotificationCitiesTimeZoneTests.cs`** | `NTF-G001…G006` (коллекция `"Api"`) | 6 |
| 🆕 **`Tests/NotificationQueueingTests.cs`** | `NTF-Q001…Q004` (коллекция `"Api"`) | 4 |
| 🆕 **`Tests/NotificationDispatchExtraTests.cs`** | `NTF-D03…D05` (коллекция `"NotificationDispatch"`) | 3 |
| 🆕 **`Tests/NotificationDispatchTests.cs`** | `NTF-D01…D02` (коллекция `"NotificationDispatch"`) | 2 |
| **Итого** | | 🆕 **449 запусков** |

Человекочитаемое описание каждого кейса — в `TEST_CATALOG.md` (~246 КБ, русский), §10.4.
**Оговорка про `LEG-036`** (гонка одновременного принятия согласия): сторож **вероятностный** —
красноту подтверждали на 20 итерациях, в репозиторий закоммичен одиночный прогон (см. §9.15).

### Как запускать (для QA — базовый прогон)

```bash
# Предусловие: доступен PostgreSQL на localhost:5432, пользователь postgres, ПУСТОЙ пароль.
# Иначе — задать SERVICEBOOKING_TEST_CONNECTION со своей строкой подключения.
# База servicebooking_test создаётся/пересоздаётся автоматически (EnsureDeletedAsync на старте).

cd /Users/ikolomeets/RiderProjects/ServiceBooking2   # 🆕 каталог именно ServiceBooking2
dotnet build ServiceBooking.sln -warnaserror   # так же, как в CI
dotnet test ServiceBooking.UnitTests     # быстрый, без БД — прогонять первым
dotnet test ServiceBooking.Tests         # основной функциональный набор, нужна БД

cd frontend && npm ci && npm run lint && npx tsc --noEmit && npm run test:run
```

Числа последнего фактического прогона (на `7a36543`; на текущем `7ab28b2` код не менялся, поэтому
числа остаются в силе; прогон выполнял не автор этого документа):
`dotnet build … -warnaserror` — **0 warnings / 0 errors**; `ServiceBooking.UnitTests` — **488/488**;
`ServiceBooking.Tests` — **449/449** (🆕 два прогона подряд **без пересоздания базы** — идемпотентность
подтверждена); `npm run test:run` — **100/100**; `tsc --noEmit` — чисто;
`npm run build` — успешно. Отдельного набора e2e/браузерных тестов в проекте **нет** — базовый
прогон QA это `ServiceBooking.Tests` (xUnit + `WebApplicationFactory` + реальная PostgreSQL).

🆕 **Важно для QA, прогоняющего базовый набор:** сетевых вызовов наружу функциональный набор не
делает — `Notifications:Provider` в `Testing` остаётся `logging`, транспорт-заглушка. Ни одного
сообщения в WhatsApp при прогоне не уходит.

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
- **Что покрыто (🆕 100):** `utils/uploadError` (14), `utils/phone` (10), 🆕 `utils/notificationError` (9),
  🆕 `utils/channelBanner` (8), `utils/authError` (7), `utils/cancelError` (6), `utils/legalError` (6),
  `pages/MasterClientsPage` (6), 🆕 `utils/timezone` (5), `pages/LegalDocumentPage` (5),
  `components/clientNotes/PhotoGallery` (5), `components/ui/Pagination` (4),
  `components/legal/ConsentGate` (4), `components/legal/LegalUpdateBanner` (4),
  `hooks/useDebouncedValue` (4), `pages/CompanyPage` (3).
- 🆕 **Весь прирост цикла 4 (+22) — это утилиты.** Ни один из новых экранов уведомлений
  (`NotificationsSection` и три его вкладки, `QrModal`, `AssignCompanyDialog`, `RiskAcceptanceModal`,
  `NotificationsAdminTab`, `UnsubscribePage`, `CityCombobox`) тестами **не покрыт**.
- Впервые появились тесты на **страницы**, а не только на утилиты.

### Чего в тестах НЕТ

- **Нет e2e-тестов через браузер.** Ни Playwright, ни Cypress. «Функциональные» здесь = API-уровень.
  Ближайшее к e2e — `deploy/ci/smoke.sh`: bash + curl против **живого контейнера** (health, регистрация,
  загрузка аватара), запускается CI-джобом `docker-build`, а не тест-раннером.
- **Покрытие фронтенда остаётся точечным** — см. §9.18.
- 🚀 **Ручной чек-лист живых проверок появился** — `DEPLOY.md` §16, семь пунктов, все закрыты с
  датами и результатами (§10.4). Автотестами эти сценарии по-прежнему не покрыты.
- **Нет шага `dotnet format`** (`.editorconfig` есть, ESLint в CI есть) — см. §9.25.
- Не покрыты автотестами: капча с реальным ключом (в `Testing` `SmartCaptcha:SecretKey` пуст →
  валидация пропускается), миграции `NormalizePhoneNumbers`/`ResyncIdentityRoles` на боевом объёме
  данных, сами скрипты `deploy/backup/*`, `deploy/rollback.sh`, `deploy/monitor/*` (bash, тестов
  нет). 🚀 Последние три, в отличие от прошлой редакции, **прогонялись вручную на живой машине**
  2026-09-17 — это не покрытие тестом, но и не «никогда не запускалось».
- 🆕 **Реальный GREEN-API не проверялся ничем.** Адаптер покрыт юнит-тестами на разбор ответов и
  сборку URL, но живого вызова к провайдеру не делал ни один тест и ни один человек — партнёрского
  аккаунта нет. Привязка по QR, реальная доставка сообщения, реальный вебхук статусов и поведение при
  бане номера существуют только в виде кода и тестов против заглушек.
- 🆕 `TEST_CATALOG.md` содержит **явный раздел «Не покрыто функциональными тестами этого прогона»** —
  это зафиксированный, а не скрытый пробел.

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
| `frontend` | Node 20 c npm-кешем; `npm ci` → ⭐ **`npm run lint`** (ESLint) → `npx tsc --noEmit` → `npm run test:run` → `npm run build` (с `VITE_SMARTCAPTCHA_SITEKEY` из **переменной репозитория**, не секрета — site-ключ публичен) → ⭐ **выгрузка артефакта `frontend-dist-<sha>`** (только для `master`/`release-candidate`/`develop` — веток, с которых деплоят; retention 30 дней) |
| `docker-build` | ⭐ теперь **не только собирает, но и запускает**: `docker build` → поднимает `postgres:16-alpine` в отдельной docker-сети → запускает образ с `ASPNETCORE_ENVIRONMENT=Production` и полным набором переменных из `DEPLOY.md` → `deploy/ci/smoke.sh` → `docker logs` при любом исходе |

Про `docker-build` важны две вещи, обе записаны комментариями прямо в workflow:
- **Запуск в `Production` — намеренный.** Это одновременно проверка, что fail-fast
  (`DeploymentSafetyChecks`) удовлетворяется **ровно тем** набором переменных, который описан в
  `DEPLOY.md` и `.env.production.example`: добавили новую обязательную переменную и забыли про
  документацию — job краснеет. 🆕 Цикл 4 добавил туда шесть обязательных `NOTIFICATIONS_*`
  переменных — этот механизм их и держит.
- **`deploy/ci/smoke.sh` гоняет реальную загрузку изображения** по HTTP в живой контейнер, то есть
  проверяет, что `SkiaSharp.NativeAssets.Linux.NoDependencies` действительно грузится на glibc-базе
  и что файл потом реально отдаётся. Ни `dotnet build`, ни `dotnet run` этого поймать не могут.
  Скрипт запускается и руками: `BASE_URL=http://localhost:5000 deploy/ci/smoke.sh`.
  В самом `ServiceBooking.API/Dockerfile` вверху стоит предупреждение: **не менять тег на `-alpine`**.

Чего в CI нет: `dotnet format --verify-no-changes` (см. §9), сбора покрытия, **авто**деплоя —
`ci.yml` только проверяет и складывает артефакт фронта. Деплой — отдельные workflow, запускаемые
человеком кнопкой (ниже).

### 🚀 Деплой по кнопке из GitHub Actions — есть и выполнялся

Два отдельных workflow (`workflow_dispatch`, вручную):

| Workflow | Куда | Гварды |
|---|---|---|
| `.github/workflows/deploy-staging.yml` | стенд, ветка `develop` | `environment: staging`, без обязательных ревьюеров — стенд должен катиться быстро |
| `.github/workflows/deploy-production.yml` | прод, **только тег на `master`** | `environment: production` с **Required reviewers**; отдельный job `guard`: ref должен быть тегом, тег должен лежать на `master`, в поле подтверждения должно быть введено ровно `deploy`. Все три отказа проверены живьём 2026-09-17: `guard: failure`, `deploy: skipped`, до SSH исполнение не доходит |

Облачный раннер GitHub подключается по SSH на 22-й порт под отдельным системным пользователем
**`ezbookdeploy`** (`DEPLOY.md` §8). Три независимых рубежа, ни один не полагается на остальные:
отдельный непривилегированный пользователь (не root, не личный, пароль заблокирован);
**`command=` в `authorized_keys`**, указывающая на `/usr/local/sbin/ezbook-deploy-wrapper.sh`
(копия `deploy/ssh-deploy-wrapper.sh`, владелец root, сам пользователь её не отредактирует) с
**закрытым** allowlist'ом `upload-release`/`deploy`/`rollback`/`health` и `*) refuse` в конце;
точечный `sudo` ровно на `nginx -t` и `systemctl reload nginx`. Секреты приложения (`.env`) в
GitHub **не заводятся** — в секретах только `DEPLOY_SSH_KEY`, `DEPLOY_USER`, `DEPLOY_HOST`
(+ переменные `DEPLOY_HOST_KEY`, `VITE_SMARTCAPTCHA_SITEKEY`).

🚀 **Форс-команда приводит рабочее дерево к коммиту принудительно** (коммит `88f1ed2`):
`git checkout --force` + `git clean -fd` (без `-x`, поэтому `.env` и `legal/` не трогаются никогда),
с печатью `git status --short` отбрасываемого **до** переключения. Причина: прежний `git checkout`
падал от любой ручной правки или постороннего файла в `/opt/ezbook/app`, причём уже **после**
заливки фронтенда, оставляя машину в промежуточном состоянии. Следствие, зафиксированное в
`DEPLOY.md`: **любая правка прямо в `/opt/ezbook/app` не переживёт следующий деплой** (§9).

### Деплой — выполнен вживую; целевая машина

**Проект развёрнут и работает: `https://ezbook.ru`, TLS от Let's Encrypt (certbot --nginx).**
Runbook: `DEPLOY.md` (~111 КБ) — 🚀 **переписан целиком** под фактическую машину; прежняя редакция
описывала AlmaLinux-VPS на reg.ru, которого в итоге не было. Windows/IIS-контур
(`DEPLOY-windows.md`) **выведен из скоупа** решением заказчика, но из репозитория не удалён (§9.13).

🚀 **Целевая машина — не VPS, а десктоп в квартире** (`DEPLOY.md`, «Целевая машина»):

| Параметр | Значение |
|---|---|
| ОС | Ubuntu 24.04 **desktop** (GNOME/GDM), не серверная установка |
| ОЗУ | **3,3 ГиБ**, свободно ~1 ГБ, **492 МиБ уже в подкачке** |
| Диск | один раздел `/dev/sda4`, 210 ГБ, ~180 ГБ свободно, **шифрования (LUKS) нет** |
| Сеть | локальный `192.168.0.93` за домашним роутером **D-Link DIR-615** со статическим белым адресом, проброс 80/443 |
| Соседи на машине | **чужое `fleetservice.service`** (системный `python3`, слушает `0.0.0.0:8443`) — трогать нельзя; GDM/gnome-remote-desktop (3389/3390), CUPS (631) |
| Файрвол | `ufw` включён; разрешены 22, 80, **443** (открыт 2026-09-16), 8443, 3389, 3390 |
| Каталоги | код — `/opt/ezbook/app` (клон `develop`), релизы фронта — `/var/www/ezbook/releases/<ts>` + симлинк `current`, бэкапы — `/var/backups/servicebooking` (root, 0700) |

Роутер **не разворачивает трафик на себя (нет hairpin NAT)** — машина не видит себя по публичному
имени. Это ломало отправку событий в трекер молча (Sentry-клиент не падает, он теряет события).
Обойдено двумя способами: `extra_hosts: errors.ezbook.ru:host-gateway` в `docker-compose.prod.yml`
(для контейнера) и строка в `/etc/hosts` (для скриптов на хосте).

**Что нашлось и было исправлено при развёртывании** — девять дефектов, каждый отдельным коммитом:

| Что было сломано | Симптом | Коммит |
|---|---|---|
| раздача `/uploads` шла из `wwwroot`, а запись — в `Storage:PublicRoot` | на чистом клоне каталога нет → всё отдавалось 404 навсегда | `3ec5dc8` (до диапазона) |
| `.dockerignore` вообще не был в репозитории (его прятал `.gitignore`) | в образ попадали локальные загрузки, исключение секретов не работало | `909dcc3` (до диапазона) |
| `try_files … /index.html` в `location /embed/` | внутренний редирект пересопоставлялся с `location /` → `/embed/` получал `X-Frame-Options: DENY` и `frame-ancestors 'none'`; **виджет не встраивался вообще, на 100% запросов**. Починено именованным `location @embed_fallback` | `017567b` |
| в стеке GlitchTip не было сервиса миграций | стек «поднимается успешно», логин отдаёт 500 `relation "users_user" does not exist` | `ec1fe75` |
| basic-auth стоял на всём поддомене, включая пути приёма событий | приложение получало 401 на каждое событие, панель выглядела как «ошибок нет» | `71c449a` |
| оба стека делили compose-проект `app` | контейнеры `postgres` схлопнулись, GlitchTip остался без своей базы; `down` одного стека снёс бы контейнеры другого | `125a72b` |
| машина не видела себя по публичному имени | события, алерты бэкапа и монитора терялись молча | `f1be54e` |
| деплой падал от любого постороннего файла в рабочем дереве | обрыв посреди прогона, уже после заливки фронта | `88f1ed2` |

Первые два дефекта исправлены **до** коммита из шапки прошлой редакции и в §8/§9 уже были описаны;
здесь они перечислены рядом, потому что относятся к одной находке — «на чистой машине не работает
то, что на машине разработчика работало».

- `docker-compose.prod.yml`: `postgres` (порт наружу не публикуется) + `api` на `127.0.0.1:5000`,
  два named volume — `api_uploads` → `/app/wwwroot/uploads` и `api_private_uploads` →
  `/app/private-uploads` (**персональные данные, бэкапить отдельно**), плюс ⭐ **bind-mount
  `./legal:/app/App_Data/legal:ro`** поверх черновика, запечённого в образ: правовые тексты меняются
  на хосте **без пересборки и без релиза** (`DEPLOY.md` §2.1). Health-check контейнера смотрит на
  `/api/health/live`, готовность (`ready`) проверяет скрипт деплоя.
  🆕 **Цикл 4 добавил сюда шесть `Notifications__*` переменных** (`EncryptionKey`, `KeyRotationAck`,
  `Provider`, `PartnerToken`, `WebhookToken`, `UnsubscribeKey`) и **записываемый** bind-mount
  `./state:/app/App_Data/state` (в отличие от `./legal`, который `:ro`). Именно туда приложение
  пишет отпечаток мастер-ключа и сверяется с ним на каждом старте. Named volume здесь **не годится
  намеренно**: `deploy-remote.sh` пересоздаёт контейнер на каждом деплое. Каталог `./state`
  (0700) должен существовать **до** первого `docker compose up`; `/state/` добавлен в `.gitignore`
  тем же правилом, что `/legal/` и `.env`.
- `deploy/nginx/ezbook.conf`: статика из `/var/www/ezbook/current` (симлинк на релиз), прокси `/api/`
  и `/uploads/` на `127.0.0.1:5000`, `client_max_body_size 6M`, TLS через `certbot --nginx`.
  `/swagger/` не проксируется. Заголовки: на уровне `server` — `Strict-Transport-Security`,
  `X-Content-Type-Options`, `Referrer-Policy`; в `location /` они **повторены намеренно** (nginx не
  наследует `add_header` между уровнями — об этом есть комментарий прямо в конфиге) плюс
  `X-Frame-Options: DENY` и полный `Content-Security-Policy` с исключениями под SmartCaptcha и
  `img-src … blob:` под приватные фото и выгрузку. `location /embed/` вынесен **отдельно и намеренно
  без анти-фрейминга** — виджет для того и существует, чтобы его встраивали; 🚀 его fallback теперь
  уходит в **именованный** `location @embed_fallback` (`rewrite ^ /index.html break`), а не в
  `try_files … /index.html`, потому что последний — внутренний редирект, который nginx
  пересопоставляет с `location /` и оттуда притаскивает framing-заголовки (коммит `017567b`).
  Обе стороны проверены живьём 2026-09-17: на `/` framing-заголовки строгие, на `/embed/test` их
  нет, HSTS/`nosniff`/`Referrer-Policy` есть везде.
  🆕 **Цикл 4 добавил маскирование токенов в access-логе nginx.** Два маршрута несут секрет прямо в
  пути: `/api/notifications/provider-webhook/{token}` (токен вебхука) и
  `/api/notifications/unsubscribe/{token}` (из него восстанавливается телефон клиента, то есть это
  ПДн, а не только креденшл). nginx пишет путь в лог **до** того, как запрос дойдёт до приложения,
  поэтому маскирование на стороне .NET его не покрывает. Сделано `map` + отдельный `log_format`
  (`notifications_masked`) и два отдельных `location` с тем же `proxy_pass` — **не** `access_log off`:
  выключение стёрло бы коды ответа и тайминги вебхука, а это единственный сигнал, когда сбоит сам
  GREEN-API.
- ⭐ **Деплой больше не собирает фронт на боевом сервере.** `deploy/deploy.sh` (на машине
  разработчика, требует `gh auth login`) скачивает артефакт `frontend-dist-<sha>`, который CI собрал
  **для этого же коммита**, кладёт его на машину новым каталогом `/var/www/ezbook/releases/<ts>/` и
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
  🆕 **В набор добавлен файл отпечатка ключа шифрования** (`notifications-key-fingerprint-<ts>.txt`)
  — он обязан ехать **в том же наборе, что и `.env`**: если восстановленный `.env` попадёт на машину
  без файла отпечатка, приложение просто запишет новый на первом старте, и защита «этот `.env` не от
  этой базы» **молча перестанет работать**. 🆕 `DEPLOY.md` §11.2 теперь **описывает восстановление
  `.env` и отпечатка** (шаг 3b) — прежняя претензия к раннбуку снята, но копия всё так же локальная
  (§9).
  ⚠️ **В копию входит и `.env`** (шаг 3b скрипта, файл `env-<ts>.txt`, права 0600) — без него дамп
  базы и тома бесполезны. Но: `DEPLOY.md` §11.1 в списке «что делает каждый прогон» этот шаг **не
  называет** (перечислено 6 шагов из 7), процедура восстановления §11.2 `.env` **не восстанавливает**,
  а комментарий в самом скрипте ссылается на «DEPLOY.md, инвентарь секретов» — раздела с таким
  названием в `DEPLOY.md` **нет** (§9.2 и §9.3).
  **Копия локальная, внешней нет** — см. §9.2 и §9.3. Восстановление прогонялось на этой машине 2026-09-17
  (заметка с фото → бэкап → удаление → восстановление → фото открывается); попутно исправлены две
  ошибки самой инструкции: имена томов (`app_api_*`, а не `ezbook_api_*` — при неверном имени docker
  молча создаёт пустой том и восстановление «успешно» ничего не восстанавливает) и требование
  выполнять шаги из-под `sudo -i`.
- ⭐ **Мониторинг:** `deploy/monitor/health-alert.{sh,service,timer}` — systemd-таймер на том же
  хосте дёргает `/api/health/ready`, после трёх подряд неудач шлёт письмо. Таймер поднят
  (`DEPLOY.md` §12.9). Ограничение прежнее и записано в раннбуке: **если вся машина недоступна,
  алерта не будет** — монитор крутится на проверяемой машине.
  `docker-compose.glitchtip.yml` — self-hosted **GlitchTip** (говорит по протоколу Sentry, поэтому
  синк `Sentry.Serilog` в API менять не нужно): четыре контейнера, отдельный стек со своими
  postgres/redis, наружу только через nginx. Выбран вместо self-hosted Sentry осознанно (~20
  контейнеров и 16 ГБ RAM против ~1 ГБ) — обоснование в шапке файла. 🚀 **Поднят и работает** на
  `errors.ezbook.ru`; появился одноразовый сервис `migrate` (схему БД не создаёт больше никто —
  ни `web`, ни `worker`), `web` стартует только после его успешного завершения, и стеку задано
  явное `name: glitchtip`. Отдельно в чек-листе зафиксировано: письмо из GlitchTip приходит на
  адрес **учётной записи в панели**, а не на ящик-отправитель; без правила оповещения в проекте
  события копятся, а письма не уходят.

**Fail-fast прод-конфигурации** живёт теперь в `Services/DeploymentSafetyChecks.cs` (вынесен из
`Program.cs` ради тестируемости — чистые статические методы, 🆕 **59 юнит-тестов**). В окружении Production
приложение **не стартует**, если `Jwt:Key` пуст/короче 32 символов/равен плейсхолдеру; если
`SuperAdmin:Password` пуст или равен `Admin12345`/`CHANGE_ME`; если `Storage:PrivateRoot` резолвится
внутри `wwwroot`; ⭐ если `Storage:PrivateRoot` резолвится внутри фактического `Storage:PublicRoot`
(или совпадает с ним); ⭐ если сам `Storage:PublicRoot` резолвится в content root приложения или выше
него (см. §5); ⭐ если не настроен `ForwardedHeaders:TrustedNetworks` (иначе rate limiting по IP
считал бы всех за один адрес docker-бриджа). Предупреждение без падения — `SuperAdmin:Phone` по
умолчанию. **На `isDraft` в `legal.json` fail-fast намеренно нет** (§9.7).

🆕 **Три новые проверки цикла 4**, все выполняются **до `Build()`** (чистые проверки конфигурации и
файловой системы, без DI):
- `ValidateNotificationSecrets` — при `Notifications:Provider=green-api` в Production обязаны быть
  заданы `EncryptionKey`, `PartnerToken`, `WebhookToken`, `UnsubscribeKey`; **нераспознанный
  `Provider` роняет старт всегда**. Часть правил работает даже в Development.
- `ValidateChannelKeyFingerprint` — сверяет отпечаток текущего мастер-ключа с файлом в
  `Notifications:KeyFingerprintPath`; **расхождение = отказ старта**, осознанная ротация
  подтверждается переменной `NOTIFICATIONS_KEY_ROTATION_ACK`. Единственное некритичное предупреждение
  этой проверки пишется **минимальным bootstrap-логгером Serilog** (консоль, JSON), потому что
  полный конвейер логирования на этот момент ещё не построен — раньше тут был `Console.WriteLine`,
  который не доходил ни до файла, ни до GlitchTip.
- `ValidateTimeZoneDatabase` — резолвит `Asia/Barnaul` (намеренно не более снисходительный
  `Europe/Moscow`); отсутствие tzdata в образе станет пойманным отказом деплоя, а не загадкой в
  рантайме. Параллельно в `Dockerfile` **явно доустановлен `tzdata`** — не потому, что базового
  образа не хватает сегодня, а чтобы не зависеть от того, что Microsoft его не вырежет.
⭐ **До коммита `909dcc3` файла `.dockerignore` в репозитории вообще не было** — он сам был
перечислен строкой в `.gitignore` и потому никогда не коммитился; любой чистый клон собирал образ
БЕЗ единого исключения, включая исключение секретов (`appsettings.Development/Production.json`).
Это важнее конкретного списка паттернов ниже, но для полноты: теперь `.dockerignore` закоммичен и
исключает `appsettings.Development.json`/`appsettings.Production.json`, оба каталога загрузок
(`ServiceBooking.API/wwwroot/uploads/**`, `ServiceBooking.API/App_Data/private-uploads/**`), рантайм-логи
Serilog (`ServiceBooking.API/logs/**`), артефакты тестовых прогонов (`**/TestResults`) и `frontend/`
(фронтенд собирается отдельной джобой CI, в образ API не входит).

**Секреты:** `.gitignore` исключает `**/appsettings.*.json` (кроме базового и `Testing`), `.env`,
`.env.production`, `.deploy.env` и ⭐ `/legal/` (каталог оператора на машине; в git лежит только
черновик `ServiceBooking.API/App_Data/legal/`). В git закоммичен только
`ServiceBooking.API/appsettings.json` с плейсхолдерами. **Но локально на машине разработчика лежат
незакоммиченные `appsettings.Development.json` и `appsettings.Production.json` с настоящими
секретами** (боевой пароль Postgres, JWT-ключ, серверный ключ SmartCaptcha) — их нельзя случайно
`git add -f`. 🚀 Появился второй носитель боевых секретов — **`.env` в `/opt/ezbook/app` на самой
машине** (`POSTGRES_PASSWORD`, `JWT_KEY`, `SUPERADMIN_PASSWORD`, `SMARTCAPTCHA_SECRET_KEY`, `SENTRY_DSN`,
`GLITCHTIP_*`, 🆕 `NOTIFICATIONS_ENCRYPTION_KEY`, `NOTIFICATIONS_KEY_ROTATION_ACK`,
`NOTIFICATIONS_PROVIDER`, `NOTIFICATIONS_PARTNER_TOKEN`, `NOTIFICATIONS_WEBHOOK_TOKEN`,
`NOTIFICATIONS_UNSUBSCRIBE_KEY`). В GitHub он не попадает никогда; в локальный бэкап попадает; вне машины
существует только в менеджере паролей оператора (§9.3).
🆕 ⚠️ **`NOTIFICATIONS_ENCRYPTION_KEY` — не рядовой секрет.** Остальные значения можно
перевыпустить (`openssl rand`), заплатив разлогиниванием пользователей или перевыпуском ключа во
внешнем кабинете. Этот — **нет**: он расшифровывает токены WhatsApp-экземпляров **чужих салонов**,
его потеря необратимо уносит все подключённые каналы. Поэтому в `DEPLOY.md` он вынесен **отдельной
строкой** инвентаря секретов, а не в общий список ротируемых.

---

## 9. Технический долг и риски (по убыванию приоритета)

⚠️ **Нумерация сместилась относительно прошлой редакции.** Блок P0 переписан целиком (было 4
пункта, стало 9), поэтому все последующие пункты сдвинулись **на +5**: бывший §9.5 стал §9.10,
бывший §9.25 — §9.30. Ссылки внутри этого документа обновлены; ссылки из чужих документов на §9.N
надо перечитывать с учётом сдвига.
🚀 — пункт появился, переформулирован или закрыт по итогам первого реального развёртывания
(`6369266..0e61369`).

🆕 **Нумерация 1–30 ниже СОХРАНЕНА от прошлой редакции** — цикл 4 добавил свой блок с буквенными
номерами (P0-A…P0-E), чтобы ссылки вида «§9.17» из других документов остались валидными.

---

**🆕 P0-цикл-4 — почему функция уведомлений написана, но не выпущена**

**A. Функция выключена тарифно и наружу не предлагается.** `AllowNotificationChannel = false` у всех
планов, включая новые (решение заказчика Q1); цена опции (`notifications.channel.price-per-month`)
не задана, а её отсутствие означает «опция не предлагается», а не «бесплатно». Суперадмин включает
флаг вручную при выпуске. **Для QA: сразу после выката «опция никому не предлагается» — ожидаемое
состояние, а не дефект.** Что блокирует выпуск:
  1. **правовая оценка по 41-ФЗ** не проведена;
  2. **партнёрского аккаунта GREEN-API нет** — реального экземпляра не создавалось ни разу;
  3. **P0-3 (восстановление `.env` из бэкапа)** — см. ниже, цена вопроса выросла качественно;
  4. **справочник городов неполон (91 запись) и нет админского способа добавить город** —
     единственный путь сейчас новая миграция.

**B. US-34 «уведомления персоналу» не реализована** — отложена осознанно, была **первой в порядке
урезания**. В коде остались члены перечисления, тексты и дефолтные шаблоны для
`StaffBookingCreated`/`StaffBookingCancelled`, но никто их не ставит в очередь (§5.1); эндпоинт из
`API_CONTRACT_CYCLE4.md` §35 не существует.

**C. Правовые тексты — заглушки.** Текст о рисках подключения WhatsApp — «рыба» с версией
`2026-09-18-draft` (`Services/NotificationRiskText.cs`), ждёт вычитки юристом. Механика принятия
версии при этом настоящая и работает. Отдельно: политика и оферта в `App_Data/legal/` остаются
черновиком `2026-09-08-draft` (§9.7) и **уведомления в WhatsApp никак не покрывают** — про отправку
сообщений клиенту в них ничего не сказано.

**D. Задача T4-D3 не выполнена: сетевой контур на боевой машине после выката не проверялся.**
Спайк показал, что `api.green-api.com` с машины достижим, но **глобального IPv6 на ней нет**, и без
keep-alive и упорядочивания адресов каждое новое соединение стоит ~5 секунд. Лечение **в коде есть**
(`GreenApiHandlerFactory` — keep-alive, `PreferIPv4` — IPv4-first `ConnectCallback`, настройки
`ConnectPreference`/`ConnectTimeoutSeconds`/`PerAddressConnectTimeoutSeconds`), покрыто юнит-тестами,
но **живой проверки после выката не было**.

**E. 🧠 Урок про тестовую инфраструктуру — записан, чтобы не повторять.** Три падения CI подряд в
конце цикла (коммиты `aae3541`, `7a36543`) вызваны **молча общими ресурсами между тестами**, и все
три давали зелёное локально и красное в CI:
  1. **база, не пересоздававшаяся под новую коллекцию** — `TestDatabaseFixture` чистит её один раз,
     а новая коллекция `"NotificationDispatch"` поднимает свой хост поверх той же базы;
  2. **счётчик отправок, считавший чужие строки** — `RecordingTransport` регистрируется один раз на
     хост, а соседние тесты оставляли в общей базе pending-строки, и проход уносил чужую за бюджет;
     исправлено фильтрацией по своему номеру телефона (приём, который соседний тест уже применял);
  3. **аккаунт суперадмина, «застолбленный» фабрикой правовых тестов** — она смотрела на одноразовый
     манифест со случайной версией, но **использовала тот же телефон суперадмина**, что и все
     остальные фабрики; чья коллекция стартовала первой на чистой базе, та и решала, с какой версией
     документов согласен общий суперадмин — после чего **весь остальной набор** получал 451. Какой
     именно тест за это платил, зависело только от порядка.
  Попутно: две проверки сравнивали `DateTime` на точное равенство со значением, прошедшим через
  Postgres (микросекунды против тиков) — на машине разработчика совпадало, в CI нет.

---

**P0 — эксплуатационные ограничения работающей системы**

🚀 **Блок «выглядит готовым, но вживую не проверялось» из прошлой редакции закрыт фактом
развёртывания и удалён.** Что именно перестало быть риском: живой деплой (выполнен, включая деплой
по кнопке и оба отказа прод-гварда), откат и обратная выкатка, восстановление из бэкапа, живой
GlitchTip с цепочкой до письма, CSP на всех трёх чувствительных сценариях (выгрузка через `blob:`,
SmartCaptcha, приватное фото), сосуществование с `fleetservice.service` на 8443, отсутствие
framing-заголовков на `/embed/`. Чек-лист `DEPLOY.md` §16 — семь пунктов из семи закрыты, дата и
результат проставлены у каждого. То, что осталось, — ниже, и это уже не «не проверено», а
«проверено и является ограничением».

1. 🚀 **Запас по памяти исчерпан: 3,3 ГиБ ОЗУ, свободно ~1 ГБ, 492 МиБ уже в подкачке.** На машине
   одновременно живут: стек приложения (`postgres` + `api`), стек GlitchTip (свои `postgres`,
   `redis`, `web`, `worker`), nginx, GNOME/GDM desktop-сессия и **чужой `fleetservice.service`**.
   Любое добавление сервиса (второй экземпляр, кеш, очередь, ещё один контейнер) требует расчёта
   памяти заранее, а не «попробуем и посмотрим»: OOM-killer на этой конфигурации выберет жертву
   сам, и ею может оказаться чужой сервис или Postgres. GlitchTip и выбирали-то вместо self-hosted
   Sentry именно по этому критерию (~1 ГБ против ~16 ГБ) — запас был съеден уже тогда.
2. 🚀 **Бэкап только локальный; внешней копии нет. При потере машины теряется всё.**
   `deploy/backup/backup.sh` кладёт дампы в `/var/backups/servicebooking` **на той же машине**, где
   лежат оригиналы. Защищает от порчи данных и ошибки оператора, **не защищает** от утраты машины
   (пожар, кража, смерть диска — диск ещё и **не зашифрован**, LUKS нет). Решение заказчика
   (SPEC R13), в `backup.sh` есть готовая заглушка `upload_offsite()` под будущую доработку. Тем же
   свойством страдает мониторинг: `health-alert.sh` крутится на проверяемой машине.
3. 🚀🆕 **Конфигурация (`.env`) при потере машины невосстановима — и теперь от неё зависят учётные
   данные ЧУЖИХ аккаунтов WhatsApp.** 🆕 **Цена вопроса выросла качественно, и это прямо названо
   блокирующим выпуск функции уведомлений (P0-цикл-4, пункт A).** Что изменилось к лучшему: раздел
   «Инвентарь секретов» в `DEPLOY.md` **появился** (раньше `backup.sh` ссылался вникуда), §11.2
   теперь **описывает восстановление `.env` и файла отпечатка ключа** (шаг 3b, с предупреждением
   восстанавливать их **одним набором**), а сам отпечаток попал в бэкап. Что **не** изменилось:
   **копия по-прежнему лежит на той же машине**, внешней нет. В `.env` лежат `Jwt:Key` (его потеря =
   разлогинивание всех), пароль Postgres, пароль и секрет GlitchTip, серверный ключ SmartCaptcha и
   🆕 **`NOTIFICATIONS_ENCRYPTION_KEY`, потеря которого необратимо уносит WhatsApp-каналы всех
   салонов**. **Часть значений выдаётся внешними кабинетами и заново берётся только оттуда.**
   Единственный реальный носитель вне машины — менеджер паролей оператора, и то, что он заполнен,
   документом не подтверждается.
4. 🚀 **RDP торчит в интернет: порты 3389/3390 открыты в `ufw` и проброшены роутером.** Это
   пред-существующая настройка машины (заказчик пользуется удалённым рабочим столом), риск принят
   **осознанно**, но до этой редакции нигде как принятый риск зафиксирован не был — `DEPLOY.md`
   упоминает эти порты только в списке занятых и в объяснении, что их «настраивал кто-то осмысленно».
   Машина с персональными данными клиентов салонов (телефоны, фото к заметкам) доступна снаружи не
   только по 22/80/443.
5. 🚀 **Репозиторий публичный, и в нём лежат раннбуки с описанием инфраструктуры.** `DEPLOY.md`
   (~111 КБ) называет домены, внутренний адрес машины, модель роутера, занятые порты, имена
   пользователей (`ezbookdeploy`), пути (`/opt/ezbook/app`, `/var/backups/servicebooking`), схему
   доступа и точные команды. Секретов там нет — но карта есть. **Решение сделать репозиторий
   публичным заказчиком не подтверждалось; вопрос открыт** и никем не закрыт.
6. 🚀 **Имя compose-проекта приложения выводится из имени каталога.** В
   `docker-compose.glitchtip.yml` имя задано явно (`name: glitchtip`) — именно после того, как оба
   стека получили проект `app` и схлопнулись по именам сервисов. В `docker-compose.prod.yml` имя
   **намеренно не задано**: оно и так `app` (по каталогу `/opt/ezbook/app`), и существующие тома
   называются `app_postgres_data`, `app_api_uploads`, `app_api_private_uploads`. Последствие:
   **переименование или перенос каталога приведёт к созданию новых пустых томов** — docker не
   ошибётся, он молча создаст пустое, и приложение поднимется с чистой базой. Ровно эта ловушка уже
   сработала на процедуре восстановления (искали `ezbook_api_*`, а тома — `app_api_*`).
7. 🚀 **Правовые тексты — ЧЕРНОВАЯ редакция, юрист их не вычитывал, и система уже работает.**
   `App_Data/legal/legal.json`: `"isDraft": true`, версии `2026-09-08-draft`. Fail-fast на черновик
   в Production **намеренно отсутствует** (решение заказчика). Отличие от прошлой редакции в том,
   что сайт теперь открыт: **каждая регистрация фиксирует согласие именно с черновиком**, и эти
   записи останутся такими после вычитки — `UserConsent` перезаписывается только при новом принятии.
   Видимость черновика обеспечена только плашкой в UI и первым абзацем самого текста.
8. 🚀 **Релиза не было: `master` отстаёт, тегов нет, «прод» формально не существует.** Развёрнута
   ветка `develop`, деплой выполнялся workflow `deploy-staging.yml`, то есть всё описанное выше —
   **стенд**, хотя и на публичном домене с настоящим TLS. `deploy-production.yml` нацелен на **ту же
   физическую машину** (второй машины нет): если кто-то создаст тег на `master` раньше решения о
   релизе, прод-воркфлоу технически выкатится на железо стенда. Единственные защиты — организационная
   (тег не создаётся) и required reviewer в environment `production`. Прямо записано в `DEPLOY.md` §9.
9. 🚀 **Локальные правки в `/opt/ezbook/app` не переживают деплой.** Форс-команда делает
   `git checkout --force` + `git clean -fd` и приводит дерево ровно к выкатываемому коммиту.
   Это **осознанное решение** (альтернатива — обрыв деплоя посреди прогона, уже после заливки
   фронта), отбрасываемое печатается в лог прогона до переключения, а `.env` и `legal/` не
   затрагиваются (они в `.gitignore`, а `clean` идёт без `-x`). Но аварийная правка «на месте»
   исчезнет без предупреждения при следующем нажатии кнопки.

**P1 — влияет на безопасность или корректность данных**

10. ⭐ **Смена номера телефона не подтверждается ничем, кроме текущего пароля.**
   `POST /api/profile/change-phone` сразу присваивает новый номер. Риск конкретный: гостевые визиты
   и заметки о клиенте ищутся по номеру, поэтому, указав чужой номер, можно прочитать чужую
   гостевую историю. Отложено **до появления SMS-канала**; решение описано в
   `SPEC_DEFERRED_NOTIFICATIONS.md` (пункт Д-1) и комментарием в `ProfileController.ChangePhone`
   (строка 151).
11. **Слабые дефолты в закоммиченном `appsettings.json` никуда не делись**
   (`Jwt:Key = "CHANGE_ME_…"`, `SuperAdmin:Password = "Admin12345"`, `SuperAdmin:Phone = "+70000000000"`).
   В цикле 3 fail-fast вынесен из `Program.cs` в чистый `Services/DeploymentSafetyChecks.cs`
   (`ValidateSecrets`, `ValidateTrustedNetworksConfigured`) и **покрыт юнит-тестами** (28 запусков),
   а CI-джоб `docker-build` стартует образ именно в `Production` — то есть проверка теперь сама под
   тестом. Остаточный риск **прежний**: в **не**-Production окружениях (стенд с
   `ASPNETCORE_ENVIRONMENT=Staging`) проверка не срабатывает вовсе.
12. 🆕 **Часовые пояса введены ЧАСТИЧНО — только там, где без них не работали уведомления.**
   Прежняя формулировка «полное отсутствие работы с часовыми поясами» больше не верна: у компании
   появились `CityId`, `TimeZoneId` и `TimeZoneIsManual`, есть справочник городов и чистый
   `CompanyTimeZoneResolver`, а расчёт «когда слать напоминание» (`NotificationTiming`) считает в
   зоне компании. **Но сама модель записи не изменилась:** `Booking.Date/StartTime/EndTime` — всё те
   же `DateOnly`/`TimeOnly` без TZ, слоты и расписание считаются без зоны, все контейнеры живут в UTC
   (`DEPLOY.md` §13, `README.md`). То есть зона сейчас — **свойство уведомлений, а не свойство
   расписания**, и источник ошибок «на границе суток» в ядре бронирования остаётся.
   Побочное следствие: **справочник городов неполон (91 запись) и пополняется только миграцией** —
   админского способа добавить город нет.
13. **Security-заголовки: Linux-контур закрыт, Windows-контур — нет.** `deploy/nginx/ezbook.conf`
   теперь отдаёт `Strict-Transport-Security`, `X-Content-Type-Options`, `Referrer-Policy`,
   `X-Frame-Options: DENY` и полноценный `Content-Security-Policy` (с явными исключениями под
   SmartCaptcha и `blob:` для приватных фото), причём `location /embed/` намеренно оставлен без
   анти-фрейминга. В **Windows/IIS-контуре** (`frontend/public/web.config`, `DEPLOY-windows.md`)
   заголовков нет вообще — этот контур **выведен из скоупа цикла 3 решением заказчика**, но
   `DEPLOY-windows.md` из репозитория не удалён, и по нему всё ещё можно развернуть систему без защиты.
14. **Юридический риск фотофиксации принят, но не снят.** Общий правовой контур появился (§4.14), но
   **согласия клиента на фотосъёмку в интерфейсе по-прежнему нет** — ни чекбокса, ни дисклеймера, ни
   хранения факта: решение Q6 цикла 2 остаётся в силе. Согласие на политику и оферту,
   которое даёт клиент, фотосъёмку **не покрывает**. Ответственность переложена на компанию-салон
   текстом в README/`docs/faq.md`.

**P2 — код без тестов, на который многое завязано / хрупкие места**

15. ⭐ **Сторож гонки согласия — вероятностный.** Тест `LEG-036` (`LegalConsentTests.cs`) ловит гонку
    «одновременное принятие согласия», и его красноту подтверждали **на 20 итерациях**, а в репозиторий
    закоммичен **одиночный прогон**. То есть зелёный LEG-036 в CI не доказывает отсутствия регрессии —
    он лишь не поймал её в этот раз.
16. ⭐ **`Booking.ClientDeleted` фактически мёртв в UI.** Бэкенд проставляет флаг при удалении
    аккаунта, он доезжает до фронта и объявлен в `frontend/src/types/index.ts` (строка 103) — и это
    **единственное** его упоминание во всём фронтенде (проверено grep'ом). Ни одна страница его не
    отображает: персонал не видит, что клиент удалился.
17. ⭐ **Пагинация `GET /api/masters/clients` работает в памяти.** Контроллер материализует весь
    список клиентов компании, фильтрует по `search` и режет `Skip/Take` там же, а не в SQL.
    Признано приемлемым ревьюером (список ограничен одной компанией) и **задокументировано
    комментарием в коде** — но с ростом базы клиентов это первый кандидат на деградацию.
    Остальные три выборки (`admin/users`, `admin/companies`, публичные отзывы) пагинируются в БД.
18. **Фронтенд покрыт точечно.** 🆕 100 тестов Vitest на ~10 000 строк TSX, и **весь прирост цикла 4
    (+22) — это утилиты**, ни одного теста на новые экраны. Покрыты мапперы ошибок,
    `formatPhone`, `timezone`, `channelBanner`, `PhotoGallery`, `Pagination`, `useDebouncedValue`,
    правовой контур (`ConsentGate`, `LegalUpdateBanner`, `LegalDocumentPage`) и по одному тесту на
    `CompanyPage` и `MasterClientsPage`. **Не покрыты**: `BookingModal`, `ManualBookingModal`,
    `RescheduleModal`, календарь `ScheduleTab`, вкладочные страницы кабинета и админки,
    `DeleteAccountPage`, `useExportData`, `useAuthedImage`, 🆕 **весь раздел уведомлений**
    (`NotificationsSection` и три вкладки, `QrModal`, `AssignCompanyDialog`, `RiskAcceptanceModal`,
    `ChannelBreachBanner`, `NotificationsAdminTab`, `UnsubscribePage`, `CityCombobox`).
19. **`CompaniesController` — 635 строк** (было 571) и 16 эндпоинтов, включая логику подписок,
    загрузку файлов, квоту фото и целиком сборку статистики (`GetStats`, ~70 строк агрегаций
    **в памяти** после `ToListAsync()`). Контроллер продолжает расти.
20. **Логика прав по-прежнему размазана по приватным копиям** `CanManageCompany`/`CanManage`, хотя их
    «членская» половина унифицирована через `CompanyMembership`. `MailingController` **до сих пор**
    не переведён на общий хелпер — единственное оставшееся исключение.
21. **Перечитывание ролей и сверка `SecurityStamp` на каждом запросе** (`Program.cs`,
    `OnTokenValidated`) — дополнительный запрос к БД на каждый аутентифицированный вызов без кеша.
    Цикл 3 добавил туда же чтение состояния согласия (claim'ы `consent_*` сверяются с актуальной
    версией документа), то есть путь на каждом запросе стал длиннее, а не короче.
22. **Шаг сетки слотов по-прежнему захардкожен 30 минутами** (`SlotCalculator.StepMinutes`).
23. **Миграция `NormalizePhoneNumbers` необратима и не проверялась на реальных данных.** Сейчас это
    безопасно (боевых данных нет), но повторно применить её к живой базе будет нельзя.
    Рядом появилась вторая миграция с данными — `ResyncIdentityRoles`, у неё **`Down` — no-op**
    (осознанно: откат пересчёта ролей бессмыслен).
24. **Ограничитель `PermitLimit` читается из конфигурации на каждый запрос** через
    `ctx.RequestServices.GetRequiredService<IConfiguration>()` — приём из цикла 2 сохранён и
    распространён на четыре новые политики.

🆕 **P2, добавленное циклом 4:**

24a. **Отправщик — самый большой новый код, никогда не работавший против настоящего провайдера.**
    `NotificationDispatchTask` (~464 строки) и `ChannelHealthTask` (~405) покрыты тестами против
    заглушек и фейковых часов; живого GREEN-API не видел ни один прогон. Всё, что касается реальных
    таймаутов, реальных кодов ошибок и реального поведения WhatsApp при бане, **проверено только по
    документации провайдера**.

24b. **Два перечисления стали хрупкими по своим числовым значениям.** `NotificationStatus.Pending`
    обязан остаться `0` (частичный индекс диспетчера объявлен сырым SQL `"Status" = 0`), а значения
    `NotificationType` — позиции битов в `EnabledTypeMask`. Перестановка члена не ломает сборку и не
    роняет тест на ровном месте: индекс останется, запрос молча уедет в full scan, а маска молча
    сменит смысл. Оба риска **зафиксированы XML-комментариями прямо на перечислениях** — но держатся
    только на том, что их прочитают.

24c. **Мастер-ключ шифрования — единственная точка отказа для всей функции.** Его потеря или подмена
    не деградирует систему частично: все сохранённые токены становятся нечитаемы, каналы уходят в
    `NeedsReconnect` с причиной `SecretUnavailable`, и каждому салону придётся заново проходить
    привязку по QR. Защит две (отпечаток + отказ старта, обязательное подтверждение ротации), обе
    **обнаруживают** проблему, но ни одна её не **восстанавливает**.

24d. **Тесты уведомлений делят ту же базу `servicebooking_test`, что и остальные 400+.** Отдельная
    коллекция `"NotificationDispatch"` отключает параллелизм у себя, но база одна на весь прогон, и
    новая фабрика поднимает поверх неё свой хост. Именно это уже дало три падения CI (P0-цикл-4,
    пункт E). Конструкция осталась — изменились только тесты, которые стали фильтровать своё.

24e. **`AdminController` и `CompaniesController` продолжили расти:** админский контроллер получил
    +211 строк (каналы, оплаты, параметры платформы), компании — +125 (города и зоны). Сервисного
    слоя по-прежнему нет.

**P3 — эксплуатация, гигиена, недоделки**

25. ⭐ **Шага `dotnet format` в CI нет.** `.editorconfig` появился (US-50) и **описывает уже
    существующий стиль**, а не задаёт новый, но `dotnet format --verify-no-changes` в CI не добавлен:
    сухой прогон даёт несколько сотен предсуществующих расхождений по переносам (в основном в
    `ServiceBooking.Tests`). Массовое переформатирование отложено отдельным коммитом — причина
    записана комментарием в шапке самого `.editorconfig`. ESLint в CI, наоборот, **добавлен**
    (`npm run lint` отдельным шагом).
26. **Устаревшие зависимости фронта с известными уязвимостями (`axios`, `form-data`, `react-router`)
    не закрыты.** Версии в `frontend/package.json` те же, что и до цикла 3 (`axios ^1.7.7`,
    `react-router-dom ^6.26.2`); `npm audit fix` закрывает часть без мажора, `react-router` требует
    мажорного апгрейда.
27. **Фичи, выглядящие готовыми в UI, но не работающие по сути** (🚀 теперь это видят живые
    пользователи на `ezbook.ru`, а не только разработчики)**:** «Рассылка» (писем нет, текст
    «Рассылка поставлена в очередь» **осознанно оставлен вводящим в заблуждение**, решение Q9),
    предоплата (платежей нет), `NotifyDaysBefore` в редакторе тарифов (уведомлений нет), обещание
    «напоминание накануне визита» на `HomePage.tsx` (Q8). README и `docs/faq.md` про это пишут
    честно — интерфейс нет.
    🆕 **Уведомления в этот список не попадают, и это важное отличие:** они не «выглядят рабочими, но
    не работают», а **не показываются вовсе**, пока суперадмин не включит тарифный флаг и не задаст
    цену. Обещание «напоминание накануне визита» на `HomePage.tsx` теперь имеет за собой код — но до
    выпуска опции оно по-прежнему невыполнимо.
28. **Локальные загруженные файлы не воспроизводимы на чистом клоне.** `wwwroot/uploads/**` и
    `App_Data/private-uploads/**` — в `.gitignore`; на машине разработчика в приватном каталоге лежат
    ~36 папок компаний с реальными JPEG. На свежем клоне ссылки из дампа БД будут битыми.
29. **Валидация DTO неполна** (`Slug`, `Bio`, `Comment`, `SendMailDto.Message`), нет запрета удалять
    последнего владельца, нет проверки статуса в `MarkPaid` — явно отложено как некритичное.
30. **`frontend/design_handoff_site_redesign/`** (10 HTML-макетов) лежит внутри `frontend/`, в сборку
    не идёт; **пустой каталог `frontend/src/components/auth/`** всё ещё на месте; два `.example`-файла
    прод-конфига (`ServiceBooking.API/appsettings.Production.json.example` и `.env.production.example`)
    описывают один и тот же прод двумя способами, актуален второй.

🚀 **Закрыто первым реальным развёртыванием** (не переоткрывать): «живого деплоя не было ни разу»;
«GlitchTip вживую не поднимался»; «цепочка ошибка → трекер → письмо не проверялась»; «CSP на
скачивании выгрузки не проверялся живьём» (проверены все три чувствительных сценария, не только
выгрузка); «откат не выполнялся на реальном systemd/nginx» (проверена и обратимость — выкатка
вперёд после отката); «восстановление из бэкапа не прогонялось на этой машине»; «неизвестно,
переживёт ли `fleetservice.service` установку docker/nginx» (переживает, отвечает как раньше);
«деплой по кнопке не проверен сквозь весь путь» (включая оба отказа прод-гварда и проверку
форс-команды снаружи по отдельным ключам). Побочно закрыты две дыры доступа, найденные при этом:
`ezbookdeploy` состоял в группе `sudo` (то есть имел `ALL : ALL`, и точечные `NOPASSWD` были
бессмысленны), и в его `authorized_keys` по ошибке попал личный ключ **без** префикса `command=`.

🆕 **Закрыто циклом 4** (не переоткрывать без причины): «интеграции с мессенджерами нет»;
«полное отсутствие часовых поясов» — закрыто **частично и только для уведомлений** (см. §9.12,
формулировка изменена, а не снята); «`DEPLOY.md` §11.1 не называет копирование `.env`, а §11.2 его
не восстанавливает» и «`backup.sh` ссылается на несуществующий раздел инвентаря секретов» — раздел
появился, процедура восстановления `.env` и отпечатка описана; «README и CHANGELOG отстали от факта
развёртывания» — закрыто коммитом `d4137dd`; «раннер `ScheduledTaskRunner` в тестах выключен
целиком, его собственное поведение проверить нечем» — закрыто `NotificationDispatchTestFactory`.

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
Всё перечисленное лежит в репозитории.

🆕 **Расхождение продуктовой документации с фактом развёртывания, о котором предупреждала прошлая
редакция, ЗАКРЫТО** коммитом `d4137dd`. Продуктовое описание цикла 4 вносилось в `README.md` и
`CHANGELOG.md` параллельно, product-analyst'ом, — **эта работа завершена коммитом `7ab28b2`**
(последним в репозитории), так что оба файла актуальны и описаны ниже по факту их текущего
содержимого, а не «в процессе».

### 10.1 Краткая продуктовая документация

| Что | Путь | Формат | Структура |
|---|---|---|---|
| Обзор продукта | `README.md` (🆕 ~24 КБ) | Markdown, русский | `## О проекте` (внутри жирными врезками «Для кого», «Роли», **«Что умеет»**, **«Чего пока нет»** — честный список отсутствующего: платежи, **работающие у клиентов уведомления** (канал WhatsApp есть в коде, опция выключена), **уведомления персоналу**, **полный справочник городов и админский способ завести город (95 из ~300)**, письма, самостоятельная оплата тарифа/опции, подтверждение смены телефона кодом, вычитанные юристом правовые тексты, клиентский просмотр фото, согласие на съёмку). 🆕 Коммит `7ab28b2` дописал в «Что умеет» пункт про город компании и большой пункт про WhatsApp-уведомления с пометкой «реализованы, но у клиентов пока не включены» → **`## Запуск`** (`### Локально, всё в Docker`, `### Локально, без Docker для API`, `### Переменные окружения и секреты`, `### CI`, `### Деплой`). В цикле 3 README вырос эксплуатационной половиной: врезки «Бэкапы» (и прямо — что копия локальная) и «Часовые пояса» (UTC везде) |
| Changelog | `CHANGELOG.md` (🆕 ~69 КБ) | Markdown, русский, по мотивам Keep a Changelog | **По датам завершения цикла, самая свежая запись сверху**; номеров версий в проекте нет. 🆕 **Самый верхний раздел — `## Не выпущено — уведомления клиенту о записи в WhatsApp`** (дописан коммитом `7ab28b2`, +107 строк): туда кладётся принятое командой, но не выкаченное на работающий адрес; дату раздел получает в момент фактического выката. Внутренняя структура записи, заданная этим коммитом и служащая образцом для следующих: «Что появилось» → «Чего эта функция не гарантирует — читать обязательно» → «Почему функция не выпущена» (четыре блокера, ни один не дефект кода) → «Сознательно отложено — это решение, а не забытое» (US-34, справочник городов) → «Ломающее изменение» (компанию нельзя создать без города) → «Решения, отменённые по ходу цикла» → «Добавлено для качества» → «Осталось как было — и по-прежнему не работает» → «Для тех, кто обращается к API напрямую». Ниже — датированные записи (`2026-09-17 — сервис впервые развёрнут`, `2026-09-15`, `2026-09-07`, …) |

GitHub Releases / wiki в проекте не используются. 🆕 Оба файла приведены в соответствие с
реальностью коммитом `d4137dd` и дописаны по итогам цикла 4 коммитом `7ab28b2` — **на момент этой
редакции они актуальны**, расхождений README/CHANGELOG с кодом не выявлено.

🆕 Пользовательская документация в `docs/**` циклом 4 **не менялась** — раздела про уведомления в
WhatsApp там пока нет (проверено: ни в диапазоне цикла, ни в последнем коммите `7ab28b2` ни один
файл `docs/` не тронут; в каталоге по-прежнему девять файлов).

Отдельно, не продуктовая, но постоянно нужная документация: **`DEPLOY.md`** (🆕 ~129 КБ, markdown,
русский) — эксплуатационный раннбук машины. Структура: «Целевая машина (факты)» → «Чек-лист всей
установки» → §0…§15 пошаговые разделы (каждый шаг с блоком «Должно получиться») → **§16 чек-лист
первого запуска с датами и вердиктами** → **«Почему так сделано»** (объяснительная часть, вынесена
в конец: почему docker не из snap, почему бэкап локальный, почему `ezbookdeploy` не root, почему
CSP устроена так, какую ветку катим и почему AlmaLinux-вариант не сохранён параллельно).
🆕 Цикл 4 дописал сюда (+192 строки): раздел **«Принятые риски и где лежат секреты»** с подразделом
**«Инвентарь секретов»** (§55), отдельную строку про `NOTIFICATIONS_ENCRYPTION_KEY` и процедуру его
ротации, создание каталога `state/` до первого запуска и шаг 3b восстановления `.env` + отпечатка
ключа в §11.2.

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
| Справочник эндпоинтов | `API_DOCUMENTATION.md` (🆕 ~247 КБ) | Markdown, русский | §1 Обзор → §2 Аутентификация → §3 Ключевые бизнес-концепции (**§3.11 «Конверт `PagedResult<T>`»**) → **§4 Справочник эндпоинтов** (основной объём) → §5 Сквозные сценарии (curl-рецепты) → §6 Справочник кодов ответа → **§7 Известные ограничения**. 🆕 **Обновлён в цикле 4**: добавлены `§4.3a GET /api/cities` и большой **§4.15 «Notifications (WhatsApp)»** с подразделами «Каналы владельца», «Настройки и шаблоны компании», «Журнал доставки и отметка в записи», «Отписка и вебхук», «Админка». Оба новых раздела **явно помечены «недоступно в текущем релизе»** |
| Контракт цикла 3 | `API_CONTRACT.md` (~51 КБ) | Markdown, русский | разделы **до 19**: контракт цикла 3, коды ошибок (включая 451) |
| 🆕 Контракт цикла 4 | **`API_CONTRACT_CYCLE4.md`** (~40 КБ) | Markdown, русский | **разделы 19–37, продолжение предыдущего файла, нумерация не пересекается**. §19 общее для всех эндпоинтов цикла → §20–27 каналы → §28–30 настройки/шаблоны/журнал → §31 город и часовой пояс → §32–33 отписка и вебхук → §34 админка → §35 (эндпоинт US-34, **срезан, в коде его нет**) → §36 сводка новых и изменённых эндпоинтов → §37 чек-лист согласования BE↔FE |

**OpenAPI/Swagger-файла в репозитории нет** — схема генерируется Swashbuckle во время работы и
доступна только в Development (`/swagger`). Postman-коллекции нет. Генерации TS-типов из схемы нет.

### 10.4 Описания тест-кейсов

**`TEST_CATALOG.md`** (🆕 ~270 КБ), Markdown, русский — человекочитаемое описание **каждого**
автоматизированного кейса, отдельно от самого кода тестов.

🆕 **Цикл 4 добавил раздел `## Notifications (US-27…US-63)`** с подразделами по файлам и указанием
**коллекции**, в которой живёт каждый набор (это существенно — см. §9, урок про общие ресурсы):
`NotificationChannelsTests` (`NTF-C001…C018`, своя фабрика на тест), `NotificationQueueingTests`
(`NTF-Q001…Q004`, коллекция `"Api"`), `NotificationWebhookUnsubscribeTests`
(`NTF-W*`/`NTF-U*`/`NTF-L*`), `NotificationDispatchTests` и `NotificationDispatchExtraTests`
(`NTF-D01…D05`, коллекция `"NotificationDispatch"`), `NotificationCitiesTimeZoneTests`
(`NTF-G001…G006`). В конце раздела — **явный подраздел «Не покрыто функциональными тестами этого
прогона (зафиксировано, не блокер)»**.

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
- ⚠️ **Раздел каталога «Документация, не обновлённая вместе с кодом» (строка 2237) устарел дважды.**
  Он утверждает, что `API_DOCUMENTATION.md` не тронут «в этом цикле» ни одной строкой. Речь про
  **цикл 3**, и уже тогда это перестало быть правдой (коммит `90a69b1`); 🆕 в цикле 4 справочник
  обновлён ещё раз (+116 строк). Замечание из каталога так и не убрали — читать его как
  актуальное нельзя.

Отдельного `TESTPLAN.md`, каталога `docs/testing/` или ручных сценариев вне `TEST_CATALOG.md`
в проекте **не найдено**.

🚀 **Ручной чек-лист живых проверок теперь существует** — это `DEPLOY.md` §16 «Первый запуск на этой
машине — что проверить вживую». Формат: markdown-чеклист (`- [x]`), семь пунктов, у каждого дата,
вердикт (`ПРОШЛО` / `НЕ ПРОШЛА` + повторная проверка после фикса) и описание найденного дефекта.
**Все семь закрыты, незакрытых ноль.** Это единственный в проекте документ с описанием *ручных*
сценариев; `TEST_CATALOG.md` описывает только автоматизированные кейсы. Отдельного набора
e2e/браузерных автотестов (Playwright, Cypress и т.п.) в репозитории **нет** — соответствующего
пакета в `frontend/package.json` и каталога с такими тестами не найдено.

### 10.5 Документы цикла работ

| Документ | Размер | Что это |
|---|---|---|
| 🆕 **`SPEC.md`** | ~276 КБ | **ЦИКЛ 4** «уведомления клиенту о записи через WhatsApp (GREEN-API), отправитель — салон, платит платформа», **редакция 6**. Истории US-27…US-63, §0 решения заказчика, §3 порядок урезания (US-34 была первой), §11 спайк сетевого контура, §15.1 вычисляемый статус оплаты, §16 шестнадцать вопросов архитектору |
| 🆕 **`ARCHITECTURE_CYCLE4.md`** | ~165 КБ | **ЦИКЛ 4, разделы 21–40** — продолжение `ARCHITECTURE.md`. Ключевые ссылки из кода: §23 модель данных цикла, **§24 шифрование чужих секретов**, §25 слои и файлы, **§26 отправщик (главный вопрос цикла)**, §27 как отправщик тестируется, §28 адаптер провайдера, §29 QR, §30 состояние канала и простой, §31 оповещение владельца, §32 вебхук, §33 тариф, **§34 часовые пояса и города**, §35 миграции, §36 порядок работ, §37 конфигурация и «приёмочные грепы», **§38 расхождения со SPEC**, §39 риски, §40 карта ответов на §16 SPEC |
| 🆕 **`API_CONTRACT_CYCLE4.md`** | ~40 КБ | **ЦИКЛ 4, разделы 19–37**, см. §10.3 |
| `SPEC_CYCLE3_PRODUCTION.md` 🆕 | ~139 КБ | **сохранённая спека цикла 3** «готовность к продакшену» — переехала сюда, потому что `SPEC.md` занят циклом 4 |
| `ARCHITECTURE.md` | ~135 КБ | **цикл 3**, разделы **до 21**. Ключевые ссылки, на которые ссылается код: §4 правовые документы, §5 модель согласий, §6.3 451 и claim'ы, §7.3/§7.4/**§19.2** (почему удаление аккаунта — надгробие, а не `DELETE`), §8 IdentityRoleSync, §9 ForwardedHeaders, §10 health, §11 логирование и GlitchTip, §12 деплой/бэкап/откат, §15.1 пагинация, §17.1/§18.2 стиль |
| `API_CONTRACT.md` | ~51 КБ | **цикл 3**, разделы до 19, см. §10.3 |
| **`SPEC_DEFERRED_NOTIFICATIONS.md`** ⭐ | ~90 КБ | SPEC **отложенного** цикла уведомлений клиенту по телефону (MAX/SMS). Тема **отложена решением заказчика, не отменена**. Здесь же живёт пункт **Д-1 «Подтверждение нового номера телефона при смене»**, на который ссылается комментарий в `ProfileController.ChangePhone` |
| **`SPEC_APPENDIX_CHANNELS.md`** ⭐ | ~57 КБ | приложение к нему: исследование каналов доставки |

**Соглашения об архиве (`docs/history/`) в репозитории по-прежнему нет**, каталога такого нет, в
README оно не описано. 🆕 **Цикл 4 решил задачу иначе — суффиксом в имени файла**
(`*_CYCLE4.md`, `SPEC_CYCLE3_PRODUCTION.md`), так что документы **двух последних циклов
одновременно лежат в корне**, а `SPEC.md`/`ARCHITECTURE.md`/`API_CONTRACT.md` без суффикса означают
разные циклы (4 и 3 соответственно). Это следует иметь в виду при любой ссылке «см. SPEC».
Документы цикла 4 **никуда не переносились** — в том числе поэтому эта редакция `CURRENT_STATE.md`
их не архивировала. 🆕 То же верно и на `7ab28b2`: каталога `docs/history/` в репозитории нет,
`SPEC.md` (цикл 4), `ARCHITECTURE_CYCLE4.md` и `API_CONTRACT_CYCLE4.md` лежат в корне как есть, и
**следующий цикл начнётся при занятом `SPEC.md`** — переименовать/перенести его придётся до того,
как в корне появится спека цикла 5.
Предыдущие редакции живут только в git-истории: SPEC цикла 2 — `git show 0492092:SPEC.md`,
цикла 1 — `git show e6b746c:SPEC.md`, ещё более ранняя — `7c86ca2`.
