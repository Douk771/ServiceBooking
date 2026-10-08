# ARCHITECTURE — цикл 40 ServiceBooking: упрощение каналов рассылок (WhatsApp / MAX)

**Разделы §40.0–§40.19.** Вход: `SPEC.md` цикла 40 (редакция 2026-10-08, решения Р1–Р10, Q-40-1…7, ответы О1–О8),
`LEGAL_REVIEW_CYCLE40.md` (требования Т40-L-01…14, §11) и **решения заказчика от 2026-10-09** (§40.0a — кодировать по ним,
а не по букве SPEC), `CURRENT_STATE.md` (шапка на `a7e3168`), код `develop` = `ece8038`. Ветка цикла —
`cycle/040-simplify-notification-channels` (подготовлена devops; архитектор веток не трогает).

**Документы цикла:**

| Файл | Что | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE40.md` (этот) | решения, модель, механизмы, структура, задачи, риски, ответы на §9 SPEC | все |
| `API_CONTRACT_CYCLE40.md` (§40.20–§40.39) | контракт словами: маршруты, порядок проверок, коды, тексты, изменения существующих маршрутов | backend, frontend, QA |
| `contracts/cycle40/openapi.yaml` (+ `openapi.json` генератом) | **источник истины по форме** (OpenAPI 3.0.3) — **в этом заходе не создан** по указанию заказчика архитектуры; полный состав описан в `API_CONTRACT_CYCLE40.md` §40.38, пишется задачей **BE-40-C первым коммитом цикла**, до кода | backend, frontend, QA, CI |
| `contracts/cycle40/channel-vectors.json` | эталон правил: оплата транспорта, доступность опций, маршрутизация, согласие на сообщения, три состояния, шаг мастера, «рассылки работают», строки цен — один набор векторов для C# и TS; состав — `API_CONTRACT_CYCLE40.md` §40.39; пишется в BE-40-C | backend, frontend, QA |

Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — документы цикла 3, по конвенции не перезаписываются.

---

## §40.0a. Решения заказчика по правовому обзору (2026-10-09)

| # | Решение | Что меняется против SPEC | Раздел |
|---|---|---|---|
| Р40-Ю1 | **WhatsApp строится, но закрыт для владельцев** до заключения живого юриста (обзор §2.1, §5.4, §10.3). Нужен переключатель доступности **каждой** опции без релиза кода. По умолчанию WhatsApp закрыт, MAX открыт. Закрытая опция: не в офере, не в мастере, не в карточках тарифов, не выдаётся триалом, заявка на неё отклоняется | О6 «триал даёт оба» — пока WhatsApp закрыт, триал даёт только MAX | §40.7.1, §40.3.5 |
| Р40-Ю2 | **Записи, созданные сотрудником** (Т40-L-10): в форме — отметка «Клиент согласился на сообщения», по умолчанию снята. Без отметки не шлём — кроме номера, принадлежащего аккаунту с действующим согласием `ProviderDelivery`. В записи хранятся версия текста согласия, время отметки и кто отметил | Q-40-6 «как до цикла» **отменён**; то же правило — для `null` от старых клиентов | §40.11 |
| Р40-Ю3 | `RebindPendingToOtherTransport` **по умолчанию выключен**: при отвязке мессенджера его ожидающие сообщения отменяются | §40.9 первой редакции (по умолчанию `true`) | §40.9 |
| Р40-Ю4 | Требования Т40-L учтены в архитектуре — таблица ниже | — | — |

**Т40-L → где закрыто:**

| ID | Требование (кратко) | Где | Кто |
|---|---|---|---|
| Т40-L-01 | Цены опций не задавать / держать неактивными до публикации D3 + Прил. 1; WhatsApp — до заключения юриста | эксплуатация: `DEPLOY.md` §29 + переключатель Р40-Ю1 (§40.7.1) | DO-40-03, SA |
| Т40-L-02 | Две отметки в мастере, текст риска полностью до отметки, один запрос | §40.7.2 | BE-40-3, FE-40-1 |
| Т40-L-03 | Шаг «Условия» перед первым QR каждого мессенджера, **включая триал**; `connect` без риска текущей версии → 409 | §40.6.3 (шаг `Terms`), §40.7.2, §40.7.3 | BE-40-3, FE-40-1 |
| Т40-L-04 | `LegalOptionGuards`: `notifications.max → TermsOwner` | §40.7.1 | BE-40-1 |
| Т40-L-05 | Название компании-отправителя в каждом сообщении, системно | §40.11a | BE-40-5 |
| Т40-L-06 | Продление оплаченных дней при выключении платформой > 24 ч | **не в объёме этого обновления** — решение заказчика; ручное продление — «Подтвердить оплату» / «Биллинг-аккаунты» по журналу выключателя (§40.19, R-40-13) | — |
| Т40-L-07 | Отметка вошедшего в ezbook/goods/dom пишет `PdnConsent/ProviderDelivery` в `ConsentLedger` | §40.11.3 | BE-40-5 |
| Т40-L-08 | Старые согласия `ProviderDelivery` не покрывают MAX | §40.11.5 — правовой стороной (Material-версия D4) + проверка QA-40-7 | legal, QA |
| Т40-L-09 | Предзаполнение «стоит» — только при действующем `ProviderDelivery` и без отписки | §40.11.4 | BE-40-5, FE-40-4 |
| Т40-L-10 | Отметка сотрудника; `null` → только аккаунт с согласием | §40.11.1–§40.11.2, Р40-Ю2 | BE-40-5, FE-40-4 |
| Т40-L-11 | `/pricing` и подписка: ссылка на `/offer-channel`, налоговая оговорка, сноска о блокировке WhatsApp; строка только при выполнении п. 6.13.13 | §40.14 | BE-40-7, FE-40-6 |
| Т40-L-12 | Версия текста и время отметки в записи ezbook | §40.2.1, §40.11.1 | BE-40-M, BE-40-5 |
| Т40-L-13 | Текст мастера про статус ИП/организации/самозанятого | `API_CONTRACT_CYCLE40.md` §40.33.12 | FE-40-1 |
| Т40-L-14 | Средства обхода блокировок не упоминать | страж `MessengerTextsForbiddenWordsTests` (§40.16) | BE-40-P, FE-40-1 |
| обзор §12 п. 9 | Где обрабатывает данные ГРИН-АПИ (WhatsApp и MAX), `ServerCountry` | §40.7.4: страна экземпляра пишется в канал, ожидаемая — по транспорту; DO-40-05 снимает значения с боя для `{{МЕСТО_ОБРАБОТКИ_ПОСРЕДНИКОМ}}` | BE-40-3, DO-40-05 |

---

## §40.0. Итог решений одним экраном

| # | Вопрос | Решение | Раздел |
|---|---|---|---|
| A1 | Где считается «транспорт X оплачен» | Одна чистая функция `ChannelOptionFunding.Evaluate` + один пакетный читатель `AccountMessagingReader`. `PaidNotificationNumbers`, `ChannelFunding.Rank(N)`, `ChannelEligibility`, чтение `PlanOptionRule` и годности подписки для двух опций — **удаляются из кода** (не из БД) | §40.3 |
| A2 | Флаг тарифа | Поле `AllowNotificationChannel` **удаляется из рекордов** `EffectivePlan`, `OrdersPlan`, `StaysPlan`; колонка БД и свойство сущности остаются; страж-тест запрещает новые чтения | §40.3.4 |
| A3 | Номер на все компании | «Каналы для компании» = живые каналы **аккаунта компании**. `ChannelCompanyAssignments` не читается и не пишется (кроме удаления строк ради составного FK) — перечень 26 мест в §40.4 | §40.4 |
| A4 | Маршрутизация при одном транспорте | «Маршрутизируемый транспорт» (оплачен ∧ номер первый живой ∧ не приостановлен ∧ номер **хоть раз привязан**). Один → всё в него; два → сохранённый режим без тихого перехода | §40.5 |
| A5 | Три состояния | `ChannelPresentation.Display(facts)` → `displayStatus` / `displayText` / `action`; шаг мастера — `WizardStep(facts)` (+ шаг `Terms`) | §40.6 |
| A6 | Мастер | `GET /api/notification-channels/overview`; `POST /api/notification-channels` — заявка **или** только «Условия» (`paymentRequest: false`); `connect` идемпотентен, требует риск **текущей** версии и заявленный статус | §40.7 |
| A7 | Автопроверка (О-40-2) | Мини-очередь на строке канала, метка в `ChannelStateTransition.Apply`, задача `channel-test-message`, ключ однократности — `ProviderInstanceId` | §40.8 |
| A8 | Ожидающие сообщения (О-40-4) | `PendingRebinder`: замена номера и передача компании — перепривязка на тот же транспорт; отвязка — **отмена** (перенос на другой мессенджер выключен, Р40-Ю3) | §40.9 |
| A9 | «Рассылки работают» (О-40-3) | `CustomerMessagingOffer` с кешем 30 с; поле в существующих публичных ответах | §40.10 |
| A10 | Согласие клиента | `MessengerConsentRule` для трёх вертикалей: `true` (отметка клиента или сотрудника) → шлём; `false` → нет; `null` → только номер аккаунта с действующим `ProviderDelivery`. Отметка вошедшего пишет согласие в `ConsentLedger`; предзаполнение — только по действующему согласию | §40.11 |
| A11 | Глобальный выключатель | `notifications.customer-messaging.enabled` (нет ключа = включено) | §40.12 |
| A12 | Админка | Таблица, карточка с двумя журналами, «Подтвердить оплату», **переключатели доступности двух опций**, выключатель; журнал `ChannelOptionChangeLogs` | §40.13 |
| A13 | Строки без своей даты (О-40-5) | Правило остаётся в коде | §40.3.3 |
| A14 | Миграция | Одна `Cycle40ChannelOptions`: опция `notifications.max`, 4 колонки `Bookings`, 4 колонки `NotificationChannels`, таблица `ChannelOptionChangeLogs`. Только добавления | §40.2 |
| A15 | Доступность опций (Р40-Ю1) | Настройки платформы `notifications.option.whatsapp.open` / `notifications.option.max.open`; без ключа — умолчание из конфигурации (`false` / `true`). Входит в «продаётся» (`sellable`) — одно место; его читают офер, мастер, заявка, триал, цены, админ-оплата | §40.7.1 |
| A16 | Название компании (Т40-L-05) | Строка «{Компания}:» перед текстом шаблона салона — системно; тексты заказов и броней уже начинаются с названия — закрепляется тестом | §40.11a |

**Найдено по коду и исправляется попутно:**
1. **Приостановка админом сейчас не останавливает отправку.** `IsSuspendedByAdmin` читается только в представлении;
   после цикла приостановленный номер **не маршрутизируемый** (§40.5).
2. **Переподключение после `NeedsReconnect` может само сорваться в `Disconnected`.** `Connect` не сбрасывает
   `ConnectedAtUtc`; `ChannelHealthTask` опрашивает и `Connecting`; `ChannelStateMapper` при `hadBeenConnected = true`
   отображает `notAuthorized` в `Disconnected`. После цикла `connect` обнуляет `ConnectedAtUtc` при создании экземпляра.
3. **Пробный период «Записей» сейчас не даёт рассылок** при тарифе без правила `Included` (цикл 28). О6 отменяет это — с
   поправкой Р40-Ю1 (только открытые опции, §40.3.5).
4. **`connect` проверяет только факт принятия риска, а не версию** (`RiskAcceptedAtUtc is null`). Новая редакция риска
   (обзор §4) иначе не требовала бы повторного принятия (Т40-L-03).

---

## §40.1. Стек: новых зависимостей — ноль

ASP.NET Core 8 + EF Core 8 + PostgreSQL 16, React + Vite + TS + TanStack Query, три фронтенда в одном npm-пакете. Новых
пакетов нет: кеш — `IMemoryCache`, задачи — `ScheduledTaskRunner` с полосами, блокировки — `AdvisoryLock`, журнал настроек —
`PlatformSettingsWriter`, журнал согласий — `ConsentLedger`.

**Масштаб и продажа как сервиса.** «Оплачено» не зависит от тарифов трёх линеек, маршрутизация — от таблицы назначений,
доступность опций — настройка платформы без релиза. Состояние в процессе — только кеши 30 с и 60 с: второй экземпляр API
возможен; `channel-test-message` захватывает работу условным `UPDATE`.

---

## §40.2. Модель данных и миграция (T-40-02, §5 SPEC)

### §40.2.1 Миграция `Cycle40ChannelOptions` — одна, закреплённым `dotnet-ef` 8.0.11, один разработчик (BE-40-M)

| Объект | Изменение | Зачем |
|---|---|---|
| `SubscriptionOptions` | **строка** `notifications.max`, `Id = c4000000-0000-4000-8000-000000000040` (`ChannelOptionCodes.MaxOptionSeedId`). `INSERT … SELECT` из строки `notifications.whatsapp`: `Name = 'MAX'`, `Description = 'Номер для уведомлений клиентам в MAX.'`, `CapabilityKey = 'notifications.max'`, `Kind`, `PricePerMonth`, `UnitName`, `MaxQuantity`, `UnitPriceText`, `IsPublic`, `IsActive` — копия WhatsApp, `SortOrder = WhatsApp.SortOrder + 1`. `WHERE NOT EXISTS (… "Code" = 'notifications.max')`. Нет строки WhatsApp → вставка с `PricePerMonth = NULL`, `IsActive = true`, `IsPublic = false` | §5.1, О7 |
| `PlanOptionRules`, `AccountSubscriptionOptions` | **ничего** | US-02, О7 |
| `Bookings` | `NotifyByMessenger boolean NULL`; `MessengerConsentVersion varchar(80) NULL`; `MessengerConsentAtUtc timestamptz NULL`; **`MessengerConsentByUserId text NULL`** (кто из сотрудников отметил; без FK — как прочие «кто» в журналах, значение переживает удаление сотрудника) | US-07, Т40-L-10, Т40-L-12 |
| `NotificationChannels` | `LastTestResult int NULL`; `LastTestResultAtUtc timestamptz NULL`; `AutoTestInstanceId varchar(100) NULL`; **`ProviderServerCountry varchar(32) NULL`**. Частичный индекс `IX_NotificationChannels_TestPending` по `("Id") WHERE "LastTestResult" IN (0, 1)` | §40.8, §40.7.4 |
| `ChannelOptionChangeLogs` (новая) | журнал изменений двух опций каналов (§40.13) | US-10 |

`Orders` уже хранят `NotifyByMessenger`, `MessengerConsentVersion`, `MessengerConsentAtUtc` (цикл 24) — колонок не
добавляется. Заказов, созданных сотрудником, в продукте нет; ручные брони «Домов» в мессенджер не пишут никогда (цикл 37,
Т37-12) — без изменений.

`Down()` удаляет таблицу, колонки и строку `notifications.max` **только если** на неё нет `AccountSubscriptionOption`
(иначе `RAISE EXCEPTION`).

**Совместимость «код до — код после»:** старый код читает только `notifications.whatsapp`; новые колонки ему не видны;
новые значения enum старый код должен показать без падения (`Cycle40RollbackReadTests`, M40-08). Старый код не знает
`Bookings.NotifyByMessenger`: после отката записи сотрудников без отметки снова получат сообщения «как до цикла» — записано
в `DEPLOY.md` §29 как известное последствие отката.

### §40.2.2 Перечисления (только дописывание в конец)

| Enum | Новые члены (значение) |
|---|---|
| `NotificationReason` | `ClientDeclinedMessenger = 34`, `PlatformMessagingDisabled = 35`, `ChannelAccountMismatch = 36` |
| `ChannelStateReason` | `ReplacedByOwner = 11`, `RebindStarted = 12`, `TestMessageSent = 13`, `TestMessageFailed = 14`, `TestMessageSkipped = 15` |
| `ConsentSource` | `MessengerOptInBooking = 12`, `MessengerOptInOrder = 13`, `MessengerOptInStay = 14` (Т40-L-07) |
| `ChannelTestResult` (новый) | `Pending = 0`, `Sending = 1`, `Sent = 2`, `Failed = 3`, `SkippedSameNumber = 4`, `SkippedNoOwnerPhone = 5`, `SkippedPlatformDisabled = 6` |
| `ChannelOptionChangeSource` (новый) | `AdminBillingAccount = 0`, `AdminChannelCard = 1`, `TrialGrant = 2`, `TrialWindowStart = 3`, `TrialExpiry = 4`, `AdminOptionEnded = 5` |
| `ChannelDisplayStatus` (только API) | `Working`, `ActionRequired`, `Off` |
| `ChannelAction` (только API) | `Pay`, `AcceptTerms`, `BindNumber`, `Reconnect`, `ReplaceNumber`, `Unbind` |
| `ChannelWizardStep` (только API) | `Payment`, `PaymentPending`, `Terms`, `Qr`, `Done`, `Unavailable`, `None` |

Хранимые значения фиксирует `Cycle40EnumAppendOnlyTests`.

### §40.2.3 Сущности

- `Booking`: `bool? NotifyByMessenger`, `string? MessengerConsentVersion`, `DateTime? MessengerConsentAtUtc`,
  `string? MessengerConsentByUserId` — в конец.
- `NotificationChannel`: `ChannelTestResult? LastTestResult`, `DateTime? LastTestResultAtUtc`, `string? AutoTestInstanceId`,
  `string? ProviderServerCountry`. Навигация `Assignments` остаётся (EF), читать запрещено стражем (§40.4.3).
- `ChannelOptionChangeLog`: `Id`, `BillingAccountId` (FK `Restrict`), `OptionCode varchar(64)`, `Source int`,
  `OldPaidUntilUtc?`, `NewPaidUntilUtc?`, `OldEndsAtUtc?`, `NewEndsAtUtc?`, `ChangedByUserId text NULL`, `ChangedAtUtc`,
  `ChannelId uuid NULL`, `Comment varchar(500) NULL`; индекс `(BillingAccountId, OptionCode, ChangedAtUtc)`; append-only,
  писатель — `ChannelOptionLog.Write`; `ShowcaseOwnership.NeverWritten`.
- `SubscriptionPlanConfig.AllowNotificationChannel` — свойство остаётся, читать запрещено стражем.

### §40.2.4 Не трогаем

`ChannelCompanyAssignments`, `SubscriptionPlanConfigs.AllowNotificationChannel`, `PlanOptionRules` двух опций, настройка
`notifications.channel.price-per-month` — остаются; код их не читает. Удаление — отдельным циклом.

---

## §40.3. Оплата по транспорту (US-01, US-02, T-40-01)

### §40.3.1 Чистое правило `ChannelOptionFunding.Evaluate`

Выход — `TransportPayment(bool Paid, DateTime? PaidUntil, bool IsTrial, DateTime? LastPaymentAt)`.

| Факт строки `AccountSubscriptionOption` | Оплачено? | `PaidUntil` |
|---|---|---|
| строки нет | нет | null |
| `EndsAtUtc ≤ now` | нет | `EndsAtUtc` |
| `PaidUntilUtc` задан | `PaidUntilUtc ≥ now` | `PaidUntilUtc` |
| `PaidUntilUtc = null`, `GrantedByTrial = true` (окно рассылок триала ещё не открыто) | `account.TrialEndsAtUtc > now` | `account.TrialEndsAtUtc` |
| `PaidUntilUtc = null`, `GrantedByTrial = false` (**наследная** строка, §5.3) | подписка «Записей» аккаунта годна | `AccountSubscription.PaidUntil` |

`Quantity ≥ 1` — «транспорт оплачен». `IsTrial = GrantedByTrial`; `LastPaymentAt = ActivatedAtUtc`. Не читаются:
`PlanOptionRule`, `MailingUntilUtc`, годность подписки (кроме наследной строки), флаги тарифов. **Доступность опции
(Р40-Ю1) на «оплачено» не влияет**: закрытие запрещает продажу, а не отключает уже купленное или пробное.

### §40.3.2 `TransportFunding.Rank`

Живые каналы транспорта (`State ≠ Replaced`) по `CreatedAt, Id`: первый — `Funded` (транспорт оплачен) или `NotPaid`;
остальные — `Unfunded` («лишний номер», §5.2 SPEC).

### §40.3.3 `AccountMessagingReader`

`LoadAsync(accountIds, now)` / `ForCompanyAsync(companyId, now)` — ровно **четыре запроса** на любой набор аккаунтов
(каналы; строки двух опций; `AccountSubscriptions`; `BillingAccounts.TrialEndsAtUtc`) + кеши настроек платформы и каталога.

```
AccountMessagingState { AccountId, PlatformEnabled, WhatsApp: TransportState, Max: TransportState, Channels[] }
TransportState { Transport, Payment, Option { Open, Sellable, PricePerMonth },
                 Primary: NotificationChannel?, Duplicates[], Routable, Working }
```

`ChannelFundingReader` — фасад прежней формы поверх `AccountMessagingReader`; `IsFundedAsync(channel, plan)` →
`IsFundedAsync(channel)`.

**О-40-5 — ответ.** Правило «`PaidUntilUtc = null` → срок подписки» **остаётся в коде** для нетриальных строк: один запрос,
который делается уже сейчас; без него код зависел бы от точности отчёта. Новые строки двух опций без даты не появляются
(§40.13). Отчёт считает такие строки для сведения.

### §40.3.4 Что удаляется из кода

| Где | Что | Чем заменяется |
|---|---|---|
| `EffectivePlan` | `AllowNotificationChannel`, `PaidNotificationNumbers`; присваивание при закрытом окне рассылок | ничем |
| `SubscriptionResolver.GetEffectivePlansForAccountsAsync` | чтение опции WhatsApp, правил трёх линеек, `PaidNumbers`, `IsOptionCurrentlyPaid` | ничем |
| `OrdersPlan`, `StaysPlan` | поле `AllowNotificationChannel` | ничем |
| `ChannelEligibility` | класс + DI | `sellable ∧ сервис включён` (§40.7.1) |
| `ChannelFunding.Rank(channels, int)` | метод | `TransportFunding.Rank` |
| `TrialMailingRulePolicy` | класс | ничем (§40.3.5) |
| `OwnerSubscriptionService` | строки «Сообщения … в MAX и WhatsApp» в `Includes` | строки цен (§40.14) |
| `CompanyNotificationsController` | `plan.AllowNotificationChannel` (4 места), 402 в настройках и шаблонах | §40.6.4 |
| `NotificationChannelsController` | 402 «…недоступно на вашем тарифе» (2 места) | §40.7 |
| `ChannelPresentation.SettingsBlockedReason(planAllowsChannel, …)` | параметр плана | без плана |

Страж `AllowNotificationChannelReadGuardTests`: `\.AllowNotificationChannel\b` — только присваивание в
`TariffCatalogSeeder.cs`; `PaidNotificationNumbers` — нигде. Поля DTO ради старых вкладок: `allowedByPlan`/`planAllowsChannel` =
`true`; `numbersPaid` = число оплаченных транспортов (0…2).

### §40.3.5 Пробный период — только открытые опции (О6 с поправкой Р40-Ю1)

`TrialActivationService.GrantAsync`: цикл по `ChannelOptionCodes.All`, **пропуская закрытые** на момент выдачи. Открытая:
строки нет → создать (`Quantity = 1`, `PaidUntilUtc = account.TrialMailingWindowEndsAtUtc`, `GrantedByTrial = true`);
триальная → оживить; платная → не трогать. Правило тарифа не читается; запись — `ChannelOptionLog.Write(TrialGrant)`.
Открытие WhatsApp позже **не** доначисляет его идущим триалам. Пробный транспорт оплачен, но условия принимаются перед
первым QR (шаг `Terms`, §40.6.3).

### §40.3.6 Тексты оплаты (`BillingTexts.FundingText(state, transport, payment, workingPhoneMasked)`)

`NotPaid` — «Номер {М} не оплачен»; `Funded` — «Оплачено до {дата}» / «Пробный период до {дата}»; `Unfunded` — «Лишний номер
{М}: сообщения уходят с {маска}. Отвяжите этот номер».

---

## §40.4. Номер работает на все компании аккаунта (US-03, T-40-03) — ответ на О-40-1

### §40.4.1 Функция «каналы для компании»

`AccountMessagingReader.ForCompanyAsync(companyId)` — единственный способ узнать каналы компании.

### §40.4.2 Полный перечень мест (поиск по `ece8038`)

**Назначения — чтение (13):**

| # | Место | После |
|---|---|---|
| 1 | `Services/NotificationScheduler.cs:164` | `ForCompanyAsync`; маршрутизация §40.5 |
| 2 | `Services/Orders/Notifications/OrderMessageScheduler.cs:35` | то же |
| 3 | `Services/Stays/StayMessageScheduler.cs:30` | то же |
| 4 | `Services/Shops/ShopChannelReader.cs:24` | `LoadAsync` → номера аккаунта (потребители `ShopNotificationsController:56,99`, `StaysCompaniesController:249,282`); `IsMessengerAvailableAsync` **удаляется**, потребители (`StorefrontController:58`, `OrderCreationService:173`, `StayBookingCreationService:161`) → `CustomerMessagingOffer` |
| 5 | `Controllers/CompanyNotificationsController.cs:49,74` | `ForCompanyAsync` |
| 6 | `…CompanyNotificationsController.cs:182` (шаблон) | 402 снимается |
| 7 | `…CompanyNotificationsController.cs:349,364,371` (сводка) | по аккаунту |
| 8 | `Services/Scheduling/Tasks/ChannelHealthTask.cs:241` | `MessagingDemand` (§40.4.4) |
| 9 | `Services/Billing/OwnerSubscriptionService.cs:68,188,277` | `HasNumber` = есть работающий номер |
| 10 | `Controllers/CompanyTransferController.cs:74` | `WillDetachFromChannel` = у исходного аккаунта есть живой канал; `WillCancelPendingNotifications` — не перепривязываемые `Pending` |
| 11 | `Controllers/AdminChannelsController.cs:34,79` | `CompanyCount` = компании аккаунта |
| 12 | `Controllers/AdminAccountDtoBuilder.cs:41,135` | `assignedCompanies` = компании аккаунта |
| 13 | `Controllers/NotificationChannelsController.cs:56,684,805,841` | `ChannelDto.companies` = все компании аккаунта |

**Назначения — запись (5):**

| # | Место | После |
|---|---|---|
| 14 | `NotificationChannelsController.AssignCompany` | 410, данные не меняются |
| 15 | `NotificationChannelsController.UnassignCompany` | 204, данные не меняются |
| 16 | `NotificationChannelsController.Replace:546` | не переносит назначения; `Pending` — `PendingRebinder` |
| 17 | `Services/Billing/CompanyTransferService.cs:316` | **удаление остаётся** (составной FK) |
| 18 | `Services/Subjects/AccountDeletionService.cs:333` | **удаление остаётся** |

**Модель и инфраструктура (не меняются):** `ChannelCompanyAssignment.cs`, `NotificationChannel.Assignments`,
`AppDbContext.cs:82,961–987`, `ShowcaseOwnership.cs:56`, `tools/bench/cycle22/seed.py`, `deploy/checks/billing-*.sql`.

**Флаг тарифа и оплата по количеству (8):**

| # | Место | После |
|---|---|---|
| 19 | `Services/SubscriptionResolver.cs:29,51,56,106,181–273` | §40.3.4 |
| 20 | `Services/Billing/ChannelEligibility.cs` | удаляется |
| 21 | `OrdersPlanResolver.cs:14,21,42`; `StaysPlanResolver.cs:10,23` | поле удаляется |
| 22 | `OwnerSubscriptionService.cs:78–81,197,280–289,323` | §40.3.4, §40.14 |
| 23 | `TrialActivationService.cs:241–322` + `TrialMailingRulePolicy.cs` | §40.3.5 |
| 24 | `CompanyNotificationsController.cs:77,189,453,465` | §40.6.4 |
| 25 | `NotificationGate.cs:81` | новая сигнатура (§40.5.3) |
| 26 | `AdminBillingController.cs:198`, `AdminAccountDtoBuilder.cs:44,115` | число оплаченных транспортов, `TransportFunding.Rank` |

Плюс `ChannelFunding.Rank` в `NotificationDispatchTask.cs:113`, `NotificationScheduler.cs:222,250`,
`OrderMessageScheduler.cs:123`, `StayMessageScheduler.cs:97`, `ChannelFundingReader.cs:73,99`. Упоминания флага в
комментариях (`AdminPlansController:366`, `CompanyPushSettingsController:18`, `CompanyNotificationSettings.cs:38`) правятся
текстом. В админке тарифов поля флага уже нет.

**Фронтенд:** `src/pages/owner/NotificationsSection.tsx` (удаляется), `NotificationSettingsTab.tsx:107`,
`goods/.../ShopNotificationsPage.tsx:216`, `dom/.../NotificationsPage.tsx:204`, `components/notifications/AssignCompanyDialog.tsx`.

**Тесты, кодирующие удаляемое поведение** (переписываются, изменения ожиданий — в отчёте разработчика):
`NotificationGateTests`, `NotificationTypeCatalogTests`, `SubscriptionResolverRulesTests`, `OrdersPlanResolverTests`,
`StaysPlanTests`, `TrialMailingRulePolicyTests` (удаляется), `ChannelPresentationTests`, `ChannelFunding*Tests`,
`NotificationChannelsTests`, `NotificationQueueingTests`, `NotificationDispatch*Tests`, `Cycle24NotificationsTests`,
`Cycle25*`, `Cycle28OutboundSuppressionTests`, `Cycle35LocksAndOffDemoTests`, `LegalPriorityTests`, `NotificationTestBase`.
**Особо:** тесты, где гость салона без согласия получает сообщение (`AccountsOnly` для гостей), меняют ожидание по
Р40-Ю2 — намеренная смена поведения.

### §40.4.3 Страж от возврата назначений

`AssignmentReadGuardTests`: `ChannelCompanyAssignments|\.Assignments\b` в `ServiceBooking.API/**/*.cs`; allow-list —
`CompanyTransferService.cs`, `AccountDeletionService.cs` (только удаление), `ShowcaseOwnership.cs`,
`ApplicationServicesExtensions.cs` (комментарий).

### §40.4.4 Простой номера — `MessagingDemand`

«У аккаунта есть активная, не витринная компания с включёнными рассылками клиентам»: салон — `EnabledTypeMask &
BookingTypesMask ≠ 0` (нет строки → по умолчанию); магазин — `CustomerMessengerEnabled`; «Дома» — `GuestMessengerEnabled`.
Один запрос на проход; `ChannelIdleCalculator.Recompute` получает `bool hasDemand`. Выключатель платформы простой не запускает.

---

## §40.5. Маршрутизация и постановка в очередь (US-09, T-40-07)

### §40.5.1 Определения

- **Маршрутизируемый транспорт:** оплачен ∧ `Primary` есть ∧ `Funded` ∧ `!IsSuspendedByAdmin` ∧ состояние ∈ {`Connected`,
  `Disconnected`, `NeedsReconnect`, `Blocked`} (номер хоть раз привязан; поломка держит `Pending`).
- **Работающий:** маршрутизируемый ∧ `Connected` ∧ сервис включён = «Работает» US-05.

Доступность опции (Р40-Ю1) на маршрутизацию не влияет (§40.3.1).

### §40.5.2 `NotificationRouting.SelectTargets`

0 маршрутизируемых → `[]` (причину даёт гейт); 1 → он (режим и приоритет не читаются); 2 → `AllChannels`: оба;
`PriorityChannel`: приоритетный, без перехода. `PriorityChannelUnavailable` при постановке больше не возникает.
`Candidate.IsUsable` → `IsRoutable`.

### §40.5.3 `NotificationGate.Evaluate` — новая сигнатура (pure)

```
Evaluate(NotificationType type, MessagingAvailability availability, CompanyNotificationSettings? settings,
         bool recipientOptedOut, MessengerConsentDecision consent, DateTime nowUtc, DateTime visitStartUtc)
MessagingAvailability(bool PlatformEnabled, bool AnyTransportPaid, bool AnyTransportRoutable)
MessengerConsentDecision = MessengerConsentRule.Evaluate(...)  // §40.11.2: Allowed | Declined | NoConsent
```

Порядок: `PlatformMessagingDisabled` → `RecipientOptedOut` → `ClientDeclinedMessenger` (`Declined`) →
`NoProviderDeliveryConsent` (`NoConsent`) → `NotOnPaidPlan` → `NoUsableChannel` → `TypeDisabledByCompany` →
`BelowMinimumLeadTime`. Решение о согласии — в одном месте, `MessengerConsentRule`; `ProviderDeliveryConsentMode` из гейта
уходит.

### §40.5.4 Диспетчер (триаж, одним пакетом на проход)

1. Сервис выключен → `Skipped`/`PlatformMessagingDisabled`.
2. `channel.BillingAccountId ≠ company.BillingAccountId` → `Skipped`/`ChannelAccountMismatch`.
3. Канал не `Funded` или приостановлен → `Skipped`/`NotOnPaidPlan`.
4. Канал `DisabledByOwner`/`Replaced` → `Skipped`/`NoUsableChannel`.
5. Дальше — как сейчас. Согласие повторно не проверяется (решено при постановке); отзыв `ProviderDelivery` вошедшим
   отменяет его `Pending` существующим путём отзыва (цикл 5).

### §40.5.5 Постановка: общий порядок для трёх планировщиков

`ShowcaseOutboundGuard` → контекст получателя → `ForCompanyAsync` (4 запроса) → `MessengerConsentRule` (≤ 2 запроса:
аккаунт получателя, `ConsentLedger.CurrentAsync`) → гейт → маршрутизация → строки.

---

## §40.6. Представление: три состояния, шаг мастера, настройки компаний (US-05, US-06, T-40-05)

### §40.6.1 Факты (`ChannelDisplayFacts`)

`Transport`, `State`, `LastStateReason`, `PhoneMasked`, `Paid`, `PaidUntil`, `IsTrial`, `RequestNewerThanPayment`,
`IsDuplicate`, `Suspended`, `PlatformEnabled`, `OptionOpen`, `OptionSellable`, `TermsAccepted`, `IdleSinceUtc`,
`IdleDeadlineUtc`.

- `RequestNewerThanPayment` = `RequestedAtUtc ≠ null ∧ (LastPaymentAt = null ∨ RequestedAtUtc > LastPaymentAt)`; владельцу
  «Оплата на проверке» = `!Paid ∧ RequestNewerThanPayment`; админу «заявка» = `RequestNewerThanPayment`.
- `TermsAccepted` = `RiskAcceptedVersion = текущая версия ChannelRiskNotice` ∧ `LegalEntityForm ≠ null` ∧ `Inn ≠ null`
  (статус заявлен, п. 6.2.1 D3) ∧ у владельца действующее согласие `TermsOwner`/`ChannelOffer` текущей версии
  (`ConsentLedger`, один запрос на владельца в overview).

### §40.6.2 Таблица `ChannelPresentation.Display` (первое сработавшее; векторы `display`)

| # | Условие | `displayStatus` | `displayText` | `action` |
|---|---|---|---|---|
| 1 | `State = Replaced` | — (владельцу не отдаётся) | — | — |
| 2 | `!PlatformEnabled` | `Off` | «Рассылки временно отключены платформой» | null |
| 3 | `Suspended` | `Off` | «Приостановлен администратором» | null |
| 4 | `State = DisabledByOwner` | `Off` | «Отключён вами» | `BindNumber`, если `Paid`; `Pay`, если `!Paid ∧ OptionSellable`; иначе null |
| 5 | `IsDuplicate` | `ActionRequired` | «Лишний номер {М}: сообщения уходят с другого номера. Отвяжите этот» | `Unbind` |
| 6 | `!Paid ∧ RequestNewerThanPayment` | `ActionRequired` | «Оплата на проверке» | null |
| 7 | `!Paid ∧ !OptionSellable` | `ActionRequired` | «Подключение {М} временно недоступно» | null |
| 8 | `!Paid ∧ PaidUntil ≠ null` | `ActionRequired` | «Оплата закончилась {дд.мм.гггг}» | `Pay` |
| 9 | `!Paid` | `ActionRequired` | «Не оплачено» | `Pay` |
| 10 | `State ∈ {NotConnected, NeedsReconnect, Disconnected(не mismatch)} ∧ !TermsAccepted` | `ActionRequired` | «Примите условия подключения {М}» | `AcceptTerms` |
| 11 | `State = NotConnected` | `ActionRequired` | «Оплата подтверждена — привяжите номер» | `BindNumber` |
| 12 | `State = Connecting` | `ActionRequired` | «Номер привязывается — отсканируйте QR» | `BindNumber` |
| 13 | `Disconnected ∧ ServerCountryMismatch` | `ActionRequired` | «Требуется вмешательство платформы» | null |
| 14 | `Disconnected` | `ActionRequired` | «Связь с {М} разорвана — подключите номер заново» | `Reconnect` |
| 15 | `NeedsReconnect ∧ SecretUnavailable` | `ActionRequired` | «Нужна повторная привязка после технических работ. Оплата сохранена» | `Reconnect` |
| 16 | `NeedsReconnect` | `ActionRequired` | «Номер отключён: им {N} {дней} никто не пользовался. Оплата сохранена — подключите заново» | `Reconnect` |
| 17 | `Blocked` | `ActionRequired` | «{М} заблокировал этот номер. Подключите другой — оплата сохранится» | `ReplaceNumber` |
| 18 | `Connected ∧ IdleSinceUtc ≠ null` | `ActionRequired` | «Номер отключится {дд.мм}: ни у одной вашей компании не включены сообщения клиентам» | null |
| 19 | `Connected` | `Working` | «Сообщения уходят с номера {маска}» | null |

`stateText` (детальный) остаётся, называет правильный мессенджер. Юнит-таблица истинности — по векторам.

### §40.6.3 Шаг мастера `ChannelPresentation.WizardStep` (первое сработавшее)

| Условие | Шаг |
|---|---|
| сервис выключен ∨ (не оплачен ∧ опция не продаётся, в т. ч. **закрыта**) | `Unavailable` |
| не оплачен ∧ RequestNewerThanPayment | `PaymentPending` |
| не оплачен | `Payment` |
| приостановлен ∨ `Blocked` ∨ (`Disconnected` ∧ `ServerCountryMismatch`) ∨ `IsDuplicate` | `None` |
| `Connected` | `Done` |
| оплачен (в т. ч. пробный) ∧ (нет живого канала ∨ `!TermsAccepted`) | **`Terms`** (Т40-L-03) |
| оплачен ∧ State ∈ {`NotConnected`, `Connecting`, `Disconnected`, `NeedsReconnect`, `DisabledByOwner`} | `Qr` |

`canRequestPayment` = (`!Paid ∨ IsTrial`) ∧ `OptionSellable`. Шаг `Terms` — та же форма, что «Оплата» (статус, ИНН,
оферта, риск), но **без заявки на оплату** (`paymentRequest: false`, §40.7.2).

### §40.6.4 Настройки компаний — три маршрута

`CompanyMessagingStatus` → `messagingActive`, `inactiveText`, `workingTransports`, `deliveryChoiceVisible` (оба работают ∨ (оба
маршрутизируемы ∧ `PriorityChannel` ∧ приоритетный не работает)), `priorityWarning`.
- Салон: 402 сняты (настройки и шаблоны); проверка «приоритет среди оплаченных» снята; `planAllowsChannel = true`.
- Магазин и «Дома»: `messengerAvailable` = есть оплаченный транспорт; 409 `MessengerUnavailable` при включении флага без
  оплаченного транспорта остаётся; 400 приоритета снят.

---

## §40.7. Мастер подключения и действия с номером (US-04, T-40-04)

### §40.7.1 Доступность и «можно купить» (Р40-Ю1, Т40-L-04)

`open(X)` = настройка платформы `notifications.option.whatsapp.open` / `notifications.option.max.open` (`"true"`/`"false"`);
**нет ключа** → конфигурация `Notifications:OptionAvailability:WhatsApp` (`false`) / `…:Max` (`true`) в `appsettings.json`.
Меняется суперадмином в админке (`PUT /api/admin/platform-settings`, журнал `PlatformSettingsWriter`) — без релиза;
конфигурация — только умолчание. Кеш настроек 60 с, инвалидируется при записи.

`sellable(X)` = `open(X)` ∧ строка опции `IsActive` ∧ `PricePerMonth ≠ null` ∧ `LegalOptionGuards.IsPubliclySellable(code)`.
`LegalOptionGuards` +`notifications.max → TermsOwner` (Т40-L-04); `OptionCapabilityCatalog` +`notifications.max`.

`ChannelOptionAvailability.Sellable` — **единственное** место решения «продаётся ли»:

| Потребитель | Закрытая (не продаётся) опция |
|---|---|
| `overview` | транспорт **не показывается**, если у аккаунта нет его живого канала и оплаты/триала; если есть — показывается с `open: false`: управление номером доступно, оплатить/продлить нельзя (`canRequestPayment = false`; неоплаченный — `wizardStep = Unavailable`) |
| `GET …/offer` | транспорт отсутствует в `transports[]`; `pricePerMonth` = null, если закрыт WhatsApp |
| `POST /notification-channels` | 409 «Подключение {М} сейчас недоступно» — до любых записей. Исключение — шаг «Условия» (`paymentRequest: false`) по уже оплаченному транспорту |
| `connect` | только для оплаченного транспорта (как было) — купленное работает |
| Триал | не выдаётся (§40.3.5) |
| `messengerAddons` | строки нет |
| Админ «Подтвердить оплату», «Биллинг-аккаунты» (создание или продление строки опции) | 409 «Опция {М} закрыта для подключения» — сначала открыть опцию |
| `availableOptions` подписки | опции каналов там не показываются вовсе (§40.13) |

### §40.7.2 Шаги «Оплата» и «Условия» = `POST /api/notification-channels`

Тело: прежние поля + `riskAccepted: {version}` (мастер шлёт всегда) + **`paymentRequest: bool`** (по умолчанию `true` —
старые вкладки ведут себя как раньше). Порядок — §40.24 контракта.
- `paymentRequest: true` (шаг «Оплата»): опция не продаётся → 409; живой канал есть ∧ оплачен ∧ не пробный → 409 «У вас уже
  есть номер {М}»; есть ∧ (не оплачен ∨ пробный) → продление заявки (`RequestedAtUtc = now`) → 200; нет → новая строка → 201.
- `paymentRequest: false` (шаг «Условия», Т40-L-03): транспорт должен быть оплачен (в т. ч. триал), иначе 409 «Сначала
  отправьте заявку на оплату {М}»; канал есть → форма, ИНН, риск, согласие с офертой обновляются, `RequestedAtUtc` **не
  трогается** → 200; нет → новая строка **без** `RequestedAtUtc` → 201. Опция может быть закрыта — это не продажа.
- Риск: версия ≠ текущей → 400 «Текст изменился, прочитайте заново». Оферта и риск пишутся одним запросом (Т40-L-02).
- Подстановка формы и ИНН — `overview.transports[].prefill`. Тексты мастера — §40.33.12 контракта (Т40-L-13, Т40-L-14).

### §40.7.3 Шаг «QR» = `connect` + опрос `qr`

`connect`:
1. Исходные состояния: `NotConnected`, `NeedsReconnect`, `DisabledByOwner`, **`Disconnected`** (кроме `ServerCountryMismatch`;
   старый экземпляр — в `OrphanedInstanceId`, событие `RebindStarted`).
2. **Условия текущей редакции** (Т40-L-03, найденное №4): `!TermsAccepted` → 409 «Условия подключения обновились — примите их
   заново». Новая редакция риска цикла поднимает версию — все каналы проходят шаг «Условия» перед следующей привязкой.
3. Идемпотентность: `Connecting` → 202 без нового экземпляра.
4. Новый экземпляр → `ConnectedAtUtc = null` (найденное №2).
5. Сервис выключен → 409; приостановлен → 409; не оплачен или не `Funded` → 402 «Номер не оплачен».

`qr`: `Connecting` без экземпляра → 200 с `qrBase64: null`; переход в `Connected` — под `channel-connect:{id}`. Инструкция
QR — сервер по транспорту; MAX — черновик до M40-02.

### §40.7.4 Страна экземпляра и место обработки (обзор §12 п. 9)

- Ожидаемая страна — **по транспорту**: `Notifications:GreenApi:ServerCountryByTransport:WhatsApp|Max`, при отсутствии —
  прежний `Notifications:GreenApi:ServerCountry`. `DeploymentSafetyChecks.ValidateGreenApiServerCountry` проверяет оба
  транспорта при `InstanceCreationEnabled = true`.
- Страна, которую сообщил провайдер, пишется в `NotificationChannels.ProviderServerCountry` (`null`, если не сообщил) и
  показывается в карточке админа; несовпадение — как сейчас (`ServerCountryMismatch`).
- **DO-40-05:** снять с боя значения `ServerCountry*` и `ProviderServerCountry` по транспортам → юристу для
  `{{МЕСТО_ОБРАБОТКИ_ПОСРЕДНИКОМ}}` (Политика 9.3.5) и П1.12.1. Тексты продукта не утверждают «серверы в РФ», пока не сверено.

### §40.7.5 «Готово», «Заменить номер», «Отвязать», старые маршруты

- «Готово» — маска, «Работает для всех ваших компаний: N», результат проверки (опрос `GET /{id}` ≤ 60 с).
- `replace` — из любого состояния, кроме `NotConnected`/`Replaced`; новая строка копирует форму, ИНН, риск и
  `RequestedAtUtc`; старый экземпляр гасится; `Pending` → новая строка (тот же транспорт). При устаревшей версии риска
  мастер нового номера начнётся с `Terms`.
- `DELETE` — как сейчас + `Pending` отменяются (§40.9).
- `POST …/companies` → 410; `DELETE …/companies/{companyId}` → 204; `accept-risk` — без изменений.

---

## §40.8. Автоматическое проверочное сообщение — ответ на О-40-2 (и R-40-4)

- **Хранение.** `LastTestResult`, `LastTestResultAtUtc`, `AutoTestInstanceId` на канале; история — `ChannelStateEvent`
  (`Connected → Connected`, `TestMessageSent/Failed/Skipped`). Не через `OutboundNotifications` (обязателен `CompanyId` и
  гейт компании, а проверка — номера аккаунта).
- **Метка.** `ChannelStateTransition.Apply`: `targetState = Connected ∧ reason = Authorized ∧ ProviderInstanceId ≠ null ∧
  AutoTestInstanceId ≠ ProviderInstanceId` → `AutoTestInstanceId = ProviderInstanceId`, `LastTestResult = Pending`. Один
  экземпляр = одна привязка = одна проверка; две вкладки пишут одно и то же.
- **Захват.** Задача `channel-test-message` (полоса `realtime`, 10 с): `UPDATE … SET LastTestResult = Sending WHERE Id = @id AND
  LastTestResult = Pending AND AutoTestInstanceId = @inst` → 1 строка = владелец отправки. `Sending` > 5 мин → `Failed` без
  повтора; `Pending` > 1 ч при `State ≠ Connected` → `Failed`. После отправки `LastTestMessageAtUtc = now`.
- **Получатель** — телефон `OwnerUserId`. Совпадает с номером канала → `SkippedSameNumber` (флаг
  `Notifications:TestMessage:AllowSameNumber`, по умолчанию `false`, включается после M40-02); нет телефона →
  `SkippedNoOwnerPhone`; сервис выключен или демо-стенд → `SkippedPlatformDisabled`.
- **Текст:** «Проверка номера {М} для уведомлений ezbook.ru: если вы видите это сообщение, номер работает.»

---

## §40.9. Судьба ожидающих сообщений — ответ на О-40-4 (`PendingRebinder`, Р40-Ю3)

Один помощник для трёх событий. Перепривязка = `ChannelId := цель` (+ `Transport` и последний сегмент `IdempotencyKey`, если
транспорт другой); строка с таким ключом уже есть → исходная отменяется.

| Событие | Цель | Нет цели |
|---|---|---|
| Отвязать номер транспорта X | **по умолчанию нет** — `Pending` отменяются (`BookingOrAssignmentCancelled`). Перенос на маршрутизируемый Y ≠ X — только при `Notifications:RebindPendingToOtherTransport = true` | отмена |
| Заменить номер | новая строка того же транспорта | — |
| Передача компании | маршрутизируемый канал того же транспорта нового аккаунта | отмена (как сейчас) |

Флаг выключен (Р40-Ю3): клиент давал согласие, видя подпись с конкретным мессенджером. Включение — настройкой после ответа
юриста, без релиза. Новые события после отвязки X идут по правилу «один транспорт» (US-09) — это маршрутизация нового
сообщения, а не перенос уже обещанного.

---

## §40.10. «Рассылки работают» для клиента — ответ на О-40-3 (`CustomerMessagingOffer`, T-40-06)

**Правило** (`CustomerMessagingOfferRule`): предусловия (сервис включён ∧ компания активна ∧ не витрина/демо ∧ флаг компании:
салон — `BookingConfirmed` или `Reminder`; магазин — `CustomerMessengerEnabled`; «Дома» — `GuestMessengerEnabled`) →
`targets` = маршрутизация над **маршрутизируемыми** транспортами и режимом → `transports = targets ∩ работающие` →
`offered = предусловия ∧ transports ≠ ∅`. `checkboxLabel` — подпись продукта «Получать уведомления о {записи|заказе|брони} в
{WhatsApp|MAX|WhatsApp и MAX}» (обзор §6.2). Наружу — только булево, транспорты, подпись.

**Выдача:** служба с кешем 30 с на компанию; поля в существующих ответах: `CompanyDto.customerMessaging` (`GET
/api/companies/{slug}`, виджет), `storefront.customerNotifications` (+`messengerTransports`, `messengerLabel`),
`PublicHouseDto.messenger`. Создание записи/заказа/брони перепроверяет без кеша.

---

## §40.11. Согласие клиента на сообщения (US-07, US-08, Р40-Ю2, Т40-L-07/09/10/12)

### §40.11.1 Что приходит и что хранится

| Вертикаль | Источник | Поле запроса | Что пишется |
|---|---|---|---|
| ezbook, клиент (гость или вошедший) | форма записи, виджет | `notifyByMessenger: true/false/null` | `NotifyByMessenger` как прислано; при `true` — `MessengerConsentVersion` = версия `BookingMessengerConsent` (или `fallback:<sha256 запасного текста>`, если текста в манифесте нет), `MessengerConsentAtUtc = now` |
| ezbook, сотрудник (ручная запись) | форма сотрудника | `notifyByMessenger: true` = отметка «Клиент согласился…»; `false`/`null` = отметки нет | `true` → `NotifyByMessenger = true`, версия `StaffBookingMessengerConsentHint` (или `fallback:`), время, **`MessengerConsentByUserId` = сотрудник**; иначе `NotifyByMessenger = null` |
| goods | `CartPanel` | `notifyByMessenger` (bool) | как сейчас (`Orders.*Consent*`), по новому правилу «предлагается» |
| dom | `BookingPanel` | `notifyByMessenger` (bool) | как сейчас (`StayBookings.*Consent*`) |

`fallback:<sha256>` — тот же приём, что у заверения реестра «Домов» (цикл 37): доказательство того, какой запасной текст видел
человек до появления текста юриста в манифесте. Новые константы `LegalTextKey.BookingMessengerConsent` и
`StaffBookingMessengerConsentHint` — **вне `All`** (деплой не ждёт юриста); запасные тексты фронта — дословно из обзора §5.3,
§7.3 (и §6.2 для `OrderMessengerConsent`, `StayMessengerConsent`).

### §40.11.2 Решение `MessengerConsentRule.Evaluate` (pure; векторы `consent`)

Вход: `notifyByMessenger` (`true|false|null`), `recipientIsAccount` (у получателя есть аккаунт: `ClientId` записи либо
аккаунт с подтверждённым номером, равным номеру получателя), `accountHasProviderDeliveryConsent`.

| `notifyByMessenger` | Решение |
|---|---|
| `false` | `Declined` → `Skipped`/`ClientDeclinedMessenger` |
| `true` | `Allowed` (отметка клиента или сотрудника и есть согласие на эту запись; у вошедшего согласие уже в журнале, §40.11.3) |
| `null` (старый клиент фронта, запись сотрудника без отметки, записи до цикла) | `Allowed` только если `recipientIsAccount ∧ accountHasProviderDeliveryConsent`, иначе `NoConsent` → `Skipped`/`NoProviderDeliveryConsent` |

Отписка проверяется гейтом **раньше** и важнее любой отметки (Q-40-5). Для заказов и броней поле всегда `bool`. Конфигурация
`Notifications:ProviderDeliveryConsent` **перестаёт влиять** на решение (правило строже `AccountsOnly` для гостей — обзор
§2.7, §5.1 — и совпадает с ним для аккаунтов); ключ остаётся ради отката, `DeploymentSafetyChecks` его проверяет.

Поиск аккаунта по номеру получателя помечается `// SUBJECT-PHONE-GATE: not-account-scoped` (проверка при постановке по номеру
самого сообщения, как соседние проверки отписки; §245.3 цикла 16) и покрывается `SubjectPhoneGateInvariantTests`.

**Изменение поведения (намеренно):** гость салона без отметки и клиент, записанный по телефону без отметки, сообщений не
получают (R-40-3; обзор §7.2).

### §40.11.3 Отметка вошедшего пишет согласие (Т40-L-07)

Создание записи/заказа/брони вошедшим с `notifyByMessenger = true`: нет действующего `PdnConsent/ProviderDelivery` →
`ConsentLedger.GrantAsync` (`PdnConsent` текущей версии, цель `ProviderDelivery`, `ConsentSource.MessengerOptInBooking|Order|
Stay`, IP, UA) **в той же транзакции**. Отметка гостя в журнал согласий не пишется — доказательство в самой записи.

### §40.11.4 Предзаполнение у вошедшего (Т40-L-09)

`GET /api/notifications/preferences` получает в конец `providerDeliveryConsent: bool`. Фронт (`useMessengerOptInDefault`):
гость → снята; вошедший с `enabled = false` (отписан) → строка «Уведомления в мессенджеры выключены в профиле» со ссылкой в
профиль ezbook (в goods и dom тоже на ezbook, пока нет US-40-13), поле не отправляется; `enabled ∧ providerDeliveryConsent`
→ стоит; иначе → снята.

### §40.11.5 Старые согласия и MAX (Т40-L-08)

Новая редакция D4 публикуется как `Material`; действующим считается согласие **текущей** редакции. **QA-40-7** проверяет, что
`ConsentLedger.CurrentAsync` не считает действующим согласие `ProviderDelivery` прежней `Material`-редакции; если считает —
задача BE-40-5 (считать такое согласие отсутствующим). Боевые данные тестовые — массового перезапроса не требуется.

### §40.11.6 DTO

`BookingDto` — в конец: `notifyByMessenger` (bool?), `messengerConsentAtUtc` (date-time?), `messengerConsentByStaff` (bool).
Имя сотрудника наружу не отдаётся.

## §40.11a. Название компании в каждом сообщении (Т40-L-05)

- **Записи салона:** `NotificationScheduler.RenderBodyAsync` ставит первой строкой `«{Company.Name}»:` перед текстом шаблона —
  **всегда**. Превью шаблона показывает ту же строку. `DefaultTemplates` не меняются.
- **Заказы:** `OrderNotificationTexts.Messenger` уже начинается с `{ShopName}:` — закрепляется тестом.
- **Брони:** `StayNotificationTexts.Messenger` содержит название компании — закрепляется тестом.
- `MessengerBodiesNameSenderTests`: три вида сообщений от двух компаний одного аккаунта — в теле название своей компании.

---

## §40.12. Глобальный выключатель (US-12, T-40-09)

Ключ `notifications.customer-messaging.enabled` (нет ключа = включено), кеш 60 с, запись — `PUT /api/admin/platform-settings`
(`customerMessagingEnabled`), журнал `PlatformSettingsWriter`.

| Место | При «выключено» |
|---|---|
| `POST /notification-channels`, `connect`, `test-message` | 409 |
| Постановка | `Skipped`/`PlatformMessagingDisabled` |
| Диспетчер | `Pending` → `Skipped`/`PlatformMessagingDisabled` |
| `CustomerMessagingOffer` | `offered = false` |
| Представление | `Off`, «Рассылки временно отключены платформой» |
| Автопроверка | `SkippedPlatformDisabled` |
| Мастер | `Unavailable` |
| Не затрагивается | экземпляры, простой, `channel-health`, оплаченные сроки, push и MAX сотрудникам |

Т40-L-06 — §40.19, R-40-13.

---

## §40.13. Админка (US-10, T-40-08)

- **Таблица** — `GET /api/admin/notification-channels` + `displayStatus`, `payment` (`Paid|NotPaid|Requested|Suspended|Trial`),
  `includeReplaced` (по умолчанию `false`); `AdminChannelDto` + поля представления; `companyCount` = компании аккаунта.
- **Сводка** — + `working`, `actionRequired`, `off`.
- **Карточка** `GET /api/admin/notification-channels/{id}` — детальное состояние, форма и ИНН, даты, `providerServerCountry`,
  последняя проверка, журнал состояний (200), журнал оплаты (`ChannelPaymentLog` + `ChannelOptionChangeLog`), компании
  аккаунта, цепочка замен.
- **«Подтвердить оплату»** `POST …/{id}/confirm-payment` `{months 1…12, comment}`: опция закрыта → 409; иначе
  `PaidUntilUtc = max(now, текущий срок) + months`, `ActivatedAtUtc = now`, `GrantedByTrial = false`; журналы
  `ChannelPaymentLog`, `ChannelOptionChangeLog`, `SubscriptionChangeLog`.
- **Доступность опций и выключатель** — в `AdminPlatformSettingsDto`: `whatsAppOptionOpen`, `maxOptionOpen`,
  `customerMessagingEnabled` (nullable на запись). Интерфейс — блок «Подключение мессенджеров»: два переключателя «Открыто для
  владельцев» с подсказкой «Закрытый мессенджер не продаётся и не выдаётся в пробном периоде; уже оплаченные номера работают до
  конца срока» и выключатель «Рассылки клиентам в мессенджеры».
- **`ChannelOptionChangeLog`** пишут: `AdminBillingController` (оба пути), `confirm-payment`, `TrialActivationService`,
  `TrialMailingWindowStarter`, `TrialLifecycleTask`.
- **«Биллинг-аккаунты»:** для двух опций `PaidUntil` обязателен; правило тарифа не проверяется; создание или продление строки
  закрытой опции → 409. Владельцу опции каналов в `availableOptions` не предлагаются.
- **Параметры «канала»:** поле цены убирается из интерфейса; сервер принимает `channelPricePerMonth` и игнорирует.

---

## §40.14. Цены мессенджеров (US-11, T-40-10, Т40-L-11)

`MessengerAddonsBuilder` (pure): по транспорту с `sellable` (открыт, с ценой, оферта опубликована) — `{transport, label,
pricePerMonth, text: "+ MAX 490 ₽/мес", footnote}`; `footnote` у WhatsApp — «Доступ к WhatsApp в России ограничен: сообщения
могут не доходить», у MAX — null. Рядом — `messengerAddonsNote {conditionsUrl: "/offer-channel", conditionsLabel: "Условия",
taxNote}`; `taxNote` = существующая налоговая оговорка страницы цен (`PricingCatalogBuilder` `notice`). Поля — в конец
`PublicPricingDto` и `OwnerSubscriptionDto`. Фронт: строки под каждой карточкой, «Условия» и оговорка рядом, сноска под
строкой WhatsApp. Средства обхода блокировок не упоминаются (Т40-L-14).

---

## §40.15. Фронтенд: структура и общие компоненты (T-40-11, T-40-12)

### §40.15.1 Общие компоненты (`frontend/src/components/notifications/`)

| Компонент | Что | Где |
|---|---|---|
| `NumbersBlock` | «Номера общие для всех ваших компаний», строки транспортов из `overview` (только открытые + закрытые, если у аккаунта уже есть номер), «Работает для всех ваших компаний: N»; ключ `['notification-numbers']`, после действий — `invalidateQueries` | ezbook, goods, dom |
| `NumberRow` | статус (цвет и текст), `displayText`, кнопка по `action` (включая `AcceptTerms`), меню «…» | `NumbersBlock` |
| `ConnectWizard` | `role="dialog"`, индикатор шага, фокус на заголовке; шаги `PaymentStep`, `PaymentPendingStep`, **`TermsStep`**, `QrStep`, `DoneStep`; начальный — `wizardStep` | `NumberRow` |
| `PaymentStep` / `TermsStep` | одна форма (статус, ИНН, оферта, риск — две раздельные отметки, текст риска полностью до отметки); `Payment` — `paymentRequest: true` и цена; `Terms` — `false`, без цены и слов об оплате; тексты §40.33.12 контракта | `ConnectWizard` |
| `QrStep`, `DoneStep` | QR с `alt`, инструкция сервера; результат проверки | `ConnectWizard` |
| `DeliveryModeBlock` | «Как доставлять» по `deliveryChoiceVisible` | ezbook, goods, dom |
| `MessengerOptIn` + `useMessengerOptInDefault` | подпись `checkboxLabel` + правовой текст ключа с запасным текстом из обзора; умолчание §40.11.4 | ezbook, goods, dom |
| `StaffMessengerConsentCheckbox` | «Клиент согласился получать сообщения об этой записи в {мессенджер}», снята по умолчанию, подсказка `StaffBookingMessengerConsentHint` | ручная запись ezbook |
| `NotificationPreferencesCard` (P2) | общий выключатель | профили трёх сайтов |
| `ChannelBreachBanner` | по `displayStatus = ActionRequired` и `action ∈ {AcceptTerms, BindNumber, Reconnect, ReplaceNumber, Unbind}` | шапка кабинета ezbook |

`src/components/pricing/MessengerAddonLines.tsx` — строки цен, «Условия», оговорка, сноска.
`src/components/admin/notifications/` — таблица, фильтры, карточка, «Подтвердить оплату», блок «Подключение мессенджеров».

### §40.15.2 Удаляется

`NotificationsSection.tsx` (с `ChannelCard`, `OfferCard`), `AssignCompanyDialog.tsx`, `RiskAcceptanceModal.tsx`,
`ChannelRequestModal.tsx` (+ тест), `QrModal.tsx`; `shared-sources.js` без `'owner/NotificationsSection'`; guard-тесты goods/dom и
ESLint-границы зелёные.

### §40.15.3 Страницы

- ezbook `CompanyManagePage`: «Уведомления клиентам», `?tab=notifications`, порядок US-06.
- ezbook `CabinetPage`: строка «Номера для сообщений клиентам — в настройках каждой компании» + ссылка; баннер по overview.
- goods, dom: `NumbersBlock` + `DeliveryModeBlock`.
- ezbook `BookingModal` (и `EmbedPage`): клиентский режим — `MessengerOptIn`; режим сотрудника (ручная запись с именем гостя)
  — `StaffMessengerConsentCheckbox` при `customerMessaging.offered`. goods `CartPanel`, dom `BookingPanel` — `MessengerOptIn`
  (в dom галочка больше не показывается безусловно).
- Админка `NotificationsAdminTab`: таблица, карточка, «Подтвердить оплату», блок «Подключение мессенджеров», 5 плиток.
- Цены: `PlanCard`, `BillingPage` — `MessengerAddonLines`.

### §40.15.4 Типы

`api-cycle40.generated.ts`; рукописные `src/types/index.ts` — `ChannelDto` (+поля), `NotificationSettings` (+5),
`CompanyDto.customerMessaging`, `CreateBookingPayload.notifyByMessenger?`, `NotificationPreferences.providerDeliveryConsent`,
`BookingDto` (+3).

---

## §40.16. Структура проекта — что добавляется и меняется

```
ServiceBooking.Core/
├── Entities/  Booking (+4), NotificationChannel (+4), ChannelOptionChangeLog (новая)
└── Enums/     NotificationReason (+3), ChannelStateReason (+5), ConsentSource (+3), ChannelTestResult, ChannelOptionChangeSource

ServiceBooking.Infrastructure/  AppDbContext (ChannelOptionChangeLog, индекс TestPending); Migrations/*_Cycle40ChannelOptions.cs

ServiceBooking.API/
├── Services/Notifications/Funding/   ChannelOptionCodes, ChannelOptionAvailability (open/sellable), ChannelOptionFunding (pure),
│                                     TransportFunding (pure), AccountMessagingReader, AccountMessagingState, MessagingDemand,
│                                     ChannelOptionLog
├── Services/Notifications/           NotificationGate, NotificationRouting, MessengerConsentRule (pure) + MessengerConsentReader,
│                                     PendingRebinder, CustomerMessagingOffer(+Rule), CompanyMessagingStatus, ChannelTestMessenger,
│                                     ChannelStateTransition (+метка), ChannelFundingReader (фасад), ChannelIdleCalculator,
│                                     PlatformSettings (+3 ключа)
├── Services/ChannelPresentation.cs   Display, WizardStep (+Terms), StateText(transport), QrInstruction, SettingsBlockedReason
├── Services/Billing/                 −ChannelEligibility, −TrialMailingRulePolicy, −ChannelFunding.Rank(int); SubscriptionResolver,
│                                     OrdersPlanResolver, OwnerSubscriptionService, TrialActivationService, BillingTexts,
│                                     MessengerAddonsBuilder, LegalOptionGuards (+max), OptionCapabilityCatalog (+max)
├── Services/DeploymentSafetyChecks   ServerCountry по транспорту
├── Services/Bookings/BookingCreationService   согласие клиента/сотрудника, ConsentLedger
├── Services/Orders/OrderCreationService, Services/Stays/StayBookingCreationService   ConsentLedger (Т40-L-07), новое «предлагается»
├── Services/NotificationScheduler.cs           контекст по аккаунту, «{Компания}:», MessengerConsentRule
├── Services/Billing/CompanyTransferService.cs  PendingRebinder
├── Services/Scheduling/Tasks/        ChannelTestMessageTask (новая), NotificationDispatchTask, ChannelHealthTask, TrialLifecycleTask
├── Controllers/                      NotificationChannelsController, NotificationsController (preferences +1 поле),
│                                     CompanyNotificationsController, ShopNotificationsController, Stays/StaysCompaniesController,
│                                     AdminChannelsController (+card, confirm-payment), AdminPlatformController (+3 поля),
│                                     AdminBillingController, CompaniesController.GetBySlug, BookingsController, StorefrontController,
│                                     Stays/StaysPublicController, CompanyTransferController, AdminAccountDtoBuilder, PricingController
└── appsettings*.json                 ScheduledTasks:channel-test-message; Notifications:OptionAvailability:{WhatsApp:false, Max:true};
                                      Notifications:TestMessage:AllowSameNumber:false; Notifications:RebindPendingToOtherTransport:false;
                                      Notifications:GreenApi:ServerCountryByTransport

ServiceBooking.UnitTests/   ChannelOptionFundingTests, ChannelOptionAvailabilityTests, TransportFundingTests,
                            NotificationRoutingSingleTransportTests, NotificationGateTests (переписан), MessengerConsentRuleTests,
                            ChannelDisplayTruthTableTests, WizardStepTests, CustomerMessagingOfferRuleTests, MessagingDemandTests,
                            PendingRebindKeyTests, MessengerAddonsBuilderTests (все — по channel-vectors.json),
                            AllowNotificationChannelReadGuardTests, AssignmentReadGuardTests, Cycle40EnumAppendOnlyTests,
                            MessengerTextsForbiddenWordsTests (Т40-L-14: «VPN», «ВПН», «прокси», «обход» в текстах сервера)
ServiceBooking.Tests/       Tests/Cycle40*: ChannelOptionsMigrationTests, OptionAvailabilityTests, NoPlanFlagTests, TrialOpenOptionsTests,
                            AllCompaniesOnNumberTests, CompanyTransferRebindTests, CrossAccountIsolationTests, WizardApiTests
                            (включая шаг «Условия» на триале), AutoTestMessageTests, ReplaceFromConnectedTests,
                            BookingMessengerConsentTests (клиент, сотрудник, null), ConsentLedgerOptInTests (Т40-L-07),
                            MessengerBodiesNameSenderTests, SingleTransportRoutingTests, PlatformSwitchTests, LegacyRoutesTests,
                            AdminChannelCardTests, PendingRebindTests, Cycle40RollbackReadTests, Cycle40ContractTests

frontend/src/components/notifications/   NumbersBlock, NumberRow, ConnectWizard (+шаги, TermsStep), DeliveryModeBlock, MessengerOptIn,
                                         useMessengerOptInDefault, StaffMessengerConsentCheckbox, NotificationPreferencesCard (P2),
                                         ChannelBreachBanner
frontend/src/components/pricing/MessengerAddonLines.tsx; frontend/src/components/admin/notifications/*
frontend/src/utils/channelRules.ts (+test по channel-vectors.json); src/types/api-cycle40.generated.ts; package.json types:api:cycle40
contracts/cycle40/       openapi.yaml, openapi.json, channel-vectors.json; contracts/redocly.yaml (+cycle40)
deploy/checks/           cycle40-channels-report.sql, cycle40-rollback-assignments.sql
.github/workflows/ci.yml; DEPLOY.md §29; API_DOCUMENTATION.md «Каналы рассылок (цикл 40)»
```

`Cycle22RouteTable.golden.txt` — **3 новых маршрута** (§40.37 контракта).

---

## §40.17. Выкат, отчёт и откат (T-40-02, DO)

- **`deploy/checks/cycle40-channels-report.sql`** (только чтение, копия боя, до выката): 1) стоп-сигнал — каналы, оплаченные
  по старому и новому правилу (ожидается 0 и 0); 2) строки опций без даты (триальные/нет); 3) аккаунты с двумя живыми каналами
  одного транспорта; 4) компании, которые начнут слать без назначения; **5) будущие визиты, созданные сотрудником, и гостевые
  записи** — сколько напоминаний перестанет уходить по Р40-Ю2 (для сведения заказчику).
- **`deploy/checks/cycle40-rollback-assignments.sql`** — только при откате: назначение каждой компании на первый живой канал
  каждого транспорта аккаунта, идемпотентно.
- **Порядок (`DEPLOY.md` §29, DO-40-03):**
  1. отчёт на копии боя → 0 оплаченных; иначе стоп;
  2. выкат (миграция при старте);
  3. **доступность и цены (Т40-L-01, Р40-Ю1):** WhatsApp закрыт (умолчание), MAX открыт; цены опций до публикации новой
     редакции D3 и Приложения № 1 не задаются (или опции неактивны) — значит, MAX тоже не продаётся до публикации, даже
     открытый: «продаётся» требует цену и опубликованную оферту (§40.7.1);
  4. выключатель рассылок включён; `NOTIFICATIONS_PROVIDER` и `ServerCountry*` — проверить на машине (DO-40-05);
  5. M40-01…08;
  6. открыть функцию владельцам — после вычитки текстов и публикации редакций.
  Откат: релиз назад + скрипт назначений; БД назад не откатывается; записи сотрудников без отметки после отката снова получат
  сообщения «как до цикла» (§40.2.1).

---

## §40.18. Разбивка работ и параллельность

Контракт (`openapi.yaml` + `channel-vectors.json`) — до кода; фронтенд стартует на prism-моке
(`npx @stoplight/prism mock contracts/cycle40/openapi.yaml --port 4040`) в день 1.

### §40.18.1 Backend

| # | Задача | SPEC / Т40-L | Зависит от | Параллельно с |
|---|---|---|---|---|
| BE-40-C | `contracts/cycle40/openapi.yaml` (§40.38 контракта) + `channel-vectors.json` (§40.39), `redocly lint`, `openapi.json` | T-40-13 | — | всё; **первый коммит** |
| BE-40-P | Чистые правила + юнит-тесты по векторам: `ChannelOptionFunding`, `ChannelOptionAvailability`, `TransportFunding`, `NotificationRouting`, `NotificationGate`, **`MessengerConsentRule`**, `ChannelPresentation.Display/WizardStep(+Terms)/StateText`, `CompanyMessagingStatus`, `CustomerMessagingOfferRule`, `MessagingDemand`, `PendingRebind`, `MessengerAddonsBuilder`, тексты + `MessengerTextsForbiddenWordsTests` | T-40-01/05/07, L-10/L-14 | BE-40-C | всё |
| BE-40-M | Сущности, enum (вкл. `ConsentSource`), `AppDbContext`, **одна миграция**, `ShowcaseOwnership`, `ChannelOptionCodes`, `Cycle40EnumAppendOnlyTests` — один разработчик, один коммит | T-40-02, L-12 | — | BE-40-P |
| BE-40-1 | Оплата и доступность: `AccountMessagingReader`, фасад `ChannelFundingReader`, удаление полей плана, `ChannelEligibility`, `TrialMailingRulePolicy`; `SubscriptionResolver`; **три ключа платформы** (доступность ×2, выключатель — чтение) + умолчания конфигурации; триал только открытых опций; `AdminBillingController` (дата, правила, закрытая → 409); `ChannelOptionLog` у всех писателей; `OwnerSubscriptionService`; `LegalOptionGuards`/`OptionCapabilityCatalog` (+max); стражи | US-01/02, Р40-Ю1, L-04 | BE-40-M, BE-40-P | BE-40-3 |
| BE-40-2 | Все компании на номере: 26 мест §40.4.2; три планировщика; диспетчер; `PendingRebinder` (отвязка = отмена по умолчанию, замена, передача); простой; `AssignmentReadGuardTests`; `ShopChannelReader` | US-03/09, Р40-Ю3 | BE-40-1 | BE-40-4 |
| BE-40-3 | Мастер: `overview` (открытые транспорты, `termsAccepted`, `wizardStep` с `Terms`); `POST` (`paymentRequest`, закрытая → 409, риск в запросе, продление); `connect` (условия текущей версии, из `Disconnected`, 202, сброс `ConnectedAtUtc`); `qr`; `replace`; `companies` 410/204; автопроверка + задача; `test-message`; **`ServerCountry` по транспорту + `ProviderServerCountry`** | US-04/14, L-02/L-03 | BE-40-1 | BE-40-2 |
| BE-40-4 | Представление в `ChannelDto`/overview; настройки трёх видов компаний; снятие 402 | US-05/06 | BE-40-1, BE-40-P | BE-40-5 |
| BE-40-5 | Клиентская сторона: `CustomerMessagingOffer` + поля в трёх публичных ответах; `Bookings` (флаг/версия/время/сотрудник); **`MessengerConsentRule` в трёх планировщиках**; **запись `ConsentLedger` при отметке вошедшего** (ezbook, goods, dom); `preferences.providerDeliveryConsent`; `BookingDto` (+3); «{Компания}:» + `MessengerBodiesNameSenderTests`; выгрузка; Т40-L-08 по результату QA-40-7 | US-07/08, T-40-06, L-05/07/09/10/12 | BE-40-2 | BE-40-4, BE-40-6 |
| BE-40-6 (P1) | Выключатель — запись и тексты; админ: фильтры, сводка, карточка (+`providerServerCountry`), `confirm-payment` (закрытая → 409); **переключатели доступности в `platform-settings`**; игнор поля цены | US-10/12, Р40-Ю1 | BE-40-1, BE-40-4 | BE-40-7 |
| BE-40-7 (P1) | `messengerAddons` + `messengerAddonsNote` в `/api/pricing` и подписке | US-11, L-11 | BE-40-1 | BE-40-6 |
| BE-40-8 | Golden маршрутов, `OpenApiContractValidatorTests` `cycle40`, функциональные `CY40-*`, `API_DOCUMENTATION.md` | T-40-13 | в конце | — |

Проверки выключателя и доступности в правилах (`PlatformEnabled`, `sellable`) входят в BE-40-P/BE-40-1 сразу; BE-40-6
добавляет только админские переключатели. Если BE-40-6 урезан — доступность меняется прямой записью ключа в `PlatformSettings`
по `DEPLOY.md` §29.

### §40.18.2 Frontend

| # | Задача | SPEC / Т40-L | Зависит от | Параллельно с |
|---|---|---|---|---|
| FE-40-0 | генерат `cycle40`, API-клиенты, `channelRules.ts` + тест по векторам, фикстуры (закрытый WhatsApp, триал на шаге `Terms`) | T-40-13 | BE-40-C | — |
| FE-40-1 | `NumbersBlock`, `NumberRow`, `ConnectWizard` (+`TermsStep`, тексты Т40-L-13, без слов об обходе), меню, удаление старых модалок, `shared-sources.js` | US-04/05, L-02/03/13/14 | FE-40-0 | FE-40-3…6 |
| FE-40-2 | ezbook: слияние вкладки, `?tab=notifications`, кабинет, баннер, `DeliveryModeBlock` | US-06/09 | FE-40-1 | FE-40-4 |
| FE-40-3 | goods, dom: `NumbersBlock` + `DeliveryModeBlock` | US-06 | FE-40-1 | FE-40-2 |
| FE-40-4 | `MessengerOptIn` + `useMessengerOptInDefault` (по `preferences.providerDeliveryConsent`) в трёх формах; **`StaffMessengerConsentCheckbox` в ручной записи ezbook**; запасные тексты из обзора §5.3, §6.2, §7.3 | US-07/08, L-09/10 | FE-40-0 | FE-40-1 |
| FE-40-5 (P1) | Админка: таблица, карточка, «Подтвердить оплату», **блок «Подключение мессенджеров»**, 5 плиток | US-10/12, Р40-Ю1 | FE-40-0 | FE-40-1 |
| FE-40-6 (P1) | `MessengerAddonLines`: строки, «Условия», оговорка, сноска WhatsApp | US-11, L-11 | FE-40-0 | всё |
| FE-40-7 (P2) | `NotificationPreferencesCard` в goods и dom; «Отправить проверочное ещё раз» | US-13/14 | FE-40-1 | — |

### §40.18.3 DevOps

| # | Задача | Когда |
|---|---|---|
| DO-40-01 | SQL-отчёт (5 блоков) и скрипт отката (§40.17) + прогон на копии боя | сразу; до мержа в `master` |
| DO-40-02 | CI: lint, `types:api:cycle40`, `contracts-to-json` (+cycle40) | после BE-40-C |
| DO-40-03 | `DEPLOY.md` §29: порядок §40.17, доступность и цены (Т40-L-01), выключатель как стоп-кран, конфигурация задачи и флагов, последствие отката для записей сотрудников | сразу |
| DO-40-04 | Стенд M40: партнёрский аккаунт GREEN-API, номера WhatsApp и MAX (не совпадающие с телефоном владельца + один совпадающий для M40-02); WhatsApp на стенде открывается переключателем только для M40 | до приёмки |
| DO-40-05 | Снять с боя `ServerCountry*` и фактическую страну экземпляров по транспортам → юристу для `{{МЕСТО_ОБРАБОТКИ_ПОСРЕДНИКОМ}}` и П1.12.1 | до публикации редакций |

DO-40-* сверяются с `git log` до мержа.

### §40.18.4 QA

`CY40-*` (T-40-13 1:1 + найденное №1–№4 + Р40-Ю1…Ю3): закрытый WhatsApp нигде не продаётся и не выдаётся триалом, но уже
оплаченный работает; открытие переключателем без релиза; запись сотрудника с отметкой / без / номер аккаунта с согласием;
`null` от старого клиента; отметка вошедшего пишет согласие; предзаполнение только при согласии; отвязка отменяет `Pending`;
триал проходит шаг `Terms`; `connect` со старой версией риска → 409; название компании во всех сообщениях общего номера;
**QA-40-7** — Т40-L-08. schemathesis; векторы в C# и TS; параллельные тесты (`connect`, `qr` + задача, две копии задачи);
регресс трёх вертикалей; откат (`Cycle40RollbackReadTests`, M40-08); grep текстов на слова обхода.

### §40.18.5 Ручные M40 (гейт выката)

M40-01 MAX: заявка → «Подтвердить оплату» → QR → «Готово» → проверочное пришло. M40-02 MAX: триал → шаг «Условия» → QR;
проверка «самому себе». M40-03 WhatsApp на стенде (открыт переключателем): то же; закрыть — пропал из мастера, цен и офера,
номер работает. M40-04 запись с галочкой → пришло; без — нет; сотрудник с отметкой → пришло; без — нет. M40-05 напоминание;
замена номера; отвязать → `Pending` отменены. M40-06 выключатель. M40-07 360 и 1280 px, клавиатура, диктор. M40-08 откат на
стенде.

### §40.18.6 Точки синхронизации BE↔FE

| Что | Где |
|---|---|
| Форма DTO, enum, коды | `openapi.yaml` → генерат |
| Доступность, оплата, маршрутизация, согласие, три состояния, шаг мастера, «рассылки работают», цены | `channel-vectors.json` |
| Тексты ошибок и мастера | `API_CONTRACT_CYCLE40.md` §40.33 |
| Что показывать | поля сервера: `open`, `action`, `wizardStep`, `canRequestPayment`, `deliveryChoiceVisible`, `customerMessaging.offered`, `preferences.providerDeliveryConsent` — фронт не вычисляет сам |

### §40.18.7 Если не укладываемся

Порядок урезания SPEC: US-40-14 → US-40-13 → US-40-11 → US-40-10. Не урезаются: доступность опций (Р40-Ю1, хотя бы ключом без
интерфейса), отметка сотрудника и правило `null` (Р40-Ю2), запись согласия вошедшего (Т40-L-07), шаг «Условия» (Т40-L-03).

---

## §40.19. Риски, решения и отклонения от буквы SPEC

| # | Риск | Решение |
|---|---|---|
| R-40-1 | Живой отправки через GREEN-API не было | M40 как гейт; выключатель — стоп-кран; инструкция MAX — черновик |
| R-40-2 | «Оплаченных каналов нет» неверно | стоп-сигнал отчёта; наследное правило в коде |
| R-40-3 | Поведение меняется без действий владельца + **меньше напоминаний** гостям и клиентам, записанным по телефону (Р40-Ю2) | принято заказчиком; блок 5 отчёта показывает масштаб |
| R-40-4 | Проверка «самому себе» | пропуск + флаг после M40-02 |
| R-40-5 | Владелец не вернётся после оплаты | строка, баннер, опрос в мастере |
| R-40-6 | Демо и витрина | `ShowcaseOutboundGuard` первым; `offered = false`; автопроверка пропускается |
| R-40-7 | Триал удваивает экземпляры | экземпляр только на шаге QR; пока WhatsApp закрыт — триал даёт один мессенджер |
| R-40-8 | Перенос `Pending` на другой мессенджер выходит за согласие | флаг **выключен** по умолчанию (Р40-Ю3) |
| R-40-9 | Удаление полей плана ломает тесты | намеренно; изменения ожиданий — в отчёте |
| R-40-10 | Приостановка начинает реально останавливать отправку | исправление; затронутых нет |
| R-40-11 | Гонки перехода в `Connected` | замок канала; идемпотентная метка; условный `UPDATE` |
| R-40-12 | Откат после записи новых enum | тест отката + M40-08; последствие для записей сотрудников — в `DEPLOY.md` |
| R-40-13 (новый) | Т40-L-06: выключение платформой «съедает» оплаченные дни | не в объёме этого обновления; до решения заказчика — ручное продление суперадмином по журналу выключателя через «Подтвердить оплату»; текст П1.9.9 — у юриста |
| R-40-14 (новый) | Закрытый WhatsApp у аккаунтов с триалом до цикла | их триальные строки работают до конца окна (купленное не отключается); новые триалы WhatsApp не получают |
| R-40-15 (новый) | Предзаполнение зависит от `ProviderDelivery` прежней редакции D4 | Т40-L-08, QA-40-7 |

**Отклонения от буквы SPEC (читать обязательно):**
1. **WhatsApp закрыт по умолчанию** (Р40-Ю1): О6 «триал даёт оба» и US-11 «две строки цен» действуют только для открытых опций.
2. **Q-40-6 отменён** (Р40-Ю2): запись сотрудника без отметки и `null` от старых клиентов шлют только на номер аккаунта с
   действующим согласием `ProviderDelivery`.
3. **US-04 «оплачено → сразу QR» уточнено**: оплаченный (в т. ч. пробный) транспорт без принятых условий текущей редакции
   идёт через шаг «Условия» (Т40-L-03).
4. **Умолчание галочки у вошедшего** — по действующему согласию `ProviderDelivery` и отсутствию отписки, а не по выключателю
   профиля (Т40-L-09; Р6 соблюдён — оба значения «из профиля»).
5. `POST /api/notification-channels` отвечает 200 при продлении заявки и принимает `paymentRequest: false` (шаг «Условия»).
6. «Оплачен ровно один транспорт» (US-09) уточнено до «маршрутизируем ровно один».
7. Галочка показывается, только если сообщение уйдёт сейчас.
8. `messengerAvailable` магазина и «Домов» — «есть оплаченный транспорт», 409 при включении флага сохраняется; у салона 402
   сняты, включая шаблоны.
9. Строки опций без даты — правило в коде (О-40-5).
10. Опции каналов не предлагаются в «доступных опциях» страницы подписки.
11. `Replaced` по умолчанию скрыты в админской таблице.
12. При отвязке мессенджера ожидающие сообщения отменяются (Р40-Ю3).
13. Машиночитаемый контракт в этом заходе не создан: состав — `API_CONTRACT_CYCLE40.md` §40.38–§40.39, задача BE-40-C.

**Ответы на вопросы архитектору (§9 SPEC):** О-40-1 — §40.4.2; О-40-2 — §40.8; О-40-3 — §40.10; О-40-4 — §40.9;
О-40-5 — §40.3.3. Открытые вопросы — к живому юристу (обзор §12 пп. 5–9) и к заказчику по R-40-13 (Т40-L-06).
