# ARCHITECTURE — цикл 40 ServiceBooking: упрощение каналов рассылок (WhatsApp / MAX)

**Разделы §40.0–§40.19.** Вход: `SPEC.md` цикла 40 (редакция 2026-10-08, решения Р1–Р10, Q-40-1…7, ответы О1–О8),
`CURRENT_STATE.md` (шапка на `a7e3168`), код `develop` = `ece8038`. Ветка цикла — `cycle/040-simplify-notification-channels`
(подготовлена devops; архитектор веток не трогает). Правовая часть — `LEGAL_REVIEW_CYCLE40.md` (пишется параллельно
legal-counsel; этот документ его не опережает и формулировок не придумывает: везде ключи и запасные тексты).

**Документы цикла:**

| Файл | Что | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE40.md` (этот) | решения, модель, механизмы, структура, задачи, риски, ответы на §9 SPEC | все |
| `API_CONTRACT_CYCLE40.md` (§40.20–§40.39) | контракт словами: маршруты, порядок проверок, коды, тексты, изменения существующих маршрутов | backend, frontend, QA |
| `contracts/cycle40/openapi.yaml` (+ `openapi.json` генератом) | **источник истины по форме** (OpenAPI 3.0.3) — **в этом заходе не создан** по указанию заказчика архитектуры; полный состав описан в `API_CONTRACT_CYCLE40.md` §40.38, пишется задачей **BE-40-C первым коммитом цикла**, до кода | backend, frontend, QA, CI |
| `contracts/cycle40/channel-vectors.json` | эталон правил: оплата транспорта, маршрутизация, три состояния, шаг мастера, «рассылки работают», строки цен — один набор векторов для C# и TS; состав — `API_CONTRACT_CYCLE40.md` §40.39; пишется в BE-40-C | backend, frontend, QA |

Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — документы цикла 3, по конвенции не перезаписываются.

---

## §40.0. Итог решений одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| A1 | Где считается «транспорт X оплачен» | Одна чистая функция `ChannelOptionFunding.Evaluate` + один пакетный читатель `AccountMessagingReader` (аккаунт → состояние двух транспортов). `PaidNotificationNumbers`, `ChannelFunding.Rank(N)`, `ChannelEligibility`, чтение `PlanOptionRule` и годности подписки для двух опций — **удаляются из кода** (не из БД) | §40.3 |
| A2 | Флаг тарифа | Поле `AllowNotificationChannel` **удаляется из рекордов** `EffectivePlan`, `OrdersPlan`, `StaysPlan` — компилятор находит всех читателей; колонка БД и свойство сущности остаются; страж-тест запрещает новые чтения | §40.3.4 |
| A3 | Номер на все компании | Одна функция «каналы для компании» = живые каналы **аккаунта компании** (`Company.BillingAccountId`). `ChannelCompanyAssignments` не читается и не пишется (кроме удаления строк ради составного FK) — полный перечень 26 мест в §40.4 | §40.4 |
| A4 | Маршрутизация при одном транспорте | Правило «маршрутизируемый транспорт» (оплачен ∧ номер первый живой ∧ не приостановлен ∧ номер **хоть раз привязан**). Ровно один такой транспорт → всё в него, режим и приоритет игнорируются. Два → сохранённый режим без тихого перехода | §40.5 |
| A5 | Три состояния | Чистая `ChannelPresentation.Display(facts)` → `displayStatus` / `displayText` / `action`, тексты называют правильный мессенджер; шаг мастера — `ChannelPresentation.WizardStep(facts)` | §40.6 |
| A6 | Мастер | Новый `GET /api/notification-channels/overview` (всё для блока «Номера» одним запросом); `POST /api/notification-channels` принимает риск в том же запросе и **продлевает** заявку существующего неоплаченного (или пробного) номера (200 вместо 409); `connect` идемпотентен, работает из `Disconnected`, сбрасывает `ConnectedAtUtc`; `replace` — из любого привязанного состояния | §40.7 |
| A7 | Автоматическое проверочное (О-40-2) | Мини-очередь **на строке канала**: метка «нужна проверка для экземпляра X» ставится в `ChannelStateTransition.Apply` при переходе в `Connected`; отправляет новая задача `channel-test-message` (полоса `realtime`, 10 с) после атомарного захвата условным `UPDATE`. Ключ однократности — `ProviderInstanceId`: один экземпляр = одна привязка = одна проверка | §40.8 |
| A8 | Ожидающие сообщения (О-40-4) | Один помощник `PendingRebinder`: при отвязке, замене номера и передаче компании `Pending` **перепривязываются**, а не отменяются, если есть законная цель; иначе отмена с причиной. Перепривязка на другой транспорт выключается флагом конфигурации (правовой вопрос §8 п. 3) | §40.9 |
| A9 | «Рассылки работают» для публичных страниц (О-40-3) | Одна служба `CustomerMessagingOffer` с кешем 30 с на компанию; результат — **поле в существующем ответе** (`CompanyDto.customerMessaging`, `storefront.customerNotifications`, `PublicHouseDto.messenger`), только булево + транспорты + готовая подпись | §40.10 |
| A10 | Галочка клиента ezbook | `Bookings.NotifyByMessenger bool?` (+ версия и время текста `BookingMessengerConsent`); `false` → `Skipped`/`ClientDeclinedMessenger`; `null` (старый клиент, запись сотрудником) → как до цикла | §40.11 |
| A11 | Глобальный выключатель | Настройка платформы `notifications.customer-messaging.enabled` (нет ключа = включено), поле в `platform-settings`; проверяется в семи местах | §40.12 |
| A12 | Админка | Таблица с фильтром по трём состояниям и оплате (существующий маршрут + параметры), карточка `GET /api/admin/notification-channels/{id}` с двумя журналами, `POST …/confirm-payment`; новый журнал изменений опций каналов `ChannelOptionChangeLogs` | §40.13 |
| A13 | Строки без своей даты (О-40-5) | **Правило оставляем в коде** (дёшево, тот же пакетный запрос), отчёт T-40-02 считает такие строки для сведения, а не как условие кода; новые строки двух опций всегда со своим сроком | §40.3.3 |
| A14 | Миграция | Одна: `Cycle40ChannelOptions` — опция `notifications.max` (копия WhatsApp, фиксированный Id), 3 колонки `Bookings`, 3 колонки `NotificationChannels`, таблица `ChannelOptionChangeLogs`. Ничего не удаляется и не переименовывается, enum — только дописываются | §40.2 |

**Найдено по коду и исправляется попутно (без этого три состояния SPEC будут врать):**
1. **Приостановка админом сейчас не останавливает отправку.** `IsSuspendedByAdmin` читается только в представлении
   (`ChannelPaymentState`, `ChannelIdleCalculator`); `ChannelFunding.Rank`, `NotificationGate`, диспетчер его не видят.
   SPEC US-05 относит «приостановлен» к «Выключен» — после цикла приостановленный номер **не маршрутизируемый** (§40.5).
2. **Переподключение после `NeedsReconnect` может само сорваться в `Disconnected`.** `Connect` не сбрасывает
   `ConnectedAtUtc`; `ChannelHealthTask` опрашивает и `Connecting`; `ChannelStateMapper` при `hadBeenConnected = true`
   отображает `notAuthorized` (обычное состояние экземпляра до сканирования QR) в `Disconnected`. Окно QR — до 15 минут,
   период опроса — 15 минут. После цикла `connect` обнуляет `ConnectedAtUtc` при создании нового экземпляра (§40.7.3).
3. **Пробный период «Записей» сейчас не даёт рассылок вовсе** при тарифе без правила `Included` (цикл 28, Q28-4). О6
   отменяет это: триал материализует обе опции без чтения правил тарифа (§40.3.5).

---

## §40.1. Стек: новых зависимостей — ноль

Проект существующий; цикл — переработка одной подсистемы монолита. ASP.NET Core 8 + EF Core 8 + PostgreSQL 16, React +
Vite + TS + TanStack Query, три фронтенда в одном npm-пакете (ezbook, goods, dom). Новых пакетов нет: кеш —
`IMemoryCache` (как `PlatformSettings` и каталог goods), фоновые задачи — существующий `ScheduledTaskRunner` с полосами,
блокировки — `AdvisoryLock`, журнал настроек — `PlatformSettingsWriter`.

**Масштаб и продажа как сервиса.** Цикл уменьшает связность: «оплачено» больше не зависит от тарифов трёх линеек, а
маршрутизация — от таблицы назначений. В процессе нет состояния, кроме 30-секундного кеша «рассылки работают» и
60-секундного кеша настроек платформы: второй экземпляр API возможен. Задача `channel-test-message` захватывает работу
условным `UPDATE` — безопасна при двух экземплярах раннера.

---

## §40.2. Модель данных и миграция (T-40-02, §5 SPEC)

### §40.2.1 Миграция `Cycle40ChannelOptions` — одна, закреплённым `dotnet-ef` 8.0.11, один разработчик (BE-40-M)

| Объект | Изменение | Зачем |
|---|---|---|
| `SubscriptionOptions` | **строка** `notifications.max`, `Id = c4000000-0000-4000-8000-000000000040` (константа `ChannelOptionCodes.MaxOptionSeedId`). `INSERT … SELECT` из строки `notifications.whatsapp`: `Name = 'MAX'`, `Description = 'Номер для уведомлений клиентам в MAX.'`, `CapabilityKey = 'notifications.max'`, `Kind`, `PricePerMonth`, `UnitName`, `MaxQuantity`, `UnitPriceText`, `IsPublic`, `IsActive` — копия WhatsApp, `SortOrder = WhatsApp.SortOrder + 1`. `WHERE NOT EXISTS (… "Code" = 'notifications.max')` — идемпотентно. Если строки WhatsApp нет (невозможно после `SeedBillingCatalog`, но защищаемся) — вставка с `PricePerMonth = NULL`, `IsActive = true`, `IsPublic = false` | §5.1, О7 |
| `PlanOptionRules` | **ничего** — правила тарифов для двух опций каналов код больше не читает (§40.3) | US-02 |
| `AccountSubscriptionOptions` | **ничего** — строки не создаются и не меняются | О7 |
| `Bookings` | `NotifyByMessenger boolean NULL`; `MessengerConsentVersion varchar(64) NULL`; `MessengerConsentAtUtc timestamptz NULL` | US-07; доказательство согласия — как у `Orders`/`StayBookings` |
| `NotificationChannels` | `LastTestResult int NULL` (→ `ChannelTestResult`); `LastTestResultAtUtc timestamptz NULL`; `AutoTestInstanceId varchar(100) NULL`. Частичный индекс `IX_NotificationChannels_TestPending` по `("Id") WHERE "LastTestResult" IN (0, 1)` | §40.8 |
| `ChannelOptionChangeLogs` (новая) | журнал изменений двух опций каналов (§40.13.3) | US-10 «журнал оплаты» |

`Down()` удаляет таблицу, колонки и строку `notifications.max` **только если** на неё нет ни одной
`AccountSubscriptionOption` (иначе `RAISE EXCEPTION` с понятным текстом — откат БД после продажи MAX не делается,
откатывается только релиз; см. §40.17).

**Совместимость «код до — код после» (откат релиза без отката БД):** старый код читает только `notifications.whatsapp` —
новая строка каталога и новые колонки ему не видны; новые значения enum в старых строках появятся, только если новый код
успел поработать, — тогда старый код должен показать их в журнале без падения (проверяется тестом
`Cycle40RollbackReadTests` и ручным M40-08).

### §40.2.2 Новые и изменённые перечисления (только дописывание в конец)

| Enum | Новые члены (значение) |
|---|---|
| `NotificationReason` | `ClientDeclinedMessenger = 34`, `PlatformMessagingDisabled = 35`, `ChannelAccountMismatch = 36` (страховка диспетчера: номер чужого аккаунта, §40.5.4) |
| `ChannelStateReason` | `ReplacedByOwner = 11`, `RebindStarted = 12` (переподключение из `Disconnected`), `TestMessageSent = 13`, `TestMessageFailed = 14`, `TestMessageSkipped = 15` |
| `ChannelTestResult` (новый, `Core/Enums`) | `Pending = 0`, `Sending = 1`, `Sent = 2`, `Failed = 3`, `SkippedSameNumber = 4`, `SkippedNoOwnerPhone = 5`, `SkippedPlatformDisabled = 6` |
| `ChannelOptionChangeSource` (новый) | `AdminBillingAccount = 0`, `AdminChannelCard = 1`, `TrialGrant = 2`, `TrialWindowStart = 3`, `TrialExpiry = 4`, `AdminOptionEnded = 5` |
| `ChannelDisplayStatus` (новый, только API) | `Working`, `ActionRequired`, `Off` |
| `ChannelAction` (новый, только API) | `Pay`, `BindNumber`, `Reconnect`, `ReplaceNumber`, `Unbind` |
| `ChannelWizardStep` (новый, только API) | `Payment`, `PaymentPending`, `Qr`, `Done`, `Unavailable`, `None` |

Значения первых четырёх хранятся числом — порядок фиксирован тестом `Cycle40EnumAppendOnlyTests` (по образцу
существующих стражей append-only).

### §40.2.3 Сущности

- `Booking`: `bool? NotifyByMessenger`, `string? MessengerConsentVersion`, `DateTime? MessengerConsentAtUtc` — в конец.
- `NotificationChannel`: `ChannelTestResult? LastTestResult`, `DateTime? LastTestResultAtUtc`, `string? AutoTestInstanceId`.
  `Assignments` (навигация) **остаётся** — она нужна EF для составного FK; читать её запрещено стражем (§40.4.3).
- `ChannelOptionChangeLog`: `Id`, `BillingAccountId` (FK → `BillingAccounts` `Restrict`), `OptionCode varchar(64)`
  (`notifications.whatsapp` | `notifications.max`), `Source int`, `OldPaidUntilUtc?`, `NewPaidUntilUtc?`, `OldEndsAtUtc?`,
  `NewEndsAtUtc?`, `ChangedByUserId text NULL` (null = система), `ChangedAtUtc`, `ChannelId uuid NULL` (если изменение
  сделано из карточки канала), `Comment varchar(500) NULL`. Индекс `(BillingAccountId, OptionCode, ChangedAtUtc)`.
  Append-only, единственный писатель — `ChannelOptionLog.Write(...)`.
- `ShowcaseOwnership`: `ChannelOptionChangeLogs` — в `NeverWritten` (иначе падает `ShowcaseOwnershipCoverageTests`).
- `SubscriptionPlanConfig.AllowNotificationChannel` — свойство остаётся (EF-маппинг колонки), пишет его только
  `TariffCatalogSeeder` (`false`), читать запрещено стражем.

### §40.2.4 Строки, которые не трогаем (§3, §5.2–§5.4 SPEC)

`ChannelCompanyAssignments` (таблица, строки, FK, индексы), `SubscriptionPlanConfigs.AllowNotificationChannel`,
`PlanOptionRules` двух опций, `PlatformSettings` `notifications.channel.price-per-month` — остаются; код их не читает.
Удаление — отдельным циклом после выката.

---

## §40.3. Оплата по транспорту (US-01, US-02, T-40-01)

### §40.3.1 Чистое правило `ChannelOptionFunding.Evaluate` (`Services/Notifications/Funding/`)

Вход — факты одной строки опции транспорта аккаунта и два «наследных» факта; выход —
`TransportPayment(bool Paid, DateTime? PaidUntil, bool IsTrial, DateTime? LastPaymentAt)`.

| Факт строки `AccountSubscriptionOption` | Оплачено? | `PaidUntil` |
|---|---|---|
| строки нет | нет | null |
| `EndsAtUtc ≤ now` | нет | `EndsAtUtc` |
| `PaidUntilUtc` задан | `PaidUntilUtc ≥ now` | `PaidUntilUtc` |
| `PaidUntilUtc = null`, `GrantedByTrial = true` (окно рассылок триала ещё не открыто, §336 цикла 18) | `account.TrialEndsAtUtc > now` | `account.TrialEndsAtUtc` |
| `PaidUntilUtc = null`, `GrantedByTrial = false` (**наследная** строка, §5.3) | подписка «Записей» аккаунта годна (`SubscriptionUsability.IsUsable`) | `AccountSubscription.PaidUntil` |

`Quantity ≥ 1` — достаточное условие («транспорт оплачен»), количество больше не означает число номеров. `IsTrial` =
`GrantedByTrial`. `LastPaymentAt` = `ActivatedAtUtc` строки — нужен для «заявка новее подтверждения оплаты» (§40.6.1).

**Не читаются** (US-02): `PlanOptionRule` ни одной линейки, `AccountSubscription.MailingUntilUtc`, годность подписки
(кроме наследной строки), флаги тарифов. Окно рассылок триала ограничивает только триальные строки — через их
собственный `PaidUntilUtc`, который выставляет `TrialMailingWindowStarter` (без изменений).

### §40.3.2 Номер транспорта: `TransportFunding.Rank` (замена `ChannelFunding.Rank(N)`)

Для каждого аккаунта и каждого транспорта: живые каналы транспорта (`State ≠ Replaced`) по `CreatedAt, Id`. Первый —
`Funded`, если транспорт оплачен, иначе `NotPaid`; остальные — `Unfunded` («лишний номер», §5.2 SPEC; в старых данных
до проверки N8 бывало два живых канала одного транспорта). Транспорты не влияют друг на друга. Перечисление
`ChannelFundingState` не меняется.

### §40.3.3 Пакетный читатель `AccountMessagingReader` (scoped, `Services/Notifications/Funding/`)

`Task<IReadOnlyDictionary<Guid, AccountMessagingState>> LoadAsync(IReadOnlyCollection<Guid> accountIds, DateTime now, ct)` и
`Task<AccountMessagingState?> ForCompanyAsync(Guid companyId, DateTime now, ct)`. Ровно **четыре запроса** на любой набор
аккаунтов (не на компанию): каналы аккаунтов; строки двух опций; `AccountSubscriptions` (наследное правило); `BillingAccounts`
(`TrialEndsAtUtc`). Плюс настройка платформы (кеш) и цены опций (кеш каталога 60 с).

```
AccountMessagingState { AccountId, PlatformEnabled, WhatsApp: TransportState, Max: TransportState, Channels[] }
TransportState { Transport, Payment (§40.3.1), Option { Sellable, PricePerMonth },
                 Primary: NotificationChannel?   // первый живой канал транспорта
                 Duplicates: NotificationChannel[],
                 Routable: bool, Working: bool }  // §40.5.1
```

`ChannelFundingReader` остаётся фасадом с прежней формой результата (`ChannelFundingInfo(State, Text, PaidUntil)`) для
его нынешних потребителей (список владельца, админка, сводка компании), но внутри — `AccountMessagingReader`.
`IsFundedAsync(channel, plan)` → `IsFundedAsync(channel)` (параметр плана исчезает — компилятор найдёт все вызовы).

**О-40-5 — ответ.** Правило «`PaidUntilUtc = null` → срок подписки» **остаётся в коде** для строк с `GrantedByTrial = false`.
Почему: оно стоит один запрос, который `ChannelFundingReader` делает уже сейчас; без него код зависел бы от результата
отчёта на копии боя (если отчёт ошибётся — тихо выключится оплаченный номер, это R-40-2 в другой форме). Новые строки
двух опций без даты больше не появляются: админ-маршруты требуют дату (§40.13.4), триальная строка без даты обрабатывается
отдельной веткой. Отчёт T-40-02 считает наследные строки для сведения. Ветка удаляется вместе с колонками в цикле
удаления наследия.

### §40.3.4 Что удаляется из кода (компилятор — главный инструмент)

| Где | Что | Чем заменяется |
|---|---|---|
| `EffectivePlan` | поля `AllowNotificationChannel`, `PaidNotificationNumbers`; присваивание в `Resolve` при закрытом окне рассылок | ничем: рассылкам план не нужен |
| `SubscriptionResolver.GetEffectivePlansForAccountsAsync` | чтение опции WhatsApp, правил трёх линеек, магазинов и «Домов» ради опции, `PaidNumbers`, `IsOptionCurrentlyPaid` | ничем (резолвер становится на 4 запроса легче) |
| `OrdersPlan`, `StaysPlan` (`OrdersPlanResolver`, `StaysPlanResolver`) | поле `AllowNotificationChannel` | ничем |
| `ChannelEligibility` | класс целиком + регистрация в DI | «можно купить» = опция продаётся ∧ сервис включён (§40.7.1) |
| `ChannelFunding.Rank(channels, int)` | метод | `TransportFunding.Rank` |
| `TrialMailingRulePolicy` | класс (читает флаг тарифа) | ничем: триал материализует обе опции всегда (§40.3.5) |
| `OwnerSubscriptionService` | строки «Сообщения гостям/покупателям в MAX и WhatsApp» в `Includes` по флагу | строки цен мессенджеров (§40.14) |
| `CompanyNotificationsController` | `plan.AllowNotificationChannel` (4 места), 402 «Недоступно на вашем тарифе», 402 «Канал не оплачен» в настройках и шаблонах | §40.6.4 |
| `NotificationChannelsController` | 402 «Подключение канала недоступно на вашем тарифе» (2 места), `eligibility` | §40.7 |
| `ChannelPresentation.SettingsBlockedReason(planAllowsChannel, …)` | параметр плана | новая сигнатура без плана |

**Страж** `AllowNotificationChannelReadGuardTests` (юнит, по образцу `SubjectPhoneGateInvariantTests`): регэксп
`\.AllowNotificationChannel\b` по `ServiceBooking.API/**/*.cs`; разрешено только присваивание в
`Services/Showcase/Tariffs/TariffCatalogSeeder.cs`. Второй страж того же теста — `PaidNotificationNumbers` не встречается
нигде в `ServiceBooking.API`.

Поля DTO, которые оставляем ради старых вкладок: `ChannelOfferDto.allowedByPlan` и `NotificationSettingsDto.planAllowsChannel` —
всегда `true`; `SubscriptionUsageDto.numbersPaid` — число оплаченных транспортов (0…2); `ChannelDto.fundingState/fundingText` —
по новому правилу с новыми текстами (§40.3.6).

### §40.3.5 Пробный период — обе опции (О6)

`TrialActivationService.GrantAsync`: вместо блока «опция WhatsApp + правило `Included` тарифа» — цикл по двум кодам
`ChannelOptionCodes.All` (`notifications.whatsapp`, `notifications.max`). Для каждого: строки нет → создать
(`Quantity = 1`, `PaidUntilUtc = account.TrialMailingWindowEndsAtUtc`, `GrantedByTrial = true`); строка есть и триальная →
оживить (как сейчас); строка есть и платная → не трогать (как сейчас, B1 цикла 18). Код опции отсутствует в каталоге →
`LogError` + сигнал GlitchTip (как сейчас для WhatsApp). Правило тарифа **не читается**. Каждая запись —
`ChannelOptionLog.Write(Source = TrialGrant)`.

`TrialMailingWindowStarter` и `TrialLifecycleTask` уже обрабатывают все строки с `GrantedByTrial` независимо от кода —
правок логики нет, добавляется только запись в журнал (`TrialWindowStart`, `TrialExpiry`).

### §40.3.6 Тексты оплаты (`BillingTexts.FundingText`, новая сигнатура `(state, transport, payment, workingPhoneMasked)`)

- `NotPaid`: «Номер {мессенджер} не оплачен» (без «опции „Рассылки в WhatsApp“» и без «раздела Подписка»).
- `Funded`: «Оплачено до {дд.мм.гггг}» / «Пробный период до {дд.мм.гггг}».
- `Unfunded`: «Лишний номер {мессенджер}: сообщения уходят с {маска рабочего}. Отвяжите этот номер».

---

## §40.4. Номер работает на все компании аккаунта (US-03, T-40-03) — ответ на О-40-1

### §40.4.1 Функция «каналы для компании»

`AccountMessagingReader.ForCompanyAsync(companyId)` = `Companies.BillingAccountId` → `AccountMessagingState` аккаунта.
Компания без `BillingAccountId` (не встречается после цикла 7, защищаемся) → «нет каналов». Это **единственный** способ
узнать каналы компании; через него работают все места §40.4.2.

### §40.4.2 Полный перечень мест чтения и записи (поиск по `ece8038`: `ChannelCompanyAssignment`, `.Assignments`,
`AllowNotificationChannel`, `PaidNotificationNumbers`, `ChannelFunding.Rank`, `ChannelEligibility`, `IsMessengerAvailableAsync`)

**Назначения — чтение (13 мест кода API):**

| # | Место | Сейчас | После |
|---|---|---|---|
| 1 | `Services/NotificationScheduler.cs:164` `BuildContextAsync` | каналы из назначений компании | `ForCompanyAsync`; маршрутизация §40.5 |
| 2 | `Services/Orders/Notifications/OrderMessageScheduler.cs:35` | то же для заказов | то же |
| 3 | `Services/Stays/StayMessageScheduler.cs:30` | то же для броней | то же |
| 4 | `Services/Shops/ShopChannelReader.cs:24` (`LoadAsync`, `IsMessengerAvailableAsync`) | номера магазина из назначений | `LoadAsync` переписан на `ForCompanyAsync` (номера аккаунта; потребители — `ShopNotificationsController:56,99`, `StaysCompaniesController:249,282`); `IsMessengerAvailableAsync` **удаляется**, его потребители (`StorefrontController:58`, `OrderCreationService:173`, `StayBookingCreationService:161`) переходят на `CustomerMessagingOffer` (§40.10) |
| 5 | `Controllers/CompanyNotificationsController.cs:49,74` (настройки GET/PUT) | назначения компании | `ForCompanyAsync` |
| 6 | `…CompanyNotificationsController.cs:182` (PUT шаблона) | «есть оплаченное назначение» → 402 | 402 снимается (§40.6.4) |
| 7 | `…CompanyNotificationsController.cs:349,364,371` (сводка) | `channelPaidUntil` по назначенным; «несколько компаний на номере» по назначениям | `channelPaidUntil` = максимум `PaidUntil` оплаченных транспортов аккаунта; `byCompany` — по всем компаниям аккаунта, если их > 1 |
| 8 | `Services/Scheduling/Tasks/ChannelHealthTask.cs:241` (простой) | `c.Assignments.Count(a => a.Company.IsActive)` | `MessagingDemand` аккаунта (§40.4.4) |
| 9 | `Services/Billing/OwnerSubscriptionService.cs:68,188,277` | `CoveredCompanyDto.HasNumber` по назначениям (три линейки) | `HasNumber` = у аккаунта есть номер в состоянии «Работает» |
| 10 | `Controllers/CompanyTransferController.cs:74` | `WillDetachFromChannel` = есть назначение | = у исходного аккаунта есть живой канал; `WillCancelPendingNotifications` = число `Pending`, которые не перепривяжутся (§40.9) |
| 11 | `Controllers/AdminChannelsController.cs:34,79` | `CompanyCount = c.Assignments.Count` | = число компаний аккаунта канала (одним `GROUP BY` на страницу) |
| 12 | `Controllers/AdminAccountDtoBuilder.cs:41,135` | `assignedCompanies = c.Assignments.Count` | = число компаний аккаунта |
| 13 | `Controllers/NotificationChannelsController.cs:56,684,805,841` (`Include(Assignments)`, `ChannelDto.companies`) | назначенные компании | `companies` = все компании аккаунта (`ChannelCompanyDto(id, name, isActive)`), одним запросом на ответ |

**Назначения — запись (5 мест):**

| # | Место | После |
|---|---|---|
| 14 | `NotificationChannelsController.AssignCompany` (`POST …/companies`) | 410 строкой без изменения данных (§40.7.7) |
| 15 | `NotificationChannelsController.UnassignCompany` (`DELETE …/companies/{companyId}`) | 204 без изменения данных |
| 16 | `NotificationChannelsController.Replace:546` (перенос назначений) | не переносит; `Pending` — через `PendingRebinder` (§40.9) |
| 17 | `Services/Billing/CompanyTransferService.cs:316` (удаление строки назначения перед сменой `BillingAccountId`) | **удаление остаётся** — составной FK `(CompanyId, BillingAccountId)` иначе отвергнет `UPDATE`; это гигиена наследных строк, не запись нового назначения |
| 18 | `Services/Subjects/AccountDeletionService.cs:333` (удаление назначений каналов удаляемого человека) | **удаление остаётся** (та же гигиена) |

**Назначения — модель и инфраструктура (не меняются):** `Core/Entities/ChannelCompanyAssignment.cs`,
`NotificationChannel.Assignments`, `AppDbContext.cs:82,961–987`, `ShowcaseOwnership.cs:56`
(`ByCompany("channelAssignments", …)` — таблица остаётся в модели, классификация обязательна), `tools/bench/cycle22/seed.py`,
`deploy/checks/billing-*.sql` (исторические проверки цикла 7).

**Флаг тарифа и оплата по количеству — чтение (8 мест):**

| # | Место | После |
|---|---|---|
| 19 | `Services/SubscriptionResolver.cs:29,51,56,106` + `:181–273` | §40.3.4 |
| 20 | `Services/Billing/ChannelEligibility.cs` (флаги «Записей», «Заказов», «Домов») | класс удаляется |
| 21 | `Services/Billing/OrdersPlanResolver.cs:14,21,42`; `Services/Stays/StaysPlanResolver.cs:10,23` | поле из рекордов удаляется |
| 22 | `Services/Billing/OwnerSubscriptionService.cs:78–81,197,280–289,323` | §40.3.4, §40.14 |
| 23 | `Services/Billing/TrialActivationService.cs:241–322` + `TrialMailingRulePolicy.cs` | §40.3.5 |
| 24 | `Controllers/CompanyNotificationsController.cs:77,189,453,465` | §40.6.4 |
| 25 | `Services/Notifications/NotificationGate.cs:81` (`PaidNotificationNumbers == 0`) | новая сигнатура (§40.5.3) |
| 26 | `Controllers/AdminBillingController.cs:198`, `AdminAccountDtoBuilder.cs:44,115` (`numbersPaid`, `ChannelFunding.Rank`) | число оплаченных транспортов, `TransportFunding.Rank` |

Плюс `ChannelFunding.Rank` в `NotificationDispatchTask.cs:113`, `NotificationScheduler.cs:222,250`,
`OrderMessageScheduler.cs:123`, `StayMessageScheduler.cs:97`, `ChannelFundingReader.cs:73,99` — все уходят в
`AccountMessagingReader`. Упоминания флага только в комментариях (`AdminPlansController:366`,
`CompanyPushSettingsController:18`, `CompanyNotificationSettings.cs:38`) правятся текстом.

**Админка тарифов:** поля «Разрешён канал уведомлений» в интерфейсе уже нет (`AdminPlanDto` его не отдаёт — тест
`MapAdminPlanDto_DoesNotExposeAllowNotificationChannelOrCreatedAt`); в цикле там ничего не меняется.

**Фронтенд — читатели `companies`/`allowedByPlan`/`planAllowsChannel`:** `src/pages/owner/NotificationsSection.tsx`
(удаляется), `src/pages/owner/NotificationSettingsTab.tsx:107` (переписывается), `goods/.../ShopNotificationsPage.tsx:216`,
`dom/.../NotificationsPage.tsx:204` (блок «Номера» вместо `ChannelCard`), `src/components/notifications/AssignCompanyDialog.tsx`
(удаляется). `CatalogListingCard.allowedByPlan` — другое поле (показ в каталоге), не трогается.

**Тесты, кодирующие удаляемое поведение** (переписываются в BE-40-1/BE-40-2, а не удаляются молча — каждое изменение
ожидания пишется в отчёт разработчика): `NotificationGateTests`, `NotificationTypeCatalogTests` (поля плана),
`SubscriptionResolverRulesTests` (`PaidNumbers`, флаг), `OrdersPlanResolverTests`, `StaysPlanTests`, `TrialMailingRulePolicyTests`
(удаляется вместе с классом), `ChannelPresentationTests`, `ChannelFunding*Tests`, функциональные `NotificationChannelsTests`
(402 по тарифу, назначения), `NotificationQueueingTests`, `NotificationDispatch*Tests`, `Cycle24NotificationsTests`,
`Cycle25*`, `Cycle28OutboundSuppressionTests`, `Cycle35LocksAndOffDemoTests`, `LegalPriorityTests` (выставляют флаг тарифа —
флаг становится лишним, тест должен остаться зелёным без него), `NotificationTestBase` (помощник «план с флагом» →
«оплаченная опция транспорта»).

### §40.4.3 Страж от возврата назначений

`AssignmentReadGuardTests` (юнит): регэксп `ChannelCompanyAssignments|\.Assignments\b` по `ServiceBooking.API/**/*.cs`,
allow-list — `CompanyTransferService.cs`, `AccountDeletionService.cs` (только `Remove`/`RemoveRange` в той же строке или
следующей), `ShowcaseOwnership.cs`, `ApplicationServicesExtensions.cs` (комментарий). Новый читатель назначений = красный
тест.

### §40.4.4 Простой номера — новое условие (`MessagingDemand`)

«У аккаунта есть хотя бы одна активная (`IsActive`), не витринная (`!IsShowcase`) компания с включёнными рассылками
клиентам». Включены = салон: `EnabledTypeMask & BookingTypesMask ≠ 0` (нет строки настроек → маска по умолчанию);
магазин: `ShopSettings.CustomerMessengerEnabled`; «Дома»: `StaysSettings.GuestMessengerEnabled`. Один запрос на все аккаунты
прохода (`EXISTS` по трём видам). `ChannelIdleCalculator.Recompute` получает `bool hasDemand` вместо счётчика назначенных
компаний; оплата — `Funded` из `TransportFunding.Rank`; приостановка — как сейчас. Выключатель платформы простой **не
запускает** (экземпляры не удаляются, US-12).

---

## §40.5. Маршрутизация и постановка в очередь (US-09, T-40-07)

### §40.5.1 Определения (чистые, `Services/Notifications/Funding/TransportFunding.cs`)

- **Маршрутизируемый транспорт** аккаунта: транспорт оплачен ∧ у него есть `Primary`-канал ∧ канал `Funded` ∧
  `!IsSuspendedByAdmin` ∧ состояние канала ∈ {`Connected`, `Disconnected`, `NeedsReconnect`, `Blocked`} (номер **хоть раз
  привязан**; временная поломка держит сообщения в `Pending`, как сейчас — §30.2 цикла 4). `NotConnected`, `Connecting`,
  `DisabledByOwner`, `Replaced` — не маршрутизируемые.
- **Работающий транспорт**: маршрутизируемый ∧ `State = Connected` ∧ сервис включён. Это и есть «Работает» из US-05.

Почему `NotConnected` не маршрутизируемый: в триале (О6) оплачены оба транспорта; владелец, открывший мастер WhatsApp и
не дошедший до QR, иначе получил бы все сообщения запертыми на непривязанном номере при приоритете WhatsApp по умолчанию.

### §40.5.2 `NotificationRouting.SelectTargets` — новое правило в начале

```
routable = кандидаты с IsRoutable (по одному на транспорт — Primary)
0 транспортов → Targets = [], SkipReason = null (причину даёт гейт: NotOnPaidPlan / NoUsableChannel)
1 транспорт   → Targets = [он] — режим и приоритет НЕ читаются (US-09, Q-40-2)
2 транспорта  → AllChannels: оба; PriorityChannel: приоритетный (он маршрутизируемый по определению) — без перехода
```

`PriorityChannelUnavailable` при постановке больше не возникает (член enum остаётся для истории). `Candidate.IsUsable`
переименовывается в `IsRoutable` (компилятор найдёт трёх вызывающих). Правило одинаково для записей, заказов и броней:
все три планировщика вызывают одну функцию.

### §40.5.3 `NotificationGate.Evaluate` — новая сигнатура (pure)

```
Evaluate(NotificationType type, MessagingAvailability availability, CompanyNotificationSettings? settings,
         bool recipientOptedOut, bool? clientWantsMessenger, DateTime nowUtc, DateTime visitStartUtc,
         ProviderDeliveryConsentMode mode = AccountsOnly, bool? recipientHasProviderDeliveryConsent = null)
MessagingAvailability(bool PlatformEnabled, bool AnyTransportPaid, bool AnyTransportRoutable)
```

Порядок причин: `PlatformMessagingDisabled` → `RecipientOptedOut` → `ClientDeclinedMessenger` (`clientWantsMessenger == false`)
→ `NoProviderDeliveryConsent` → `NotOnPaidPlan` (ни один транспорт не оплачен) → `NoUsableChannel` (оплачен, но ничего не
маршрутизируется) → `TypeDisabledByCompany` → `BelowMinimumLeadTime`. Отписка стоит раньше галочки (Q-40-5): в журнале
видна настоящая причина.

### §40.5.4 Диспетчер `NotificationDispatchTask` (фаза триажа)

Добавляется до проверки гейта, одним пакетом на проход:
1. Сервис выключен → все кандидаты `Skipped`/`PlatformMessagingDisabled` (US-12: копить не надо).
2. `channel.BillingAccountId ≠ company.BillingAccountId` строки → `Skipped`/`ChannelAccountMismatch` (страховка «номер
   аккаунта A не шлёт за компанию аккаунта B»; один запрос `Companies` по `CompanyId` кандидатов).
3. Канал не `Funded` (по `TransportFunding.Rank` + `AccountMessagingReader`) или приостановлен → `Skipped`/`NotOnPaidPlan`.
4. Канал `DisabledByOwner`/`Replaced` (строки сюда попадать не должны — их разбирает `PendingRebinder`) → `Skipped`/`NoUsableChannel`.
5. Дальше — как сейчас (гейт, удержание до `Connected`, отправка).

### §40.5.5 Постановка: общий порядок для трёх планировщиков

`ShowcaseOutboundGuard` (первым, без изменений) → контекст получателя → `AccountMessagingReader.ForCompanyAsync` (4 запроса,
не зависят от числа компаний аккаунта — NFR «без N+1») → гейт → маршрутизация → строки. Для записей
`clientWantsMessenger = booking.NotifyByMessenger`; для заказов и броней — `null` (их собственные флаги уже решают до
постановки, как сейчас).

---

## §40.6. Представление: три состояния, шаг мастера, настройки компаний (US-05, US-06, T-40-05)

### §40.6.1 Факты (`ChannelDisplayFacts`)

`Transport`, `State`, `LastStateReason`, `PhoneMasked`, `Paid`, `PaidUntil`, `IsTrial`, `RequestNewerThanPayment`,
`IsDuplicate` (`Unfunded`), `Suspended`, `PlatformEnabled`, `OptionSellable`, `IdleSinceUtc`, `IdleDeadlineUtc`.

`RequestNewerThanPayment` = `channel.RequestedAtUtc ≠ null ∧ (LastPaymentAt = null ∨ RequestedAtUtc > LastPaymentAt)` —
«заявка новее последнего подтверждения оплаты». Для владельца «Оплата на проверке» = `!Paid ∧ RequestNewerThanPayment`;
для админа «заявка» = `RequestNewerThanPayment` (видна и заявка, поданная во время пробного периода). Так истёкшая оплата
не висит вечно как «на проверке», а повторная заявка (§40.7.2) снова переводит номер в «Оплата на проверке».

### §40.6.2 Таблица `ChannelPresentation.Display` (первое сработавшее правило; векторы — `channel-vectors.json → display`)

| # | Условие | `displayStatus` | `displayText` (сервер, дословно — `API_CONTRACT_CYCLE40.md` §40.33) | `action` |
|---|---|---|---|---|
| 1 | `State = Replaced` | — (владельцу не отдаётся) | — | — |
| 2 | `!PlatformEnabled` | `Off` | «Рассылки временно отключены платформой» | null |
| 3 | `Suspended` | `Off` | «Приостановлен администратором» | null |
| 4 | `State = DisabledByOwner` | `Off` | «Отключён вами» | `BindNumber`, если `Paid`, иначе `Pay` |
| 5 | `IsDuplicate` | `ActionRequired` | «Лишний номер {М}: сообщения уходят с другого номера. Отвяжите этот» | `Unbind` |
| 6 | `!Paid ∧ RequestNewerThanPayment` | `ActionRequired` | «Оплата на проверке» | null |
| 7 | `!Paid ∧ !OptionSellable` | `ActionRequired` | «Подключение {М} временно недоступно» | null |
| 8 | `!Paid ∧ PaidUntil ≠ null` | `ActionRequired` | «Оплата закончилась {дд.мм.гггг}» | `Pay` |
| 9 | `!Paid` | `ActionRequired` | «Не оплачено» | `Pay` |
| 10 | `State = NotConnected` | `ActionRequired` | «Оплата подтверждена — привяжите номер» | `BindNumber` |
| 11 | `State = Connecting` | `ActionRequired` | «Номер привязывается — отсканируйте QR» | `BindNumber` |
| 12 | `Disconnected ∧ ServerCountryMismatch` | `ActionRequired` | «Требуется вмешательство платформы» | null |
| 13 | `Disconnected` | `ActionRequired` | «Связь с {М} разорвана — подключите номер заново» | `Reconnect` |
| 14 | `NeedsReconnect ∧ SecretUnavailable` | `ActionRequired` | «Нужна повторная привязка после технических работ. Оплата сохранена» | `Reconnect` |
| 15 | `NeedsReconnect` | `ActionRequired` | «Номер отключён: им {N} {дней} никто не пользовался. Оплата сохранена — подключите заново» | `Reconnect` |
| 16 | `Blocked` | `ActionRequired` | «{М} заблокировал этот номер. Подключите другой — оплата сохранится» | `ReplaceNumber` |
| 17 | `Connected ∧ IdleSinceUtc ≠ null` | `ActionRequired` | «Номер отключится {дд.мм}: ни у одной вашей компании не включены сообщения клиентам» | null |
| 18 | `Connected` | `Working` | «Сообщения уходят с номера {маска}» | null |

`{М}` — «WhatsApp» / «MAX» (`TransportDisplayName`). Старое поле `stateText` (`ChannelPresentation.StateText`) остаётся,
получает параметр транспорта (тексты `Disconnected`/`Blocked` больше не говорят «WhatsApp» про MAX) и теряет слова
«Назначенные салоны». Юнит-таблица истинности (T-40-05): 8 состояний × {оплачен, не оплачен, заявка, приостановлен,
сервис выключен} × 2 транспорта — генерируется по векторам.

### §40.6.3 Шаг мастера `ChannelPresentation.WizardStep` (по транспорту; первое сработавшее)

| Условие | Шаг |
|---|---|
| сервис выключен ∨ (не оплачен ∧ опция не продаётся) | `Unavailable` |
| нет живого канала ∨ (не оплачен ∧ !RequestNewerThanPayment) | `Payment` |
| не оплачен ∧ RequestNewerThanPayment | `PaymentPending` |
| приостановлен ∨ `Blocked` ∨ (`Disconnected` ∧ `ServerCountryMismatch`) ∨ `IsDuplicate` | `None` (действие — из меню или «Нужно действие») |
| оплачен ∧ State ∈ {`NotConnected`, `Connecting`, `Disconnected`, `NeedsReconnect`, `DisabledByOwner`} | `Qr` |
| `Connected` | `Done` |

Отдельно в overview — `canRequestPayment` = `!Paid ∨ IsTrial` (пробный номер можно оплатить заранее: пункт меню
«Оплатить после пробного периода» открывает мастер на шаге «Оплата»).

### §40.6.4 Настройки компаний — три маршрута

Общий для трёх: `CompanyMessagingStatus` (чистая функция над `AccountMessagingState` и режимом компании) →
`messagingActive` (сервис включён ∧ есть работающий транспорт), `inactiveText`, `workingTransports`,
`deliveryChoiceVisible`, `priorityWarning`.

- `deliveryChoiceVisible` = оба транспорта работают ∨ (оба маршрутизируемы ∧ режим `PriorityChannel` ∧ приоритетный не
  работает). Во втором случае `priorityWarning` = «Приоритетный номер не работает: выберите другой или „во все“» (US-09).
- Салон (`/api/companies/{id}/notification-settings`): 402 снимаются; `PUT` больше не проверяет «приоритет среди оплаченных»
  (любое значение принимается — при одном транспорте правило §40.5.2 его игнорирует, при двух он маршрутизируем);
  `planAllowsChannel = true`; `channel` = канал приоритетного транспорта аккаунта; `blockedReason` — новые тексты без плана.
  Шаблоны: 402 «Канал не оплачен» снимается (шаблоны, как и типы, готовятся до оплаты).
- Магазин и «Дома»: `messengerAvailable` = у аккаунта есть **оплаченный** транспорт (не обязательно работающий — так
  владелец может включить флаг до привязки QR); 409 `MessengerUnavailable` при включении флага без оплаченного транспорта
  остаётся (флаги этих вертикалей SPEC не меняет); 400 «Приоритетный канал должен быть среди оплаченных…» снимается;
  `channels` = номера аккаунта; тексты `MessengerUnavailableText` без «раздела Подписка».

---

## §40.7. Мастер подключения и действия с номером (US-04, T-40-04)

### §40.7.1 «Можно купить / подключить»

`sellable(X)` = строка опции X `IsActive ∧ PricePerMonth ≠ null ∧ LegalOptionGuards.IsPubliclySellable` (в
`LegalOptionGuards` и `OptionCapabilityCatalog` добавляется `notifications.max` с тем же требованием `TermsOwner`, что у
WhatsApp). Покупка новая — нужно `sellable ∧ сервис включён`. Уже оплаченный канал работает до конца срока, даже если
опция снята с продажи (US-01).

### §40.7.2 Шаг «Оплата» = `POST /api/notification-channels` (заявка)

Новое в теле: `riskAccepted: {version}` (необязательно для старых вкладок; мастер шлёт всегда). Порядок — §40.24 контракта.
Ключевое:
- 402 «по тарифу» нет.
- Живой канал транспорта уже есть: **оплачен и не пробный** → 409 «У вас уже есть номер {М}»; **не оплачен или пробный** →
  заявка продлевается на этой же строке (`RequestedAtUtc = now`, форма и ИНН обновляются, согласие с офертой пишется снова)
  → **200**. Так повторная оплата после истечения не упирается в 409 и не плодит строки. Нет живого канала → новая строка → 201.
- Риск: версия ≠ текущей `ChannelRiskNotice` → 400 «Текст изменился, прочитайте заново»; совпала →
  `RiskAcceptedAtUtc/Version` пишутся **в той же транзакции**, что и заявка. Согласие с офертой — `ConsentLedger`, как сейчас.
- Подстановка формы лица и ИНН — из `overview.transports[].prefill` (последний канал аккаунта с непустым ИНН).

### §40.7.3 Шаг «QR» = `POST …/{id}/connect` + опрос `GET …/{id}/qr`

Изменения `connect`:
1. Разрешённые исходные состояния: `NotConnected`, `NeedsReconnect`, `DisabledByOwner` (как сейчас) **плюс `Disconnected`**
   (кроме `ServerCountryMismatch`). Для `Disconnected` с живым экземпляром: под тем же `channel-connect:{id}` старый экземпляр
   уходит в `OrphanedInstanceId` (порядок «база → провайдер → повтор из базы», §30.4 цикла 4), событие
   `Disconnected → Connecting` с причиной `RebindStarted`; удаление у провайдера — `ChannelHealthTask` (уже умеет).
2. **Идемпотентность.** Канал уже `Connecting` (с экземпляром или ещё без него — первый запрос в полёте) → **202** с тем же
   телом, новый экземпляр не создаётся. Двойной клик и две вкладки дают ровно один экземпляр (advisory-lock + повторное
   чтение состояния, как сейчас).
3. При создании нового экземпляра `ConnectedAtUtc = null` и `AutoTestInstanceId` не трогается (новый экземпляр ≠ старый →
   проверка уйдёт заново). Исправляет найденный дефект №2 §40.0.
4. Проверки: сервис выключен → 409; приостановлен → 409; транспорт не оплачен или канал не `Funded` → 402 «Номер не оплачен»;
   риск не принят → 409 (как сейчас).

Изменения `qr`: канал `Connecting` без записанного экземпляра → **200** `{state: Connecting, qrBase64: null,
refreshAfterSeconds: 2}` вместо 409 (окно между двумя `SaveChanges` в `connect`). Переход в `Connected` делается **под
`channel-connect:{id}`** с повторным чтением состояния — две вкладки, опрашивающие QR, пишут одно событие, а не два.

**Инструкция QR** — сервер, по транспорту (`overview.transports[].qrInstruction`, массив шагов). WhatsApp: «Настройки →
Связанные устройства → Привязка устройства». MAX: шаги из документации GREEN-API MAX + «отключите пароль входа» —
**черновик сверяется на M40** (R-40-1).

QR истёк, окно закрыто — владелец снова открывает мастер: `Connecting` → сразу опрос; экземпляр снят по тайм-ауту
(`NotConnected`) → новый `connect`. Оплата не повторяется.

### §40.7.4 Шаг «Готово»

Наступает, когда `qr` вернул `Connected`. Фронт показывает маску номера, «Работает для всех ваших компаний: N» и результат
проверки из `GET /api/notification-channels/{id}` → `lastTest` (опрос раз в 3 с до результата ≠ `Pending`/`Sending`, не
дольше 60 с; дальше — «Результат проверки появится в списке номеров»). Неудача проверки состояние номера не меняет.

### §40.7.5 «Заменить номер» (`POST …/{id}/replace`) — из любого привязанного состояния (Q-40-4)

Разрешено: `State ∉ {NotConnected, Replaced}`. В одной транзакции под `channel-connect:{id}`: новая строка (тот же
транспорт, аккаунт, владелец, форма, ИНН, **принятие риска копируется**, `RequestedAtUtc` копируется); старый экземпляр →
`OrphanedInstanceId`, секрет стирается; старая строка → `Replaced`, причина `ReplacedAfterBan` (из `Blocked`) или
`ReplacedByOwner`; `Pending` старой строки → `PendingRebinder` на новую строку (тот же транспорт, ключ не меняется).
После коммита — best-effort `Logout` + `DeleteInstance` (как `DecommissionInstanceAsync`). Ответ прежней формы; фронт сразу
открывает мастер на шаге «QR» нового канала. Повторной оплаты нет: новый канал — первый живой транспорта → `Funded`.

### §40.7.6 «Отвязать» (`DELETE …/{id}`) — без изменения поведения, кроме судьбы `Pending` (§40.9)

### §40.7.7 Старые маршруты

`POST …/{id}/companies` → **410** «Назначать компании больше не нужно: номер работает для всех ваших компаний» (тело
не читается, данные не меняются; права — как у остальных маршрутов: чужой канал → 404). `DELETE …/{id}/companies/{companyId}` →
**204** всегда для своего канала. `POST …/{id}/accept-risk` — работает как сейчас.

---

## §40.8. Автоматическое проверочное сообщение — ответ на О-40-2 (и R-40-4)

### §40.8.1 Где хранится результат

На строке канала: `LastTestResult`, `LastTestResultAtUtc`, `AutoTestInstanceId`; история — `ChannelStateEvent` с
`FromState = ToState = Connected` и причиной `TestMessageSent` / `TestMessageFailed` / `TestMessageSkipped`, деталь — текст
результата. Строка канала — быстрое чтение для мастера и списка, журнал — для админа. Ручная проверка (`test-message`)
пишет те же поля и событие.

**Почему не через очередь `OutboundNotifications`:** у строки очереди обязателен `CompanyId`, она проходит гейт компании
(отписка, согласие, типы, витрина), попадает в журнал и сводку компании — а проверка принадлежит номеру аккаунта. Отдельная
мини-очередь на строке канала проще и атомарна.

### §40.8.2 Однократность

1. **Метка.** В `ChannelStateTransition.Apply` (единственное место перехода в `Connected`: QR, вебхук, опрос
   `channel-health`): если `targetState = Connected ∧ reason = Authorized ∧ ProviderInstanceId ≠ null ∧
   AutoTestInstanceId ≠ ProviderInstanceId` → `AutoTestInstanceId = ProviderInstanceId`, `LastTestResult = Pending`,
   `LastTestResultAtUtc = now`. Сохраняется вызывающим вместе с переходом.
   Один экземпляр = одна привязка: первая привязка, переподключение (§40.7.3 — всегда новый экземпляр) и замена (новая
   строка, новый экземпляр) дают новую проверку; возврат того же экземпляра из `Disconnected` в `Connected` (телефон
   был офлайн) — нет. Две вкладки пишут одинаковые значения — двойной метки не бывает.
2. **Захват.** Задача `channel-test-message` (полоса `realtime`, период 10 с, `ScheduledTasks:channel-test-message`):
   выбирает `LastTestResult = Pending ∧ State = Connected` (частичный индекс), и для каждого делает
   `UPDATE "NotificationChannels" SET "LastTestResult" = 1 /*Sending*/, "LastTestResultAtUtc" = @now
   WHERE "Id" = @id AND "LastTestResult" = 0 AND "AutoTestInstanceId" = @inst` — затронута 1 строка = эта копия процесса
   владеет отправкой. Отправка — `transportRegistry.For(transport).SendAsync` с ключом экземпляра, текст
   `TestMessageText(transport)` («Проверка номера {М} для уведомлений ezbook.ru: если вы видите это сообщение, номер
   работает.»).
3. **Сбои.** `Sending` старше 5 минут (процесс упал во время отправки) → `Failed` «Не удалось подтвердить отправку» —
   **без повторной отправки** (at-most-once важнее: два сообщения владельцу хуже одного пропущенного). `Pending` старше 1 часа
   при `State ≠ Connected` → `Failed` «Номер не был на связи».
4. После отправки `LastTestMessageAtUtc = now` — ручная проверка подчиняется прежнему ограничению «раз в 5 минут».

### §40.8.3 Получатель и «сообщение самому себе» (R-40-4)

Получатель — телефон `OwnerUserId` канала (как у ручной проверки). Поведение GREEN-API при отправке на номер, совпадающий с
номером экземпляра, **документом не подтверждено** (для WhatsApp это чат «Вы», для MAX — неизвестно). Решение:
- нормализованный номер канала = нормализованный телефон владельца → **не отправляем**, `SkippedSameNumber`, текст
  «Проверочное сообщение не отправлено: номер совпадает с телефоном вашего аккаунта. Проверьте работу записью на другой
  номер»;
- нет телефона у аккаунта → `SkippedNoOwnerPhone` «У вашего аккаунта не указан номер телефона»;
- сервис выключен или процесс на демо-стенде (`DemoModeOptions.Enabled`) → `SkippedPlatformDisabled`;
- флаг `Notifications:TestMessage:AllowSameNumber` (по умолчанию `false`) — включается после того, как M40-02 подтвердит,
  что отправка «самому себе» доходит.
Пропуск проверки не переводит номер в «Нужно действие».

---

## §40.9. Судьба ожидающих сообщений — ответ на О-40-4 (`PendingRebinder`)

Один помощник `Services/Notifications/PendingRebinder.cs` для трёх событий. Вход: строки `Pending` и «цель» (канал или ничего).
Перепривязка = `ChannelId := цель`, а если транспорт цели другой — `Transport := транспорт цели` и в `IdempotencyKey`
меняется последний сегмент `:{транспорт}` (у всех трёх форматов ключа — записи, заказы, брони — транспорт последний; тест
на все три). Если строка с новым ключом уже есть — исходная **отменяется** (`BookingOrAssignmentCancelled`), дубля не будет.

| Событие | Цель для каждой строки | Нет цели |
|---|---|---|
| Отвязать номер транспорта X (`DELETE`) | маршрутизируемый транспорт Y ≠ X того же аккаунта (после отвязки X эффективно «один транспорт» → правило US-09) | отмена |
| Заменить номер | новая строка того же транспорта | — (цель есть всегда) |
| Передача компании (`CompanyTransferService`, шаг 6) | маршрутизируемый канал **того же** транспорта **нового** аккаунта | отмена (как сейчас) |

Почему не «всегда отмена»: в очереди лежат напоминания на дни вперёд; при отвязке одного мессенджера из двух они
пропали бы, хотя второй работает. В режиме «во все» вторая строка события уже стоит в очереди Y — ключ совпадёт, исходная
отменится, дубля нет. **Правовая оговорка:** перенос на **другой** мессенджер может выходить за согласие клиента, если
текст галочки называет конкретный мессенджер (§8 п. 3 SPEC). Поэтому перенос между транспортами управляется флагом
`Notifications:RebindPendingToOtherTransport` (по умолчанию `true`; `false` → при отвязке только отмена). Решение по
умолчанию пересматривается по ответу legal-counsel без релиза.

---

## §40.10. «Рассылки работают» для клиента — ответ на О-40-3 (`CustomerMessagingOffer`, T-40-06)

### §40.10.1 Правило (чистое `CustomerMessagingOfferRule.Evaluate`)

1. Предусловия: сервис включён ∧ компания активна ∧ не витринная и не на демо-стенде (`ShowcaseOutboundGuard.IsSuppressed`)
   ∧ флаг компании (салон: в маске включён `BookingConfirmed` или `Reminder`; магазин: `CustomerMessengerEnabled`;
   «Дома»: `GuestMessengerEnabled`).
2. `targets` = та же маршрутизация, что при постановке (§40.5.2): над **маршрутизируемыми** транспортами и режимом компании.
3. `transports` = `targets` ∩ **работающие**; `offered` = предусловия ∧ `transports ≠ ∅`.

Так подпись называет ровно тот мессенджер, куда сообщение уйдёт сейчас. Пример: оба оплачены и привязаны, режим «только
WhatsApp», WhatsApp разорван → сообщение встанет в очередь WhatsApp и будет ждать → галочки нет (не обещаем того, что
сейчас не доставится). `checkboxLabel` = «Получать уведомления в WhatsApp» / «… в MAX» / «… в WhatsApp и MAX». Ни номера,
ни состояния, ни оплаты наружу не уходит (NFR приватности).

### §40.10.2 Выдача без лишних запросов

Служба `CustomerMessagingOffer` (scoped, `Services/Notifications/`): `GetAsync(companyId)` и `GetManyAsync(companyIds)` с
`IMemoryCache` 30 с на компанию (ключ `customer-messaging-offer:{companyId}`). Промах кеша = `AccountMessagingReader` (4
запроса на аккаунт) + настройки компании (1 запрос). Результат встраивается **полем в существующий ответ**, отдельного
публичного маршрута нет:

| Сайт | Маршрут | Поле |
|---|---|---|
| ezbook, страница салона и виджет `/embed/:slug` | `GET /api/companies/{slug}` | `CompanyDto.customerMessaging` (в конец; заполняется только этим маршрутом, в списках — null) |
| goods, витрина | `GET /api/storefront/{slug}` | `customerNotifications.messengerOffered` (пересчитывается по правилу) + `customerNotifications.messengerTransports`, `messengerLabel` (в конец) |
| dom, страница дома | `GET /api/stays/public/companies/{slug}/houses/{houseSlug}` | `PublicHouseDto.messenger` (в конец) |

Создание записи/заказа/брони **перепроверяет** правило без кеша (`GetAsync(companyId, bypassCache: true)`): для goods и dom
`notifyByMessenger = true` при `offered = false` молча сохраняется как `false` (как сейчас); для ezbook значение
сохраняется как прислано — гейт при постановке всё равно решит по фактам.

---

## §40.11. Галочка клиента в записи ezbook (US-07, US-08)

- `POST /api/bookings`: в конец `CreateBookingDto` — `bool? NotifyByMessenger = null`.
- `BookingCreationService`: `isStaffManualBooking` → `NotifyByMessenger = null` всегда (Q-40-6); иначе — как прислано.
  `true` → `MessengerConsentVersion` = версия текста `BookingMessengerConsent` из манифеста (null, если текста ещё нет),
  `MessengerConsentAtUtc = now` (как у заказов и броней).
- `NotificationScheduler.BuildContextAsync` читает `booking.NotifyByMessenger` → гейт (§40.5.3). Перенос и отмена берут
  сохранённое значение — новой логики не нужно, контекст строится по записи.
- `LegalTextKey.BookingMessengerConsent` — новая константа **вне `All`** (деплой не ждёт юриста), запасной текст на фронте.
- Выгрузка данных субъекта (`GET /api/profile/export`): в конец записи брони — `notifyByMessenger`,
  `messengerConsentVersion`, `messengerConsentAtUtc`.
- **Умолчание галочки — только фронт** (сервер не подставляет `true`): гость — снята; вошедший — `GET
  /api/notifications/preferences.enabled` (`true` → стоит; `false` → вместо галочки строка «Уведомления в мессенджеры
  выключены в профиле» со ссылкой, поле в запрос **не отправляется**: клиента не спрашивали, решает отписка). Гостю статус
  отписки не раскрывается: ответ создания записи о мессенджере ничего не говорит.

Общий фронтовый компонент `MessengerOptIn` + хук `useMessengerOptInDefault(offer)` в `src/components/notifications/` —
одна реализация для ezbook (`BookingModal`, виджет), goods (`CartPanel`) и dom (`BookingPanel`). Текстовый ключ — параметр
(`BookingMessengerConsent` / `OrderMessengerConsent` / `StayMessengerConsent`).

---

## §40.12. Глобальный выключатель (US-12, T-40-09)

Ключ `PlatformSettings.CustomerMessagingEnabledKey = "notifications.customer-messaging.enabled"`, значения `"true"`/`"false"`,
**нет ключа или не разбирается = включено**. Чтение — `PlatformSettings.IsCustomerMessagingEnabledAsync()` (кеш 60 с,
`PlatformSettingsWriter` инвалидирует после коммита — как остальные ключи). Запись — `PUT /api/admin/platform-settings`,
поле `customerMessagingEnabled` (null = не менять), журнал — `PlatformSettingsWriter`, лог Information «кто и когда».

| Место | Поведение при «выключено» |
|---|---|
| `POST /notification-channels`, `connect`, `test-message` | 409 «Подключение временно недоступно» / «Рассылки временно отключены платформой» |
| Постановка (три планировщика) | `Skipped`/`PlatformMessagingDisabled` |
| Диспетчер | `Pending` → `Skipped`/`PlatformMessagingDisabled`, не отправляются |
| `CustomerMessagingOffer` | `offered = false` |
| Представление (overview, список, настройки компаний) | `Off`, «Рассылки временно отключены платформой» |
| Автопроверка | `SkippedPlatformDisabled` |
| Мастер | `wizardStep = Unavailable` |
| Не затрагивается | экземпляры у провайдера, простой, `channel-health`, оплаченные сроки, push и MAX сотрудникам |

Включение возвращает всё без действий владельца; пропущенные сообщения не досылаются (они уже `Skipped`). Переменная
`NOTIFICATIONS_PROVIDER` и `InstanceCreationEnabled` работают как прежде (технический уровень).

---

## §40.13. Админка (US-10, T-40-08)

### §40.13.1 Таблица — существующий `GET /api/admin/notification-channels` + параметры

Новые параметры: `displayStatus` (`Working|ActionRequired|Off`), `payment` (`Paid|NotPaid|Requested|Suspended|Trial`),
`includeReplaced` (по умолчанию `false` — `Replaced` скрыты; сейчас они в списке). Старые `state`, `paymentState`,
`transport` работают. Фильтрация по вычисляемым полям — «загрузить кандидатов → посчитать → отфильтровать → страница»
(как сейчас для `paymentState`; строк — по одной на канал). В конец `AdminChannelDto`: `displayStatus`, `displayText`,
`stateText`, `phoneMasked`, `paymentText` («оплачено до DD.MM.YYYY» / «не оплачено» / «заявка от DD.MM» /
«приостановлен» / «пробный до DD.MM.YYYY»), `isSuspended`, `createdAt`, `availableActions`. `companyCount` = компании аккаунта.

### §40.13.2 Сводка, карточка, подтверждение оплаты

- Сводка: в конец `AdminChannelSummaryDto` — `working`, `actionRequired`, `off` (без `Replaced`); `pendingRequests` —
  по `RequestNewerThanPayment`; `expiringIn7Days` — по `PaidUntil` транспорта.
- Карточка `GET /api/admin/notification-channels/{id}` — §40.31 контракта: детальное состояние и причина, форма лица и ИНН,
  даты, последняя проверка, журнал состояний (`ChannelStateEvent`, последние 200), журнал оплаты (слияние
  `ChannelPaymentLog` канала и `ChannelOptionChangeLog` транспорта аккаунта, по времени ↓), компании аккаунта, ссылки
  `replacedByChannelId` / `replacesChannelId`.
- `POST /api/admin/notification-channels/{id}/confirm-payment` `{months: 1…12, comment?}`: под `billing-account:{accountId}`;
  `base = max(now, PaidUntilUtc действующей строки)`; `PaidUntilUtc = base + months`; `EndsAtUtc = null`, `Quantity = 1`,
  `GrantedByTrial = false`, `ActivatedAtUtc = now` (это и закрывает «Оплата на проверке»); пишет `ChannelPaymentLog`
  (старый → новый срок, «payment-confirmed: …»), `ChannelOptionChangeLog` (`AdminChannelCard`) и `SubscriptionChangeLog`
  (сводка опций было → стало — тот же журнал, что у назначения в «Биллинг-аккаунтах»).

### §40.13.3 Журнал изменений опций `ChannelOptionChangeLog`

Пишут: `AdminBillingController` (оба пути — «Записи» и «Заказы/Дома», включая «опция не прислана → `EndsAtUtc`»),
`confirm-payment`, `TrialActivationService`, `TrialMailingWindowStarter`, `TrialLifecycleTask`. Только для двух кодов
`ChannelOptionCodes.All`; только если срок или окончание реально изменились.

### §40.13.4 «Биллинг-аккаунты» — изменения назначения опций

Для двух кодов каналов: `PaidUntil` **обязателен** (400 «Для опций WhatsApp и MAX укажите дату окончания оплаты»);
проверка правила тарифа («Опция недоступна на выбранном тарифе») **не применяется**. Для остальных опций — как сейчас.
Владельцу на странице подписки две опции каналов **не предлагаются** в `availableOptions` (платят через мастер — один путь
оплаты), но видны в `options` (купленное).

### §40.13.5 Параметры «канала» в админке

Поле цены (`channelPricePerMonth`) из интерфейса убирается; сервер принимает его и **игнорирует** (не пишет — старые вкладки
не затрут настройку). Срок простоя, параметры триала, «Публичные цены» — как сейчас; выключатель — §40.12.

---

## §40.14. Цены мессенджеров в карточках тарифов (US-11, T-40-10)

`MessengerAddonsBuilder` (чистый) над каталогом опций: для каждого транспорта с `sellable` — `{transport, label, pricePerMonth,
text}`, где `text` = «+ WhatsApp 490 ₽/мес» (без «от»; цена без копеек, если целая, иначе с двумя знаками). Порядок —
WhatsApp, MAX. В конец `PublicPricingDto` (`GET /api/pricing`, кеш каталога) и `OwnerSubscriptionDto` (`GET
/api/billing/subscription`, три линейки) — поле `messengerAddons`. Одинаково для всех тарифов: фронт выводит один и тот же
список под каждой карточкой (`MessengerAddonLines` в `src/components/pricing/`).

---

## §40.15. Фронтенд: структура и общие компоненты (T-40-11, T-40-12)

### §40.15.1 Новые общие компоненты (`frontend/src/components/notifications/`)

| Компонент | Что | Где используется |
|---|---|---|
| `NumbersBlock` | блок «Номера»: «Номера общие для всех ваших компаний», две строки транспортов, «Работает для всех ваших компаний: N» с раскрытием списка; данные — `GET /notification-channels/overview` (query key `['notification-numbers']`); после любого действия — `invalidateQueries` этого ключа и `['notification-channels']` | ezbook вкладка салона, goods, dom |
| `NumberRow` | статус (цвет **и** текст), `displayText`, одна кнопка по `action`, меню «…» (заменить, отвязать, «Оплатить после пробного периода» при `isTrial`, P2 — проверка ещё раз), лишние номера | `NumbersBlock` |
| `ConnectWizard` | `role="dialog"`, `aria-modal`, индикатор шага, фокус на заголовке шага при смене; шаги `PaymentStep`, `PaymentPendingStep`, `QrStep`, `DoneStep`; начальный шаг — `wizardStep` сервера; на `PaymentPending` опрос overview раз в 15 с → переход в `Qr` сам, когда сервер вернёт `Qr` | `NumberRow` |
| `PaymentStep` | цена, форма лица, ИНН (подстановка), галочка оферты (`/offer-channel`), галочка риска (раскрывающийся текст `risk` + `connectionNotice`), кнопка активна при обеих галочках и правдоподобном ИНН (утилита проверки из нынешней формы заявки переносится в `src/utils/`) | `ConnectWizard` |
| `QrStep` | `connect` → опрос `qr` по `refreshAfterSeconds`; `<img alt="QR-код для привязки номера {М}">`; инструкция из `qrInstruction`; «QR истёк — показать новый» | `ConnectWizard` |
| `DoneStep` | маска, N компаний, результат проверки (опрос `GET /{id}` ≤ 60 с) | `ConnectWizard` |
| `DeliveryModeBlock` | «Как доставлять» по `deliveryChoiceVisible` + `priorityWarning` | ezbook, goods, dom |
| `MessengerOptIn` + `useMessengerOptInDefault` | галочка клиента (§40.11) | ezbook, goods, dom |
| `NotificationPreferencesCard` (перенос из `ProfilePage.tsx`, P2) | общий выключатель; текст называет записи, заказы и брони | профили трёх сайтов |
| `ChannelBreachBanner` (переписывается) | баннер по `displayStatus = ActionRequired` и `action ∈ {BindNumber, Reconnect, ReplaceNumber, Unbind}` или простой; ссылка в раздел | шапка кабинета ezbook |

`src/components/pricing/MessengerAddonLines.tsx` — две строки цен. `src/components/admin/notifications/` — таблица,
фильтры, карточка (ezbook-only, админка).

### §40.15.2 Удаляется

`src/pages/owner/NotificationsSection.tsx` (вместе с `ChannelCard`, `OfferCard`), `components/notifications/AssignCompanyDialog.tsx`,
`RiskAcceptanceModal.tsx`, `ChannelRequestModal.tsx` (+ тест), `QrModal.tsx`. `shared-sources.js`: из
`SHARED_EZBOOK_PAGES` убирается `'owner/NotificationsSection'` (новые компоненты лежат в `src/components/**` — общие
по каталогу); guard-тесты goods/dom (`sharedSources.guard.test.ts`) и ESLint-границы зелёные; комментарий в
`eslint.config.js:53` правится.

### §40.15.3 Страницы

- ezbook `CompanyManagePage`: вкладка «Уведомления» → «Уведомления клиентам»; поддержка `?tab=notifications` (ссылки из
  баннера и кабинета); подвкладка «Настройки» = `NotificationSettingsTab` в порядке US-06: `NumbersBlock` →
  `DeliveryModeBlock` → «Какие сообщения отправлять» (неактивно с `inactiveText`, но сохраняемо) → `StaffPushSettingsCard`
  с подзаголовком «Сотрудникам». «Шаблоны» и «Журнал» — без изменений.
- ezbook `CabinetPage`: вкладка «Уведомления» показывает строку «Номера для сообщений клиентам — в настройках каждой
  компании» и ссылку на первый салон (`overview.companies` с `kind = Services`, иначе — ссылка на сайт вида из
  `kinds-summary`). Баннер в шапке — по `overview`.
- goods `ShopNotificationsPage`, dom `NotificationsPage`: `NumbersBlock` вместо `ChannelCard` + `ChannelRequestModal` +
  «заявки»; свои флаги, push, MAX сотрудникам — без изменений; `DeliveryModeBlock` вместо своего выбора режима.
- ezbook `BookingModal` (и через него `EmbedPage`), goods `CartPanel`, dom `BookingPanel`: `MessengerOptIn` по
  `customerMessaging` / `customerNotifications` / `messenger` публичного ответа. В dom галочка сейчас показывается всегда —
  после цикла только при `messenger.offered`.
- Админка `NotificationsAdminTab`: таблица + фильтры + карточка (выезжающая панель) + «Подтвердить оплату» + выключатель
  «Рассылки клиентам в мессенджеры»; 5 плиток вместо 8; поле цены убрано.
- Цены: `PlanCard` (`/pricing`), `BillingPage` (ezbook `/billing`; goods/dom используют её через `shared-sources`) —
  `MessengerAddonLines` под списком возможностей.

### §40.15.4 Типы

`src/types/api-cycle40.generated.ts` (`npm run types:api:cycle40`); рукописные `src/types/index.ts` — `ChannelDto`
(+`displayStatus`, `displayText`, `action`, `lastTest`), `ChannelOffer.transports[].pricePerMonth`, `NotificationSettings`
(+5 полей), `CompanyDto.customerMessaging`, `CreateBookingPayload.notifyByMessenger?`.

---

## §40.16. Структура проекта — что добавляется и меняется

```
ServiceBooking.Core/
├── Entities/  Booking (+3), NotificationChannel (+3), ChannelOptionChangeLog (новая)
└── Enums/     NotificationReason (+3), ChannelStateReason (+5), ChannelTestResult, ChannelOptionChangeSource (новые)

ServiceBooking.Infrastructure/
├── Data/AppDbContext.cs      ChannelOptionChangeLog, индекс TestPending
└── Migrations/               *_Cycle40ChannelOptions.cs

ServiceBooking.API/
├── Services/Notifications/Funding/   ChannelOptionCodes, ChannelOptionFunding (pure), TransportFunding (pure),
│                                     AccountMessagingReader, AccountMessagingState, MessagingDemand, ChannelOptionLog
├── Services/Notifications/           NotificationGate (новая сигнатура), NotificationRouting (+правило одного транспорта),
│                                     PendingRebinder, CustomerMessagingOffer(+Rule), CompanyMessagingStatus,
│                                     ChannelTestMessenger (+TestMessageText), ChannelStateTransition (+метка проверки),
│                                     ChannelFundingReader (фасад), ChannelPaymentState, ChannelIdleCalculator (bool demand),
│                                     PlatformSettings (+ключ выключателя)
├── Services/ChannelPresentation.cs   Display, WizardStep, StateText(transport), QrInstruction, SettingsBlockedReason
├── Services/Billing/                 удалить ChannelEligibility, TrialMailingRulePolicy, ChannelFunding.Rank(int);
│                                     SubscriptionResolver, OrdersPlanResolver, OwnerSubscriptionService, TrialActivationService,
│                                     BillingTexts.FundingText, MessengerAddonsBuilder, LegalOptionGuards, OptionCapabilityCatalog
├── Services/Stays/                   StaysPlanResolver (−поле), StayMessageScheduler, StayBookingCreationService
├── Services/Orders/                  OrderMessageScheduler, OrderCreationService
├── Services/Shops/ShopChannelReader  на AccountMessagingReader (−IsMessengerAvailableAsync)
├── Services/Bookings/BookingCreationService   NotifyByMessenger, версия согласия
├── Services/NotificationScheduler.cs           контекст по аккаунту, гейт, маршрутизация
├── Services/Billing/CompanyTransferService.cs  PendingRebinder
├── Services/Scheduling/Tasks/        ChannelTestMessageTask (новая), NotificationDispatchTask, ChannelHealthTask (простой),
│                                     TrialLifecycleTask (журнал)
├── Controllers/                      NotificationChannelsController (overview, create, connect, qr, replace, companies),
│                                     CompanyNotificationsController, ShopNotificationsController, Stays/StaysCompaniesController,
│                                     AdminChannelsController (+card, confirm-payment), AdminPlatformController (+выключатель),
│                                     AdminBillingController (дата обязательна, правила не читаются, журнал),
│                                     CompaniesController.GetBySlug, StorefrontController, Stays/StaysPublicController,
│                                     CompanyTransferController, AdminAccountDtoBuilder, PricingController
├── DTOs/Notifications/               ChannelDto (+4), ChannelOfferDto (+цены), NumbersOverviewDto, ChannelTestDto,
│                                     NotificationSettingsDto (+5), CustomerMessagingOfferDto, Admin* (+card)
└── appsettings*.json                 ScheduledTasks:channel-test-message, Notifications:TestMessage:AllowSameNumber,
                                      Notifications:RebindPendingToOtherTransport

ServiceBooking.UnitTests/   ChannelOptionFundingTests, TransportFundingTests, NotificationRoutingSingleTransportTests,
                            NotificationGateTests (переписан), ChannelDisplayTruthTableTests, WizardStepTests,
                            CustomerMessagingOfferRuleTests, MessagingDemandTests, PendingRebindKeyTests,
                            MessengerAddonsBuilderTests (векторы channel-vectors.json), AllowNotificationChannelReadGuardTests,
                            AssignmentReadGuardTests, Cycle40EnumAppendOnlyTests
ServiceBooking.Tests/       Tests/Cycle40*: ChannelOptionsMigrationTests, TransportFundingTests, NoPlanFlagTests, TrialBothOptionsTests,
                            AllCompaniesOnNumberTests, CompanyTransferRebindTests, CrossAccountIsolationTests, WizardApiTests,
                            AutoTestMessageTests, ReplaceFromConnectedTests, BookingMessengerOptInTests, SingleTransportRoutingTests,
                            PlatformSwitchTests, LegacyRoutesTests, AdminChannelCardTests, PendingRebindTests, Cycle40RollbackReadTests,
                            Cycle40ContractTests (OpenApiContract по cycle40)

frontend/src/components/notifications/   NumbersBlock, NumberRow, ConnectWizard (+шаги), DeliveryModeBlock, MessengerOptIn,
                                         useMessengerOptInDefault, NotificationPreferencesCard (P2), ChannelBreachBanner
frontend/src/components/pricing/MessengerAddonLines.tsx
frontend/src/components/admin/notifications/   ChannelsTable, ChannelFilters, ChannelCardPanel, ConfirmPaymentDialog
frontend/src/utils/channelRules.ts (+test по channel-vectors.json)
frontend/src/types/api-cycle40.generated.ts; package.json types:api:cycle40; scripts/contracts-to-json.mjs (+cycle40)
contracts/cycle40/       openapi.yaml, openapi.json, channel-vectors.json
contracts/redocly.yaml   +cycle40 в списке
deploy/checks/           cycle40-channels-report.sql, cycle40-rollback-assignments.sql
.github/workflows/ci.yml lint/types/json cycle40
DEPLOY.md §29, API_DOCUMENTATION.md раздел «Каналы рассылок (цикл 40)»
```

`Cycle22RouteTable.golden.txt` — **3 новых маршрута** (§40.37 контракта); атрибуты существующих маршрутов не меняются.

---

## §40.17. Выкат, отчёт и откат (T-40-02, DO)

### §40.17.1 `deploy/checks/cycle40-channels-report.sql` — только чтение, на копии боевой базы **до** выката

Четыре блока, каждый выводит число и (до 50) строк:
1. **Стоп-сигнал.** Каналы, оплаченные по старому правилу (`notifications.whatsapp` с `Quantity ≥ 1`, не `EndsAtUtc ≤ now`,
   `PaidUntilUtc ≥ now` или `null` при годной подписке, правило тарифа `Extra/Included`) **и** по новому. Ожидается **0 и 0**.
   Любая строка → выкат останавливается до решения заказчика (R-40-2).
2. Строки опций каналов с `PaidUntilUtc IS NULL`, раздельно `GrantedByTrial` true/false (§5.3, О-40-5).
3. Аккаунты с двумя и более живыми каналами одного транспорта (§5.2).
4. Компании, которые начнут слать без назначения (для сведения, О3): активные компании аккаунтов, у которых есть живой
   канал, без строки назначения на этот транспорт.

### §40.17.2 `deploy/checks/cycle40-rollback-assignments.sql` — только на случай отката релиза

Идемпотентно (`INSERT … WHERE NOT EXISTS` по `(CompanyId, Transport)`): каждой компании каждого аккаунта — назначение на
**первый живой** канал каждого транспорта аккаунта (`ORDER BY "CreatedAt", "Id"`). Только добавление. Без него старый код
после отката не шлёт за компании, созданные после выката (§5.2 SPEC).

### §40.17.3 Порядок (`DEPLOY.md` §29, DO-40-03)

1. Отчёт §40.17.1 на копии боя → 0 оплаченных; иначе стоп.
2. Выкат релиза (миграция применяется при старте, как обычно).
3. Убедиться, что выключатель «Рассылки клиентам» включён, а `NOTIFICATIONS_PROVIDER` — как на бою (проверить на машине,
   не по документам).
4. M40-01…08 (§40.18.5) на реальных номерах.
5. Открыть функцию реальным владельцам — только после вычитки текстов legal-counsel (§8 SPEC).
Откат: релиз назад + скрипт §40.17.2; БД назад не откатывается.

---

## §40.18. Разбивка работ и параллельность

Контракт (`openapi.yaml` + `channel-vectors.json`) готов **до** кода — фронтенд стартует на prism-моке
(`npx @stoplight/prism mock contracts/cycle40/openapi.yaml --port 4040`) в день 1.

### §40.18.1 Backend

| # | Задача | SPEC | Зависит от | Параллельно с |
|---|---|---|---|---|
| BE-40-C | `contracts/cycle40/openapi.yaml` по `API_CONTRACT_CYCLE40.md` §40.38 + `channel-vectors.json` §40.39, `redocly lint` зелёный, `openapi.json` генератом | T-40-13 | — | всё; **первый коммит** |
| BE-40-P | Чистые правила + юнит-тесты по векторам: `ChannelOptionFunding`, `TransportFunding`, `NotificationRouting` (один транспорт), `NotificationGate` (новая сигнатура), `ChannelPresentation.Display/WizardStep/StateText`, `CompanyMessagingStatus`, `CustomerMessagingOfferRule`, `MessagingDemand`-предикат, `PendingRebind` (ключи трёх форматов), `MessengerAddonsBuilder`, тексты | T-40-01, -05, -07 | BE-40-C (векторы) | всё |
| BE-40-M | Сущности, enum, `AppDbContext`, **одна миграция** `Cycle40ChannelOptions` (§40.2), `ShowcaseOwnership`, `ChannelOptionCodes`, `Cycle40EnumAppendOnlyTests` — **один разработчик, один коммит** | T-40-02 | — | BE-40-P |
| BE-40-1 | Оплата: `AccountMessagingReader`, фасад `ChannelFundingReader`, удаление полей из `EffectivePlan`/`OrdersPlan`/`StaysPlan`, `ChannelEligibility`, `TrialMailingRulePolicy`; `SubscriptionResolver`; триал на обе опции; `AdminBillingController` (дата обязательна, правила не читаются); `ChannelOptionLog` во всех писателях; `OwnerSubscriptionService` (тексты, `HasNumber`, `availableOptions`); `LegalOptionGuards`/`OptionCapabilityCatalog` (+max); стражи §40.3.4 | US-01, US-02 | BE-40-M, BE-40-P | BE-40-3 |
| BE-40-2 | Все компании на номере: 26 мест §40.4.2; три планировщика; диспетчер (§40.5.4); `PendingRebinder` (отвязка, замена, передача); простой по `MessagingDemand`; `AssignmentReadGuardTests`; `ShopChannelReader` | US-03, US-09 | BE-40-1 | BE-40-4 |
| BE-40-3 | Мастер: `overview`; `POST` (риск в запросе, продление заявки, подстановка); `connect` (из `Disconnected`, идемпотентный 202, сброс `ConnectedAtUtc`); `qr` (без 409 в окне, переход под замком); `replace` из любого привязанного; `companies` 410/204; автопроверка (`ChannelStateTransition` + `ChannelTestMessageTask` + конфигурация); `test-message` пишет результат | US-04, US-14 | BE-40-1 | BE-40-2 |
| BE-40-4 | Представление в `ChannelDto`/overview; настройки компаний трёх видов (§40.6.4), снятие 402 (настройки, шаблоны) | US-05, US-06 | BE-40-1, BE-40-P | BE-40-5 |
| BE-40-5 | `CustomerMessagingOffer` + кеш, поля в трёх публичных ответах; `Bookings.NotifyByMessenger` + версия согласия + `BookingMessengerConsent`; перепроверка при создании записи/заказа/брони; выгрузка | US-07, US-08, T-40-06 | BE-40-2 | BE-40-4, BE-40-6 |
| BE-40-6 (P1) | Выключатель (§40.12, семь мест); админ: фильтры, сводка, карточка, `confirm-payment`, игнор поля цены | US-10, US-12 | BE-40-1 (журнал), BE-40-4 (представление) | BE-40-7 |
| BE-40-7 (P1) | `messengerAddons` в `/api/pricing` и подписке | US-11 | BE-40-1 | BE-40-6 |
| BE-40-8 | `Cycle22RouteTable.golden.txt`, `OpenApiContractValidatorTests` `[InlineData("cycle40")]`, функциональные `CY40-*`, `API_DOCUMENTATION.md` | T-40-13 | в конце | — |

US-12 по SPEC — P1, но не урезается последним: проверка выключателя в гейте (`PlatformEnabled`) входит в BE-40-P/BE-40-2
сразу (это одна строка правила); BE-40-6 добавляет только админский переключатель и тексты.

### §40.18.2 Frontend

| # | Задача | SPEC | Зависит от | Параллельно с |
|---|---|---|---|---|
| FE-40-0 | `types:api:cycle40`, генерат, API-клиенты (`notificationChannelsApi.overview`, админ), `channelRules.ts` + тест по `channel-vectors.json`, фикстуры | T-40-13 | BE-40-C | — |
| FE-40-1 | `NumbersBlock`, `NumberRow`, `ConnectWizard` и шаги, меню «…», удаление старых модалок, `shared-sources.js` + guard-тесты | US-04, US-05, T-40-11 | FE-40-0 | FE-40-3…6 |
| FE-40-2 | ezbook: слияние вкладки салона (US-06 порядок), `?tab=notifications`, кабинет (строка + ссылка), `ChannelBreachBanner`, `DeliveryModeBlock` | US-06, US-09, T-40-12 | FE-40-1 | FE-40-4 |
| FE-40-3 | goods `ShopNotificationsPage`, dom `NotificationsPage` на `NumbersBlock` + `DeliveryModeBlock` | US-06 | FE-40-1 | FE-40-2 |
| FE-40-4 | `MessengerOptIn` + умолчания; ezbook `BookingModal`/виджет, goods `CartPanel`, dom `BookingPanel` | US-07, US-08 | FE-40-0 | FE-40-1 |
| FE-40-5 (P1) | Админка: таблица, фильтры, карточка с журналами, «Подтвердить оплату», выключатель, 5 плиток, без цены | US-10, US-12 | FE-40-0 | FE-40-1 |
| FE-40-6 (P1) | `MessengerAddonLines` в `PlanCard` и `BillingPage` | US-11 | FE-40-0 | всё |
| FE-40-7 (P2) | `NotificationPreferencesCard` в профилях goods и dom; «Отправить проверочное ещё раз» в меню | US-13, US-14 | FE-40-1 | — |

### §40.18.3 DevOps

| # | Задача | Когда |
|---|---|---|
| DO-40-01 | `deploy/checks/cycle40-channels-report.sql`, `cycle40-rollback-assignments.sql` (§40.17) + прогон на копии боя, отчёт заказчику | сразу; результат — до мержа в `master` |
| DO-40-02 | CI: `redocly lint` + строка в `contracts/redocly.yaml`, `types:api:cycle40` + `git diff --exit-code`, `contracts-to-json.mjs` (+cycle40) и сверка JSON | после BE-40-C |
| DO-40-03 | `DEPLOY.md` §29 (порядок §40.17.3, выключатель как стоп-кран, конфигурация `channel-test-message`, флаги) | сразу |
| DO-40-04 | Стенд для M40: реальный партнёрский аккаунт GREEN-API, два тестовых номера (WhatsApp, MAX), **не** совпадающие с телефоном владельца, плюс один совпадающий для M40-02; `NOTIFICATIONS_PROVIDER=green-api`, `InstanceCreationEnabled=true`; кто проводит — назначает заказчик (R-40-1) | до приёмки |

Воркфлоу реализации может пропустить DevOps-трек — DO-40-* сверяются с `git log` до мержа.

### §40.18.4 QA

Кейсы `CY40-*` в `TEST_CATALOG.md` (список T-40-13 SPEC 1:1 + §40.0 «найдено попутно» №1–№3); schemathesis по
`contracts/cycle40/openapi.yaml`; векторы `channel-vectors.json` в C# и TS; матрица «8 состояний × 5 фактов × 2 транспорта»;
параллельные тесты: два `connect` → один экземпляр; две вкладки `qr` + задача → ровно одно проверочное; две копии задачи
`channel-test-message` → одна отправка; регресс салонов/магазинов/«Домов» — полный прогон, числа не ниже базы цикла 37;
**откат** — старый код (сборка `ece8038`) читает базу после новой миграции и после записи новых значений enum
(`Cycle40RollbackReadTests` + ручной M40-08); ручные M40 (§40.18.5); «зелёный прогон ≠ функционал» — grep классов §40.16.

### §40.18.5 Ручные M40 (гейт выката, не мержа)

M40-01 WhatsApp: заявка → «Подтвердить оплату» в карточке → QR → «Готово» → проверочное пришло. M40-02 то же MAX + проверка
«самому себе» на номере владельца (решение по флагу §40.8.3). M40-03 запись с галочкой → подтверждение пришло; без галочки — не
пришло. M40-04 напоминание. M40-05 замена номера из `Connected`; отвязать и подключить снова; сообщения из очереди не потерялись.
M40-06 выключатель. M40-07 мастер и галочки на 360 и 1280 px, клавиатура и экранный диктор. M40-08 откат на стенде: релиз
назад + скрипт назначений → салоны шлют.

### §40.18.6 Точки синхронизации BE↔FE

| Что | Где зафиксировано |
|---|---|
| Форма DTO, enum, коды | `openapi.yaml` → генерат |
| Три состояния, шаг мастера, маршрутизация, «рассылки работают», строки цен | `channel-vectors.json` |
| Тексты 400/402/409/410/429 | `API_CONTRACT_CYCLE40.md` §40.33 — фронт печатает `response.data` дословно |
| Что показывать (кнопки, блоки, галочки) | поля сервера: `action`, `wizardStep`, `canRequestPayment`, `deliveryChoiceVisible`, `messagingActive`, `customerMessaging.offered` — фронт не вычисляет сам |

### §40.18.7 Если не укладываемся

Порядок урезания SPEC: US-40-14 → US-40-13 → US-40-11 → US-40-10. При урезанном US-10 оплату подтверждают в
«Биллинг-аккаунтах» (§40.13.4 работает без карточки), журнал `ChannelOptionChangeLog` пишется всё равно. US-12 не режется;
если всё же — стоп-краном служит «у опции нет цены» (§40.7.1).

---

## §40.19. Риски, решения и отклонения от буквы SPEC

| # | Риск | Решение |
|---|---|---|
| R-40-1 | Живой отправки через GREEN-API не было ни разу | M40 на реальных номерах как гейт выката (DO-40-04); выключатель — стоп-кран; инструкция MAX — черновик до M40 |
| R-40-2 | «Оплаченных каналов нет» неверно | отчёт §40.17.1 блок 1 — стоп-сигнал; наследное правило §40.3.3 остаётся в коде |
| R-40-3 | Поведение меняется без действий владельца (О2, О3, О4) | принято заказчиком; блок 4 отчёта даёт число затронутых компаний |
| R-40-4 | Проверка «самому себе» | пропуск с понятным текстом + флаг после M40-02 (§40.8.3) |
| R-40-5 | Владелец не вернётся после оплаты | «Оплата подтверждена — привяжите номер» в разделе, баннер в шапке кабинета, опрос в открытом мастере |
| R-40-6 | Демо и витрина | `ShowcaseOutboundGuard` первым в планировщиках и диспетчере (без изменений); `CustomerMessagingOffer` для витрины → `offered = false`; автопроверка на демо-стенде пропускается; мастер в демо доходит до «Оплата на проверке» |
| R-40-7 | Триал удваивает экземпляры | экземпляр создаётся только на шаге QR (как сейчас) |
| R-40-8 (новый) | Перенос `Pending` на другой мессенджер выходит за согласие клиента | флаг `RebindPendingToOtherTransport` (§40.9), вопрос legal-counsel |
| R-40-9 (новый) | Удаление полей из рекордов плана ломает много тестов | это намеренно: компилятор и стражи показывают все места; каждое изменённое ожидание — в отчёте разработчика |
| R-40-10 (новый) | Приостановка админом начинает реально останавливать отправку | исправление (§40.0 №1): на бою оплаченных каналов нет, затронутых нет; описано в `CHANGELOG` |
| R-40-11 (новый) | Две копии `qr`/вебхук/опрос одновременно переводят в `Connected` | переход под `channel-connect:{id}`; метка проверки идемпотентна; захват задачи — условный `UPDATE` |
| R-40-12 (новый) | Откат после записи новых enum в журнал | `Cycle40RollbackReadTests` + M40-08 |

**Отклонения от буквы SPEC (читать обязательно):**
1. **`POST /api/notification-channels` отвечает 200 вместо 409**, если у аккаунта уже есть **неоплаченный или пробный**
   живой канал транспорта: заявка продлевается на той же строке (§40.7.2). Без этого повторная оплата после истечения
   невозможна.
2. **«Оплачен ровно один транспорт» (US-09) уточнено до «маршрутизируем ровно один»** (§40.5.1): оплаченный, но ни разу не
   привязанный или отключённый владельцем номер не забирает на себя сообщения. Иначе триал (оба оплачены) запирал бы
   сообщения на непривязанном WhatsApp.
3. **Галочка клиента показывается, только если сообщение уйдёт сейчас** (§40.10.1): при сломанном приоритетном номере в
   режиме «только приоритетный» галочки нет, хотя второй номер работает — следствие запрета тихого перехода (US-09).
4. **`messengerAvailable` магазина и «Домов» — «есть оплаченный транспорт»**, а 409 `MessengerUnavailable` при включении флага
   сохраняется (флаги этих вертикалей SPEC не меняет); у салона 402 сняты полностью, включая шаблоны.
5. **Строки опций без даты** — правило оставлено в коде независимо от отчёта (О-40-5, §40.3.3).
6. **Две опции каналов не предлагаются** в «доступных опциях» страницы подписки — один путь оплаты через мастер (§40.13.4).
7. **`Replaced` по умолчанию скрыты** в админской таблице (сейчас показываются) — по US-10.
8. **Машиночитаемый контракт** в этом заходе не создан (указание заказчика архитектуры): состав — `API_CONTRACT_CYCLE40.md`
   §40.38–§40.39, задача BE-40-C — первый коммит цикла.

**Ответы на вопросы архитектору (§9 SPEC):** О-40-1 — §40.4.2 (26 мест + фронт + тесты); О-40-2 — §40.8; О-40-3 — §40.10;
О-40-4 — §40.9; О-40-5 — §40.3.3. Вопросов к заказчику архитектура не добавляет; к legal-counsel — R-40-8 (перенос между
мессенджерами) и отклонение №3 (подпись галочки называет конкретный мессенджер).
