# ARCHITECTURE — цикл 22 ServiceBooking: рефакторинг

**Разделы §370–§385.** Вход: `SPEC_CYCLE22_REFACTORING_DEAD_CODE.md` (цикл 22, решения Р1–Р6, US-22-01…US-22-08-bis), отчёты трёх
аудитов на `a08c6ca` (сведены сюда — отдельными файлами не хранятся). Отправная точка — `develop` =
`a08c6ca`, ветка `cycle/022-refactoring-dead-code`.

**API-контракт.** Отдельного `API_CONTRACT_CYCLE22.md` нет: новых эндпоинтов и полей нет. Изменения
контракта — **только удаления**, перечислены в §380 и отражаются в `API_DOCUMENTATION.md`.

---

## §370. Порядок пакетов работ (коммиты идут в этом порядке)

| Пакет | Что | Почему в этом месте |
|---|---|---|
| **P1** | Фронт: мёртвый код, сгенерированные типы, дедупликация (§376–§377) | Не пересекается с бэкендом, может идти параллельно P2–P4 |
| **P2** | Бэкенд: удаление мёртвого кода и заглушек 410 (§371–§373) | Сначала сокращаем то, что потом будем двигать |
| **P3** | Бэкенд: оптимизация запросов и дедупликация (§375, §377-B) | Правки внутри методов — до того, как методы переедут в другие файлы |
| **P4** | Бэкенд: миграции — индекс, финансирование канала, удаление колонок (§379–§380) | После P3: переводит чтения на общий `ChannelFundingReader`, введённый в P3 |
| **P5** | Разрезание контроллеров и `Program.cs` (§378) | Механический перенос; последним среди содержательных — минимальное окно конфликтов (R22-1) |
| **P6** | `using` через `dotnet format` (§373) | Конвенция `.editorconfig`: массовое переформатирование — отдельным последним коммитом |
| **P7** | Документы закрытия: CHANGELOG, README, API_DOCUMENTATION, TEST_CATALOG, CURRENT_STATE | — |

Каждый пакет заканчивается полным прогоном (юнит + функциональные + фронт) и отдельным коммитом(ами).

## §371. P2 — мёртвые символы бэкенда (класс A: ссылок 0)

| Файл | Символ | Действие |
|---|---|---|
| `API/DTOs/Billing/AdminBillingDtos.cs` | `CapabilityDto`, `AssignSubscriptionInput`, `AssignOptionInput`, `AdminSubscriptionChangeLogDto`, `AdminSubscriptionRequestDto` | удалить |
| там же | `AdminOptionInput`, `AdminOptionDto`, `AdminBillingAccountListItemDto`, `AdminBillingAccountDto`, `AdminAccountCompanyDto`, `AdminAccountChannelDto` | продукт их не использует, используют только тесты как формы десериализации → **перенести в `ServiceBooking.Tests`** (или перевести тесты на живые `Billing_*`-типы) и удалить из API |
| `AdminBillingController.cs:967-982` | `Billing_*` префиксы | после удаления двойников — переименовать в обычные имена (префикс существовал только из-за коллизии) |
| `AdminController.cs` | `MapAdminChannelDto` (приватный, 0 вызовов), `AdminChannelPaymentDto`, `UpdatePlanDto` (тестовый помощник `AdminTests.ToUpdateDto` перевести на `AdminPlanInput`) | удалить |
| `Services/Billing/ChannelFunding.cs` | `ChannelFundingResult` | удалить |
| `Services/Legal/LegalReadinessReportBuilder.cs` | `LegalReadinessDriftDto` | удалить |
| `Services/Legal/ConsentLedger.cs` | `CurrentAllAsync` + упоминания в XML-комментариях | удалить |
| `AdminBillingController.ValidateOptionInput` | параметр `existingCode` | удалить параметр |
| `OwnerSubscriptionService.ToSubscribedOptionDto` | параметр `now` | удалить |
| `TrialLifecycleTask.SelfHealMissedWindowStartsAsync` | параметр `now` | удалить |
| `appsettings.json`, `appsettings.Production.json.example` | `SmartCaptcha:SiteKey` | удалить |

**Проверка (R22-5):** для каждого символа в отчёте коммита — `grep -rnw <Symbol> --include=*.cs` = 0.

## §372. P2 — заглушки 410 (Р3)

Удалить `AdminController.UpdateSubscription` (`PUT api/admin/owners/{ownerUserId}/subscription`),
`AdminController.RecordChannelPayment` (`POST api/admin/notification-channels/{id}/payment`) и помощник
`LegacyEndpointGone`, если других пользователей у него нет. Тесты, проверявшие 410, переписать на 404
(`CY22-01`). `API_DOCUMENTATION.md` — убрать/пометить «удалён в цикле 22».

## §373. P2/P6 — пакеты и `using`

- Удалить избыточные `PackageReference` (приходят транзитивно): `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  в API; `Microsoft.EntityFrameworkCore` и `Microsoft.AspNetCore.Identity.EntityFrameworkCore` в
  Infrastructure; `Microsoft.EntityFrameworkCore.Design` в Tests. **Условие:** `dotnet restore` +
  сборка + прогон без изменений, `dotnet list package --include-transitive` показывает ту же версию.
  Если версия транзитивной ссылки отличается от явной — ссылку **оставить** (явная закрепляет версию).
- P6: в `.editorconfig` — `dotnet_diagnostic.IDE0005.severity = suggestion` (исключив
  `**/Migrations/**`), затем `dotnet format style --diagnostics IDE0005` по решению. Ожидание:
  ~79 правок в ~72 файлах. `GenerateDocumentationFile` для работы IDE0005 при сборке — не включаем
  в проекты (только локально для `format`).

## §374. Что выглядит мёртвым, но остаётся (класс D)

Правовые/аудиторские колонки (`NotificationTemplateHistory.*`, `RiskAcceptedVersion`,
`GuardianConfirmationVersion`, `PlatformSettingChangeLog.Old/NewValue`, `MailLog.*`, биллинговые
`Activated*/Requested*/UpdatedAtUtc`, `ChannelCompanyAssignment.AssignedAtUtc/AssignedByUserId`,
`ProviderSecretKeyId`, `ReasonDetail`, `DeliveredAtUtc`, `PushSubscription.ConsecutiveFailures`),
`NotificationChannel.ContactEmail` (зарезервировано циклом 4), `AccountSubscription.OwnerUserId`,
все члены перечислений, `WorkingDays.PreviousWorkingDay` (эталон тестов), тестовые зонды LegalKit
(`HasAnyAttributeMatch`, `AnchorsPending`), обязательные члены интерфейсов, эндпоинты без UI
(вебхуки, служебные админские, CRUD опций, перенос компании), `ChannelPaymentLog` (пишется при
приостановке канала — фактическое поведение, документ цикла 7 неточен).

## §375. P3 — оптимизация запросов (ответы не меняются)

| ID | Где | Изменение |
|---|---|---|
| F1+F2 | `MastersController` `GET clients` | SQL: группировка по `ClientId ?? GuestPhone` (последняя дата, число визитов), поиск (с `PhoneNormalizer.ParseSearch`, D2), сортировка и `Skip/Take` в БД; затем сводки и заметки (≤50 на клиента) **только для ключей страницы**, заметки — через `ToLookup`. Сохранить точный порядок и тай-брейкеры прежней сортировки |
| F3 | `CompaniesController.GetStats` | убрать неиспользуемый `Include(Service)`, `AsNoTracking`; агрегаты (счётчики по статусу, суммы, по мастерам, дням, услугам) — в SQL; «новые клиенты» — `GroupBy` + `Min(Date)` в периоде, `CountAsync`. Денежные суммы — `decimal` в SQL, как и в C# |
| F4 | `ReportsController` | проекция вместо `Include(Master)`, `AsNoTracking` |
| F7 | `BookingsController` GetById / client / master | `AsNoTracking`; проекция — там, где `MapToDto` позволяет без изменения DTO |
| F8 | `ReminderStatusesForAsync` | проекция без `Body` |
| F9 | `AvailabilityService`, `SlotService` | `AsNoTracking` на рабочих часах и перерывах |
| F10–F12 | `AdminController` компании / записи / каналы | `MemberCount` проекцией, словари вместо `FirstOrDefault` в цикле, пагинация в SQL при `paymentState == null` |
| F13–F15 | каналы / удаление аккаунта | убрать N+1 (`Include`/`Where(Id in …)`), пакетное чтение финансирования (`ChannelFundingReader`, §379) |
| F6 | `PhotoRetentionCleanupTask` | условие `CompanyId == companyId` (индекс `(CompanyId, CreatedAt)`) |
| F17 | создание записи → `NotificationScheduler.OnBookingCreatedAsync` | передать уже вычисленный `EffectivePlan` необязательным параметром; **без** кеша в scope |
| F18 | `SubscriptionResolver:164` | словарь вместо `FirstOrDefault` в `ToDictionary` |
| F20 | `Program.cs` `OnTokenValidated` | один запрос `{SecurityStamp, Roles}`; без кеширования |
| F21 | `GET /api/companies` | `ShowInPublicListing` в SQL |
| F22 | `ScheduledTaskRunner.TickAsync` | состояния задач одним запросом за тик |
| F23 | все GET-действия | `CancellationToken ct` → EF |
| F24 | `NotificationDispatchTask` | расшифровка секрета канала — один раз на группу, та же семантика ошибки |
| F5 | индекс | см. §379 |

Не делаем: F16 (выигрыш ничтожен, путь создания записи), F19, пагинацию `GET bookings/master`.

## §376. P1 — фронт: удаление

- `frontend/design_handoff_site_redesign/` целиком; записи о нём в `eslint.config.js`,
  `.prettierignore`; комментарий `components/ui/Icon.tsx:37`.
- `src/types/api-cycle{10,11,15,16,17}.generated.ts`, их скрипты `types:api:*` в `package.json`,
  соответствующие строки шагов CI «API types must match committed contracts»
  (`.github/workflows/ci.yml` ~146–156, ~187–195). **Контракты `contracts/cycleN/openapi.yaml`
  остаются** (их читает `redocly lint`, это документация).
- `src/api/adminTrialTerms.ts`; `adminApi.getSubscriptionHistory/getSubscriptionDiagnostics` и их
  типы; `bookingsApi.getOccupied`; `companiesApi.getAll` (+ комментарий-предупреждение);
  `legalApi.getDocuments`; `notificationChannelsApi.get`; `billingApi.getTrial` (+ его тест).
- Неиспользуемые типы: `types/index.ts` — `Master`, `Address*` (5), `PhoneVerificationMethod`;
  `api/billing.ts` — 11 алиасов; `api/adminBilling.ts` — 4 алиаса.
- Используемые только тестами: `bookingTotalDuration` (переключить `BookingModal` на неё — тогда
  она живая, иначе удалить), `getTrialRefusalCode`, `detectIosSafariNotInstalled` — удалить с тестами.
- `vitest.config.ts`: блок `coverage` без установленного `@vitest/coverage-v8` — удалить.
- Снять `export` с ~50 символов, используемых только в своём файле (кроме экспортируемых ради тестов).
- Поля `paidFrom` в типах каналов (Р6) — удалить; `paidUntil` в типах, если бэкенд перестаёт его
  отдавать (§380).
- **Исключения `knip`** (не удалять): `@redocly/cli` (CI), генерации циклов 7/9/13/14/18, `public/sw.js`.

## §377. Дедупликация

**Бэкенд (P3):**

| ID | Один дом |
|---|---|
| D1 | `Services/Billing/SubscriptionUsability`: `IsUsable(sub, now)` + `Expression<Func<AccountSubscription,bool>> UsableAt(now)`; 8 копий заменить. Проекция с CASE в `AdminBillingController` (B4) — оставить, с комментарием-ссылкой |
| D2 | `PhoneNormalizer.ParseSearch(search) → (IsPhone, Term)` |
| D3 | `BookingsController`: общий `ResolveServicesAsync` для `ResolveTotalDurationAsync` и `Create`; сообщение Create для мастера вне компании — сохранить |
| D4 | расширения `Booking.TotalDurationMinutes()`, `Booking.ServiceNames()` |
| D5 | `ScheduleFallbackPolicy.For(manual, extendedHours, isStaff)` |
| D6 | `CompanyMembership.IsStaffRole` (выражение); `CanManageBookingAsync` → `MasterId == userId || CompanyAccess.CanManageCompanyAsync` |
| D7 | `CompanyDtoAssembler` (переезжает в P5 из `CompaniesController`) |
| D8 | `BuildAdminPlanDtoAsync(plan)` |
| D9 | `UserWindowPolicy(...)` рядом с `IpWindowPolicy`; чтение конфигурации на запрос сохранить |
| D10 | `VisitStartResolver.ResolveAsync(db, bookingIds, ct)` |
| D11 | чистая `TrialEligibility.Evaluate(...)`; закрывает **C18-11** |

**Фронт (P1):** `utils/dateFormat.ts` (`fmtDate`, `fmtDateTime`, `—` для пустого) вместо локальных
обёрток и инлайнов; `formatRub` → `utils/`, заменить 27 инлайнов `toLocaleString('ru-RU') ₽` там, где
формат **идентичен**; `formatMonthlyPrice` вместо 3 инлайнов; подписи статусов записи — одна таблица
(`StatusBadge`), **формулировки выбрать по `MyBookingsPage`** (то, что сейчас видит мастер) и
перечислить изменившиеся строки в CHANGELOG; `TYPE_LABELS` → `utils/notificationTransport.ts`;
`ReviewModal` → `<Modal>`, `VerifyPhoneDialog` → `useOverlayDismiss`.

## §378. P5 — разрезание (маршруты те же)

| Было | Стало |
|---|---|
| `AdminController` | `AdminController` (пользователи, компании, записи, владельцы), `AdminPlansController` (тарифы), `AdminChannelsController` (каналы), `AdminPlatformController` (настройки платформы, хранение) — все `[Route("api/admin")]` + прежние атрибуты авторизации |
| `ProfileController` | `ProfileController` (профиль), `ProfileConsentsController` (согласия, прежние маршруты); выгрузка и удаление аккаунта → сервисы `Services/Subjects/SubjectDataExporter`, `AccountDeletionService` (контроллер вызывает их) |
| `BookingsController` | `BookingAvailabilityController` (occupied, slots, availability — прежние маршруты и политики лимитов), тело создания записи → `Services/Bookings/BookingCreationService` (вместе с D3); остальное — `BookingsController` |
| `CompaniesController` | `CompanyMembersController` (участники), `Services/Companies/CompanyStatsService` (GetStats), `CompanyDtoAssembler` (MapToDto и помощники; его же использует `CompanyAddressController`) |
| `AdminBillingController` | `AdminOptionsController` (опции), `AdminTrialController` (триал), остальное — `AdminBillingController` |
| `Program.cs` | `Startup/*Extensions.cs`: `ValidateDeployment`, `AddNotifications` (web-push, MAX, провайдеры), `AddServiceBookingRateLimiting` (+ D9), `AddAuth`, посев при старте. **Порядок вызовов сохраняется построчно** |

`NotificationChannelsController` не режется; из него выносится `ChannelFundingReader` (§379).

## §379. P4 — финансирование канала вместо `PaidUntilUtc` (Р2)

`Services/Notifications/ChannelFundingReader` — из `NotificationChannelsController.LoadFundingAsync`/
`IsChannelFundedAsync`, **пакетно** по списку биллинг-аккаунтов (F14). Возвращает на канал
`{ IsFunded, PaidUntilUtc? }`, где `PaidUntilUtc` = `PaidUntilUtc` опции WhatsApp, иначе период
подписки (текущая семантика `LoadFundingAsync:713-724`). Переводятся:

| Место | Было | Стало |
|---|---|---|
| `ChannelPaymentState` | `channel.PaidUntilUtc` | принимает `(isFunded, paidUntil)` из читателя |
| `CompanyNotificationsController:335,415,417` | колонка | читатель; `SettingsChannelDto.paidUntil` — из финансирования |
| `AdminController` сводка каналов (`ExpiringIn7Days`, `PendingRequests`) и список | колонка | читатель (пакетно) |
| `ChannelHealthTask:251` → `ChannelIdleCalculator` | колонка | читатель (пакетно на проход) |
| `NotificationChannelsController` замена канала (:537-574) | копирует/обнуляет колонки | не трогает; `ReplaceChannelResponseDto.paidUntil` — из читателя |
| `AdminController.SetSuspendedAsync` → `ChannelPaymentLog.Old/NewPaidUntil` | колонка | значение из читателя на момент приостановки |

Затем миграция `DropChannelLegacyPaidPeriod`: удаление `PaidFromUtc`, `PaidUntilUtc` из
`NotificationChannels`; из DTO — `ChannelDto.PaidFrom`, `AdminChannelDto.PaidFrom` (Р6).
**Тесты `CY22-02…`:** счётчики админки, статус оплаты в настройках компании и простой канала — по
оплаченной/неоплаченной/истекающей опции.

Индекс: миграция `AddBookingsMasterDateIndex` — `HasIndex(b => new { b.MasterId, b.Date })`
(`IncludeProperties` не используем — лишний размер при малом выигрыше). Одиночный индекс `MasterId`
становится префиксом составного — **удалить** его в той же миграции.

## §380. Изменения контракта API (только удаления)

| Что | Было | Стало |
|---|---|---|
| `PUT /api/admin/owners/{id}/subscription` | 410 | 404 |
| `POST /api/admin/notification-channels/{id}/payment` | 410 | 404 |
| `ChannelDto.paidFrom`, `AdminChannelDto.paidFrom` | всегда `null` | поля нет |
| `SettingsChannelDto.paidUntil`, `AdminChannelDto.paidUntil`, `ReplaceChannelResponseDto.paidUntil` | устаревшая колонка | фактическое финансирование (Р2) |

## §381. Тесты цикла

`CY22-01` (404 вместо 410), `CY22-02…05` (финансирование канала: сводка админки, настройки компании,
простой, замена канала), `CY22-06…08` (равенство `masters/clients` на наборе с гостями, поиском по
телефону/имени, границами страниц), `CY22-09…10` (равенство `stats`: деньги, мульти-услуги, новые
клиенты), `CY22-11` (отзыв роли действует на следующем запросе после F20). Юнит — на `TrialEligibility`,
`SubscriptionUsability`, `ScheduleFallbackPolicy`, `PhoneNormalizer.ParseSearch`.

## §382. Проверка «маршруты не сдвинулись» (P5)

До P5 снять таблицу `{HTTP-метод, шаблон, контроллер.действие, [Authorize] роли/политики,
[EnableRateLimiting], [RequiresOwnerTerms]}` — скриптом по исходникам или через
`IActionDescriptorCollectionProvider` в тесте — и сравнить после: разница допускается **только** в
имени контроллера/действия. Результат сравнения — в отчёт коммита P5 и `TEST_CATALOG.md`.

## §383. Документация

`API_DOCUMENTATION.md` (§380), `CHANGELOG.md` («Не выпущено»: для пользователей — только Р2 в админке
и формулировки статусов записи, если изменятся; для команды — остальное), `README.md` (сводка
структуры, если описывает контроллеры), `CURRENT_STATE.md` (редакция 🧽, закрытые §9.17/§9.19/§9.21/
§9.30/C18-11, новые долги C22-*, исправления §5.1 п. 3 и про `ChannelPaymentLog`), `TEST_CATALOG.md`.

## §384. Долги, которые цикл создаёт осознанно

- **C22-L1** — версии принятых текстов (`RiskAcceptedVersion`, `GuardianConfirmationVersion`) не в
  выгрузке субъекта — к legal-counsel.
- **C22-1** — навигационные свойства без ссылок (8 шт.).
- **C22-2** — единый разбор ошибок axios во `utils/*Error.ts`.
- **C22-3** — F16 (idempotency-ключи уведомлений одним запросом).

## §385. Риски исполнения

Смотри SPEC §5. Дополнительно: пакет P5 — самый объёмный по диффу, но самый механический; если в
ходе P5 обнаружится, что шов не чистый (общий приватный помощник на две половины), помощник выносится
в `internal static` класс рядом, а не дублируется.
