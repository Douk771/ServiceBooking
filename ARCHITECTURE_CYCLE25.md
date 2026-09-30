# ARCHITECTURE — цикл 25 ServiceBooking: «Заказы», цикл 3 «Аналитика, каталог по городу и MAX для персонала» (goods.ezbook.ru)

**Разделы §495–§519** (A10: после §494 — максимума в `API_CONTRACT_CYCLE24.md`; пересечений с документами прежних
циклов нет — проверено поиском по `*.md`).

**Вход:**
- `SPEC.md` цикла 25 (Q-25-1…Q-25-8 решены по колонке «Рекомендую», §0);
- `CURRENT_STATE.md` на `f739382` (блок ⏰24, §9 C24-*);
- код ветки `cycle/025-goods-orders-insights-max` (= `develop` `2405f8d`, код = `f739382`);
- документы цикла 24: `ARCHITECTURE_CYCLE24.md` §446–§469, `API_CONTRACT_CYCLE24.md` §470–§494, `contracts/cycle24/`.

Ветку подготовил devops. Архитектор ветки не трогает.

**Документы цикла:**

| Файл | Что | Кто читает |
|---|---|---|
| `ARCHITECTURE_CYCLE25.md` (этот) | решения, модель, структура, задачи, риски | все |
| `API_CONTRACT_CYCLE25.md` (§520–§542) | контракт словами: порядок проверок, тексты, коды, изменения существующих маршрутов | backend, frontend, QA |
| `contracts/cycle25/openapi.yaml` | **источник истины по форме** (OpenAPI 3.0.3): prism-мок, `openapi-typescript`, schemathesis, redocly | backend, frontend, QA, CI |

Корневые `ARCHITECTURE.md`/`API_CONTRACT.md` — документы цикла 3, по конвенции не перезаписываются.
`contracts/cycle23/goods-routes.json` остаётся **единственным** источником маршрутов goods (новые — FE-0, §513).

**Стек, структура репозитория и все решения циклов 23–24 сохраняются.** Цикл — расширение: новых внешних
интеграций, пакетов, сервисов и переменных окружения, кроме одного рубильника (`STAFFMAX_ENABLED`, §510.3), нет.

---

## §495. Итог решений — ответы на SPEC §7 (A1–A10) одним экраном

| # | Вопрос SPEC | Решение | Раздел |
|---|---|---|---|
| A1 | Привязка чата MAX | Таблица `StaffMaxLinks` (одна строка на **аккаунт**, Q-25-3) + одноразовые сессии `StaffMaxLinkSessions` по образцу `PhoneVerificationSession`: в БД только SHA-256 payload, TTL 10 мин | §498 |
| A1 | Общий вебхук на два сценария | Различение по **префиксу payload**: `v1.` — подтверждение телефона (как есть), `sm1.` — привязка персонала. Вебхук-обработчик цикла 14 получает расширяемый список `IMaxBotUpdateHandler`; ветка телефона не меняется ни строкой логики | §498.3 |
| A1 | Остановка бота | Два пути: апдейт `bot_stopped` (подписка расширяется, с откатом на старый список типов при отказе платформы) и 403/404 при отправке. Привязка переходит в `StoppedInMax`, шифртекст чата стирается | §498.4 |
| A1 | Исходящая отправка | **Новый отдельный интерфейс** `IMaxBotMessenger.SendAsync → MaxSendOutcome`, реализуется тем же синглтоном `MaxBotClient` (общие лимитеры частоты). `IMaxBotClient` цикла 14 и его подделки в тестах не меняются | §499.3 |
| A1 | Очередь | **Отдельная таблица** `StaffMaxMessages` (не колонка в `StaffPushNotifications`): строка очереди — на **чат**, а не на пользователя и устройство. Иначе дедупликация по чату (Q-25-3) не выражается уникальным ключом | §499.1 |
| A1 | Дедупликация | Уникальный `IdempotencyKey = {type}:{orderEventId}:{chatKey}`; `ChatKey` = HMAC идентификатора чата. Одно событие — одна строка на чат, сколько бы аккаунтов его ни привязали | §499.1 |
| A1 | Платформенная настройка | `Notifications:StaffMax:Enabled` (в репозитории `false`) **и** `PhoneVerification:Provider = max-bot`. Для **подключения** дополнительно нужен живой вебхук (`WebhookSubscribed`), для **отправки** — нет | §498.1 |
| A1 | Где выбираются получатели | Только `OrderNotificationPlanner` (события заказа) и `OrderLimitWarner` (предупреждение владельцу, как у push). Права перепроверяются при отправке | §499.2 |
| — | C24-8 (долгий проход задач) | `ScheduledTaskRunner` получает **полосы**: `realtime` (три диспетчера: push персоналу, push покупателю, MAX персоналу) идёт своим циклом параллельно полосе `main` | §499.5 |
| A2 | История | Фильтры накладываются внутри диапазона `PickupDate` по индексу `(CompanyId, PickupDate, PickupStartUtc)`. Новый покрывающий индекс `(CompanyId, PickupDate) INCLUDE (Status, EstimatedTotal, FinalTotal)` для итогов. Для карточки покупателя — `(CompanyId, CustomerPhone)`. Поиск по фрагменту телефона — без индекса, в пределах периода. Итог по фильтру — один агрегатный запрос, страница — второй. Запрос истории — **POST с телом**: телефон не попадает в query-строку | §501 |
| A3 | Сводка | Агрегаты в SQL: `GROUP BY Status` (1 запрос), топ товаров `GROUP BY ProductId, Unit` (1), по дням (P1, 1), прошлый период (P1, 1). Одно выражение «итог заказа» для истории и сводки — `OrderReportExpressions.Total` | §502 |
| A4 | Лист сборки | Выборка §451.5 цикла 24 + статусы `Accepted` (+ `New` переключателем), интервал по `PickupStartUtc` (у ASAP — ориентир), группировка — чистый `PickListBuilder`. Сетка слотов дня — новая чистая `PickupSchedule.DaySlots(day)` (без отсечения «уже прошло») | §503 |
| A5 | Покупатель | Ключ — нормализованный `Orders.CustomerPhone` в пределах магазина. **Непрозрачный id в адресе — id любого заказа этого покупателя в этом магазине** (`customerRef`). Заметка — `ShopCustomerNotes (CompanyId, Phone)` | §504 |
| A6 | Каталог | Анонимный `GET /api/goods/catalog`. Кандидаты — один SQL, правило приёма — **пакетный** `ShopGateLoader.LoadManyAsync` (4 запроса на всю страницу) и та же `ShopOrderingGate.Evaluate`. Сортировка по вычисленному состоянию — в памяти над списком города (≤ 1000). Кеш списка города 30 с. Политика частоты `goods-catalog` 120/мин по IP. `/` goods — каталог; P1 `/city/:cityId` | §505 |
| A7 | Тарифный флаг | `OrdersPlan.AllowPublicListing` из существующей колонки тарифа линейки «Заказы». Миграция ставит `true` системному бесплатному тарифу линейки. Поле — в админке тарифов линейки. Салонный резолвер, `PublicListingQuery` и `GET /api/companies*` не меняются | §505.2 |
| A8 | Права | Три новых разрешения `ShopAccess`: `ViewOrderReports` (история, лист сборки, карточка — персонал), `EditCustomerNotes` (персонал), `ViewSummary` (владелец, SuperAdmin) | §506 |
| A9 | Долги G | T-25-01: разворачиваются NAT64/6to4/IPv4-compatible/IPv4-translated; **`64:ff9b:1::/48` и Teredo `2001::/32` отклоняются целиком**. T-25-02: `UseProxy = false` через фабрику обработчика с юнит-тестом. T-25-03: 400 «День недели указан дважды» + `uniqueItems` + аудит 400 в `contracts/cycle24`. T-25-04: `OrderPickupContext.WorkingDay` становится **обязательным**, `OrderPickupContext.For` удаляется; единственные производители — `ShopGateLoader.PickupContextAsync` / новый пакетный `PickupContextsAsync` | §507 |
| A10 | Нумерация | §495–§519 (этот документ), §520–§542 (контракт); контракт — `contracts/cycle25/`; маршруты goods — `contracts/cycle23/goods-routes.json` | — |

---

## §496. Стек: новых зависимостей — ноль

| Потребность | Чем закрываем | Почему не новое |
|---|---|---|
| Отправка в MAX | существующий `MaxBotClient` (HTTP, лимитеры `System.Threading.RateLimiting`) + новый интерфейс `IMaxBotMessenger` | бот и токен уже есть; SDK MAX не нужен |
| Шифрование id чата | `SecretProtector` + `Notifications:EncryptionKey` (как ключи push) | тот же ключ уже обязателен для каналов и push |
| Непрозрачный ключ чата | `ExternalAccountKey.Compute` (HMAC, `PhoneVerification:ExternalKeyHmac`) с доменным префиксом `max-chat:` | новый секрет не нужен |
| QR подключения | `QrImage.EncodePng` (цикл 14) | — |
| Отчёты | EF Core LINQ → SQL-агрегаты, покрывающий индекс Npgsql (`IncludeProperties`) | OLAP/материализованные представления избыточны при ≤ 200 000 заказов магазина в год |
| Поиск по фрагменту телефона/имени | `ILIKE` внутри периода | `pg_trgm` — новое расширение БД ради ~200 000 строк; период уже сужает выборку |
| Кеш каталога | `IMemoryCache` (in-process, 30 с) | Redis — лишняя стоимость хостинга; при двух экземплярах API у каждого свой кеш, расхождение ≤ 30 с допустимо (SPEC: ≤ 60 с) |
| Печать листа сборки | CSS `@media print` в браузере | PDF-генерация не нужна (SPEC §3: только печать страницы) |

**Масштаб и продажа как сервиса.** Всё новое состояние в БД: привязки, сессии, очередь, заметки. Кеш каталога —
только ускорение. Второй экземпляр API ничему не мешает: диспетчер MAX защищён advisory-lock задачи, in-flight меткой
и уникальным ключом, как push. Отчёты читают ту же БД; при росте на порядок их можно перевести на реплику чтения без
изменения контракта.

---

## §497. Модель данных

Сущности — `ServiceBooking.Core/Entities/`, конфигурация — `AppDbContext`. Перечисления хранятся числом и **только
дописываются**. **Миграция цикла одна** — `Cycle25OrdersInsightsMax`, создаётся закреплённым `dotnet-ef` 8.0.11
**одним разработчиком одним коммитом** первой задачей (BE-M, §512). После каждого мерджа `develop` в ветку —
пересборка Designer-снимка и зелёный `deploy/ci/check-migration-snapshots.sh` (урок C23-4). `dotnet ef migrations
remove` на миграциях циклов 23–25 не применять.

### §497.1 Изменения существующих таблиц

| Таблица | Изменение | Зачем |
|---|---|---|
| `ShopSettings` | `StaffMaxEnabled bool NOT NULL DEFAULT true` (**DB-default обязателен**: `OrderEventLog` делает `INSERT … ON CONFLICT` со списком колонок, §448.1) | флаг магазина «Сообщения сотрудникам в MAX» (US-25-02) |
| `Orders` | индекс `IX_Orders_CompanyId_CustomerPhone` `(CompanyId, CustomerPhone) WHERE "CustomerPhone" IS NOT NULL` | карточка покупателя, заметка, срок хранения заметок |
| `Orders` | индекс `IX_Orders_Report` `(CompanyId, PickupDate) INCLUDE (Status, EstimatedTotal, FinalTotal)` | итоги истории и сводки индексным сканом без чтения строк |
| `Companies` | **данные:** `UPDATE "Companies" SET "ShowInPublicListing" = true WHERE "Kind" = 1` | §519 п. 1: у магазинов значение было жёстко `false` кодом, это не выбор владельца |
| `SubscriptionPlanConfigs` | **данные:** `UPDATE … SET "AllowPublicListing" = true WHERE "IsSystemFree" AND "Line" = 1` | Q-25-7: бесплатному тарифу линейки показ разрешён; сид цикла 24 поставил `false` |

Новых колонок в `Companies` и `SubscriptionPlanConfigs` нет: используются существующие `ShowInPublicListing` и
`AllowPublicListing` (Q-25-7).

### §497.2 Новые сущности

**`StaffMaxLink`** — привязка чата MAX к аккаунту (одна на человека).

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | uuid, генерируется приложением **до** вставки | входит в AAD шифрования |
| `UserId` | text, FK AspNetUsers `Cascade`, **уникальный** | Q-25-3: одна привязка на аккаунт; повторное подключение перезаписывает строку |
| `ChatKey` | varchar(64), индекс (не уникальный) | `ExternalAccountKey.Compute(ExternalKeyHmac, "max-chat:" + chatId)`; один чат может быть привязан к нескольким аккаунтам |
| `ChatIdCiphertext` | text, null | `SecretProtector.Encrypt(chatId, Notifications:EncryptionKey, aad: "staff-max-link:{Id}")`; `null` после остановки бота |
| `KeyId` | varchar(16), null | как у `PushSubscription` (ротация ключа) |
| `Status` | int (`StaffMaxLinkStatus { Active = 0, StoppedInMax = 1 }`) | |
| `LinkedAtUtc` | timestamptz | для кабинета «Подключено 30.09.2026» |
| `StoppedAtUtc` | timestamptz, null | |
| `LastSuccessAtUtc` | timestamptz, null | |
| `ConsecutiveFailures` | int, default 0 | |

**Идентификатор чата не отдаётся ни в одном DTO и не входит в выгрузку** (как `ExternalAccountKey` цикла 14).

**`StaffMaxLinkSession`** — одноразовая ссылка подключения.

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | uuid | |
| `UserId` | text, FK AspNetUsers `Cascade`, индекс | |
| `PayloadHash` | varchar(64), **уникальный** | SHA-256 payload (`PayloadGenerator.Hash`); сам payload не хранится |
| `CreatedAtUtc`, `ExpiresAtUtc` | timestamptz | TTL `Notifications:StaffMax:LinkSessionTtlMinutes` = 10 |
| `CompletedAtUtc` | timestamptz, null | момент привязки; повторное использование → «ссылка устарела» |

Новая сессия пользователя удаляет его прежние незавершённые сессии (в той же транзакции).

**`StaffMaxMessage`** — очередь сообщений в MAX. Форма — `StaffPushNotification` без `UserId`/`SubscriptionId`.

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | uuid | |
| `CompanyId` | uuid FK Companies `Restrict` | магазин события |
| `OrderId` | uuid FK Orders `SetNull`, null | `null` у предупреждения о лимите |
| `ChatKey` | varchar(64) | адресат — чат, не пользователь |
| `Type` | int `NotificationType` | `StaffOrderCreated`, `StaffOrderCancelledByCustomer`, `OwnerOrderLimitWarning` (существующие члены) |
| `Text` | varchar(2000) | готовый текст **без ПДн покупателя** (§525 контракта) |
| `Status` | int `NotificationStatus` | существующее перечисление |
| `Reason` | int `NotificationReason`, null | |
| `ReasonDetail` | varchar(300), null | |
| `AttemptCount`, `LastAttemptAtUtc?`, `NextAttemptAtUtc?` | | in-flight метка и повторы, как у push |
| `ExpiresAtUtc` | timestamptz | постановка + 1 ч (правило push, §455) |
| `SentAtUtc?`, `CreatedAt` | | |
| `IdempotencyKey` | varchar(200), **уникальный** | `{type}:{orderEventId}:{chatKey}`; у предупреждения — `{keySeed}:{chatKey}` |

Частичный индекс диспетчера `IX_StaffMaxMessages_Dispatch (ExpiresAtUtc, CreatedAt) WHERE "Status" = 0` — копия
индекса `StaffPushNotifications`.

**`ShopCustomerNote`** — заметка магазина о покупателе.

| Поле | Тип | Смысл |
|---|---|---|
| `Id` | uuid | |
| `CompanyId` | uuid FK Companies `Restrict` | |
| `Phone` | varchar(20) | канонический телефон (`PhoneNormalizer`), как `Orders.CustomerPhone` |
| `Text` | varchar(1000) | непустой; пустой ввод = удаление строки |
| `CreatedAtUtc`, `UpdatedAtUtc` | timestamptz | |
| `UpdatedByUserId` | text, null, **без FK** | как `ShopSettings.AcceptanceChangedByUserId` |
| `UpdatedByName` | varchar(200) | снимок имени; при удалении аккаунта автора → «Удалённый пользователь», `UpdatedByUserId = null` |

Уникальный индекс `(CompanyId, Phone)`.

### §497.3 Перечисления (append)

- `StaffMaxLinkStatus { Active = 0, StoppedInMax = 1 }` — новое.
- `NotificationReason` +: `StaffMaxDisabledByShop`, `StaffMaxChatUnavailable`, `StaffMaxNoRecipient`,
  `StaffMaxPlatformDisabled`, `StaffMaxRateLimited`.
- `LegalTextKey` +: `ShopCustomerNoteNotice` — **вне `LegalTextKey.All`** (как `OrderCheckoutNotice`), [legal L17].
- `NotificationType` — новых членов **нет** (`StaffOrderCreated`, `StaffOrderCancelledByCustomer`,
  `OwnerOrderLimitWarning` уже есть, §469).

### §497.4 Down()

Удаляет четыре таблицы, колонку и индексы; возвращает `ShowInPublicListing = false` для `Kind = 1` и
`AllowPublicListing = false` системному тарифу «Заказов». Данные заметок и привязок при откате теряются — это
записывается в `DEPLOY.md` §23 (DO-4).

### §497.5 Инварианты (проверяются тестами)

1. Все инварианты §448.4 цикла 24 в силе.
2. У аккаунта не больше одной строки `StaffMaxLinks`.
3. `Status = Active` ⇔ `ChatIdCiphertext != null`.
4. `ShopCustomerNotes` существуют только у компаний `Kind = Orders`; `Text` непустой и ≤ 1000.
5. На одно событие заказа — не больше одной строки `StaffMaxMessages` на `ChatKey`.
6. Ни один DTO и ни одна секция выгрузки не содержит `ChatIdCiphertext`, `ChatKey` или id чата.

---

## §498. MAX персоналу: подключение и отключение (US-25-01, US-25-03)

### §498.1 Доступность (Q-25-2)

`StaffMaxAvailability` (одно место, `Services/StaffMax/`):

| Признак | Условие | Что даёт |
|---|---|---|
| `Enabled` | `Notifications:StaffMax:Enabled` ∧ `PhoneVerification:Provider = max-bot` ∧ задан `BotUsername` | постановка и отправка сообщений |
| `CanLink` | `Enabled` ∧ `PhoneVerificationDiagnostics.WebhookSubscribed` | выдача ссылки подключения (без вебхука «Начать» не дойдёт до сервера) |

- В репозитории `Enabled = false`. На машине включает человек переменной `STAFFMAX_ENABLED=true` (DO-2, DO-6).
- **Fail-fast** (`DeploymentSafetyChecks.ValidateStaffMax`, вне Development/Testing): при `Enabled = true` обязательны
  `PhoneVerification:Provider = max-bot`, непустые `Notifications:EncryptionKey` и `PhoneVerification:ExternalKeyHmac`.
  Иначе старт падает с понятным сообщением — конвенция проекта для рубильников (`DEPLOY.md` §23).
- При `Enabled = false` блок кабинета показывает «Сообщения в MAX пока не включены на платформе», планировщик не ставит
  строки, задача `staff-max-dispatch` помечает стоящие строки `Skipped(StaffMaxPlatformDisabled)`.

### §498.2 Подключение

1. `POST /api/staff-max/link-sessions` (вошедший; политика `staff-max-link` 10/ч на пользователя):
   - не участник ни одного активного магазина (`CompanyMembership.IsStaffRole`, `Kind = Orders`, `IsActive`) → 409
     строка;
   - `!Enabled` или `!CanLink` → 409 строка;
   - иначе: удалить незавершённые сессии пользователя, создать новую; payload = `StaffMaxPayload.Generate()` =
     `"sm1." + base64url(32 байта CSPRNG)` (47 символов, < 128 — лимит `start` у MAX); deep link
     `https://max.ru/<BotUsername>?start=<payload>`, веб-ссылка `https://web.max.ru/<BotUsername>?start=<payload>`,
     QR PNG (сбой QR не блокирует — как §141 цикла 14). **Сетевых вызовов нет.**
2. Человек нажимает «Начать» → MAX шлёт `bot_started` с `payload` на существующий вебхук
   `POST /api/phone-verification/max/webhook/{token}` (домен `PhoneVerification:Max:PublicBaseUrl` = ezbook.ru; на goods
   этот путь nginx закрывает 404 — так и остаётся).
3. `StaffMaxStartHandler.HandleStartAsync` (одна транзакция):
   - сессия по `PayloadHash`; нет, истекла или уже завершена → ответ бота `StaffMaxTexts.LinkExpired`;
   - `!Enabled` → ответ `StaffMaxTexts.Disabled`, привязки нет;
   - пользователь больше не участник ни одного магазина → ответ `StaffMaxTexts.NoShops`, привязки нет;
   - upsert `StaffMaxLinks` по `UserId`: новый `ChatKey`, шифртекст, `Status = Active`, `LinkedAtUtc = now`,
     `StoppedAtUtc = null`, счётчик сбоев 0; `session.CompletedAtUtc = now`;
   - после коммита ответ бота `StaffMaxTexts.Linked(shopNames)` **[legal L15]** через существующий
     `IMaxBotClient.SendMessageAsync(…, requestContact: false)` — бот отвечает тому, кто его запустил.
   - Повтор того же апдейта: сессия уже завершена, привязка этого пользователя с тем же `ChatKey` активна → ответ
     `StaffMaxTexts.AlreadyLinked`, без записи.
4. Кабинет опрашивает `GET /api/staff-max` каждые 2 с, пока статус `Pending` (≤ TTL). «Подключено» видно через ≤ 2 с
   после обработки апдейта (SPEC: ≤ 5 с).

### §498.3 Общий вебхук — изоляция от подтверждения телефона (R25-5)

- Новый интерфейс в `Services/PhoneVerification/Max/`:
  ```csharp
  public interface IMaxBotUpdateHandler
  {
      bool CanHandleStart(string payload);                         // по префиксу
      Task HandleStartAsync(MaxUpdateData update, CancellationToken ct);
      Task HandleStoppedAsync(MaxUpdateData update, CancellationToken ct);
  }
  ```
- `MaxWebhookHandler` получает `IEnumerable<IMaxBotUpdateHandler>` и меняется в двух местах:
  - в `HandleBotStartedAsync` **первой строкой** после проверки непустого payload: если какой-то обработчик
    `CanHandleStart(payload)` — отдать ему и выйти. Иначе — существующий код без изменений (`v1.`, неизвестные
    префиксы → «ссылка устарела», как было);
  - новый `case "bot_stopped"`: вызвать `HandleStoppedAsync` у всех обработчиков; ответа в чат нет.
- Подсистема подтверждения телефона не знает про заказы: зависимость однонаправленная, регистрируется в DI
  (`StaffMaxStartHandler : IMaxBotUpdateHandler`). Без регистраций поведение цикла 14 бит-в-бит.
- Регресс: все тесты цикла 14 + явный тест «подтверждение работает при `StaffMax:Enabled = true` и активных
  привязках» (R25-5), тест «payload `sm1.` не трогает `PhoneVerificationSessions`», тест «payload `v1.` не трогает
  `StaffMaxLinkSessions`».

### §498.4 Отключение и остановка бота

| Событие | Что делаем |
|---|---|
| «Отключить» в кабинете (`DELETE /api/staff-max/link`) | удалить строку `StaffMaxLinks` и незавершённые сессии; 204 идемпотентно |
| `bot_stopped` (чат `chat_id`) | все `StaffMaxLinks` с этим `ChatKey` → `StoppedInMax`, `ChatIdCiphertext = null`, `StoppedAtUtc = now` |
| отправка вернула 403/404 (`MaxSendOutcome.ChatUnavailable`) | то же, что `bot_stopped`; строка очереди `Skipped(StaffMaxChatUnavailable)` |
| удаление аккаунта | `Cascade` + явное удаление в `AccountDeletionService` (привязка и сессии) |
| пользователь снова нажал «Начать» без payload | ничего (лог, как сейчас): возобновление — только новой ссылкой из кабинета |

Сообщения прекращаются **сразу**, включая стоящие в очереди: диспетчер перепроверяет привязку перед каждой отправкой
(§499.4). Кабинет для `StoppedInMax` показывает «Отключено: бот остановлен в MAX» и кнопку «Подключить снова».

**Подписка вебхука.** `MaxBotClient.SubscribeAsync` отправляет `update_types = ["bot_started", "message_created",
"bot_stopped"]`. Если платформа отвечает не 2xx, клиент **в том же вызове** повторяет подписку со старым списком из
двух типов и пишет warning: подтверждение телефона не должно зависеть от нового типа апдейта. Остановка тогда
обнаруживается только по 403/404 при отправке. Проверяется юнит-тестом клиента на подставном HTTP-обработчике.

---

## §499. MAX персоналу: постановка и отправка (US-25-02, US-25-04)

### §499.1 Постановка — `OrderStaffMaxQueue` (`Services/Orders/Notifications/`)

Только добавляет строки в транзакцию вызывающего, `SaveChanges` не вызывает, сетевых вызовов нет (конвенция
`OrderStaffPushQueue`).

```
QueueForStaffAsync(order, orderEventId, type, text):
  userIds = участники магазина (IsStaffRole) кроме order.CustomerUserId          -- та же выборка, что у push
  links   = StaffMaxLinks WHERE UserId IN userIds AND Status = Active
  foreach chatKey in links.Select(ChatKey).Distinct():                           -- дедупликация по чату
      add StaffMaxMessage { CompanyId, OrderId, ChatKey, Type, Text,
                            ExpiresAtUtc = now + 1h, IdempotencyKey = $"{type}:{orderEventId}:{chatKey}" }

QueueForOwnerAsync(shopId, ownerUserId, keySeed, text):                           -- US-25-04 (P1)
  link = StaffMaxLinks WHERE UserId = ownerUserId AND Status = Active
  add StaffMaxMessage { CompanyId = shopId, OrderId = null, Type = OwnerOrderLimitWarning, IdempotencyKey = $"{keySeed}:{chatKey}" }
```

Конфликт уникального ключа при повторной постановке невозможен в штатном потоке (события уникальны); при
повторе вставки строки с тем же ключом не добавляются (проверка `Any` перед `Add` в пределах транзакции).

### §499.2 Точка «что произошло»

- `OrderNotificationFlags` получает `StaffMaxEnabled` = `ShopSettings.StaffMaxEnabled ∧ StaffMaxAvailability.Enabled`.
- Чистый `OrderNotificationPlan.For` получает поле результата `StaffMax` (true ⇔ есть `StaffType` ∧ флаг). Таблица
  событий §458 не меняется: MAX идёт **теми же событиями и получателями**, что push персоналу (US-25-02).
- `OrderNotificationPlanner.OnEventAsync`: после push — `staffMaxQueue.QueueForStaffAsync(...)`, если `plan.StaffMax`.
  Флаг push (`StaffPushEnabled`) и флаг MAX независимы (SPEC: push и MAX независимы).
- `OrderLimitWarner`: рядом с `pushQueue.QueueForOwnerAsync` — `staffMaxQueue.QueueForOwnerAsync` с тем же `keySeed`
  (P1, US-25-04). Флаг магазина MAX на предупреждение владельцу **не** влияет (как у push, §459.6).
- Тексты — `StaffMaxTexts` (чистый класс), собираются из тех же `OrderTextFacts`, ссылка — только
  `PublicSiteLinks.StaffOrdersUrl(shopId, orderId)` (новый метод: `{OrdersBaseUrl}/cabinet/{shopId}/orders?order={orderId}`)
  и `PublicSiteLinks.OrdersSubscriptionUrl()` (`{OrdersBaseUrl}/cabinet/subscription`). Форма — §525 контракта.

### §499.3 Исходящая отправка — `IMaxBotMessenger`

```csharp
public abstract record MaxSendOutcome
{
    public sealed record Sent : MaxSendOutcome;
    public sealed record ChatUnavailable(int StatusCode) : MaxSendOutcome;   // 403, 404 — бот остановлен/заблокирован, чата нет
    public sealed record RateLimited : MaxSendOutcome;                       // 429 или локальный лимитер не дал лизинг
    public sealed record Transient(string Detail) : MaxSendOutcome;          // 5xx, таймаут, сеть
    public sealed record Rejected(int StatusCode, string Detail) : MaxSendOutcome; // 400/401 — ошибка конфигурации/формы, без повтора
}

public interface IMaxBotMessenger
{
    Task<MaxSendOutcome> SendAsync(string chatId, string text, CancellationToken ct);
}
```

- Реализует тот же синглтон `MaxBotClient` (общие `_globalLimiter` 30 rps и `_perChatLimiter` 2 msg/s — лимиты MAX
  соблюдаются для обоих сценариев вместе). Классификация ответа — чистый `MaxSendResponseClassifier` с юнит-тестами.
- При `PhoneVerification:Provider = stub` регистрируется `StubMaxBotMessenger` (возвращает `Sent`, без
  `HttpClient` — структурная гарантия цикла 14). В функциональных тестах — подделка, записывающая отправки.
- Тело запроса — `POST messages?chat_id=…` с `{ text }` (без клавиатуры), заголовок `Authorization: <token>` — как у
  `SendMessageAsync`. Токен и id чата в логи не пишутся (категории `max-bot` уже заглушены).
- Чтобы диспетчер не выедал бюджет подтверждений: параллельность диспетчера ≤ `Notifications:StaffMax:MaxParallel`
  (4) и глобальный потолок `Notifications:StaffMax:MaxMessagesPerSecond` (10) — свой `TokenBucketRateLimiter` в
  задаче. У подтверждения телефона остаётся ≥ 20 rps из 30.

### §499.4 Диспетчер — `StaffMaxDispatchTask` (`staff-max-dispatch`, полоса `realtime`, период 5 с)

Форма `StaffPushDispatchTask`: пакет по частичному индексу, истекшие → `Expired(PushTtlExhausted)`, остальные —
параллельно (≤ 4), каждая строка в своём scope. Перед отправкой строка перепроверяется:

| Проверка | Отказ |
|---|---|
| `StaffMaxAvailability.Enabled` | `Skipped(StaffMaxPlatformDisabled)` |
| тип заказа: `ShopSettings.StaffMaxEnabled` магазина | `Skipped(StaffMaxDisabledByShop)` |
| есть **хотя бы одна** `Active` привязка с этим `ChatKey`, чей пользователь: для типов заказа — `CompanyMembership.IsStaffAsync(CompanyId, UserId)` и `UserId != order.CustomerUserId`; для `OwnerOrderLimitWarning` — `BillingAccount.OwnerUserId` магазина | `Skipped(StaffMaxNoRecipient)` |

Id чата расшифровывается из этой привязки (AAD `staff-max-link:{Id}`); ошибка ключа → `Failed(PushAuthRejected)`
как у push. In-flight метка (`AttemptCount++`, `LastAttemptAtUtc`) — **до** сетевого вызова.

| `MaxSendOutcome` | Строка | Привязки |
|---|---|---|
| `Sent` | `Sent`, `SentAtUtc`, `Reason = Delivered` | `LastSuccessAtUtc`, сбои = 0 |
| `ChatUnavailable` | `Skipped(StaffMaxChatUnavailable)` | все с этим `ChatKey` → `StoppedInMax` (§498.4) |
| `RateLimited` | остаётся `Pending`, `Reason = StaffMaxRateLimited`, `NextAttemptAtUtc = now + 10 с`, попытка не считается | — |
| `Transient` | повтор через 1 / 5 / 15 мин, после `MaxAttempts` (4) — `Failed(RetriesExhausted)` | `ConsecutiveFailures++` |
| `Rejected` | `Failed(PushAuthRejected)` + `LogError` (конфигурация бота) | — |

Недоставленное видно в `StaffMaxMessages` (статус, причина) и в логе прохода (`staff-max-dispatch pass: …`).
Сбой MAX никогда не откатывает действие над заказом: постановка — только вставка строк в транзакцию события.

### §499.5 Полосы планировщика (закрывает C24-8 для диспетчеров)

- `ScheduledTaskOptions` получает `Lane` из `ScheduledTasks:{name}:Lane` (по умолчанию `main`).
- `ScheduledTaskRunner.ExecuteAsync` один раз определяет полосы зарегистрированных задач и запускает **по циклу на
  полосу** (`Task.WhenAll`). Тик полосы — `ScheduledTasks:Lanes:{lane}:TickSeconds`, иначе общий
  `ScheduledTasks:TickSeconds`. `TickAsync` фильтрует задачи своей полосы. Остальная логика раннера (снимок состояний,
  advisory-lock на задачу, бюджет времени, запись состояния) не меняется.
- Конфигурация: `staff-push-dispatch`, `customer-order-push-dispatch`, `staff-max-dispatch` → `Lane: realtime`,
  `PeriodSeconds: 5`; `Lanes:realtime:TickSeconds = 5`. Полоса `main` — все остальные, тик прежний (10 с).
- Задачи одной полосы по-прежнему последовательны; разные полосы не блокируют друг друга. Состояния задач —
  отдельные строки `ScheduledTaskStates`, гонок нет.
- Тест (функциональный, `NotificationDispatchTestFactory`): задача `main`, спящая 30 с, не задерживает проход
  `staff-max-dispatch` дольше 10 с.

**Своевременность (SPEC §6):** постановка в транзакции события — 0 с (требование ≤ 5 с); проход раз в 5 с + сеть
MAX ≤ 20 с (таймаут клиента) → p95 доставки < 30 с при работающем MAX.

---

## §500. Отчётные периоды и «сегодня» — общее для истории, сводки, листа сборки

- **Рабочий день магазина — единственное «сегодня»** (T-25-04, §507.4): `ShopGateLoader.PickupContextAsync` →
  `OrderPickupContext.WorkingDay`.
- Периоды считает **сервер** — чистый `ReportPeriod.Resolve(preset, from?, to?, workingDay)`:

  | `preset` | `from` … `to` (включительно, `DateOnly`, по `PickupDate`) |
  |---|---|
  | `Today` | рабочий день |
  | `Yesterday` | рабочий день − 1 |
  | `Last7Days` | рабочий день − 6 … рабочий день |
  | `Last30Days` | рабочий день − 29 … рабочий день |
  | `ThisMonth` | 1-е число месяца рабочего дня … последний день месяца |
  | `LastMonth` | прошлый календарный месяц целиком |
  | `Custom` | `from` … `to` из запроса; `to < from` или длина > 366 дней → 400 |

- Ответ отдаёт разрешённые `from`/`to` и подпись периода («7 дней: 24–30 сен»). Фронт хранит в адресе `preset` (и
  `from`/`to` у `Custom`) и **не считает даты сам**.
- «Прошлый период той же длины» (P1, сводка): `[from − len, to − len]`, где `len` — число дней периода (у
  `ThisMonth`/`LastMonth` — предыдущий календарный месяц целиком).

---

## §501. История заказов (US-25-05, A2)

### §501.1 Маршрут

`POST /api/shops/{shopId}/order-history` (персонал, `ViewOrderReports`, политика `shop-reports`). **POST с телом**, а
не GET: фильтр «покупатель» может содержать цифры телефона, а query-строки оседают в логах nginx и приложения (SPEC
§6). Тело — `OrderHistoryQuery` (§526 контракта). Запрос идемпотентен и ничего не меняет.

### §501.2 Выборка — `OrderReportQueries.History` (`Services/Orders/Reports/`)

```
base = Orders WHERE CompanyId = @shop AND PickupDate BETWEEN @from AND @to                     -- индекс (CompanyId, PickupDate, …)
  [AND Status IN @statuses]
  [AND Number = @number]
  [AND Total(o) BETWEEN @amountFrom AND @amountTo]                                             -- OrderReportExpressions.Total
  [customer: CustomerSearchTerm.Parse(@customer)]
     Phone(digits ≥ 4, только цифры/пробелы/+()-) → PersonalDataErased = false AND CustomerPhone LIKE '%' || digits || '%'   // SUBJECT-PHONE-GATE: not-account-scoped — shop's own orders within a shop-scoped staff search (US-25-05)
     Name(иначе, ≥ 2 символа)                     → PersonalDataErased = false AND CustomerName ILIKE '%' || escaped || '%'
totals = SELECT count(*), count(*) FILTER (Status = Issued), sum(Total(o)) FILTER (Status = Issued) FROM base   -- 1 запрос
page   = base ORDER BY PickupDate DESC, PickupStartUtc DESC, CreatedAtUtc DESC, Id  (или ASC) OFFSET (page-1)*50 LIMIT 50
         SELECT … , ItemCount = Items.Count                                                     -- 1 запрос
```

- `Total(o)` = `o.Status == Issued ? (o.FinalTotal ?? o.EstimatedTotal) : o.EstimatedTotal` — **одно**
  `Expression<Func<Order, decimal>>` для истории, сводки и карточки. Юнит-тест: совпадает с
  `OrderDtoMapper.DisplayTotal` для всех восьми статусов.
- Экранирование `%`/`_`/`\` в `ILIKE` — как в `CompaniesController.EscapeLikeWildcards` (вынести в общий
  `LikePattern.Contains`).
- Поиск по фрагменту телефона — без индекса: период ≤ 366 дней × 500 заказов = ≤ 183 000 строк одного магазина,
  фильтр по `CompanyId, PickupDate` сужает скан. Бюджет p95 < 500 мс подтверждает бенч QA на 200 000 заказов (§515).
  Если не проходит — план Б без изменения контракта: `pg_trgm` GIN по `CustomerPhone`/`CustomerName` отдельной
  миграцией.
- Строка: телефон **маскированный** `OrderPhoneMask.Mask` → `+7 (···) ···-12-34` (новая чистая функция, последние 4
  цифры; `PhoneDisplayMask` цикла 14 не трогаем — у него другой формат). Обезличенный заказ: `customerName = null`,
  `customerPhoneMasked = null`, `personalDataErased = true`, в поиск по покупателю не попадает.
- Итоговая строка «Найдено 128 заказов, выдано на 54 300 ₽» и пустой текст собирает сервер (§526 контракта).
- Нажатие на строку → существующий `GET /api/shops/{shopId}/orders/{orderId}` (карточка с журналом и действиями).

### §501.3 Производительность

| Запрос | Индекс | Ожидание на 200 000 заказов |
|---|---|---|
| итоги по фильтру | `IX_Orders_Report` (index-only при чистой карте видимости) | ≤ 150 мс на 366 дней |
| страница 50 строк | `(CompanyId, PickupDate, PickupStartUtc)` в порядке сортировки | ≤ 50 мс |
| поиск по телефону/имени | скан периода | ≤ 400 мс на 366 дней (проверяется бенчем) |

---

## §502. Сводка за день и период (US-25-06, US-25-07, A3)

`GET /api/shops/{shopId}/summary?period=&from=&to=&top=Amount|Quantity&compare=` (владелец и SuperAdmin,
`ViewSummary`; сотрудник → 403; политика `shop-reports`). Телефонов в запросе нет — GET допустим.

`OrderReportQueries.Summary` — все агрегаты в SQL (урок §9.17):

1. **Статусы:** `SELECT Status, count(*), sum(Total(o)) FROM base GROUP BY Status` → заказов всего, в работе
   (`New/Accepted/Ready`), выдано (число и сумма), отклонено, отменено магазином, отменено покупателем, не забрано.
2. **Топ-10:** по заказам `Issued` периода:
   ```sql
   SELECT oi."ProductId", oi."Unit",
          sum(COALESCE(oi."QuantityActual", oi."QuantityOrdered")) AS qty,        -- шт или граммы
          sum(COALESCE(oi."LineTotalFinal", oi."LineTotalEstimated")) AS amount
   FROM "Orders" o JOIN "OrderItems" oi ON oi."OrderId" = o."Id"
   WHERE o."CompanyId" = @shop AND o."PickupDate" BETWEEN @from AND @to AND o."Status" = 3 /*Issued*/
   GROUP BY oi."ProductId", oi."Unit"
   ORDER BY amount DESC   -- или ключ «количество», см. ниже
   LIMIT 10
   ```
   - Группировка по `ProductId` (переименованный товар — одна строка); `ProductId = null` (не должно встречаться, товары
     удаляются мягко) — группа по `NameSnapshot`.
   - Название: `Products.Name` у неудалённого; у удалённого — `NameSnapshot` строки из самого позднего заказа
     (второй маленький запрос только для удалённых из топ-10).
   - «По количеству»: ключ сортировки — штуки у штучных, **килограммы** у весовых (`граммы / 1000`). Решение §519 п. 9.
   - Количество весовых — в граммах, текст — `OrderTexts.Quantity` («12,35 кг»).
3. **По дням (P1):** `GROUP BY PickupDate` — заказов, выдано, на сумму; только при длине периода > 1 дня.
4. **Прошлый период (P1):** запрос 1 на `[from − len, to − len]`; разницы в процентах считает чистый
   `ShopSummaryMath.Delta` («+12 %», «−3 %», «—» при нулевой базе).

- Доли: от числа заказов периода в **конечных** статусах; при нуле — «—». Процент — целое, `AwayFromZero`.
- Средний чек: `OrderMoney.FromKopecks(round(ToKopecks(issuedAmount) / issuedCount, AwayFromZero))`; при 0 выданных —
  `null` и текст «—».
- Пояснение «Оплата на месте, платформа её не видит» — поле ответа (Q-25-4).
- **Критерий совпадения** (SPEC US-25-06): сводка за день с `preset = Today` и история с тем же периодом и всеми
  статусами дают одинаковые «заказов» и «выдано на сумму» — один `base` и один `Total`. Функциональный тест.
- Производительность: 366 дней ≈ 183 000 заказов и ~500 000 строк позиций; запросы 1 и 3 — по `IX_Orders_Report`,
  запрос 2 — по FK-индексу `OrderItems(OrderId)`. Ожидание < 1,5 с (требование < 3 с).

---

## §503. Лист сборки (US-25-08, A4)

`GET /api/shops/{shopId}/picklist?date=&from=&to=&includeNew=` (персонал, `ViewOrderReports`, политика
`shop-reports`).

- `date` — дата выдачи; по умолчанию текущий рабочий день. Допустимо: любая прошедшая, будущие до
  `рабочий день + PreorderDays`; дальше → 400.
- `from`/`to` — `"HH:mm"` локального времени **рабочего дня `date`**; правило цикла 24 (§449.1): `to ≤ from` —
  интервал через полночь, `"00:00"` в `to` — до полуночи. Не переданы — весь день (с 00:00 `date` до конца последнего
  интервала часов, включая «хвост» после полуночи). Один слот = `from`/`to` границ слота.
- `includeNew` — по умолчанию `true`.

**Выборка** (расширение §451.5):

```sql
SELECT o.*, oi.*  FROM "Orders" o JOIN "OrderItems" oi ON oi."OrderId" = o."Id"
WHERE o."CompanyId" = @shop AND o."PickupDate" = @date
  AND o."PickupStartUtc" >= @fromUtc AND o."PickupStartUtc" < @toUtc          -- у ASAP PickupStartUtc = ориентир
  AND o."Status" IN (1 /*Accepted*/ [, 0 /*New*/ при includeNew])
```

Состав — текущий (правки персонала уже в `OrderItems`). Один запрос + справочник категорий/товаров для порядка.

**Группировка — чистый `PickListBuilder`:**
- «По товарам»: ключ `(ProductId ?? NameSnapshot, Unit)`; сумма `QuantityOrdered` (шт или граммы); число заказов;
  у весовых — разбивка по заказам («3 заказа: 500 г, 1,2 кг, 650 г»); признак «есть непринятые». Порядок —
  `ProductCategories.Position`, затем `Products.Position`, товары без категории и удалённые — в конце по имени
  (`CatalogOrdering` цикла 23).
- «По времени»: группы — слоты сетки магазина, в которые попадает `PickupStartUtc` (у ASAP — слот, содержащий
  ориентир; вне сетки — отдельная группа «Вне расписания» по времени). Внутри — заказы по `PickupStartUtc`,
  `CreatedAtUtc`: номер, время («к 12:30» / «≈ 12:20»), позиции с количеством, комментарий, пометка «Не принят».
  **Без имени и телефона** [legal L18].
- Сетка слотов дня для выбора интервала — новая чистая `PickupSchedule.DaySlots(DateOnly day)`: все слоты шага
  `SlotStepMinutes` внутри интервалов дня **без** отсечения по «сейчас» и без учёта `ScheduledEnabled` (нужна и для
  прошедших дат). Векторы добавляются в `contracts/cycle24/pickup-schedule-vectors.json` новым разделом `daySlots`.
- В ответе: название магазина, подпись даты и интервала, `generatedAtUtc` и текст «по состоянию на 11:42» (по поясу
  магазина), пустой текст («На 12:00–12:15 заказов нет»).
- Автообновление раз в минуту и кнопка «Обновить» — на фронте (тот же маршрут). Печать — CSS `@media print` (§509).
- Производительность: ≤ 500 заказов × ~5 позиций = 2 500 строк одним индексным запросом → < 100 мс.

---

## §504. Карточка покупателя и заметка (US-25-09…11, A5)

### §504.1 Кто такой «покупатель» и чем он адресуется

- Покупатель = `(CompanyId, CustomerPhone)` по **необезличенным** заказам магазина (Q-25-6). Заказы аккаунта и
  гостевые с этим номером — одна карточка. Другие магазины того же аккаунта не видны: все запросы с `CompanyId`.
- **`customerRef` в адресе = id любого заказа этого покупателя в этом магазине.** Адрес карточки:
  `/cabinet/:shopId/customers/:customerRef`. Карточка открывается из карточки заказа, id заказа там уже есть.
  - непрозрачно, неперечислимо (Guid v4), телефона нет ни в пути, ни в query;
  - не нужен новый секрет или таблица «покупателей»;
  - сервер: заказ `customerRef` должен принадлежать `shopId` и иметь `CustomerPhone != null`, иначе 404 (не
    оракул). Дальше всё — по его телефону.
  - Отвергнуто: HMAC телефона (нужна новая колонка на `Orders` и ключ), шифртекст телефона (длинный и нестабильный
    адрес), отдельная таблица покупателей (новая сущность ПДн без выгоды).

### §504.2 Карточка — `GET /api/shops/{shopId}/customers/{customerRef}?page=`

Персонал (`ViewOrderReports`), политика `shop-reports`. Запросы по индексу `(CompanyId, CustomerPhone)`:

1. агрегат: заказов всего, выдано (число, сумма `Total`), отменил сам (`CancelledByCustomer`), не забрал
   (`NotPickedUp`), первый и последний `PickupDate`; `phoneVerified` = есть заказ с `CustomerKind = Customer` ∧
   `CustomerPhoneVerified` (только положительная отметка, как у салонов);
2. имя — из последнего заказа (`CreatedAtUtc DESC`);
3. страница заказов (20, новые сверху), строки формы истории без телефона;
4. заметка `ShopCustomerNotes (CompanyId, Phone)`.

Все сравнения по телефону помечаются `// SUBJECT-PHONE-GATE: not-account-scoped — shop's own customer card (US-25-09)`
(сторож `SubjectPhoneGateInvariantTests` цикла 16).

### §504.3 Заметка

- `PUT /api/shops/{shopId}/customers/{customerRef}/note { text }` (персонал, `EditCustomerNotes`):
  - `text` обрезается по краям; длина > 1000 → 400 «Заметка — не длиннее 1000 символов»;
  - пусто → удалить строку (`note: null` в ответе);
  - иначе upsert по `(CompanyId, Phone)`, `UpdatedAtUtc = now`, `UpdatedByUserId`, `UpdatedByName` — снимок
    `FirstName LastName` автора. Последняя запись выигрывает (конкурентной правки не контролируем — заметка одна,
    правят редко).
- `GET /api/shops/{shopId}/customers/{customerRef}/note` — лёгкое чтение для P1 «Есть заметка» в карточке заказа на
  экране заказов (фронт вызывает при открытии карточки заказа с телефоном). DTO доски не меняется — горячий путь
  опроса не утяжеляется.
- Строка-предупреждение под полем — `GET /api/legal/texts/ShopCustomerNoteNotice`; на 404 фронт показывает
  нейтральный fallback (§508). Покупатель заметку не видит нигде; администраторских маршрутов к заметкам нет.

### §504.4 Правила ПДн (US-25-11)

| Процесс | Что делаем |
|---|---|
| удаление аккаунта (`AccountDeletionService`) | заказы обезличиваются как в цикле 23 → выпадают из карточек. Заметки `WHERE Phone = scope.GuestMatchPhone` удаляются **только при подтверждённом номере** (`SubjectScope.GuestMatchPhone != null`) — правило цикла 16. У заметок, где удаляемый — автор, `UpdatedByName = «Удалённый пользователь»`, `UpdatedByUserId = null` |
| выгрузка (`SubjectDataExporter`) | секция `shopCustomerNotes: [{shopName, updatedAtUtc}]` — **факт без текста**, только при `GuestMatchPhone != null` [legal L17] |
| срок хранения | правило `shop-customer-notes` (`Services/Retention/Rules/ShopCustomerNoteRule.cs`): удаляет заметку, если у магазина нет ни одного заказа с этим телефоном и `PersonalDataErased = false`. Срок не задаёт — следует за обезличиванием (`order-personalization` инертно, `OrderPersonalDataDays = 0`) [legal L20]. Уважает глобальный `DryRun` |
| обращение через форму | ручная процедура оператора (SQL по `ShopCustomerNotes.Phone`), как для заметок салона; описание — `docs/personal-data.md` и `INCIDENT_CHECK_PROCEDURE_CYCLE16.md` (BE-8) |

---

## §505. Каталог магазинов по городу (US-25-12…14, A6, A7)

### §505.1 Кто виден (Q-25-7) — одна функция

`CatalogListingRules.Evaluate(company, settingsHoursSet, hasPublishedProduct, ordersPlan)` (чистая) →
`{visible, checklist}`:

| Условие | Код в чек-листе владельца | Текст |
|---|---|---|
| `IsActive` (не заблокирован) | `ShopBlocked` | «Магазин заблокирован администратором» |
| `WorkingHoursJson != null` | `NoWorkingHours` | «Задайте часы работы» |
| есть товар `IsPublished ∧ DeletedAtUtc = null` | `NoPublishedProducts` | «Опубликуйте хотя бы один товар» |
| `ordersPlan.AllowPublicListing` | `NotAllowedByPlan` | «Показ в каталоге не входит в ваш тариф» |
| `Company.ShowInPublicListing` | `HiddenByOwner` | «Показ выключен в настройках» |

`OrdersPlan` получает поле `AllowPublicListing` (из `SubscriptionPlanConfig.AllowPublicListing` тарифа линейки;
`FallbackFree` — `true`). Салонный `SubscriptionResolver`/`PublicListingQuery` не трогаются: у «Записей» флаг читается
как раньше.

### §505.2 Настройка владельца и тариф

- `GET /api/shops/{shopId}/catalog-listing` (персонал, `ViewShop`) → `{showInCatalog, allowedByPlan, visible,
  statusText, checklist}`.
- `PUT /api/shops/{shopId}/catalog-listing { showInCatalog }` (владелец, `ManageShop`, `[RequiresOwnerTerms]`):
  включить при `!allowedByPlan` → 409 `CatalogConflictDto { code: CatalogListingNotAllowedByPlan }`; выключить можно
  всегда. Пишет `Company.ShowInPublicListing`.
- `CompanyCreationService`: у магазина `ShowInPublicListing = true` (было жёстко `false`); у салона — как было.
- Админка тарифов: у формы линейки «Заказы» (`PlansTab`, `isOrdersForm`) появляется чекбокс «Показ в каталоге goods»
  → существующее поле `allowPublicListing`. Бэкенд `AdminPlansController` уже пишет поле для любой линейки — правка
  только фронта. Меняется без деплоя.
- Салоны не затронуты: `GET /api/companies`, `GET /api/companies/public` фильтруют `Kind = Services` (проверено по
  коду) — регрессионный тест «магазины с `ShowInPublicListing = true` не попадают в салонные списки».

### §505.3 Публичный маршрут — `GET /api/goods/catalog?cityId=&openNow=&search=&page=`

Анонимно, политика `goods-catalog` (120/мин по IP), 20 карточек на страницу. `GoodsCatalogService`:

1. **Кандидаты (1 запрос):** `Companies WHERE Kind = Orders AND IsActive AND ShowInPublicListing [AND CityId = @city]
   AND EXISTS(ShopSettings.WorkingHoursJson IS NOT NULL) AND EXISTS(опубликованный неудалённый товар)`.
2. **Тариф (2 запроса):** `OrdersPlanResolver.GetForAccountsAsync` по аккаунтам кандидатов → отсечь
   `!AllowPublicListing`. Отсечение в памяти одним правилом (§505.1), SQL-двойника нет — меньше мест расхождения.
3. **Правило приёма пакетно:** новый `ShopGateLoader.LoadManyAsync(IReadOnlyList<Company>, nowUtc)` —
   `ShopSettings` (1), `ShopSpecialDays` диапазона для всех (1), тарифы (уже есть), `OrderMonthlyUsages` по аккаунтам и
   месяцам (1) → для каждого `ShopOrderingGate.Evaluate` — **та же** функция, что на странице магазина. Одиночный
   `LoadAsync` переписывается как частный случай `LoadManyAsync`, чтобы логика сборки входа была одна.
4. **Состояние карточки:** `openState` = `gate.OpenState` (текст «Открыто до 21:00» / «Закрыто, откроемся завтра в
   9:00»); приём:

   | Группа | Условие | `acceptance` | Текст |
   |---|---|---|---|
   | 1 | `gate.Accepting ∧ gate.Asap.Available` | `AcceptingNow` | «Принимает заказы» |
   | 2 | `gate.Accepting ∧ ¬Asap ∧ ScheduledAvailable` | `PreorderOnly` | «Можно заказать заранее» |
   | 3 | иначе (пауза, выключатель, лимит, тариф, нет времени) | `NotAccepting` | «Временно не принимает заказы» |

   Покупателю **не** раскрывается точная причина (как на витрине, §450).
5. **Кеш:** результат шагов 1–4 для ключа `cityId|all` — `IMemoryCache` на `Orders:CatalogCacheSeconds` (30). Поиск,
   «Открыто сейчас», сортировка и страница — поверх закешированного списка в памяти (≤ 1000 элементов).
6. **Поиск:** по названию и адресу, без учёта регистра, ≤ 100 символов (длиннее — обрезается, как у
   `companies/public`), внутри уже отфильтрованных видимых магазинов. Скрытые не находятся и не входят в
   `totalCount`.
7. **Порядок:** группа 1 → 2 → 3, внутри — по `Name` (культура ru), затем `Id`.
8. **Ответ:** карточки `{slug, path: "/<slug>", name, address?, cityName, openState, acceptance, acceptanceText}`
   (город — всегда, фронт показывает его только в режиме «Все города»), `totalCount`, `page`, `pageSize`, `city`
   (`{id, name, region, label}` выбранного города или `null`), `emptyText` («В этом городе пока нет магазинов на
   goods»). Неизвестный `cityId` → пустая страница, не 404 (конвенция `companies/public`). Никаких цен, фото,
   рейтингов, реквизитов [legal L19].

**Нагрузка:** холодный запрос — 6 индексных запросов на ≤ 200 кандидатов, `Evaluate` в памяти ≈ 1 мс на магазин →
p95 < 150 мс; тёплый — < 10 мс. Расхождение с витриной ≤ 30 с (SPEC: ≤ 60 с).

### §505.4 Фронт

- `/` goods — `CatalogHomePage`: выбор города (общий `CityCombobox` ezbook, «Все города»), запоминание в
  `localStorage` ключом `goods-home-city` (отдельно от `home-city` ezbook: другой origin, но ключ задаётся явно),
  поиск, «Открыто сейчас», страницы, карточки со ссылкой `path`. Нижний блок «Для бизнеса» — краткий текст бывшей
  `LandingPage` со ссылками «Открыть магазин на goods» (`/cabinet/new`) и «Кабинет» (`/cabinet`); те же ссылки — в
  шапке (`GoodsNavbar`). Отдельного маршрута лендинга нет — новых зарезервированных слов не вводим (§519 п. 12).
- P1 `/city/:cityId` — тот же компонент с городом из адреса (первый сегмент `city` уже в `reservedSlugs`). Кнопка
  «Поделиться» копирует `/city/<id>`. Неизвестный id → пустой список с текстом, не 404.
- Доступность: список — `<ul>`, карточка — ссылка с доступным именем «<название>, <состояние>, <приём>»; фильтры — с
  клавиатуры; состояние — текстом.

---

## §506. Права (`ShopAccess`) и изоляция (A8)

| Действие | Owner | Staff | SuperAdmin | Разрешение |
|---|---|---|---|---|
| история, лист сборки, карточка покупателя, чтение заметки | ✓ | ✓ | ✓ | `ViewOrderReports` (новое) |
| правка заметки | ✓ | ✓ | ✓ | `EditCustomerNotes` (новое) |
| сводка | ✓ | — (403) | ✓ | `ViewSummary` (новое) |
| статус показа в каталоге (GET) | ✓ | ✓ | ✓ | `ViewShop` |
| показ в каталоге (PUT), флаг «Сообщения сотрудникам в MAX» (в `notification-settings`) | ✓ | — | ✓ | `ManageShop` |
| подключение своего MAX (`api/staff-max`) | участник ≥ 1 активного магазина | ✓ | — | членство, не `ShopAccess` |

- Порядок проверок — как в цикле 23: токен → существование и тип (салон/нет → 404) → роль (403 пустым телом).
- Удалённый из магазина сотрудник теряет доступ **сразу**: права читаются из `CompanyMembers` на каждом запросе; кеша
  прав нет.
- Матрица изоляции (`CompanyKindIsolationTests`): все новые `/api/shops/{id}/…` с id салона → 404; `customerRef`
  заказа **другого** магазина → 404; заметка магазина A не видна из магазина B при одинаковом телефоне.
- В меню кабинета сотрудник не видит «Сводку» (`ShopManageDto.myRole`), сервер всё равно отвечает 403.

---

## §507. Долги цикла 24 (блок G, A9)

### §507.1 T-25-01 (C24-5) — `PushAddressGuard.IsPublic` для IPv6 с вложенным IPv4

Порядок (чистая функция, юнит-тест на каждый диапазон с публичным **и** приватным IPv4 внутри):

| Диапазон IPv6 | Решение |
|---|---|
| `::ffff:0:0/96` IPv4-mapped | как сейчас: развернуть и проверить IPv4 |
| `::ffff:0:0:0/96` IPv4-translated (RFC 2765) | развернуть последние 32 бита |
| `64:ff9b::/96` NAT64 well-known | развернуть последние 32 бита |
| `2002::/16` 6to4 | развернуть байты 2–5 |
| `::/96` IPv4-compatible (кроме `::` и `::1`, уже отсечены) | развернуть последние 32 бита |
| `64:ff9b:1::/48` NAT64 local-use (RFC 8215) | **отклонить целиком**: позиция IPv4 зависит от длины префикса сети, достоверно не развернуть; публичные push-сервисы там не живут |
| `2001::/32` Teredo | **отклонить целиком**: клиентский IPv4 закрыт XOR, протокол устарел, push-сервисы там не живут |
| `2001:db8::/32` документация, `100::/64` discard-only | отклонить (попутно) |

IPv4-списки не меняются, кроме добавления документационных `192.0.2.0/24`, `198.51.100.0/24`, `203.0.113.0/24`.

### §507.2 T-25-02 (C24-6) — web-push без системного прокси

- Новый `WebPushHandlerFactory.Create()` (`Services/Notifications/WebPush/`) возвращает `SocketsHttpHandler
  { UseProxy = false, AllowAutoRedirect = false, ConnectCallback = PushAddressGuard.ConnectAsync }`;
  `NotificationServicesExtensions` вызывает его в `ConfigurePrimaryHttpMessageHandler`.
- Юнит-тест на объект фабрики: `UseProxy == false`, `AllowAutoRedirect == false`, `ConnectCallback` задан.

### §507.3 T-25-03 (C24-4) — повторяющиеся дни недели и полнота 400 в `contracts/cycle24`

- Сервер: `WeekdayMask.FromDays` (или валидатор входа) при повторе дня → 400 «День недели указан дважды» (текст уже
  есть у часов, §489) для `ProductInput.availableWeekdays` (POST и PUT товара) и `CategoryWeekdaysInput.weekdays`.
- `contracts/cycle24/openapi.yaml`:
  - `uniqueItems: true` у `CategoryWeekdaysInput.weekdays`, `ProductInput.availableWeekdays`; у
    `WorkingHoursInput.days` уникальность по `dayOfWeek` схемой не выражается — описать в `description`;
  - аудит: все `BadRequest(...)` контроллеров `ShopSchedule`, `ShopMenu`, `ShopNotifications`, `ShopCatalog` (маршруты
    цикла 24), `ShopOrders` (`pickup`), `Storefront` (`?date`, `pickup-slots`), `PublicOrders` (`push-subscription*`),
    `Push` (`site`), `Billing` (`line`) сверяются с `responses` операций; недостающие `'400': PlainTextError`
    дописываются (включая 400 от привязки модели ASP.NET — `ValidationProblemDetails` описывается как
    `application/problem+json` там, где сервер реально его отдаёт).
- После правки — `npm run types:api:cycle24` и коммит генерата (CI сверяет). redocly — зелёный. Schemathesis по
  `contracts/cycle24` — без новых находок (QA).

### §507.4 T-25-04 (C24-9) — одна точка «рабочего дня»

- `OrderPickupContext(TimeZoneInfo Zone, DateTime NowUtc, DateOnly WorkingDay)` — `WorkingDay` **обязателен**,
  `Today => WorkingDay`. Метод `OrderPickupContext.For` **удаляется**. Компилятор находит всех, кто считал «сегодня»
  по календарю.
- Производители контекста — только `ShopGateLoader`:
  - `PickupContextAsync(shop, settings, nowUtc)` — как сейчас;
  - новый пакетный `PickupContextsAsync(IReadOnlyCollection<Guid> shopIds, nowUtc)` → `Dictionary<Guid,
    OrderPickupContext>`: `Companies` (пояс), `ShopSettings` (часы, настройки времени), `ShopSpecialDays` диапазона —
    3 запроса на любое число магазинов.
- Потребители:
  - `PublicOrderService.ListMineAsync` («Мои заказы») — пакетный вызов по магазинам списка;
  - `OrderDtoMapper.ToPublic(…, OrderPickupContext pickupContext)` — параметр обязательный (fallback убрать);
  - `OrderNotificationPlanner.OnEventAsync` → `BuildFacts(order, orderEvent, shop, today)`: `today` = `WorkingDay` из
    `PickupContextAsync` (одна дополнительная выборка особых дней на событие — допустимо). Это автоматически чинит
    push, web-push, мессенджер покупателю и новые сообщения MAX.
- Критерий: часы «пт 18:00–03:00», заказ в сб 01:00 на слот 01:30 → в push, в сообщении MAX, в мессенджере и в «Моих
  заказах» — «к 1:30» без «завтра» и без даты. Юнит-тест `BuildFacts`/`StaffTimePhrase` на границе суток и
  функциональный тест «Мои заказы» с подменённым `INotificationClock`.
- `-warnaserror`: после удаления `For` не должно остаться неиспользуемых `using`/параметров; `WorkingDay` не
  nullable — предупреждений nullable нет.

---

## §508. Правовые места [legal] и заглушки

| # | Что ждёт | Заглушка цикла 25 | Куда ляжет заключение |
|---|---|---|---|
| L15 | служебные сообщения в MAX: допустимость, состав, хранение id чата, текст подключения, риск бота | рубильник выключен в репозитории; состав — номер, магазин, время, число позиций, сумма, ссылка **без имени и телефона**; id чата зашифрован, в DTO и выгрузке нет; тексты бота — нейтральные константы `StaffMaxTexts` (§525 контракта) | `StaffMaxTexts` — одна правка; при необходимости отдельный текст согласия в кабинете (ключ `LegalTextKey`, вне `All`) |
| L17 | карточка и заметка: основание, информирование, счётчики «отменил/не забрал», предупреждение у поля, текст в выгрузке | ключ `ShopCustomerNoteNotice` **вне `All`**, fallback фронта — «Заметку видят только сотрудники этого магазина. Покупатель её не видит.»; выгрузка — факт без текста; счётчики показываются (SPEC US-25-09) | текст в `legal.json` → ключ в `All`; флаг выгрузки текста — одна строка в `SubjectDataExporter` |
| L18 | история, поиск по телефону, данные листа сборки, агрегаты | лист сборки без имени и телефона; в истории телефон маскирован; сводка — агрегаты | правка DTO листа сборки |
| L19 | каталог: агрегатор, сведения о продавце, согласие магазина, адрес ИП | в карточке — только название, адрес (если указан), город, состояние, приём | поля карточки каталога |
| L20 | сроки: привязка MAX, очередь, заметки | привязка — пока активна; остановленная — 30 дней (`Retention:StaffMaxStoppedLinkDays`, для показа «Отключено…»); сессии — сутки после истечения; очередь — 90 дней (`Retention:StaffMaxMessageDays`); заметки — пока есть необезличенный заказ (правило `shop-customer-notes`) | конфиг `Retention:*` |

Тексты, которые видит покупатель, сотрудник или получатель сообщения, команда **не придумывает**: все места выше —
либо фиксированный нейтральный текст из SPEC, либо fallback, либо ключ вне `All`.

---

## §509. Фронтенд

### §509.1 goods (`frontend/goods/`)

| Экран / модуль | Что добавляется |
|---|---|
| `/` 🆕 (`CatalogHomePage`) | каталог по городу (§505.4); `LandingPage` снимается с маршрута, её текст — блок «Для бизнеса» |
| `/city/:cityId` 🆕 (P1) | тот же компонент, город из адреса |
| `GoodsNavbar` | ссылки «Открыть магазин на goods», «Кабинет» |
| `/cabinet/devices` | блок «Заказы в MAX» (`StaffMaxCard`): кнопка, ссылка + QR на компьютере (разметку QR/ссылки переиспользовать из подтверждения телефона ezbook), опрос статуса 2 с, «Подключено 30.09.2026», «Отключить», «Отключено: бот остановлен в MAX», «пока не включены на платформе» |
| `/cabinet/:shopId/notifications` | переключатель «Сообщения сотрудникам в MAX» (`staffMaxEnabled`), при `staffMaxAvailable = false` — с объяснением |
| `/cabinet/:shopId/history` 🆕 | фильтры (период-пресеты, свой диапазон, статусы-мультивыбор, покупатель, сумма от/до, номер), сортировка, итог, таблица 50 строк с `<th scope>`, страницы, пустой текст, пометка «Данные покупателя удалены»; открытие существующей `OrderDetailsModal`. **Фильтры — в query адреса, кроме «покупателя»**: он живёт в `location.state` (переживает «назад» и обновление, в адрес и логи не попадает) |
| `/cabinet/:shopId/summary` 🆕 | только `myRole ∈ {Owner, SuperAdmin}`: показатели, доли, топ с переключателем, пояснение об оплате; P1 — таблица по дням и «+12 %» |
| `/cabinet/:shopId/picklist` 🆕 | дата, интервал (весь день / слот из `slots` / свой «с — по»), «Включая непринятые», виды «По товарам» / «По времени», «Обновить», автообновление 60 с при видимой вкладке, «Печать» (`window.print()`), печатная вёрстка `@media print`: ч/б, без меню и кнопок, шрифт ≥ 14 pt, шапка «Магазин · дата · интервал · по состоянию на 11:42» |
| `/cabinet/:shopId/customers/:customerRef` 🆕 | карточка: имя, телефон (полный, `tel:`), «номер подтверждён», счётчики, заказы постранично, заметка (textarea ≤ 1000, счётчик символов, «кто и когда», предупреждение `ShopCustomerNoteNotice` / fallback) |
| `OrderDetailsModal` | имя покупателя — ссылка на карточку (`customerRef = order.id`, только при `customerPhone != null`); P1 — «Есть заметка: …» через `GET …/customers/{orderId}/note` |
| `/cabinet/:shopId/settings` | переключатель «Показывать магазин в каталоге goods.ezbook.ru», неактивен при `!allowedByPlan` с текстом; чек-лист причин из `catalog-listing` |
| меню кабинета (`ShopLayout`) и экран заказов | ссылки «История», «Лист сборки», «Сводка» (последняя — только владельцу) |

- Типы — из генерата `src/types/api-cycle25.generated.ts` (`npm run types:api:cycle25`). Генераты cycle23/24 остаются
  для неизменённых DTO.
- eslint: разрешить goods импорт `CityCombobox` из ezbook (рядом с `BillingPage`, `NotificationsSection`).
- Фронт **не** считает периоды, доли, средний чек, группировку листа сборки, видимость в каталоге, порядок каталога.

### §509.2 ezbook (`frontend/src/`) — весь перечень

1. `pages/admin/PlansTab.tsx`: в форме линейки «Заказы» (`isOrdersForm`) — чекбокс «Показ в каталоге goods»
   (`allowPublicListing`), бейдж в списке тарифов.
2. Больше ничего. Салон не видит ни одного нового элемента.

---

## §510. Инфраструктура, CI, конфигурация

### §510.1 nginx — **изменений нет**

- Новые API-маршруты лежат под `/api/` — `location /api/` goods и ezbook уже проксирует их.
- Новые SPA-маршруты (`/city/…`, `/cabinet/…`) обслуживает `try_files … /index.html` goods.
- Вебхук MAX остаётся на ezbook.ru (`/api/phone-verification/max/webhook/…`), на goods закрыт 404 — так и надо.
- **Ручной шаг человека по серверному vhost в этом цикле не требуется.** Напоминание: C24-1 (серверный vhost goods
  без `location = /sw.js` и манифеста) — отдельный ручной шаг, при выкате проверить (DO-6).

### §510.2 CI (`.github/workflows/ci.yml`)

1. redocly lint: `../contracts/cycle25/openapi.yaml` — в шаг CI и в комментарий-список `contracts/redocly.yaml`.
2. `package.json`: `"types:api:cycle25": "openapi-typescript ../contracts/cycle25/openapi.yaml -o src/types/api-cycle25.generated.ts"`;
   в CI — `npm run types:api:cycle25` и `src/types/api-cycle25.generated.ts` в `git diff --exit-code`.
3. Генерат cycle24 пересобирается после T-25-03 (сверка уже есть в CI).
4. `check-migration-snapshots.sh` — зелёный с новой миграцией.

### §510.3 Конфигурация (`appsettings.json`, закоммичено)

```json
"Notifications": { "StaffMax": { "Enabled": false, "LinkSessionTtlMinutes": 10, "PollIntervalSeconds": 2,
                                  "MaxParallel": 4, "MaxMessagesPerSecond": 10, "MaxAttempts": 4, "BatchSize": 100 } },
"Orders": { "CatalogCacheSeconds": 30, "CatalogPageSize": 20, "HistoryPageSize": 50, "CustomerOrdersPageSize": 20 },
"Retention": { "StaffMaxStoppedLinkDays": 30, "StaffMaxLinkSessionDays": 1, "StaffMaxMessageDays": 90 },
"ScheduledTasks": {
  "Lanes": { "realtime": { "TickSeconds": 5 } },
  "staff-push-dispatch":          { "Lane": "realtime", "PeriodSeconds": 5 },
  "customer-order-push-dispatch": { "Lane": "realtime", "PeriodSeconds": 5 },
  "staff-max-dispatch":           { "Lane": "realtime", "PeriodSeconds": 5, "Enabled": true, "MaxRunMinutes": 1 } },
"RateLimits": { "shop-reports": { "PermitLimit": 120, "WindowMinutes": 1 },
                "goods-catalog": { "PermitLimit": 120, "WindowMinutes": 1 },
                "staff-max-link": { "PermitLimit": 10, "WindowMinutes": 60 } }
```

- `appsettings.Testing.json` поднимает три новых лимита.
- `docker-compose.prod.yml`: `Notifications__StaffMax__Enabled=${STAFFMAX_ENABLED:-false}` — **единственная новая
  переменная окружения**; включает человек на машине (Q-25-2).
- 429 новых политик — «Слишком много запросов — подождите минуту» (`RateLimitingExtensions`).

---

## §511. Структура проекта — что добавляется

```
ServiceBooking.Core/
├── Entities/  StaffMaxLink.cs, StaffMaxLinkSession.cs, StaffMaxMessage.cs, ShopCustomerNote.cs; ShopSettings (+StaffMaxEnabled)
└── Enums/     StaffMaxLinkStatus.cs; NotificationReason (+5 в конец); LegalTextKey (+ShopCustomerNoteNotice — вне All)

ServiceBooking.Infrastructure/Migrations/  *_Cycle25OrdersInsightsMax.cs (+ Designer)

ServiceBooking.API/
├── Controllers/
│   ├── StaffMaxController.cs            🆕 api/staff-max: GET, POST link-sessions, DELETE link
│   ├── ShopReportsController.cs         🆕 api/shops/{id}: POST order-history, GET summary, GET picklist
│   ├── ShopCustomersController.cs       🆕 api/shops/{id}/customers/{customerRef}: GET, GET/PUT note
│   ├── GoodsCatalogController.cs        🆕 api/goods/catalog (анонимно)
│   ├── ShopsController.cs               + GET/PUT {id}/catalog-listing
│   ├── ShopNotificationsController.cs   + staffMaxEnabled/staffMaxAvailable
│   └── PhoneVerificationController.cs   без изменений (вебхук делегирует через MaxWebhookHandler)
├── Services/
│   ├── StaffMax/            🆕 StaffMaxAvailability, StaffMaxOptions, StaffMaxPayload (чистый), StaffMaxLinkService,
│   │                           StaffMaxStartHandler (: IMaxBotUpdateHandler), StaffMaxTexts (чистый), StaffMaxChatKey
│   ├── PhoneVerification/Max/  IMaxBotUpdateHandler.cs 🆕, IMaxBotMessenger.cs 🆕, MaxSendOutcome.cs 🆕,
│   │                           MaxSendResponseClassifier.cs 🆕 (чистый), StubMaxBotMessenger.cs 🆕;
│   │                           MaxBotClient (+IMaxBotMessenger, +bot_stopped с откатом), MaxWebhookHandler (+делегирование)
│   ├── Orders/Notifications/   OrderStaffMaxQueue.cs 🆕; OrderNotificationPlan (+StaffMax), OrderNotificationPlanner
│   │                           (+MAX, рабочий день), OrderNotificationTexts (без изменений формы)
│   ├── Orders/Reports/      🆕 ReportPeriod (чистый), OrderReportExpressions, OrderReportQueries, CustomerSearchTerm (чистый),
│   │                           OrderPhoneMask (чистый), ShopSummaryMath (чистый), PickListBuilder (чистый), ShopCustomerService
│   ├── Orders/              OrderDtoMapper (OrderPickupContext.WorkingDay обязателен), PublicOrderService (пакетный контекст),
│   │                        OrderLimitWarner (+MAX)
│   ├── Shops/               ShopAccess (+3 разрешения), ShopGateLoader (+LoadManyAsync, +PickupContextsAsync),
│   │                        PickupSchedule (+DaySlots), CatalogListingRules 🆕 (чистый), GoodsCatalogService 🆕,
│   │                        GoodsCatalogOrdering 🆕 (чистый)
│   ├── Billing/             OrdersPlanResolver (+AllowPublicListing)
│   ├── Companies/           CompanyCreationService (магазин: ShowInPublicListing = true)
│   ├── PublicSites/         PublicSiteLinks (+StaffOrdersUrl, +OrdersSubscriptionUrl)
│   ├── Notifications/WebPush/  PushAddressGuard (T-25-01), WebPushHandlerFactory 🆕 (T-25-02)
│   ├── Scheduling/          ScheduledTaskRunner (полосы), ScheduledTaskOptions (+Lane), Tasks/StaffMaxDispatchTask.cs 🆕
│   ├── Retention/Rules/     ShopCustomerNoteRule 🆕, StaffMaxLinkRule 🆕 (остановленные + сессии), StaffMaxMessageRule 🆕
│   ├── Subjects/            SubjectDataExporter (+shopCustomerNotes, +staffMaxLink), AccountDeletionService (+заметки, +привязка)
│   └── DeploymentSafetyChecks (+ValidateStaffMax)
├── Startup/                 регистрации; RateLimitingExtensions (+3 политики); NotificationServicesExtensions (фабрика web-push)
└── appsettings*.json

ServiceBooking.UnitTests/  PushAddressGuardTests (+диапазоны), WebPushHandlerFactoryTests, ReportPeriodTests, CustomerSearchTermTests,
                           OrderPhoneMaskTests, ShopSummaryMathTests, PickListBuilderTests, PickupScheduleVectorsTests (+daySlots),
                           CatalogListingRulesTests, GoodsCatalogOrderingTests, StaffMaxPayloadTests, StaffMaxTextsTests,
                           MaxSendResponseClassifierTests, OrderNotificationPlanTests (+StaffMax), OrderNotificationTextsTests (рабочий день),
                           OrderReportExpressionsTests (Total = DisplayTotal), ShopAccessTests (+3), ScheduledTaskOptionsTests (Lane),
                           MaxBotClientSubscribeTests (откат списка типов)
ServiceBooking.Tests/      Tests/Cycle25{StaffMax,History,Summary,PickList,Customers,Catalog,Debts,PersonalData,Isolation}Tests.cs,
                           Infrastructure/Cycle25TestBase.cs (подменяемые INotificationClock и IMaxBotMessenger)

frontend/goods/src/        pages/CatalogHomePage.tsx 🆕, pages/cabinet/{HistoryPage,SummaryPage,PickListPage,CustomerPage}.tsx 🆕,
                           components/staffMax/StaffMaxCard.tsx 🆕, components/catalog-listing/*, components/reports/*,
                           api/{staffMax,reports,customers,goodsCatalog,catalogListing}.ts 🆕, styles/print.css 🆕
frontend/src/              pages/admin/PlansTab.tsx; types/api-cycle25.generated.ts 🆕
contracts/cycle25/         openapi.yaml 🆕
contracts/cycle24/         openapi.yaml (T-25-03), pickup-schedule-vectors.json (+daySlots)
contracts/cycle23/goods-routes.json   +spaRoutes (§513 FE-0)
.github/workflows/ci.yml, contracts/redocly.yaml, docker-compose.prod.yml, DEPLOY.md §23, deploy/deploy-remote.sh
```

---

## §512. Разбивка работ — backend

Контракт готов **до** кода: фронт стартует в первый день на `npx @stoplight/prism mock contracts/cycle25/openapi.yaml
--port 4025`.

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| **BE-M** | сущности, перечисления (append), `AppDbContext`, **одна** миграция §497 (таблицы, колонка, индексы, два `UPDATE` данных, `Down`). **Один разработчик, один коммит, первым.** Затем `check-migration-snapshots.sh` | — | BE-P, BE-7 |
| **BE-P** | чистые классы + юнит-тесты: `ReportPeriod`, `CustomerSearchTerm`, `OrderPhoneMask`, `ShopSummaryMath`, `PickListBuilder`, `PickupSchedule.DaySlots` (+векторы), `CatalogListingRules`, `GoodsCatalogOrdering`, `StaffMaxPayload`, `StaffMaxTexts`, `MaxSendResponseClassifier`, `OrderNotificationPlan` (+StaffMax), `OrderReportExpressions.Total`, `ShopAccess` (+3), `PushAddressGuard` (T-25-01). Сигнатуры §498–§505 **фиксируются в первый день** | — | всё |
| **BE-1** MAX: подключение | `StaffMaxOptions/Availability`, `ValidateStaffMax`, `IMaxBotUpdateHandler` + делегирование в `MaxWebhookHandler`, `StaffMaxStartHandler`, `bot_stopped`, подписка с откатом в `MaxBotClient.SubscribeAsync`, `StaffMaxController` (GET/POST/DELETE), политика `staff-max-link`, регресс цикла 14 (R25-5) | BE-M, BE-P | BE-2…BE-7 |
| **BE-2** MAX: отправка | `IMaxBotMessenger` в `MaxBotClient` (+ stub), `OrderStaffMaxQueue`, хук в планировщик и `OrderLimitWarner` (P1), `StaffMaxDispatchTask`, полосы раннера (§499.5), `staffMaxEnabled` в `notification-settings` | BE-M, BE-P. **`MaxBotClient.cs` правят BE-1 (подписка) и BE-2 (новый интерфейс) — мерджить последовательно, BE-1 первым** | BE-1, BE-3…BE-6 |
| **BE-3** История и сводка | `OrderReportQueries` (history, summary, days/previous — P1), `ShopReportsController` (order-history, summary), политика `shop-reports`, тест совпадения сводки и истории | BE-M, BE-P | всё |
| **BE-4** Лист сборки | выборка §503, `picklist` в `ShopReportsController` | BE-M, BE-P (`PickListBuilder`, `DaySlots`) | всё (общий контроллер с BE-3: BE-3 создаёт файл, BE-4 добавляет метод) |
| **BE-5** Покупатель | `ShopCustomerService`, `ShopCustomersController` (card, note GET/PUT), `LegalTextKey.ShopCustomerNoteNotice`, ПДн §504.4 (выгрузка, удаление, правило ретенции), маркеры `SUBJECT-PHONE-GATE` | BE-M, BE-P | всё |
| **BE-6** Каталог | `OrdersPlan.AllowPublicListing`, `ShopGateLoader.LoadManyAsync` (+ `LoadAsync` через него), `GoodsCatalogService` + кеш, `GoodsCatalogController`, политика `goods-catalog`, `catalog-listing` GET/PUT, `CompanyCreationService` (магазин → `true`), регресс салонного каталога | BE-M, BE-P | всё |
| **BE-7** Долги G | T-25-02 (фабрика), T-25-03 (400 + `contracts/cycle24` + генерат), T-25-04 (`OrderPickupContext.WorkingDay`, `PickupContextsAsync`, планировщик, «Мои заказы») | — . T-25-04 и BE-2 оба правят `OrderNotificationPlanner`: **T-25-04 мерджится до BE-2**. T-25-04 и BE-6 оба правят `ShopGateLoader`: разные методы, конфликт механический | всё |
| **BE-8** ПДн, документы, эталоны | ретенция MAX (`StaffMaxLinkRule`, `StaffMaxMessageRule`), выгрузка/удаление привязки, `Cycle22RouteTable.golden.txt`, матрица изоляции, `API_DOCUMENTATION.md` §4.20 (дополнение цикла 25), `docs/personal-data.md` (ручная процедура по заметкам) | BE-1…BE-7 | — |

**Правило для всех:** сборка с `-warnaserror` — без неиспользуемых параметров, `using` и nullable-предупреждений;
новые сравнения по телефону — с маркером `// SUBJECT-PHONE-GATE:`; перед объявлением готовности — `grep` классов §511
(«зелёный прогон ≠ функционал»).

---

## §513. Разбивка работ — frontend

| # | Задача | Зависит от | Параллельно с |
|---|---|---|---|
| **FE-0** | `types:api:cycle25` + генерат; в `contracts/cycle23/goods-routes.json` → `spaRoutes`: `/cabinet/:shopId/history`, `/cabinet/:shopId/summary`, `/cabinet/:shopId/picklist`, `/cabinet/:shopId/customers/:customerRef`, `/city/:cityId` (P1). Первые сегменты `cabinet`, `city` уже в `reservedSlugs` — новых слов нет. API-клиенты goods; eslint-исключение `CityCombobox` | — | всё |
| **FE-1** | `StaffMaxCard` на `/cabinet/devices`; флаг MAX на `/cabinet/:shopId/notifications` | FE-0 | FE-2…FE-7 |
| **FE-2** | `/cabinet/:shopId/history` (URL-состояние без покупателя) + ссылки из экрана заказов и меню | FE-0 | |
| **FE-3** | `/cabinet/:shopId/summary` (P0 показатели и топ; P1 по дням и сравнение) | FE-0 | |
| **FE-4** | `/cabinet/:shopId/picklist` + печатная вёрстка + автообновление | FE-0 | |
| **FE-5** | `/cabinet/:shopId/customers/:customerRef`, заметка, ссылка из `OrderDetailsModal`; P1 «Есть заметка» | FE-0 | |
| **FE-6** | `/` каталог, шапка, P1 `/city/:cityId` | FE-0 | |
| **FE-7** | показ в каталоге в `/cabinet/:shopId/settings`; ezbook `PlansTab` — флаг тарифа «Заказов» | FE-0 | |

---

## §514. DevOps — отдельными задачами с критериями готовности

| # | Задача | Когда | Готово, если |
|---|---|---|---|
| **DO-1** | CI: redocly lint `contracts/cycle25/openapi.yaml` (шаг `ci.yml` + список в `contracts/redocly.yaml`); `types:api:cycle25` + `git diff --exit-code src/types/api-cycle25.generated.ts` | сразу (контракт уже в ветке) | CI зелёный на ветке, оба шага видны в логе; порча `$ref` в cycle25 даёт красный CI |
| **DO-2** | `docker-compose.prod.yml`: `Notifications__StaffMax__Enabled=${STAFFMAX_ENABLED:-false}`; `.env.example`/`.env.dev.example` — комментарий | сразу | `docker compose config` показывает переменную с `false` по умолчанию |
| **DO-3** | `deploy/deploy-remote.sh`, смоук goods после готовности API: `GET https://$GOODS_HOST/api/goods/catalog` через локальный nginx (`--resolve`) = 200 и `Content-Type: application/json`, тело содержит `"items"`. Провал → `rollback_hint`, код 1; отключение — прежний `GOODS_SMOKE=0` | после BE-6 | ручной прогон на стенде зелёный; при остановленном API — красный |
| **DO-4** | `DEPLOY.md` §23 «Цикл 25»: миграция (новые таблицы, бэкфилл `ShowInPublicListing` магазинов, флаг бесплатного тарифа, что теряется при `Down`); рубильник `STAFFMAX_ENABLED` — предпосылки (`PHONEVERIFY_PROVIDER=max-bot`, бот настроен, `NOTIFICATIONS_ENCRYPTION_KEY` и `PHONEVERIFY_EXTERNAL_KEY` заданы, иначе fail-fast), порядок включения, как проверить (`GET /api/staff-max` → `available: true`), как выключить; переподписка вебхука с `bot_stopped` (происходит сама при старте/продлении; при откате списка — warning в логе); nginx не меняется | до мерджа | раздел есть, ссылки на §498.1 и §510 |
| **DO-5** | гигиена миграций: после каждого мерджа `develop` в ветку — пересборка Designer-снимка `Cycle25OrdersInsightsMax`, `check-migration-snapshots.sh`; `migrations remove` не применять | на каждом мердже | скрипт зелёный |
| **DO-6** | **ручной шаг человека после выката на стенд:** (1) решить и включить `STAFFMAX_ENABLED=true` в `.env` машины, пересоздать контейнер `api` (Q-25-2); (2) живой смоук MAX: подключить аккаунт сотрудника тестового магазина, оформить заказ → сообщение в MAX ≤ 30 с, «Отключить» → сообщения прекращаются, остановить бота в MAX → кабинет показывает «Отключено…»; (3) **регресс подтверждения телефона** через того же бота (R25-5); (4) напоминание C24-1 — сверить серверный vhost goods (`/sw.js`, манифест) | после выката | чек-лист в `TEST_CATALOG.md` («Цикл 25», ручные) отмечен или записано «рубильник не включён» |

Правка nginx на сервере в этом цикле **не нужна** (§510.1). Стенд goods и бой ezbook живут на одной машине: рубильник
`STAFFMAX_ENABLED` касается только участников магазинов goods — салоны его не видят.

---

## §515. QA

- Кейсы `CY25-*` в `TEST_CATALOG.md`, раздел «Цикл 25».
- Schemathesis по `contracts/cycle25/openapi.yaml` и повторно по `contracts/cycle24/openapi.yaml` (T-25-03) — без
  новых находок.
- Векторы `pickup-schedule-vectors.json` (включая `daySlots`) зелёные.
- **Бенч (обязателен для A2/A3):** сгенерировать магазин с 200 000 заказов за 366 дней (скрипт в
  `tools/bench/cycle25/`, по образцу `tools/bench/cycle22/`): история без фильтров, с поиском по 4 цифрам телефона и
  по имени, со всеми фильтрами — p95 < 500 мс; сводка 1/31/366 дней — < 1 с / < 1 с / < 3 с; лист сборки на 500
  заказов — < 300 мс; карточка — < 300 мс; каталог на 200 магазинов анонимно — < 300 мс. Итоги —
  `BENCHMARK_CYCLE25.md`.
- Критерий совпадения «сводка за день = итог истории» (US-25-06).
- MAX (с подделкой `IMaxBotMessenger`): одно событие → одно сообщение в чат, привязанный к двум аккаунтам; удалённый
  сотрудник, выключенный флаг магазина, «Отключить», остановка бота, выключенная платформа — стоящие строки не
  уходят; сбой MAX не откатывает заказ; повтор задачи не шлёт дубль; полоса `realtime` не ждёт долгую задачу `main`.
- Регресс цикла 14 целиком + «подтверждение работает при включённых сообщениях персоналу» (R25-5).
- T-25-04 на границе суток: push, MAX, мессенджер, «Мои заказы».
- Изоляция (§506); права (сотрудник → 403 на сводку; удалённый сотрудник → 403 сразу).
- ПДн: выгрузка (факт заметки без текста, привязка без id чата), удаление (заметки только при подтверждённом номере,
  привязка удалена, автор заметки → «Удалённый пользователь»), правило ретенции заметок.
- Каталог: все пять условий видимости; скрытые не находятся поиском и не входят в `totalCount`; салонные списки без
  магазинов; порядок групп.
- Базовая линия — не ниже 2166 / 1030 / 1050.

---

## §516. Точки синхронизации и порядок резки

| Что | Где зафиксировано |
|---|---|
| форма DTO, коды, перечисления | `contracts/cycle25/openapi.yaml` (генерат — единственный источник типов) |
| тексты 400/409/429, сообщения MAX и бота | `API_CONTRACT_CYCLE25.md` (сервер собирает, фронт печатает) |
| маршруты goods | `contracts/cycle23/goods-routes.json` |
| сетка слотов листа сборки | `pickup-schedule-vectors.json`, раздел `daySlots` |
| периоды и «сегодня» | только сервер (`ReportPeriod`, рабочий день) |

**Если не укладываемся (R25-1)** — P1 режутся целиком, в порядке SPEC: US-25-07 (по дням и сравнение) → US-25-14
(`/city/:cityId`) → US-25-04 (MAX-предупреждение о лимите) → P1-пункт US-25-10 («Есть заметка» на экране заказов).
P0, включая блок G, не режутся. Форма контракта от резки не меняется: P1-поля ответа (`days`, `previous`) остаются
`nullable` и приходят `null`.

---

## §517. Совместимость и выкат

- Для ezbook ломающих изменений нет. Салонный код меняется закрытым списком: `MaxWebhookHandler` (делегирование по
  префиксу), `MaxBotClient` (новый интерфейс, список типов подписки с откатом), `ScheduledTaskRunner` (полосы),
  `PushAddressGuard`/фабрика web-push (строже), `PlansTab` (поле для линейки «Заказы»).
- Для goods все поля ответов добавочные; `staffMaxEnabled` во входе `notification-settings` необязателен (null — не
  менять), фронт цикла 24 работает в окне выката.
- **Поведенческие изменения, которых требует SPEC:** главная goods — каталог; магазины стенда видимы в каталоге при
  выполнении условий (бэкфилл §497.1); «сегодня» в текстах и «Моих заказах» — рабочий день; повторяющиеся дни
  недели → 400; push-клиент не ходит через прокси и строже к IPv6.
- Порядок выката: деплой (миграция на старте) → смоук (DO-3) → ручной шаг DO-6 (рубильник MAX — отдельно, по решению
  человека).

---

## §518. Риски

| # | Риск | Решение |
|---|---|---|
| R-1 (R25-2) | Блокировка бота за сообщения персоналу отключит подтверждение телефона | рубильник выключен в репозитории; бот пишет только запустившим его; в сообщениях нет ПДн покупателя; свой бюджет частоты (≤ 10 из 30 rps); L15 до боевого включения |
| R-2 (R25-5) | Регресс подтверждения из-за общего вебхука | делегирование по префиксу первой строкой, ветка `v1.` не меняется; подписка с откатом на старый список типов; регресс цикла 14 + явные тесты §498.3 + живой смоук DO-6 |
| R-3 | Форма ответов MAX (403/404 при блокировке, `bot_stopped`) не подтверждена вживую | классификатор — один файл с тестами; при расхождении меняется только он; проверка в DO-6 (урок цикла 14: живой проход находил расхождения) |
| R-4 | Медленная история на фрагменте телефона | бенч QA на 200 000; план Б — `pg_trgm` отдельной миграцией без изменения контракта |
| R-5 | Сводка и история разойдутся в цифрах | одно выражение `Total`, один `base`; функциональный тест совпадения |
| R-6 (R25-3) | Свободный текст заметки (здоровье и т. п.) | лимит 1000, предупреждение L17, не видна покупателю и администратору; ограничение продукта названо прямо |
| R-7 (R25-4) | Каталог стенда публичен | приемлемо для стенда; владелец выключает показ |
| R-8 | Полосы раннера — новая параллельность в планировщике | задачи разных полос не делят состояние; advisory-lock на задачу; тест «долгая `main` не держит `realtime`» |
| R-9 | Бэкфилл `ShowInPublicListing` покажет магазины стенда без согласия владельца | значение и раньше не было выбором владельца (жёсткий `false` в коде); показ требует часов и товаров; стенд с тестовыми данными; владелец выключает; L19 |
| R-10 (R25-1) | Объём | параллельный план, prism с первого дня, порядок резки §516 |
| R-11 | Правовые места L15, L17–L20 | нейтральные заглушки §508; goods остаётся стендом |

---

## §519. Отклонения от буквы SPEC и решения сверх неё (читать обязательно)

1. **US-25-12 «у магазинов стенда переключатель принимает текущее значение `ShowInPublicListing`» — не так:
   миграция ставит `true` всем магазинам.** Сейчас `CompanyCreationService` пишет магазину жёстко `false`, то есть
   «текущее значение» — не выбор владельца, и по букве SPEC ни один магазин стенда не попал бы в каталог. Новые
   магазины — `true` (Q-25-7).
2. **`customerRef` — id заказа покупателя**, а не отдельный идентификатор покупателя (§504.1). Непрозрачно, без
   новой сущности и ключа. Разные заказы одного покупателя дают разные адреса одной карточки — допустимо.
3. **История запрашивается `POST …/order-history` с телом**, а не GET: иначе фрагмент телефона попал бы в
   query-строку и логи. На фронте фильтр «покупатель» хранится в `location.state`, а не в адресе.
4. **Периоды считает сервер** по пресету; фронт дат не вычисляет (единое «сегодня» = рабочий день).
5. **T-25-01: `64:ff9b:1::/48` и Teredo `2001::/32` отклоняются целиком**, а не разворачиваются (§507.1).
6. **T-25-04: `OrderPickupContext.For` удаляется**, `WorkingDay` обязателен — «одна точка рабочего дня»
   обеспечивается компилятором.
7. **Исходящая отправка MAX — отдельный интерфейс `IMaxBotMessenger`**, а не новый метод `IMaxBotClient`: код и
   подделки цикла 14 не трогаются.
8. **Очередь MAX — отдельная таблица с адресатом «чат»**, а не колонка транспорта в `StaffPushNotifications`.
9. **Топ «по количеству»**: штучные сравниваются в штуках, весовые — в килограммах. Иначе смешанный список не
   упорядочить; пометка «кг» в строке снимает двусмысленность.
10. **C24-8 закрывается для диспетчеров полосами планировщика**, не параллелизацией всех задач. Остальные задачи
    остаются последовательными в полосе `main`.
11. **Остановленная привязка хранится 30 дней без id чата** — ради текста «Отключено: бот остановлен в MAX» [L20].
12. **Лендинг goods снимается с `/` без нового адреса**: его текст — блок на главной, ссылки — в шапке. Новое
    зарезервированное слово (`/business`) могло бы совпасть с адресом существующего магазина.
13. **P1 «Есть заметка»** — отдельный лёгкий `GET …/customers/{orderId}/note` при открытии карточки заказа, а не поле
    в DTO доски: опрос экрана заказов (до 200 rps) не утяжеляется.
14. **Подписка вебхука на `bot_stopped` — с автоматическим откатом** на прежний список типов, если платформа её не
    примет.
